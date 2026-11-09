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

## Head tracking

After calibrating (press **C**, or wait a second with your upper body in view), the game head follows yours:
- **Lean or duck** and the view moves with you (dodges count).
- **Turn your head** left or right and the view turns. The turn is multiplied (about 2.5×) so your eyes can stay on the screen.
- **Nod** up or down and the view tilts. In the Meta XR Simulator, tilting stays on the Simulator's mouse look.

Small head movements (under 4°) are ignored, so the view holds still while you watch the screen. Press **C** again to make the way you're facing now straight ahead. The settings (gain, dead zone, smoothing) are on the `WebcamHeadDriver` component. It adds itself in the editor to every scene that has the bridge.

In the Meta XR Simulator (Windows), turn the Simulator's own **Webcam tracking** off, because only one program can use the webcam. The bridge then supplies both your hands and your head.

## In Unity
1. Press **Play**, then start the bridge (either order works).
2. Hold your hands up at a comfortable casting distance and press **C** to calibrate depth.
3. The top-left overlay shows the active source, finger straightness, palm direction and the last gesture.

| Spell | Gesture | Key |
|---|---|---|
| Firebolt | index + middle out, point them at the camera for a moment (or flick towards it) | 1 |
| Aegis | fist with the palm side towards you, raise it, then bring it down; the shield stays while you hold the fist | 2 (hold) |
| Seal | both hands together, then pull apart sideways | 3 |
| Volt Burst | fist, then burst open | 4 |
| Lumen | open palm up, hand raised (set `lumenHeight` to about -0.1 for webcams) | 5 |
| Sunwheel | both index fingers draw a circle | 6 |
| Grab (Tether) | fist | G (hold) |
| Spark Blade | fist at your hip, draw it up fast like a sword from its sheath; the sword stays while you hold the fist, swing it to hit | 7 (hold) |
| Skyhammer | both fists together, raise them to head height, slam them down; shockwave on the floor | 8 |

Thresholds are on the `GestureRecognizer` component. Webcam depth is approximate,
so do the final tuning on a real Quest.
