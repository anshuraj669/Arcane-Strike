# Arcane Strike — Ubuntu Tech Laptop Setup (step by step)

Machine: Ubuntu 26.04 · RTX 4060 · user `ben10` · Unity **6000.3.27f1**
Project: `~/Desktop/myProjects/HackathonProject/ArcaneStrike`

Do the stages **in order**. Each stage ends with a ✅ check — don't move on until it passes.

---

## Stage 0 — Read this first (Ubuntu limits)

Meta officially supports **Windows and macOS** as development computers. On Ubuntu:

| Works on Ubuntu ✅ | Does NOT work on Ubuntu ❌ |
|---|---|
| Unity Editor 6.3 + Android build support | Meta XR Simulator (Windows / Apple-Silicon Mac only) |
| Meta XR All-in-One SDK (v207) in the editor | Meta Horizon Link (stream to headset — Windows only) |
| Building the APK | Meta Quest Developer Hub (MQDH — Windows / Mac only) |
| `adb` install to a real headset over USB | |
| Git / GitHub / Photon Fusion | |

**What this means for you:** this laptop can **build the whole game**. For testing **without a headset**,
use the Meta XR Simulator on the **art laptop** if it runs Windows (or a Windows dual-boot later).
The rules allow the demo video to be recorded **in the XR Simulator**, so no headset is strictly needed to record it.

---

## Stage 1 — Already done on this laptop (verify only)

Run these in a terminal (Ctrl+Alt+T):

```bash
git --version && git lfs version
dotnet --version            # 10.x
adb version | head -1
nvidia-smi --query-gpu=name --format=csv,noheader
ls ~/Unity/Hub/Editor/       # must show 6000.3.27f1
ls ~/Unity/Hub/Editor/6000.3.27f1/Editor/Data/PlaybackEngines/   # must show AndroidPlayer
```

✅ All commands print a version, the editor folder exists, and `AndroidPlayer` is listed.

---

## Stage 2 — Accounts (do in a browser, ~20 min)

1. **Meta developer account**: go to <https://developers.meta.com/horizon/> → Log in with your Meta account.
   - Create an **Organization** (Developer Dashboard → *Create Organization*), e.g. `ArcaneStrike Studio`.
   - Meta may ask you to **verify** the account (phone / 2-factor). Do it — Developer Mode needs it.
   - Your teammate (artist) also needs a Meta account with Developer Access. Invite them to the org:
     *Dashboard → your org → Members → Add*.
2. **Start Program membership check**: log in at <https://communityforums.atmeta.com/category/horizon-developer-forum>.
   You should see the **"Start Member"** flair next to your name (appears ~24 h after you applied).
   No flair after 24 h → file a ticket at <https://developers.meta.com/vr/support/>.
3. **Devpost**: open <https://start-developer-competition-26.devpost.com/> → click **Join hackathon**.
   You are the **Team Representative** (you submit). Add your teammate to the project later on Devpost.
4. **Create the app in the Developer Dashboard**: *Dashboard → Create New App* → name **Arcane Strike** →
   platform **Meta Quest / Horizon Store**. Note the **App ID** (you need it in Stage 11).
5. **Photon account** (free): <https://dashboard.photonengine.com> → *Create a New App* →
   type **Fusion**, SDK **Fusion 2**, name `ArcaneStrike`. Copy the **App ID**.

✅ You have: Meta org, Start flair (or ticket filed), Devpost joined, Meta App ID, Photon Fusion App ID.

---

## Stage 3 — Open the project in Unity (on the NVIDIA GPU)

1. Open a **new** terminal (so the alias loads) and run:
   ```bash
   unityhub-nv
   ```
   Always start Unity this way — it forces the RTX 4060 instead of the weak AMD iGPU.
2. In Unity Hub → **Projects → Add → Add project from disk** → choose
   `~/Desktop/myProjects/HackathonProject/ArcaneStrike` → open it.
3. First open takes 5–10 min (it builds the `Library` folder). Wait.
4. Check the GPU: *Help → About Unity* is not enough — instead, while Unity is open, run in a terminal:
   ```bash
   nvidia-smi | grep -i unity
   ```

✅ Unity opens with no red errors in the Console, and `nvidia-smi` lists a `Unity` process.

---

## Stage 4 — Install the Meta XR All-in-One SDK (v207)

1. *Window → Package Management → Package Manager*.
2. Click **+** (top-left) → **Install package by name…**
3. Name: `com.meta.xr.sdk.all` · Version: `207.0.1` → **Install**.
4. Unity will show pop-ups:
   - "Restart editor?" → **Yes / Restart**.
   - If it asks which XR backend → choose **OpenXR** (the old Oculus XR plugin is deprecated).
   - "Enable new Input System backends?" → **Yes** (it restarts again).
5. Wait for compiling to finish (spinner bottom-right stops).

✅ Package Manager → *In Project* shows **Meta XR All-in-One SDK 207.0.1**, and the menu **Meta** appears in the top bar.

---

## Stage 5 — XR Plug-in Management (OpenXR)

