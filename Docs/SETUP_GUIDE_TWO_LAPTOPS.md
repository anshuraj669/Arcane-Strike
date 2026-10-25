# ARCANE STRIKE — Two-Laptop Setup Guide

| Laptop | Role | OS | Owns |
|---|---|---|---|
| **💻 Laptop T (Tech)** | Code, networking, MR, builds, Quest deploys | Ubuntu 26.04 · RTX 4060 · 45 GB RAM | `Scripts/`, scenes, project settings, builds |
| **🎨 Laptop A (Art)** | Characters, animation, VFX, UI, audio | Windows or macOS (steps for both) | `Art/`, `VFX/`, `Audio/`, `UI/` art |

Both laptops work on **one shared Unity project** through **Git + GitHub**.

---

## 0. How the Two Laptops Work Together

```
 🎨 LAPTOP A (Art)                    ☁ GitHub (private repo)                 💻 LAPTOP T (Tech)
 ─────────────────                    ───────────────────────                 ──────────────────
 Blender  → .fbx  ┐                                                         ┌→ Unity (scripts, scenes)
 Krita    → .png  ├→ Unity (same      ⇄  ArcaneStrike repo (+ Git LFS)   ⇄ ├→ Build APK
 Mixamo   → anims ┘   version):                                             └→ adb → 🥽 Quest 3
                      imports, materials,
                      animators, visual prefabs

 .blend / .kra source files ──→ ☁ Shared Google Drive folder (NOT in Git)
```

**Golden rule: never edit the same file on both laptops.**

| Folder | Owner | The other laptop… |
|---|---|---|
| `Assets/_Project/Art/**` | 🎨 Art | only reads |
| `Assets/_Project/VFX/**`, `Audio/**`, `UI/Sprites/**` | 🎨 Art | only reads |
| `Assets/_Project/Scripts/**` | 💻 Tech | only reads |
| `Assets/_Project/Scenes/**`, `Prefabs/Gameplay/**` | 💻 Tech | only reads |
| `ProjectSettings/`, `Packages/` | 💻 Tech | **never** changes (ask Tech) |
| `Assets/_Project/Sandbox/Art/` | 🎨 Art | Art's own test scene, free to edit |

**How art reaches gameplay (the prefab hand-off):**
1. Art makes a **visual prefab** (e.g. `CH_Ember_Visual.prefab`): model, materials, Animator. No scripts.
2. Tech makes the **gameplay prefab** (`Ember.prefab`) that *contains* the visual prefab as a nested prefab, and adds scripts and colliders.
3. When Art updates the model or animation, the gameplay prefab updates automatically, with no merge conflicts.

---

## 1. Accounts (do these first, about 30 minutes)

| Account | Who | Why |
|---|---|---|
| **GitHub** | Both | Shared repo. Tech creates it and invites Art. |
| **Unity ID** | Both | Unity Personal license (free) |
| **Meta developer account** + **Meta VR Start Program** | Both (the Devpost submitter must be in Start) | Developer mode, Dashboard, "Competition" release channel |
| **Devpost** | Both | Join the hackathon as a team |
| **Photon** (dashboard.photonengine.com) | Tech | Fusion 2 App ID for Rift Link |
| **Adobe account** (free) | Art | Mixamo auto-rigging and animations |
| **Google Drive** shared folder `ArcaneStrike_Source` | Both | `.blend` / `.kra` source files (too big for Git) |

---

## 2. 💻 Laptop T (Tech, Ubuntu) — Step by Step

Status on this laptop: `git` ✅ · `adb` ✅ · VS Code ✅ · Java ✅ · **missing: `git-lfs`, Unity Hub**. Free disk: about 185 GB (Unity + Android needs about 25 GB).

### 2.1 Base tools
```bash
sudo apt update
sudo apt install -y git-lfs dotnet-sdk-10.0 android-sdk-platform-tools-common   # 26.04 ships .NET 10 (no 8.0)
git lfs install

git config --global user.name  "Your Name"
git config --global user.email "you@example.com"
git config --global init.defaultBranch main
```
`android-sdk-platform-tools-common` adds the udev rules so Linux can see the Quest over USB without root.

