"""
Arcane Strike — webcam hand bridge (DEV ONLY).

Tracks your hands with the laptop webcam (MediaPipe Hand Landmarker) and streams
21 joints per hand to Unity over UDP, where WebcamHandSource places them in front
of the player's head. Lets you test gesture spells without a Quest headset.

Packet (JSON, ~30/s):
  {"t": <seconds>, "hands": [{"side": "Left"|"Right", "score": 0.97, "p": [x,y,z, ... 21 joints]}]}
  x = player's right (m), y = up (m), z = distance from the webcam (m)

Usage:
  python hand_bridge.py                 # camera 0, UDP 127.0.0.1:5065
  python hand_bridge.py --camera 2 --fov 70 --swap-hands
Keys in the preview window: q / Esc = quit

Fast punches blur the webcam image and MediaPipe briefly loses the hand. For up to
--coast seconds the bridge keeps sending the hand, moved along its last velocity,
so Unity still sees the whole punch instead of a frozen hand.
"""

import argparse
import json
import math
import socket
import sys
import time
import urllib.request
from pathlib import Path

import cv2
import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision

MODEL_URL = (
    "https://storage.googleapis.com/mediapipe-models/hand_landmarker/"
    "hand_landmarker/float16/1/hand_landmarker.task"
)
MODEL_PATH = Path(__file__).with_name("hand_landmarker.task")

# MediaPipe joint order (matches HandJoint in Unity). Used for the preview drawing.
BONES = [
    (0, 1), (1, 2), (2, 3), (3, 4),
    (0, 5), (5, 6), (6, 7), (7, 8),
    (5, 9), (9, 10), (10, 11), (11, 12),
    (9, 13), (13, 14), (14, 15), (15, 16),
    (13, 17), (0, 17), (17, 18), (18, 19), (19, 20),
]


def parse_args():
    ap = argparse.ArgumentParser(description="Webcam → Unity hand bridge for Arcane Strike")
    ap.add_argument("--camera", type=int, default=0, help="webcam index (default 0)")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=5065, help="must match WebcamHandSource.port")
    ap.add_argument("--fov", type=float, default=65.0,
                    help="webcam horizontal field of view in degrees (most laptop cams: 60–75)")
    ap.add_argument("--width", type=int, default=640)
    ap.add_argument("--height", type=int, default=480)
    ap.add_argument("--swap-hands", action="store_true",
                    help="use this if your left hand shows up as Right in the preview")
    ap.add_argument("--depth-smoothing", type=float, default=0.3,
                    help="0..0.95; smooths the jittery distance estimate (higher hides punches toward the camera)")
    ap.add_argument("--min-confidence", type=float, default=0.3,
                    help="tracking/presence confidence; lower keeps fists and blurry hands tracked longer")
    ap.add_argument("--coast", type=float, default=0.12,
                    help="seconds to keep sending a lost hand along its last velocity (0 = off)")
    ap.add_argument("--exposure", type=float, default=None,
                    help="manual webcam exposure (driver units, try 50-150). Shorter = less motion blur; needs a bright room")
    ap.add_argument("--no-preview", action="store_true")
    return ap.parse_args()


def ensure_model():
    if MODEL_PATH.exists():
        return
    print(f"Downloading hand model to {MODEL_PATH} ...")
    urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)


def to_player_space(img_lms, world_lms, w, h, focal, prev_depth, smoothing):
    """Returns (points[21] as (x right, y up, z distance), wrist depth)."""
    # Apparent size of the wrist → middle knuckle bone gives the distance to the camera.
    real = math.dist(
        (world_lms[0].x, world_lms[0].y, world_lms[0].z),
        (world_lms[9].x, world_lms[9].y, world_lms[9].z),
    )
    pix = math.dist((img_lms[0].x * w, img_lms[0].y * h), (img_lms[9].x * w, img_lms[9].y * h))
    depth = focal * real / max(pix, 1.0)
    if prev_depth is not None:
        depth = prev_depth * smoothing + depth * (1.0 - smoothing)

    cx, cy = w / 2.0, h / 2.0
    wx = (img_lms[0].x * w - cx) * depth / focal
    wy = (img_lms[0].y * h - cy) * depth / focal
    w0 = world_lms[0]

    pts = []
    for lm in world_lms:
        # Camera space of the (mirrored) image: x right, y down, z away from the camera.
        x = wx + (lm.x - w0.x)
        y = wy + (lm.y - w0.y)
        z = depth + (lm.z - w0.z)
        pts.extend((round(x, 4), round(-y, 4), round(z, 4)))
    return pts, depth