1. *Edit → Project Settings → XR Plug-in Management*.
2. **Android tab** (robot icon): tick **OpenXR**.
3. Click **OpenXR** in the left list (under XR Plug-in Management) → **Android tab**:
   - In **OpenXR Feature Groups**, tick **Meta XR**.
   - Feature list: make sure **Meta XR Feature** / **Meta Quest Support** are ticked.
4. **Standalone (desktop) tab**: also tick **OpenXR** → Feature Group **Meta XR** (lets Play Mode load Meta components).

✅ No yellow/red warning icons next to OpenXR on the Android tab.

---

## Stage 6 — Project Setup Tool (auto-fixes Quest settings)

1. *Meta → Tools → Project Setup Tool* (also at *Edit → Project Settings → Meta XR*).
2. **Android tab** → **Fix All** under *Outstanding Issues*.
3. Then **Apply All** under *Recommended Items* (read each one; all are safe for us).
4. Repeat on the **Standalone tab**.
5. Repeat steps 2–4 until both tabs show **0 outstanding issues**.

✅ Project Setup Tool: Android = 0 issues, Standalone = 0 issues.

---

## Stage 7 — Player Settings (check what the tool set)

*Edit → Project Settings → Player → Android tab*:

| Setting | Value |
|---|---|
| Company Name | `ArcaneStrikeStudio` (no brand names) |
| Product Name | `Arcane Strike` |
| Package Name (Other Settings → Identification) | `com.arcanestrikestudio.arcanestrike` |
| Version / Bundle Version Code | `0.1.0` / `1` (increase the code on every upload) |
| Color Space | **Linear** |
| Graphics APIs | **Vulkan** only (remove OpenGLES3 if listed) |
| Scripting Backend | **IL2CPP** |
| Target Architectures | **ARM64** only |
| Minimum / Target API Level | leave what the Project Setup Tool chose |
| Texture compression (Build Profile) | **ASTC** |
| Internet Access | **Require** (needed for Rift Link / Photon) |

✅ All rows match.

---

## Stage 8 — Quest features (hands, passthrough, scene, anchors)

1. Create a working scene: *File → New Scene → Basic (URP)* → save as
   `Assets/_Project/Sandbox/Tech/Sandbox_Tech.unity`.
2. Delete **Main Camera**.
3. *Meta → Tools → Building Blocks* and drag in, in this order:
   - **Camera Rig**
   - **Passthrough**
   - **Hand Tracking** (and **Real Hands** if offered)
   - **MR Utility Kit** (Scene / Room model)
4. Select **[BuildingBlock] Camera Rig** → **OVRManager** component → *Quest Features*:
   - Hand Tracking Support: **Hands Only** (contest = hands-first; no controller needed ever)
   - Passthrough Support: **Required**
   - Scene Support: **Required**
   - Anchor Support: **Enabled**
   - (Depth API) Environment Depth / Boundary-less: enable if listed
   - *Permission Requests On Startup*: tick **Scene**
5. Add a test **Cube** at position (0, 1.2, 0.6), scale 0.2.
6. *File → Save*.

✅ Project Setup Tool still shows 0 issues after adding the blocks (re-run Fix All if not).

---

## Stage 9 — Project layers & tags (for Rift Link)

*Edit → Project Settings → Tags and Layers*:
- Layer 8: `BeyondPortal`
- Layer 9: `Spell`
- Layer 10: `Enemy`
- Tags: `WardStone`, `Rift`

✅ Layers visible in the Layer dropdown of any object.

---

## Stage 10 — Photon Fusion 2

1. Download **Fusion 2 SDK** (`.unitypackage`) from <https://doc.photonengine.com/fusion/current/getting-started/sdk-download>
   (log in with the Photon account from Stage 2).
2. In Unity: *Assets → Import Package → Custom Package…* → pick the file → **Import** everything.
3. The **Fusion Hub** window opens → paste your **Fusion App ID** → Save.
4. *Tools → Fusion → Realtime Settings* (or the `PhotonAppSettings` asset) → **Fixed Region** = `in` (India) or `asia`.

✅ No compile errors; `PhotonAppSettings` shows your App ID.

---

## Stage 11 — First APK build (proves the whole pipeline)

1. *File → Build Profiles* → select **Android** → **Switch Platform** if not active.
2. **Open Scene List** → add `Sandbox_Tech` (tick it, remove others).
3. Click **Build** → save to `~/Desktop/myProjects/HackathonProject/ArcaneStrike/Builds/ArcaneStrike_0.1.0.apk`
   (`Builds/` is ignored by Git — good).
4. First build takes 5–15 min (IL2CPP compiling). Watch the Console.
5. Verify in a terminal:
   ```bash
   ls -lh ~/Desktop/myProjects/HackathonProject/ArcaneStrike/Builds/*.apk
   ```

✅ An `.apk` file exists (roughly 40–80 MB). This means your laptop is fully set up to make Quest builds.

---

## Stage 12 — Test without a headset (on Ubuntu)

Since the XR Simulator does not run on Linux, test game logic in **Play Mode**:

