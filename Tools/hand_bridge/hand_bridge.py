"""
Arcane Strike — webcam body + hand bridge (DEV ONLY).

Tracks your upper body (MediaPipe Pose) and your hands (MediaPipe Hand Landmarker)
with the laptop webcam and streams them to Unity over UDP, where WebcamHandSource
places them around the player's head. Lets you test gesture spells, punches and
dodges without a Quest headset, sitting at the desk or standing back from the laptop.

Range: hands alone work from ~0.3 m. Standing play works best at 1.2–2.0 m with
1280x720 (the default), because the bridge finds each hand from the body's wrist and
zooms in on it, so hands are still tracked when they're small in the image.

Packet (JSON, ~25-30/s):
  {"t": <seconds>,
   "hands": [{"side": "Left"|"Right", "score": 0.97, "p": [x,y,z, ... 21 joints]}],
   "body":  {"score": 0.9, "p": [x,y,z, ... 11 joints], "v": [visibility x 11]}}   (only when seen)
  x = player's right (m), y = up (m), z = distance from the webcam (m)
  Body joint order (UpperBodyJoint in Unity): Head, LeftEar, RightEar, LeftShoulder, RightShoulder,
  LeftElbow, RightElbow, LeftWrist, RightWrist, LeftHip, RightHip.

Usage:
  python hand_bridge.py                 # camera 0, 1280x720, UDP 127.0.0.1:5065
  python hand_bridge.py --camera 2 --fov 70 --swap-hands --swap-body
  python hand_bridge.py --no-body --width 640 --height 480   # old hands-only mode (faster)
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

HAND_MODEL_URL = (
    "https://storage.googleapis.com/mediapipe-models/hand_landmarker/"
    "hand_landmarker/float16/1/hand_landmarker.task"
)
HAND_MODEL_PATH = Path(__file__).with_name("hand_landmarker.task")
POSE_MODEL_URLS = {
    "lite": "https://storage.googleapis.com/mediapipe-models/pose_landmarker/"
            "pose_landmarker_lite/float16/latest/pose_landmarker_lite.task",
    "full": "https://storage.googleapis.com/mediapipe-models/pose_landmarker/"
            "pose_landmarker_full/float16/latest/pose_landmarker_full.task",
}

# MediaPipe hand joint order (matches HandJoint in Unity). Used for the preview drawing.
BONES = [
    (0, 1), (1, 2), (2, 3), (3, 4),
    (0, 5), (5, 6), (6, 7), (7, 8),
    (5, 9), (9, 10), (10, 11), (11, 12),
    (9, 13), (13, 14), (14, 15), (15, 16),
    (13, 17), (0, 17), (17, 18), (18, 19), (19, 20),
]

# MediaPipe Pose indices, as labelled by the model (anatomical left/right of a NON-mirrored person).
P_NOSE, P_L_EAR, P_R_EAR = 0, 7, 8
P_L_SHOULDER, P_R_SHOULDER, P_L_ELBOW, P_R_ELBOW = 11, 12, 13, 14
P_L_WRIST, P_R_WRIST, P_L_PINKY, P_R_PINKY, P_L_INDEX, P_R_INDEX = 15, 16, 17, 18, 19, 20
P_L_HIP, P_R_HIP = 23, 24

# Body joints sent to Unity, in this order (matches UpperBodyJoint).
BODY_JOINTS = ["Head", "LeftEar", "RightEar", "LeftShoulder", "RightShoulder",
               "LeftElbow", "RightElbow", "LeftWrist", "RightWrist", "LeftHip", "RightHip"]
BODY_BONES = [(1, 0), (2, 0), (3, 4), (3, 5), (5, 7), (4, 6), (6, 8), (3, 9), (4, 10), (9, 10)]


def parse_args():
    ap = argparse.ArgumentParser(description="Webcam → Unity body + hand bridge for Arcane Strike")
    ap.add_argument("--camera", type=int, default=0, help="webcam index (default 0)")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=5065, help="must match WebcamHandSource.port")
    ap.add_argument("--fov", type=float, default=65.0,
                    help="webcam horizontal field of view in degrees (most laptop cams: 60–75)")
    ap.add_argument("--width", type=int, default=1280, help="1280x720 lets you stand ~1.5 m back")
    ap.add_argument("--height", type=int, default=720)
    ap.add_argument("--swap-hands", action="store_true",
                    help="use this if your left hand shows up as Right in the preview")
    ap.add_argument("--no-body", action="store_true", help="hands only (faster, but only works close to the camera)")
    ap.add_argument("--pose-model", choices=("lite", "full"), default="full",
                    help="full = more accurate arms; lite = faster on slow laptops")
    ap.add_argument("--swap-body", action="store_true",
                    help="use this if the preview labels your left shoulder/wrist as R")
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


def ensure_model(path, url):
    if path.exists():
        return
    print(f"Downloading {path.name} ...")
    urllib.request.urlretrieve(url, path)


class Camera:
    """Pinhole model of the (mirrored) webcam image. Camera space: x right, y down, z away from the camera."""

    def __init__(self, w, h, fov):
        self.w, self.h = w, h
        self.cx, self.cy = w / 2.0, h / 2.0
        self.focal = (w / 2.0) / math.tan(math.radians(fov) / 2.0)

    def unproject(self, u, v, depth):
        return ((u - self.cx) * depth / self.focal, (v - self.cy) * depth / self.focal, depth)


def to_wire(points):
    """Camera space (y down) → packet (y up), flattened and rounded."""
    out = []
    for x, y, z in points:
        out.extend((round(x, 4), round(-y, 4), round(z, 4)))
    return out


def mid(a, b):
    return ((a[0] + b[0]) / 2.0, (a[1] + b[1]) / 2.0)


def xyz(lm):
    return (lm.x, lm.y, lm.z)


def mid3(a, b):
    return ((a.x + b.x) / 2.0, (a.y + b.y) / 2.0, (a.z + b.z) / 2.0)


# ---------------------------------------------------------------- body

class BodyTracker:
    def __init__(self, args):
        path = Path(__file__).with_name(f"pose_landmarker_{args.pose_model}.task")
        ensure_model(path, POSE_MODEL_URLS[args.pose_model])
        self.landmarker = vision.PoseLandmarker.create_from_options(vision.PoseLandmarkerOptions(
            base_options=mp_python.BaseOptions(model_asset_path=str(path)),
            running_mode=vision.RunningMode.VIDEO,
            num_poses=1,
            min_pose_detection_confidence=0.5,
            min_pose_presence_confidence=0.5,
            min_tracking_confidence=0.5,
        ))
        # The image is mirrored, so the model's "left" is the player's right. --swap-body undoes this.
        left = dict(shoulder=P_L_SHOULDER, elbow=P_L_ELBOW, wrist=P_L_WRIST, index=P_L_INDEX,
                    pinky=P_L_PINKY, hip=P_L_HIP, ear=P_L_EAR)
        right = {k: v + 1 for k, v in left.items()}
        self.L, self.R = (right, left) if not args.swap_body else (left, right)
        self.smoothing = args.depth_smoothing
        self.depth = None

    def track(self, mp_image, ts, cam):
        """Returns the tracked upper body, or None. 'pts' are the 11 joints in camera space."""
        res = self.landmarker.detect_for_video(mp_image, ts)
        if not res.pose_landmarks:
            self.depth = None
            return None
        img, world = res.pose_landmarks[0], res.pose_world_landmarks[0]
        L, R = self.L, self.R
        px = lambda i: (img[i].x * cam.w, img[i].y * cam.h)

        sh_i = mid(px(L["shoulder"]), px(R["shoulder"]))
        sh_w = mid3(world[L["shoulder"]], world[R["shoulder"]])
        if min(img[L["hip"]].visibility, img[R["hip"]].visibility) > 0.5:
            # Shoulder → hip length barely changes when you turn, so it gives the steadiest distance.
            real = math.dist(sh_w, mid3(world[L["hip"]], world[R["hip"]]))
            pix = math.dist(sh_i, mid(px(L["hip"]), px(R["hip"])))
        else:
            real = math.dist(xyz(world[L["shoulder"]]), xyz(world[R["shoulder"]]))
            pix = math.dist(px(L["shoulder"]), px(R["shoulder"]))
        depth = cam.focal * real / max(pix, 1.0)
        if self.depth is not None:
            depth = self.depth * self.smoothing + depth * (1.0 - self.smoothing)
        self.depth = depth

        anchor = cam.unproject(sh_i[0], sh_i[1], depth)
        order = [P_NOSE, L["ear"], R["ear"], L["shoulder"], R["shoulder"],
                 L["elbow"], R["elbow"], L["wrist"], R["wrist"], L["hip"], R["hip"]]
        pts, vis, pix_pts = [], [], []
        for i in order:
            w = world[i]
            pts.append((anchor[0] + w.x - sh_w[0], anchor[1] + w.y - sh_w[1], anchor[2] + w.z - sh_w[2]))
            vis.append(round(img[i].visibility, 2))
            pix_pts.append((int(img[i].x * cam.w), int(img[i].y * cam.h)))
        return {"pts": pts, "vis": vis, "px": pix_pts, "depth": depth, "img": img, "L": L, "R": R}


# ---------------------------------------------------------------- hands

def size_depth(img_px, world_lms, cam):
    """Distance to the hand from its apparent size (wrist → middle knuckle). Responsive but noisy."""
    real = math.dist(xyz(world_lms[0]), xyz(world_lms[9]))
    return cam.focal * real / max(math.dist(img_px[0], img_px[9]), 1.0)


def hand_points(img_px, world_lms, cam, depth_override, prev_depth, smoothing):
    """Hand joints in camera space. img_px = 21 pixel positions in the full frame.
    depth_override: wrist distance worked out by the caller (body wrist + hand-size changes);
    otherwise it comes from the apparent size of the hand."""
    if depth_override is not None:
        depth = depth_override
    else:
        depth = size_depth(img_px, world_lms, cam)
        if prev_depth is not None:
            depth = prev_depth * smoothing + depth * (1.0 - smoothing)
    wrist = cam.unproject(img_px[0][0], img_px[0][1], depth)
    w0 = world_lms[0]
    return [(wrist[0] + lm.x - w0.x, wrist[1] + lm.y - w0.y, wrist[2] + lm.z - w0.z) for lm in world_lms], depth


def hand_crop(body, side, cam):
    """Square crop around one hand, found from the body's wrist, index and pinky. None if not visible."""
    img, ix = body["img"], body[side[0]]
    if img[ix["wrist"]].visibility < 0.5:
        return None
    p = lambda i: (img[i].x * cam.w, img[i].y * cam.h)
    wrist, index, pinky, elbow = p(ix["wrist"]), p(ix["index"]), p(ix["pinky"]), p(ix["elbow"])
    centre = ((wrist[0] + index[0] + pinky[0]) / 3.0, (wrist[1] + index[1] + pinky[1]) / 3.0)
    size = max(3.0 * math.dist(wrist, index), 1.1 * math.dist(wrist, elbow), 80.0)
    return int(centre[0] - size / 2), int(centre[1] - size / 2), int(size), wrist


