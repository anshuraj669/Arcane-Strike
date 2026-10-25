# ARCANE STRIKE — Syncing the Two Laptops

How the Tech laptop (Ubuntu) and the Art laptop (Windows or macOS) share one Unity project.

```
 🎨 ART LAPTOP                         ☁ GITHUB                          💻 TECH LAPTOP (Ubuntu)
 GitHub Desktop  ── push / pull ──▶  anshuraj669/ArcaneStrike  ◀── push / pull ──  terminal (git)
 Unity 6000.3.27f1                    (private, Git LFS)                      Unity 6000.3.27f1
        │                                                                          │
        └──────────── .blend / .kra source files ─▶ ☁ Google Drive ◀───────────────┘
```

| What | Where it syncs |
|---|---|
| Unity project (scripts, scenes, prefabs, materials, exported `.fbx`/`.png`/`.wav`) | **GitHub** repo |
| Art source files (`.blend`, `.kra`, `.psd`, reference images) | **Google Drive** folder `ArcaneStrike_Source` |
| Test builds (`.apk`) | Not synced. Tech installs them on the Quest. |

---

## Part A — 💻 Tech Laptop

### A1. Already done ✅
| Step | Status |
|---|---|
| Git, Git LFS 3.7.1, .NET 10, Unity Hub 3.22.4 | ✅ installed |
| Unity **6000.3.27f1** + Android Build Support | ✅ installed |
| Unity Personal license | ✅ active |
| Project created at `~/Desktop/myProjects/HackathonProject/ArcaneStrike` (Universal 3D, Android) | ✅ |
| Folder structure `Assets/_Project/...` + folder `.meta` files | ✅ |
| `.gitignore`, `.gitattributes` (LFS + Unity Smart Merge) | ✅ |
| Unity Smart Merge driver in global git config | ✅ |
| Local repo with first commits | ✅ |
| SSH key works with GitHub (account **anshuraj669**) | ✅ |

### A2. Create the GitHub repo (you, about 2 minutes)
1. Go to **github.com/new** (logged in as `anshuraj669`).
2. Repository name: **`ArcaneStrike`** · Visibility: **Private**.
3. ⚠️ **Do NOT** tick "Add a README", ".gitignore" or "license". The repo must be **empty**.
4. Click **Create repository**.

### A3. Push the project
```bash
cd ~/Desktop/myProjects/HackathonProject/ArcaneStrike
git remote add origin git@github.com:anshuraj669/ArcaneStrike.git
git push -u origin main
```
Refresh the GitHub page. You should see `Assets/`, `Docs/`, `Packages/`, `ProjectSettings/`.

### A4. Invite the artist
GitHub → `ArcaneStrike` → **Settings → Collaborators → Add people** → the artist's GitHub username → they accept the email invite.

### A5. Protect `main` (recommended)
Settings → **Branches → Add branch protection rule** (or *Rulesets*) → branch `main` → ✅ *Require a pull request before merging*. Nobody can break `main` by accident; all work goes through branches.

---

## Part B — 🎨 Art Laptop

### B1. Install (pick your OS)

| Tool | Windows | macOS |
|---|---|---|
| **Git + Git LFS** | git-scm.com → installer (LFS included). Keep the defaults. | Install Homebrew (brew.sh) → `brew install git git-lfs` |
| **GitHub Desktop** | desktop.github.com | desktop.github.com |
| **Unity Hub** | unity.com/download | unity.com/download |
| **Google Drive for desktop** | google.com/drive/download | google.com/drive/download |

Then open a terminal (**Windows:** *Git Bash* · **macOS:** *Terminal*) and run:
```bash
git lfs install
git config --global user.name  "Artist Name"
git config --global user.email "artist@example.com"
```

### B2. Install the SAME Unity version
1. Unity Hub → sign in → Preferences → **Licenses → Add → Get a free personal license**.
2. **Installs → Install Editor**. Find **6000.3.27f1**; if it isn't listed, use the **Archive** tab or unity.com/releases/editor/archive.
3. Modules: ✅ **Android Build Support** (+ OpenJDK, Android SDK & NDK Tools).
   *Why: the project's target is Android. Without the module, Unity can't open it as-is and would re-import everything.*

> ⚠️ **Never open the project with any other Unity version**, and never click "upgrade project". If Unity Hub warns about a version mismatch, stop and install 6000.3.27f1.

### B3. Unity Smart Merge (one command)
**Windows (Git Bash):**
```bash
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver "'C:/Program Files/Unity/Hub/Editor/6000.3.27f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %B %A %A"
git config --global merge.unityyamlmerge.recursive binary
```
**macOS (Terminal):**
```bash
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver "'/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/Tools/UnityYAMLMerge' merge -p %O %B %A %A"
git config --global merge.unityyamlmerge.recursive binary
```
If Unity Hub installed somewhere else, adjust the path (Unity Hub → Installs → ⋮ → *Show in Explorer/Finder*).