### 2.2 Unity Hub (official apt repo)
```bash
wget -qO - https://hub.unity3d.com/linux/keys/public \
  | gpg --dearmor | sudo tee /usr/share/keyrings/Unity_Technologies_ApS.gpg > /dev/null
sudo sh -c 'echo "deb [signed-by=/usr/share/keyrings/Unity_Technologies_ApS.gpg] https://hub.unity3d.com/linux/repos/deb stable main" > /etc/apt/sources.list.d/unityhub.list'
sudo apt update && sudo apt install -y unityhub
```
> ⚠️ Unity officially lists Ubuntu 22.04/24.04. 26.04 usually works. If the Editor fails to start, run it from a terminal and install whatever library the error names.

### 2.3 Unity Editor
1. Open Unity Hub → sign in → **Installs → Install Editor → Unity 6.3 LTS — `6000.3.27f1`** (the project's pinned version).
2. Tick modules: **Android Build Support** → **OpenJDK** + **Android SDK & NDK Tools**.
3. The Art laptop must install **exactly `6000.3.27f1`** too (Hub → Installs → Install Editor → *Archive* if it's no longer listed).

Command-line alternative (what the Tech laptop used):
```bash
unityhub --headless install --version 6000.3.27f1 --module android --childModules
```

Run Unity on the NVIDIA GPU instead of the AMD iGPU:
```bash
__NV_PRIME_RENDER_OFFLOAD=1 __GLX_VENDOR_LIBRARY_NAME=nvidia unityhub
# Optional alias: add to ~/.zshrc
echo 'alias unityhub-nv="__NV_PRIME_RENDER_OFFLOAD=1 __GLX_VENDOR_LIBRARY_NAME=nvidia unityhub"' >> ~/.zshrc
```

### 2.4 Code editor
- **VS Code** (already installed): add the extensions **Unity** (Microsoft) and **C# Dev Kit**.
- Or **JetBrains Rider** (free for non-commercial use), which has stronger Unity support.
- Unity → *Edit → Preferences → External Tools → External Script Editor* → pick the one you use.

### 2.5 Quest 3 developer setup
1. Meta Horizon app on your phone → Devices → your Quest → **Developer Mode ON**. Your Meta account must belong to a developer organization.
2. Connect the Quest by USB-C. Inside the headset, accept **"Allow USB debugging" → Always allow**.
3. Check:
```bash
adb devices          # should print:  <serial>   device
```
   If it says `unauthorized`, put the headset on and accept the prompt. If it says `no permissions`, unplug, then `sudo udevadm control --reload-rules`, and plug back in.
4. Optional, wireless adb:
```bash
adb tcpip 5555
adb connect <quest-ip>:5555     # IP: Quest Settings → Wi-Fi → your network
```

### 2.6 Create the project
Recommended location (the docs move into the repo):
```
~/Desktop/myProjects/HackathonProject/ArcaneStrike/
```
1. Unity Hub → **New project** → template **Universal 3D** → name `ArcaneStrike` → location above.
2. *File → Build Profiles* → **Android** → **Switch Platform**.
3. *Window → Package Manager → Unity Registry* → install **Meta XR All-in-One SDK** (`com.meta.xr.sdk.all`). Restart when asked.
4. *Meta → Tools → Project Setup Tool* → select Android → **Fix All**, then **Apply All**.
5. Fusion 2: download the Fusion 2 SDK from Photon (or the Asset Store) → import → paste your App ID → **Realtime Settings → Fixed Region**.
6. *Project Settings → Editor*: Asset Serialization = **Force Text**, Version Control = **Visible Meta Files** (both are defaults; check them).
7. *Project Settings → Tags and Layers*: add `BeyondPortal`.
8. Player Settings → Android → **Internet Access: Require**.

### 2.7 Folder structure (Tech creates it once and commits)
```
ArcaneStrike/
├─ Assets/
│  └─ _Project/
│     ├─ Art/
│     │  ├─ Characters/   Ember/ Shades/ Morrath/ Bosses/
│     │  │     └─ <Name>/ Models/ Textures/ Materials/ Animations/ Prefabs/
│     │  ├─ Props/        Lantern/ WardStone/ Portal/
│     │  └─ Environment/  Hollow/
│     ├─ VFX/             Spells/ Rifts/ Hits/
│     ├─ Audio/           VO/ SFX/ Music/
│     ├─ UI/              Sprites/ Fonts/
│     ├─ Scripts/         Core/ Input/ Spells/ Enemies/ Bosses/ MR/ Link/ Net/ Narrative/ UI/
│     ├─ Prefabs/         Gameplay/
│     ├─ Data/
│     ├─ Scenes/          Boot.unity  Game.unity
│     └─ Sandbox/         Art/ Tech/        ← personal test scenes
├─ Docs/                  ARCANE_STRIKE_GDD.md  RIFT_LINK_GUIDE.md  this guide
├─ .gitignore
└─ .gitattributes
```

### 2.8 Git files

**`.gitignore`**
```gitignore
# Unity generated
/[Ll]ibrary/
/[Tt]emp/
/[Oo]bj/
/[Bb]uild/
/[Bb]uilds/
/[Ll]ogs/
/[Uu]ser[Ss]ettings/
/[Mm]emoryCaptures/
/[Rr]ecordings/

# IDE
.vs/
.vscode/
.idea/
*.csproj
*.sln
*.suo
*.user
*.pidb
*.booproj

# Builds / OS
*.apk
*.aab
*.unitypackage
.DS_Store
Thumbs.db

# Art source files live in Google Drive, not Git
*.blend
*.blend1
*.kra
*.psd
```

**`.gitattributes`**
```gitattributes
# Unity YAML — always LF, smart merge
*.unity    merge=unityyamlmerge eol=lf
*.prefab   merge=unityyamlmerge eol=lf
*.asset    merge=unityyamlmerge eol=lf
*.mat      merge=unityyamlmerge eol=lf
*.anim     merge=unityyamlmerge eol=lf
*.controller merge=unityyamlmerge eol=lf
*.meta     eol=lf
*.cs       text eol=lf
*.shader   text eol=lf

# Large binaries → Git LFS
*.fbx  filter=lfs diff=lfs merge=lfs -text
*.png  filter=lfs diff=lfs merge=lfs -text
*.jpg  filter=lfs diff=lfs merge=lfs -text
*.tga  filter=lfs diff=lfs merge=lfs -text
*.exr  filter=lfs diff=lfs merge=lfs -text
*.wav  filter=lfs diff=lfs merge=lfs -text
*.ogg  filter=lfs diff=lfs merge=lfs -text
*.mp3  filter=lfs diff=lfs merge=lfs -text
*.ttf  filter=lfs diff=lfs merge=lfs -text
*.otf  filter=lfs diff=lfs merge=lfs -text
*.mp4  filter=lfs diff=lfs merge=lfs -text
```

> ⚠️ **GitHub LFS quota:** free accounts get a limited LFS storage and bandwidth allowance (check GitHub's current limits). Keep exported textures at 1024 px or below, and keep `.blend`/`.kra` sources in Google Drive. If you hit the limit, buy an LFS data pack or switch to Unity Version Control.

**Unity Smart Merge** (run once on each laptop; put your exact version in the path):
```bash
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver \
  "$HOME/Unity/Hub/Editor/<VERSION>/Editor/Data/Tools/UnityYAMLMerge merge -p %O %B %A %A"
git config --global merge.unityyamlmerge.recursive binary
```

### 2.9 Create the repo and invite Art
```bash
cd ~/Desktop/myProjects/HackathonProject/ArcaneStrike
mkdir -p Docs && cp "../Meta VR development/"*.md Docs/
git init
git add .gitignore .gitattributes
git commit -m "Add Unity gitignore and LFS attributes"
git add .
git commit -m "Initial Unity 6 + Meta XR project"
# Create an EMPTY private repo on github.com named ArcaneStrike, then:
git remote add origin git@github.com:<you>/ArcaneStrike.git
git push -u origin main
```
GitHub → repo → **Settings → Collaborators → Add** the Art person.

### 2.10 Prove the pipeline works: "Hello Passthrough"
1. Open `Scenes/Game.unity`. *Meta → Tools → Building Blocks* → add **Camera Rig**, **Passthrough**, **Hand Tracking**.
2. Add a cube 1 m in front of the camera.
3. *File → Build Profiles → Android → Build* → `Builds/ArcaneStrike.apk`
```bash
adb install -r Builds/ArcaneStrike.apk
adb logcat -s Unity            # watch logs while you play
```
4. In the headset: *Library → Unknown Sources → ArcaneStrike*. **You should see your room and a cube, and your real hands tracked.** ✅ Tech setup done.

---

## 3. 🎨 Laptop A (Art) — Step by Step

### 3.1 Install
| Tool | Purpose | Notes |
|---|---|---|
| **Git + Git LFS** | Sync with the repo | Windows: git-scm.com (LFS included). macOS: `brew install git git-lfs`. Then run `git lfs install`. |
| **GitHub Desktop** | Easy commit/pull/push GUI | Recommended for the artist |
| **Unity Hub** + **exact same Unity version** as Tech | Import art, build materials, Animators, VFX, visual prefabs | Add module **Android Build Support** (needed to open an Android-target project without re-importing everything) |
| **Blender 4.x LTS** | Modeling, rigging, animation | blender.org |
| **Krita** (or GIMP) | Textures, UI art, concept art | Free |
| **Inkscape** | Spell icons, wrist HUD icons (SVG → PNG) | Free |
| **Mixamo** (website) | Auto-rig and animate humanoid Shades/Morrath | Export FBX for Unity |
| **Audacity** | Voice lines (Ember/Vey/Morrath), SFX cleanup | Free |
| Optional: **Meta Quest Link** + **Meta Quest Developer Hub** | Preview art in the headset straight from Unity | **Windows only.** Worth it if Art is on Windows. |

### 3.2 Get the project
1. Accept the GitHub invite → **GitHub Desktop → Clone** `ArcaneStrike`.
2. Unity Hub → **Add project from disk** → pick the cloned folder → open with **the same version**.
3. The first open takes 10–30 minutes (Library import). That's normal.
4. Run the Smart Merge `git config` lines from §2.8 (Windows path: `C:\Program Files\Unity\Hub\Editor\<VERSION>\Editor\Data\Tools\UnityYAMLMerge.exe`).

### 3.3 Art sandbox
- Work in `Assets/_Project/Sandbox/Art/ArtSandbox.unity`. It's your scene, and Tech never opens it.
- Put a Camera Rig and a grey "room" there to judge scale (1 Unity unit = 1 meter).

---

## 4. 🎨 Art Pipeline Specs (Quest 3 friendly)

### 4.1 Budgets
| Asset | Triangles | Textures | Materials |
|---|---|---|---|
| **Ember** (hero, always on screen) | ≤ 4k | 1 × 1024 | 1 |
| Shade (Wisp, Crawler, Phantom…) | 1.5k – 4k | 512 – 1024 | 1 |
| Brute / Hollow Knight | ≤ 8k | 1024 | 1–2 |
| Boss (Gnasher, Gatekeeper…) | ≤ 15k | 2 × 1024 | 2 |
| Morrath | ≤ 15k | 2 × 1024 | 2 |
| Props (Lantern, Ward Stone) | ≤ 1.5k | 512 | 1 |
| **Whole screen at once** | **≤ 150k** | — | **≤ 100 draw calls** |

### 4.2 Style rules (from the GDD)
- Stylised, low-poly, **glowing**. It must read clearly over any messy real room.
- Palette: Spark gold `#FFC94A` · Hollow violet `#6B3FD9` · Shade black-blue `#0E1630` · Volt cyan `#4FE3FF`.
- Shades: dark soft silhouettes with **bright white eyes**.
- **Glow = emissive color + an additive sprite halo.** Avoid full-screen Bloom, which is expensive on Quest.
- Prefer **URP Simple Lit / Unlit**. Avoid large transparent surfaces (overdraw kills Quest frame rate).

### 4.3 Blender → Unity export (FBX)
| Setting | Value |
|---|---|
| Scene units | Metric, Unit Scale **1.0** (1 BU = 1 m) |
| Before export | `Ctrl+A → All Transforms` on meshes and the armature |
| Export → Include | Selected Objects, **Mesh + Armature** |
| Transform | Scale **1.0**, **Apply Scalings: FBX Units Scale**, Forward **−Z Forward**, Up **Y Up**, ✅ **Apply Transform** |
| Armature | ❌ Add Leaf Bones |
| Animation | ✅ Bake Animation, one action per clip (or one FBX per clip) |

In Unity (model import settings):
- **Ember**: Rig = *Generic* (floating flame, no legs).
- **Shades / Morrath** from Mixamo: Rig = *Humanoid*, so Mixamo clips can be shared between them.
- Materials: *Extract Materials* → move them into `Materials/` → switch to URP Simple Lit.

### 4.4 Naming
```
CH_Ember.fbx            character mesh + rig
ANIM_Ember_Idle.fbx     animation clip
T_Ember_BaseColor.png   T_Ember_Emissive.png   (textures)
M_Ember.mat             material
AC_Ember.controller     Animator Controller
CH_Ember_Visual.prefab  visual prefab (Art's hand-off to Tech)
VFX_Firebolt.prefab     SFX_Firebolt_Cast.wav   VO_Ember_Prologue_01.wav
```

### 4.5 Art list by priority (matches the GDD build weeks)
| Week | Asset | Animations / states |
|---|---|---|
| **1** (Oct 22–26) | **Ember** | Idle-float, Talk, Happy, Scared, Fly-around |
| 1 | **Wisp** + **Dart Wisp** | Float, Attack, Hit, Death (dissolve into motes) |
| 1 | **Rift** (wall crack + portal) | Open, Idle, Seal (VFX) |
| 1 | **Lantern** prop | Lid-open |
| 2 | **Spell VFX**: Firebolt, Aegis shield, Volt Burst, Seal threads | — |
| 2 | **Crawler** | Crawl, Peek, Leap, Death |
| 2 | **Phantom** | Fade-in/out, Drift, Death |
| 2 | **Spark hand glow** material + wrist HUD icons | — |
| 3 | **Ward Stone** (3 level variants) | Glow pulse, Crack, Mend |
| 3 | **Spitter**, **Brute**, **Mites** | Move, Attack, Death |
| 3 | **Bosses**: Gnasher, Weeping Veil, Morrath's Hand | Attack patterns, Death |
| 3 | **Warden Vey** hologram | Idle, Talk |
| 3 | **Rift Link**: partner Spark hands + lantern-mask head, gold Link portal | Pinch blend, Wave |
| 4 | **Morrath, the Hollow King** | Float, Summon, Kneel, Dissolve |
| 4 | Hollow backdrop (portal view), UI polish, end card + logo | — |

Animator rule: Art builds the **Animator Controller** with clear parameter names (`Speed`, `Attack`, `Hit`, `Die`, `Talk`) and writes them into the hand-off message. Tech's scripts only set those parameters.

### 4.6 Licensed sources only (contest rule §17–19)
Kenney (CC0), Quaternius (CC0), Mixamo, Poly Haven (CC0), Freesound (check each license), Unity Asset Store (check license). **No** brands, logos or real people. Keep `Docs/CREDITS.md` with every external asset + license.

---

## 5. Daily Workflow

### 5.1 Every work session (both laptops)
```
1. Close Unity
2. Pull       (GitHub Desktop: "Fetch → Pull"  |  terminal: git pull)
3. Open Unity, work ONLY in the folders you own
4. Save scene/project (Ctrl+S)
5. Close Unity (or File → Save Project)
6. Commit small + clear message:  "Art: Ember idle + talk animations"
7. Pull again → Push
8. Tell the other person what changed (chat message, see 5.3)
```

### 5.2 Branches (keep it simple for a 4-week hackathon)
- `main` = always opens and builds.
- Art works on `art/<thing>`, Tech on `dev/<thing>`. Merge to `main` with a GitHub Pull Request **at least once a day**.
- Tech builds the APK only from `main`.

### 5.3 Hand-off message template (Art → Tech)
```
✅ Ready: CH_Ember_Visual.prefab  (Art/Characters/Ember/Prefabs)
Animator params: Talk (bool), Happy (trigger), Scared (trigger)
Pivot: center of flame, facing +Z
Tris: 3.2k · Texture: 1024 · Material: M_Ember (Simple Lit + emissive)
Notes: eyes are separate child "Eyes" so you can make them look at the player
```

### 5.4 If you get a merge conflict
- **`.prefab` / `.unity` / `.mat`**: Smart Merge (§2.8) already runs automatically during `git pull`. If a conflict is still left, keep the owner's version: `git checkout --theirs <file>` or `--ours`, then `git add <file>`.
- **`.fbx` / `.png`** (LFS binaries): they can't be merged, so pick one version. This is exactly why the folder-ownership rule exists.
- **`ProjectSettings/`**: always take Tech's version.

---

## 6. Previewing in the Headset

| Who | How |
|---|---|
| 💻 Tech (Linux) | Build APK → `adb install -r` (about 2–5 min per loop) |
| 🎨 Art on **Windows** | Quest Link cable/Air Link → press **Play** in Unity → see art in the headset instantly. You need Meta Horizon Link app + Meta XR Simulator optional. |
| 🎨 Art on **macOS** | No Quest Link. Either build an APK and sideload with MQDH (Mac version exists), or ask Tech for a daily build. |

Share **one Quest** by doing daily "headset reviews": Tech installs the latest `main` build, and both people check art and gameplay together.

---

## 7. Day-1 Checklists

### 💻 Laptop T (Tech)
- ☐ `git-lfs`, `dotnet-sdk-8.0`, udev rules installed (§2.1)
- ☐ Unity Hub + Unity 6 LTS + Android modules; **version written down** (§2.3)
- ☐ Unity runs on the NVIDIA GPU (§2.3)
- ☐ VS Code extensions or Rider set as the external editor (§2.4)
- ☐ `adb devices` shows the Quest as `device` (§2.5)
- ☐ Project created, Meta XR SDK + Project Setup Tool "Fix All" (§2.6)
- ☐ Fusion 2 imported, App ID + Fixed Region (§2.6)
- ☐ Folder structure, `.gitignore`, `.gitattributes`, Smart Merge config (§2.7–2.8)
- ☐ Repo pushed, Art invited (§2.9)
- ☐ **Hello Passthrough** runs on the Quest (§2.10)

### 🎨 Laptop A (Art)
- ☐ Git + LFS + GitHub Desktop (§3.1)
- ☐ Unity Hub + **exact same Unity version** + Android module (§3.1)
- ☐ Blender, Krita, Inkscape, Audacity installed; Mixamo login works
- ☐ Repo cloned, project opens without errors (§3.2)
- ☐ Smart Merge config (§3.2)
- ☐ Art sandbox scene with a scale reference (§3.3)
- ☐ Google Drive `ArcaneStrike_Source` folder synced
- ☐ **Test hand-off:** export a cube from Blender as `CH_Test.fbx` → import → `CH_Test_Visual.prefab` → commit → push → Tech pulls and sees it in the Quest build ✅

When the last item passes on both laptops, the setup is done.

---

## 8. Troubleshooting
| Problem | Fix |
|---|---|
| Project opens with pink materials on one laptop | Different Unity/URP version. Both must match `ProjectSettings/ProjectVersion.txt`. |
| Images show as tiny text files after pulling | Git LFS not installed on that laptop → `git lfs install && git lfs pull` |
| `.blend` errors on the Tech laptop | A `.blend` got into `Assets/`. Unity needs Blender to import it. Export FBX instead and move the `.blend` to Google Drive. |
| Prefab changes "disappear" after a pull | Both edited the same prefab. Use the nested visual-prefab hand-off (§0). |
| `adb devices` empty on Ubuntu | Different USB-C cable/port (must be a data cable), re-accept USB debugging in the headset, reload udev rules |
| Unity is slow on Ubuntu | Not running on the NVIDIA GPU (§2.3). Also check the Library folder is on the NVMe drive. |
| Pushing fails: LFS quota exceeded | Shrink textures, move sources to Drive, or buy an LFS data pack |
