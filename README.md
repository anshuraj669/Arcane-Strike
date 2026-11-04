# Arcane Strike

A hands-first VR action game for **Meta Quest 3 / 3S**, made for the Meta VR Start Developer Competition 2026.

An ordinary student cleans out an old attic and finds their grandfather's brass lantern. When their blood touches it, a rift tears open. In a temple of blue fire, their soul leaves their body and meets **Ember**, a spirit of flame. Ember tells them they are a **Weaver**, and a warrior's armour and a sword of light form on their body. Then Ember leads them into the world of the **Kalakh**, the shadow creatures, to fight.

You play with your own hands, no controller buttons needed. You lift lids, rub the dust off the lantern, raise your palms to wake the Jyoti Seal, draw the sword from over your shoulder, and parry, duck and dodge.

## Chapter 1: The Awakening

| Scene | What happens |
|---|---|
| **Attic** (`Assets/_Project/Scenes/Attic.unity`) | A walk at midnight up to an old house's attic. Open the shaking chest, find and clean the lantern, cut your finger on it. The blood wakes the lantern and a rift pulls you in. |
| **Ascension Temple** (`AscensionCave.unity`) | Touch the bronze cauldron. Blue fire lifts you, and your soul watches the suit-up while Ember speaks. Optional sword practice with Ember (14 lessons), then a bridge leads into the Kalakh's world. |
| **Monster World** (`MonsterWorld.unity`) | A basalt ledge under a pink moon. 15 Ashhorn soldiers come in waves of 1, 2, 4 and 8, each with its own attacks, then the Kalakh Warlord. If you fall, you wake in the temple and try again. |

Play time is about 8–10 minutes. Powers unlock chapter by chapter, and Chapter 1's prize is the sword.

## Requirements

| | Version |
|---|---|
| Unity | **6000.3.27f1** (Unity 6.3 LTS), with **Android Build Support** |
| Render pipeline | URP 17.3 |
| Meta XR SDK | All-in-One **207.0.1** (Interaction SDK, MR Utility Kit) |
| OpenXR | 1.17.1, Meta OpenXR 2.5.0 |
| Git LFS | Required: models, textures and audio are stored with LFS |
| Headset | Meta Quest 3 / 3S. On Windows you can use the **Meta XR Simulator** instead. |

## Getting started

```bash
git lfs install
git clone git@github.com:anshuraj669/Arcane-Strike.git
cd Arcane-Strike
git lfs pull            # without this, models are tiny placeholder files and scenes look empty
```

1. Open the folder in Unity Hub with **6000.3.27f1**. The first import takes a while.
2. Run **Arcane Strike → Put Scenes In Story Order** so the build plays Attic → Temple → Monster World.
3. Open `Assets/_Project/Scenes/Attic.unity` and press **Play**.

The first Play on a new machine shows everything in cyan while Unity compiles the shaders. Wait for it to finish.

### Testing without a headset

- **Meta XR Simulator (Windows/macOS):** set Left and Right input to **Hand**, and switch on **Webcam tracking → Enable hand tracking** to play with your real hands. Controllers also work: the scenes use controller-driven hand poses, so a controller acts as a tracked hand.
- **Webcam bridge (any OS, dev only):** `Tools/hand_bridge` tracks your hands and upper body with MediaPipe and streams them to the editor. See [Tools/hand_bridge/README.md](Tools/hand_bridge/README.md).
- **Keyboard:** every story step and move has a key. Click into the Game view, then press **F1** to see the full list.

| Key | Action |
|---|---|
| W A S D, right mouse button | Walk, look around |
| E | Interact / do the next story step |
| N | Skip a line or a step |
| P | Raise both palms (hold) |
| Left click | Slash |
| F / Q / B / U / I / V | Thrust / overhead / rising slash / cleave / lunge / whirlwind |
| Left Shift (hold) | Guard / parry |
| R (hold), then click | Charge the blade, then release a Jyoti Wave |
| ← → ↓, Space | Dodge left / right, duck, jump |
| F9 / F10 | Kill every enemy / jump to the ending (debug) |

### Building for Quest

**File → Build Profiles → Android**, then **Switch Platform**, connect the headset (with developer mode on) and choose **Build And Run**. Check that the game stays at 60 fps or more on a Quest 3S.

## Project layout

```
Assets/_Project/
  Scenes/       Attic, AscensionCave (the temple), MonsterWorld
  Scripts/
    Input/      hand sources (Quest hands, webcam bridge), gestures, dodges, locomotion, keyboard map
    Combat/     sword, arms and gauntlets, spells, Kalakh enemies and their moves
    Story/      Attic, Cave (temple), Rift (Ascension, arena), World (the hunt), Ember, comfort vignette
    Core/       room setup (MRUK), power unlocks per chapter, platform fixes
    Editor/     scene and character builders (Arcane Strike menu)
  Art/          characters, environments, materials and textures
  Resources/    shaders and data loaded at runtime (fire, Ember's voice lines)
Tools/
  blender/      Python scripts that build the models (run headless: blender -b -P <script>)
  textures/     scripts that generate the procedural textures
  sky/          the night sky panorama
  hand_bridge/  webcam hand and body tracking for testing in the editor
Docs/           setup guides for both laptops
```

Most environments and characters are made by scripts. To change one, edit its script in `Tools/`, run it again, then run the matching **Arcane Strike** menu item in Unity (for example **Apply Attic Model**, **Create Ascension Temple Scene** or **Create Monster World Scene**).

## Team setup guides

- [Ubuntu (tech laptop) setup](Docs/UBUNTU_TECH_SETUP.md) · [Windows (art laptop) setup](Docs/WINDOWS_ART_SETUP.pdf)
- [Two-laptop setup](Docs/SETUP_GUIDE_TWO_LAPTOPS.md) · [Keeping both laptops in sync](Docs/SYNC_GUIDE.md)

## Credits

- Textures: CC0 materials from [ambientCG](https://ambientcg.com).
- Furniture and props: CC0 models from [Poly Haven](https://polyhaven.com/models).
- Everything else (characters, monsters, temple, effects) was made for this project.

Full lists are in `Assets/_Project/Art/Environment/Attic/Textures/CREDITS.txt`.
