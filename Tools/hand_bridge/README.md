# Webcam hand bridge (dev only)

Test Arcane Strike's gesture spells with your laptop webcam instead of a Quest.
Python tracks your hands (MediaPipe) and sends 21 joints per hand to Unity over UDP `127.0.0.1:5065`.
Unity's `WebcamHandSource` puts them in front of the player's head, and `GestureRecognizer`
detects spells with the same code that runs on the Quest.

## Run
- Linux/macOS: `./run.sh`
- Windows: `run.bat` (needs Python 3.12)

First run sets up `.venv` and downloads the hand model (~8 MB). A preview window shows the tracked hands.
Options: `--camera 1` (another webcam), `--fov 70` (camera field of view), `--swap-hands` (if left/right are reversed), `--no-preview`.

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
