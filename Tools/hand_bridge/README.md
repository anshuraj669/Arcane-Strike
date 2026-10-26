# Webcam body + hand bridge (dev only)

Test Arcane Strike's gesture spells with your laptop webcam instead of a Quest.
Python tracks your hands (MediaPipe) and sends 21 joints per hand to Unity over UDP `127.0.0.1:5065`.
Unity's `WebcamHandSource` puts them in front of the player's head, and `GestureRecognizer`
detects spells with the same code that runs on the Quest.

## Run
- Linux/macOS: `./run.sh`
- Windows: `run.bat` (needs Python 3.12)

First run sets up `.venv` and downloads the hand model (~8 MB). A preview window shows the tracked hands.
First run also downloads the pose model (~9 MB). The preview shows your upper body (green), hands, and a distance hint at the top.
Options: `--camera 1` (another webcam), `--fov 70` (camera field of view), `--swap-hands` / `--swap-body` (if left/right are reversed),
`--pose-model lite` (faster on slow laptops), `--no-body --width 640 --height 480` (old hands-only mode), `--no-preview`.

## Where to stand

| Distance from webcam | What works |
|---|---|
| 0.3 – 0.9 m (sitting at the desk) | Hands and gestures only. Press **C** with your hands up. |
| **1.2 – 2.0 m (best: ~1.5 m)** | Upper body + hands (hands are found from your wrists and zoomed in). Head, shoulders and hips in view. Press **C** standing naturally. |
| over 2.3 m | Body works, hands get lost. Step closer. |

Tips: webcam at chest height, light in front of you (not a bright window or tube light behind you), plain clothes with sleeves that contrast with the wall.

## In Unity
1. Press **Play**, then start the bridge (either order works).
2. Hold your hands up at a comfortable casting distance and press **C** to calibrate depth.
3. The top-left overlay shows the active source, finger straightness, palm direction and the last gesture.

| Spell | Gesture | Key |
|---|---|---|
| Firebolt | index + middle out, flick towards the camera | 1 |
| Aegis | open palm facing the camera, hold | 2 (hold) |
| Seal | both hands together, then pull apart sideways | 3 |
| Volt Burst | fist, then burst open | 4 |
| Lumen | open palm up, hand raised (set `lumenHeight` to about -0.1 for webcams) | 5 |
| Sunwheel | both index fingers draw a circle | 6 |
| Grab (Tether) | fist | G (hold) |

Thresholds are on the `GestureRecognizer` component. Webcam depth is approximate,
so do the final tuning on a real Quest.