1. **No Meta runtime on Linux:** Meta's native plugins (OVRPlugin, Meta XR Audio) have no Linux build, so
   `OVRManager`, **MRUK**, Environment Depth and Meta XR Audio throw `DllNotFoundException` every frame in Play Mode.
   `Assets/_Project/Scripts/Core/LinuxEditorMetaGuard.cs` switches them off in the Linux editor only.
   Test rooms/rifts/passthrough in the **Meta XR Simulator on the Windows laptop**; on Ubuntu, test logic with
   a plain grey-box room (a few cubes as walls).
2. **Fake movement:** add a simple fly-camera script (WASD + right-mouse look) on the rig for editor only
   (`#if UNITY_EDITOR`). The `DebugRig` from `RIFT_LINK_GUIDE.md` already does this for Rift Link.
3. **Fake spells:** add editor-only keyboard shortcuts (e.g. `1` = Firebolt, `2` = Aegis) that call the same
   spell functions the hand gestures call. Gesture tuning happens later on a real headset or XR Simulator.
4. **Two-player test:** run one Play Mode client + one Linux standalone build (`Build Profiles → Linux`) both joining
   the same Fusion session.

✅ Pressing ▶ gives no red DllNotFoundException spam and you can fly around the grey-box room.

---

## Stage 13 — When you get a headset (borrowed / rented)

1. Install the **Meta Horizon** app on your phone → log in with the **same Meta account** → pair the headset.
2. Phone app → *Devices → Headset settings → Developer Mode* → **ON** → reboot headset.
3. Plug the headset into this laptop with a **data** USB-C cable.
4. In the headset: accept **"Allow USB debugging?"** → tick *Always allow*.
5. Terminal:
   ```bash
   adb devices          # must show your device as "device" (not "unauthorized")
   adb install -r ~/Desktop/myProjects/HackathonProject/ArcaneStrike/Builds/ArcaneStrike_0.1.0.apk
   ```
6. In the headset: *Library → filter "Unknown Sources"* → open **Arcane Strike**.
7. Or in Unity: *Build Profiles → Run Device = your Quest* → **Build And Run**.
8. Logs while playing: `adb logcat -s Unity`
9. Performance check: must hold **60 fps minimum** (judges' rule). Use the *OVR Metrics Tool* overlay.

---

## Stage 14 — Upload to the "Competition" release channel (needed to submit)

1. Developer Dashboard → your app → *Distribution → Release Channels* → **Create** a channel named exactly
   **`Competition`**.
2. Upload the APK through the Dashboard's web upload (*Builds → Upload*) — works from any OS.
   (Alternatives: the `ovr-platform-util` command-line tool, or MQDH on the art laptop if it runs Windows/Mac.)
3. In the Competition channel → **Create Invite URL** → copy it. This link goes in the Devpost form.
   (Judges need the link — a raw APK file is NOT accepted.)

---

## Stage 15 — Save your work to Git

```bash
cd ~/Desktop/myProjects/HackathonProject/ArcaneStrike
git checkout -b dev/meta-sdk-setup
git add -A
git commit -m "Add Meta XR SDK 207, OpenXR, Fusion 2 and tech sandbox scene"
git push -u origin dev/meta-sdk-setup     # after the GitHub repo exists (see SYNC_GUIDE.md)
```
Then open a Pull Request into `main` and merge it, so the art laptop gets the same packages.

---

## Contest compliance checklist (from the official site, checked Oct 24 2026)

- [ ] Deadline: **Nov 18, 2026, 12:00 PM PT** (= Nov 19, 00:30 IST). No changes allowed after that until winners (~Dec 11).
- [ ] Track **Gaming**, Division **New Experience** (all code started after Sep 24, 2026 ✔).
- [ ] **Hands-first:** the whole game playable without ever pairing a controller.
- [ ] Seated, 2-ft radius; a satisfying moment in ≤10 min; fast start, clean pause/resume.
- [ ] Judged on **Quest 3 / 3S**; **≥60 fps**.
- [ ] Judging = 4 × 25%: Innovation & Creativity · Experience Design · Technical Implementation · Polish & Presentation.
- [ ] Passthrough must be **purposeful** (your real room changes the game) and MRUK must work in **rooms you never tested**.
- [ ] Bonus points for platform features: hand tracking, passthrough, scene (MRUK), spatial anchors, **gaze** (ISDK v207+), FoV-aware UI (keep UI near the centre).
- [ ] Submit: **Competition release channel invite URL** + **video < 3 min** (public YouTube/Vimeo; headset **or XR Simulator** footage; real gameplay, not AI video) + Devpost form (name, 140-char tagline, track, division, ~500-word description, target launch date, team names/emails/roles).
- [ ] App free and available until winners are announced.
- [ ] Everything in English; no brands/logos, no real people, only assets you own or have a license for.
- [ ] Meta gives **no hardware** — arrange your own headset.
- [ ] Special award to aim for: **Best Social & Multiplayer Experience ($25k)** — that's Rift Link. Also **Best First Five Minutes**.