### B4. Clone the project
1. Accept the GitHub invite email.
2. **GitHub Desktop → File → Options/Settings → Accounts → Sign in** to GitHub.
3. **File → Clone repository → `anshuraj669/ArcaneStrike`** → choose a local path **without spaces or OneDrive/iCloud** (e.g. `C:\Dev\ArcaneStrike` or `~/Dev/ArcaneStrike`) → **Clone**.
   ⚠️ Never put the project inside a synced cloud folder (OneDrive, iCloud, Google Drive). They corrupt Unity's files.
4. Unity Hub → **Projects → Add → Add project from disk** → pick the cloned folder.
5. Open it. The **first open takes 10–30 minutes** while Unity builds the `Library/` folder. That's normal; let it finish.

### B5. Check that LFS worked
In Git Bash / Terminal, inside the project folder:
```bash
git lfs ls-files     # lists LFS files (images, fbx…) — fine if empty at the start
git lfs pull         # downloads any LFS files that came down as pointers
```
If an image in Unity shows as a broken/tiny text file, LFS wasn't installed before cloning. Run `git lfs install && git lfs pull`.

---

## Part C — ☁ Google Drive for Art Source Files

1. On Google Drive, create a folder **`ArcaneStrike_Source`** and **share it** with the other person (Editor access).
2. Structure:
   ```
   ArcaneStrike_Source/
   ├─ Characters/  Ember/  Shades/  Morrath/  Bosses/      ← .blend files
   ├─ Textures/    (.kra / .psd working files)
   ├─ Concept/     (sketches, references, mood boards)
   ├─ Audio_Raw/   (raw voice recordings)
   └─ Video/       (demo video footage)
   ```
3. **Art laptop:** Google Drive for desktop syncs it automatically.
4. **Tech laptop (Ubuntu):** there's no official Drive app. Use drive.google.com in the browser, or *Settings → Online Accounts → Google* (GNOME) to browse it in the Files app.

**Rule:** source files (`.blend`, `.kra`) → Drive. **Exported** files (`.fbx`, `.png`, `.wav`) → Unity project → GitHub. The repo's `.gitignore` already blocks `.blend`/`.kra`/`.psd`, so they can't be committed by accident.

---

## Part D — The Daily Sync Loop

### D1. Branch rules
| Branch | Who | Purpose |
|---|---|---|
| `main` | Nobody directly | Always opens and builds. Only updated through Pull Requests. |
| `art/<thing>` | 🎨 Art | e.g. `art/ember-model`, `art/firebolt-vfx` |
| `dev/<thing>` | 💻 Tech | e.g. `dev/gestures`, `dev/rift-link` |

Merge your branch into `main` with a Pull Request **at least once a day**, so the two laptops never drift far apart.

### D2. 🎨 Art laptop: each session (GitHub Desktop)
```
START
 1. Close Unity
 2. GitHub Desktop: Current branch → main → "Fetch origin" → "Pull origin"
 3. Current branch → New branch → art/<thing>   (or switch to your existing art branch
    and use Branch → "Update from main")
 4. Open Unity → work ONLY in Art/, VFX/, Audio/, UI/Sprites/, Sandbox/Art/
END
 5. Unity: File → Save (Ctrl/Cmd+S) → File → Save Project → close Unity
 6. GitHub Desktop: check the changed files list (only your folders!)
    → write a summary like "Art: Ember idle + talk animations" → "Commit to art/<thing>"
 7. "Push origin"
 8. "Create Pull Request" → on GitHub: Create → Merge pull request (or ask Tech to review)
 9. Send the hand-off message (template in D5)
```

### D3. 💻 Tech laptop: each session (terminal)
```bash
cd ~/Desktop/myProjects/HackathonProject/ArcaneStrike

# START — close Unity first
git switch main
git pull                              # get the artist's merged work
git switch -c dev/<thing>             # new task branch
#   …or, for an existing branch:  git switch dev/<thing> && git merge main

# … work in Unity: Scripts/, Scenes/, Prefabs/Gameplay/, ProjectSettings/ …

# END — save in Unity (Ctrl+S, File → Save Project), close Unity
git status                            # only your folders should appear
git add -A
git commit -m "Dev: firebolt gesture + projectile"
git push -u origin dev/<thing>
# GitHub → "Compare & pull request" → Merge
```