def main():
    args = parse_args()
    ensure_model()

    cap = cv2.VideoCapture(args.camera)
    cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)
    cap.set(cv2.CAP_PROP_FPS, 60)
    cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)  # always process the newest frame, not a queued old one
    if args.exposure is not None:
        cap.set(cv2.CAP_PROP_AUTO_EXPOSURE, 1)  # V4L2: 1 = manual, 3 = auto
        cap.set(cv2.CAP_PROP_EXPOSURE, args.exposure)
    if not cap.isOpened():
        sys.exit(f"Could not open webcam {args.camera}. Try --camera 1 or 2.")

    options = vision.HandLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path=str(MODEL_PATH)),
        running_mode=vision.RunningMode.VIDEO,
        num_hands=2,
        min_hand_detection_confidence=0.4,
        min_hand_presence_confidence=args.min_confidence,
        min_tracking_confidence=args.min_confidence,
    )
    landmarker = vision.HandLandmarker.create_from_options(options)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (args.host, args.port)
    start = time.monotonic()
    last_ts = -1
    prev_depth = {"Left": None, "Right": None}
    # Last real sighting per hand: (time, points, velocity per coordinate in m/s).
    last = {"Left": None, "Right": None}
    fps_t, fps_n, fps = time.monotonic(), 0, 0.0

    print(f"Streaming hands to udp://{args.host}:{args.port}  (q or Esc to quit)")
    while True:
        ok, frame = cap.read()
        if not ok:
            print("Webcam frame grab failed; retrying...")
            time.sleep(0.05)
            continue

        # Mirror the image so it behaves like a mirror and MediaPipe's handedness is correct.
        frame = cv2.flip(frame, 1)
        h, w = frame.shape[:2]
        focal = (w / 2.0) / math.tan(math.radians(args.fov) / 2.0)

        ts = int((time.monotonic() - start) * 1000)
        if ts <= last_ts:
            ts = last_ts + 1
        last_ts = ts

        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        result = landmarker.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), ts)

        hands = []
        seen = set()
        for img_lms, world_lms, handed in zip(result.hand_landmarks, result.hand_world_landmarks, result.handedness):
            side = handed[0].category_name
            if args.swap_hands:
                side = "Left" if side == "Right" else "Right"
            if side in seen:  # never send two hands with the same side
                continue
            seen.add(side)

            pts, prev_depth[side] = to_player_space(
                img_lms, world_lms, w, h, focal, prev_depth[side], args.depth_smoothing)
            now = ts / 1000.0
            vel = [0.0] * len(pts)
            if last[side] is not None and 0 < now - last[side][0] < 0.2:
                dt = now - last[side][0]
                vel = [max(-6.0, min(6.0, (a - b) / dt)) for a, b in zip(pts, last[side][1])]
            last[side] = (now, pts, vel)
            hands.append({"side": side, "score": round(handed[0].score, 3), "p": pts})

            if not args.no_preview:
                px = [(int(lm.x * w), int(lm.y * h)) for lm in img_lms]
                color = (255, 180, 80) if side == "Left" else (60, 150, 255)
                for a, b in BONES:
                    cv2.line(frame, px[a], px[b], color, 2)
                for p in px:
                    cv2.circle(frame, p, 3, (255, 255, 255), -1)
                cv2.putText(frame, f"{side} {prev_depth[side]:.2f}m", (px[0][0] - 30, px[0][1] + 25),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.6, color, 2)

        for side in ("Left", "Right"):
            if side in seen or last[side] is None:
                continue
            gap = ts / 1000.0 - last[side][0]
            if gap <= args.coast:
                # Lost mid-movement (usually motion blur): keep it moving along its last velocity.
                t0, pts, vel = last[side]
                coasted = [round(p + v * gap, 4) for p, v in zip(pts, vel)]
                hands.append({"side": side, "score": 0.0, "coast": True, "p": coasted})
            else:
                prev_depth[side] = None
                last[side] = None

        # Always send, even with no hands, so Unity knows the bridge is alive.
        sock.sendto(json.dumps({"t": round(ts / 1000.0, 3), "hands": hands}).encode(), target)

        fps_n += 1
        if time.monotonic() - fps_t >= 1.0:
            fps, fps_n, fps_t = fps_n / (time.monotonic() - fps_t), 0, time.monotonic()

        if not args.no_preview:
            cv2.putText(frame, f"{fps:.0f} fps -> {args.host}:{args.port}", (10, 25),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
            cv2.imshow("Arcane Strike hand bridge", frame)
            if cv2.waitKey(1) & 0xFF in (ord("q"), 27):
                break

    cap.release()
    cv2.destroyAllWindows()
    landmarker.close()


if __name__ == "__main__":
    main()