def detect_in_crop(image_landmarker, rgb, crop):
    """Runs the hand model on a zoomed crop. Returns (21 pixel points in the full frame, world landmarks) or None."""
    x0, y0, size, wrist = crop
    pad = size  # pad so crops near the edge keep the hand centred
    padded = cv2.copyMakeBorder(rgb, pad, pad, pad, pad, cv2.BORDER_CONSTANT)
    patch = padded[y0 + pad:y0 + pad + size, x0 + pad:x0 + pad + size]
    if patch.size == 0:
        return None
    patch = cv2.resize(patch, (256, 256))
    res = image_landmarker.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=patch))
    if not res.hand_landmarks:
        return None
    img_px = [(x0 + lm.x * size, y0 + lm.y * size) for lm in res.hand_landmarks[0]]
    if math.dist(img_px[0], wrist) > 0.6 * size:  # found some other hand in the crop
        return None
    return img_px, res.hand_world_landmarks[0]


# ---------------------------------------------------------------- main

def main():
    args = parse_args()
    ensure_model(HAND_MODEL_PATH, HAND_MODEL_URL)

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

    landmarker = vision.HandLandmarker.create_from_options(vision.HandLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path=str(HAND_MODEL_PATH)),
        running_mode=vision.RunningMode.VIDEO,
        num_hands=2,
        min_hand_detection_confidence=0.4,
        min_hand_presence_confidence=args.min_confidence,
        min_tracking_confidence=args.min_confidence,
    ))
    # Second hand model for zoomed crops (standing back from the camera).
    crop_landmarker = None if args.no_body else vision.HandLandmarker.create_from_options(vision.HandLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path=str(HAND_MODEL_PATH)),
        running_mode=vision.RunningMode.IMAGE,
        num_hands=1,
        min_hand_detection_confidence=0.3,
        min_hand_presence_confidence=args.min_confidence,
    ))
    body_tracker = None if args.no_body else BodyTracker(args)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (args.host, args.port)
    start = time.monotonic()
    last_ts = -1
    prev_depth = {"Left": None, "Right": None}
    # Slow average of each hand's size-based distance. Body wrist distance is steady but barely moves when
    # you push toward the camera; the hand growing in the image does, so we add (size distance - its average).
    size_avg = {"Left": None, "Right": None}
    # Last real sighting per hand: (time, points, velocity per coordinate in m/s).
    last = {"Left": None, "Right": None}
    fps_t, fps_n, fps = time.monotonic(), 0, 0.0

    print(f"Streaming {'hands' if args.no_body else 'body + hands'} to udp://{args.host}:{args.port}  (q or Esc to quit)")
    while True:
        ok, frame = cap.read()
        if not ok:
            print("Webcam frame grab failed; retrying...")
            time.sleep(0.05)
            continue

        # Mirror the image so it behaves like a mirror and MediaPipe's handedness is correct.
        frame = cv2.flip(frame, 1)
        h, w = frame.shape[:2]
        cam = Camera(w, h, args.fov)

        ts = int((time.monotonic() - start) * 1000)
        if ts <= last_ts:
            ts = last_ts + 1
        last_ts = ts
        now = ts / 1000.0

        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        body = body_tracker.track(mp_image, ts, cam) if body_tracker else None
        result = landmarker.detect_for_video(mp_image, ts)

        # Where the body says each wrist is (camera space), to give hands a steady distance.
        body_wrist = {}
        if body:
            for side, j in (("Left", 7), ("Right", 8)):
                if body["vis"][j] > 0.5:
                    body_wrist[side] = body["pts"][j]

        found = {}  # side -> (pixel points, world landmarks, score, zoomed)
        for img_lms, world_lms, handed in zip(result.hand_landmarks, result.hand_world_landmarks, result.handedness):
            side = handed[0].category_name
            if args.swap_hands:
                side = "Left" if side == "Right" else "Right"
            if side in found:  # never send two hands with the same side
                continue
            found[side] = ([(lm.x * w, lm.y * h) for lm in img_lms], world_lms, handed[0].score, False)

        # Standing back: hands are too small for the full-frame model, so zoom in on each body wrist.
        crops = {}
        if body and crop_landmarker:
            for side in ("Left", "Right"):
                if side in found:
                    continue
                crop = hand_crop(body, side, cam)
                if crop is None:
                    continue
                crops[side] = crop
                hit = detect_in_crop(crop_landmarker, rgb, crop)
                if hit:
                    found[side] = (hit[0], hit[1], 0.8, True)

        hands = []
        for side, (img_px, world_lms, score, zoomed) in found.items():
            override = None
            if side in body_wrist:
                sd = size_depth(img_px, world_lms, cam)
                size_avg[side] = sd if size_avg[side] is None else size_avg[side] * 0.95 + sd * 0.05
                push = max(-0.4, min(0.4, sd - size_avg[side]))
                override = body_wrist[side][2] + push
            pts, prev_depth[side] = hand_points(img_px, world_lms, cam, override, prev_depth[side], args.depth_smoothing)
            wire = to_wire(pts)
            vel = [0.0] * len(wire)
            if last[side] is not None and 0 < now - last[side][0] < 0.2:
                dt = now - last[side][0]
                vel = [max(-6.0, min(6.0, (a - b) / dt)) for a, b in zip(wire, last[side][1])]
            last[side] = (now, wire, vel)
            hands.append({"side": side, "score": round(score, 3), "p": wire})

            if not args.no_preview:
                px = [(int(u), int(v)) for u, v in img_px]
                color = (255, 180, 80) if side == "Left" else (60, 150, 255)
                for a, b in BONES:
                    cv2.line(frame, px[a], px[b], color, 2)
                for p in px:
                    cv2.circle(frame, p, 3, (255, 255, 255), -1)
                cv2.putText(frame, f"{side} {prev_depth[side]:.2f}m{' zoom' if zoomed else ''}",
                            (px[0][0] - 30, px[0][1] + 25), cv2.FONT_HERSHEY_SIMPLEX, 0.6, color, 2)

        for side in ("Left", "Right"):
            if side in found or last[side] is None:
                continue
            gap = now - last[side][0]
            if gap <= args.coast:
                # Lost mid-movement (usually motion blur): keep it moving along its last velocity.
                t0, pts, vel = last[side]
                coasted = [round(p + v * gap, 4) for p, v in zip(pts, vel)]
                hands.append({"side": side, "score": 0.0, "coast": True, "p": coasted})
            else:
                prev_depth[side] = None
                size_avg[side] = None
                last[side] = None

        packet = {"t": round(now, 3), "hands": hands}
        if body:
            packet["body"] = {"score": round(sum(body["vis"][:5]) / 5.0, 2), "p": to_wire(body["pts"]), "v": body["vis"]}
        # Always send, even with nothing tracked, so Unity knows the bridge is alive.
        sock.sendto(json.dumps(packet).encode(), target)

        fps_n += 1
        if time.monotonic() - fps_t >= 1.0:
            fps, fps_n, fps_t = fps_n / (time.monotonic() - fps_t), 0, time.monotonic()

        if not args.no_preview:
            if body:
                for a, b in BODY_BONES:
                    if min(body["vis"][a], body["vis"][b]) > 0.5:
                        cv2.line(frame, body["px"][a], body["px"][b], (120, 255, 120), 2)
                for j, label in ((3, "L"), (4, "R"), (7, "L"), (8, "R")):
                    if body["vis"][j] > 0.5:
                        cv2.putText(frame, label, (body["px"][j][0] + 8, body["px"][j][1] - 8),
                                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, (120, 255, 120), 2)
            for x0, y0, size, _ in crops.values():
                cv2.rectangle(frame, (x0, y0), (x0 + size, y0 + size), (200, 200, 200), 1)
            draw_distance_hint(frame, body, args.no_body)
            cv2.putText(frame, f"{fps:.0f} fps -> {args.host}:{args.port}", (10, 25),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
            cv2.imshow("Arcane Strike hand bridge", frame)
            if cv2.waitKey(1) & 0xFF in (ord("q"), 27):
                break

    cap.release()
    cv2.destroyAllWindows()
    landmarker.close()
    if crop_landmarker:
        crop_landmarker.close()


def draw_distance_hint(frame, body, no_body):
    """Tells you whether you're standing at a good distance for body + hand tracking."""
    if no_body:
        return
    if body is None:
        text, color = "Upper body not visible - step back until head, shoulders and hips are in view", (0, 165, 255)
    else:
        d = body["depth"]
        if d < 0.9:
            text, color = f"{d:.1f} m - close: hands work, step back to ~1.5 m for full upper body", (0, 165, 255)
        elif d > 2.3:
            text, color = f"{d:.1f} m - too far, hands may get lost; step closer", (0, 165, 255)
        else:
            text, color = f"{d:.1f} m - good distance", (0, 220, 0)
    cv2.putText(frame, text, (10, 55), cv2.FONT_HERSHEY_SIMPLEX, 0.6, color, 2)


if __name__ == "__main__":
    main()