### D4. Folder ownership (the rule that prevents conflicts)
| Folder | Owner | Other laptop |
|---|---|---|
| `Assets/_Project/Art/`, `VFX/`, `Audio/`, `UI/Sprites/`, `UI/Fonts/` | 🎨 Art | read only |
| `Assets/_Project/Sandbox/Art/` | 🎨 Art | never opens |
| `Assets/_Project/Scripts/`, `Scenes/`, `Prefabs/Gameplay/`, `Data/` | 💻 Tech | read only |
| `Assets/_Project/Sandbox/Tech/` | 💻 Tech | never opens |
| `ProjectSettings/`, `Packages/` | 💻 Tech | **never commit changes**. In GitHub Desktop, right-click → *Discard changes* if they appear. |
| `Docs/` | Both | edit different files, or tell each other first |

**Hand-off pattern:** Art builds `CH_Ember_Visual.prefab` (model + material + Animator) in `Art/…/Prefabs/`. Tech's `Prefabs/Gameplay/Ember.prefab` *contains* it as a nested prefab and adds the scripts. Each person edits only their own prefab.

### D5. Hand-off message (Art → Tech, in your chat)
```
✅ Merged to main: CH_Ember_Visual.prefab  (Art/Characters/Ember/Prefabs)
Animator params: Talk (bool), Happy (trigger), Scared (trigger)
Pivot: flame center, facing +Z · Tris 3.2k · Texture 1024 · Material M_Ember
Notes: eyes are a separate child "Eyes" for look-at
```

### D6. Adding a package or changing settings
Only Tech changes `Packages/manifest.json` or `ProjectSettings/`. After Tech merges such a change:
- Art pulls `main`, opens Unity, and waits for the package import.
- If Art needs a package (e.g. a VFX tool), ask Tech to add it.

---

## Part E — First Sync Test (do this once, both directions)

**Test 1: Tech → Art**
1. Tech: after A3, the project is on GitHub.
2. Art: clone + open (B4). ✅ The project opens with no errors and no "upgrade" prompt.

**Test 2: Art → Tech**
1. Art: new branch `art/sync-test`. In Unity, create `Art/Props/Lantern/T_SyncTest.png` (any image) and a cube prefab `Art/Props/Lantern/Prefabs/SyncTest_Visual.prefab`.
2. Commit → Push → Pull Request → Merge.
3. Tech: `git switch main && git pull`, then:
   ```bash
   git lfs ls-files          # T_SyncTest.png must be listed here (stored in LFS)
   ```
4. Tech opens Unity. ✅ The image and prefab show up and look correct.

**Test 3: Tech → Art again**
1. Tech: branch `dev/sync-test` → create `Scripts/Core/SyncTest.cs` → commit → push → PR → merge.
2. Art: pull `main` → open Unity. ✅ The script appears and compiles with no errors.

**Clean up:** Art deletes the test assets in a new branch → PR → merge. Both pull.

When all three tests pass, **the two laptops are fully synced.** 🎉

---

## Part F — Problems & Fixes

| Problem | Cause | Fix |
|---|---|---|
| Unity asks to **upgrade** the project | Wrong Unity version | Cancel. Install **6000.3.27f1** (check `ProjectSettings/ProjectVersion.txt`). |
| Pink/magenta materials on one laptop | Unity/URP version mismatch or interrupted import | Same version on both; *Assets → Reimport All* |
| Images are tiny text files / textures missing | Git LFS not installed before cloning | `git lfs install && git lfs pull` |
| `ProjectSettings/*.asset` keeps showing as changed on the Art laptop | Unity rewrites them on open | Discard them (only Tech commits settings) |
| Merge conflict in a `.prefab` / `.unity` | Both edited the same file | Smart Merge usually resolves it. If not: keep the **owner's** version → `git checkout --theirs <file>` (or `--ours`) → `git add <file>` → commit |
| Merge conflict in `.png` / `.fbx` | Binary files can't merge | Keep the owner's version (same commands). This is why ownership matters. |
| "Missing script" on a prefab | A `.cs` file was committed without its `.meta`, or vice versa | Always commit `.meta` files together with their asset |
| Push rejected: "fetch first" | The branch on GitHub has newer commits | `git pull` (or GitHub Desktop *Pull*), then push again |
| Push fails: LFS quota exceeded | GitHub's free LFS storage/bandwidth is used up | Keep textures ≤ 1024 px; keep sources on Drive; delete unused test assets; check GitHub → Settings → Billing |
| `.blend` error on the Tech laptop | A `.blend` ended up in `Assets/` | Move it to Drive; export `.fbx` instead |
| GitHub Desktop shows thousands of changes | `Library/` or `Temp/` not ignored | Make sure the project's root `.gitignore` is present and you opened the right folder |

### Golden rules
1. **Pull before you start, push when you stop.**
2. **Close Unity before pulling or switching branches.**
3. **Same Unity version: 6000.3.27f1.**
4. **Stay in your own folders.**
5. **Commit `.meta` files with their assets.**
6. **Source files → Drive. Exports → Git.**
