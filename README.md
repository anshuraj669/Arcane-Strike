# Arcane Strike

A hands-first, story-driven VR action game for **Meta Quest 3 / 3S**, made for the **Meta VR Start Developer Competition 2026**.

**▶ Demo video (2:54):** https://youtu.be/8B5Auyx1FF8

An ordinary college student cleans out his late grandfather's attic and finds a broken brass lantern. A drop of his blood lights its wick, and a rift tears open and pulls him in. In a temple of blue fire his soul leaves his body and meets **Ember**, a spirit of flame bound to the lantern. Ember tells him he is a **Weaver**, a wielder of *Jyoti*, the living light. An outfit and a sword of light form on his body, and Ember leads him into the world of the **Kalakh**, the shadow creatures, to fight. Then he wakes up at home, and it was not a dream.

You play with your own hands, with no buttons needed. You lift lids, wipe the dust off the lantern, raise your palms to wake the Jyoti Seal, draw the sword from over your shoulder, then slash, parry, duck and dodge.

## What's in the build

### Chapter 1: The Awakening

| Scene | What happens |
|---|---|
| **Attic** (`Attic.unity`) | A midnight walk up to an old house's attic. Clean the boxes, open the shaking chest, find and clean the lantern, cut your finger on it. The blood wakes the lantern and a rift pulls you in. |
| **Ascension Temple** (`AscensionCave.unity`) | Walk up to the bronze cauldron and touch it. Blue fire lifts you, and your soul watches the suit-up while Ember speaks. Optional sword practice with Ember, then a bridge leads into the Kalakh's world. |
| **Monster World** (`MonsterWorld.unity`) | A basalt ledge under a pink moon. 15 Ashhorn soldiers leap in from all sides in waves of 1, 2, 4 and 8, each with its own attacks. Then the Kalakh Warlord arrives. If you fall, you wake in the temple and try again. |

### Chapter 2: Midnight Hours

| Scene | What happens |
|---|---|
| **Attic Wake** (`Chapter2_AtticWake.unity`) | You wake on the attic floor: *"A dream… just a hallucination."* You go downstairs at 23:42. |
| **Bedroom** (`Chapter2_Bedroom.unity`) | Moonlight through the blinds. Ember gathers in the closet corner, the Jyoti Seal burns itself onto your left wrist, and you choose how to answer. Then you touch the Seal and the room shatters into the rift: **QUEST ACCEPTED: FIRST IGNITION**. |

Powers unlock chapter by chapter: Chapter 1's prize is the sword, and Chapter 2 begins the Seal. Play time is about 12–15 minutes. Chapter 2 continues past the bedroom in a later update (`Chapter2_Breach`, not in this build).

## Highlights

- **Hands are the controller.** Gestures drive the story and combat: palms to the face, a fist that compresses heat, drawing the sword over the right shoulder, guard and parry.
- **Fully voiced, with no subtitles.** Ember and the player's inner voice speak every line.
- **Comfort first.** A comfort vignette and smooth fades between scenes. The Ascension is a first-person out-of-body view, never a forced third-person camera.
- **Varied combat.** Sword techniques (slash, thrust, overhead, rising slash, cleave, lunge, whirlwind, charged Jyoti Wave), and soldiers that never share an attack.
- **Original content.** The characters, monsters, temple, outfit and effects are all original, and most are built by Python scripts in Blender (see [Credits](#credits)).

## Requirements

| | Version |
|---|---|
| Unity | **6000.3.27f1** (Unity 6.3 LTS), with **Android Build Support** |
| Render pipeline | URP 17.3 |
| Meta XR SDK | All-in-One **207.0.1** (Interaction SDK, MR Utility Kit) |
| OpenXR | 1.17.1, Meta OpenXR 2.5.0 |
| Git LFS | **Required.** Models, textures and audio are stored with LFS. |
| Headset | Meta Quest 3 / 3S. You can also play in the editor without one (see below). |

## Getting started

```bash
git lfs install
git clone git@github.com:anshuraj669/Arcane-Strike.git
cd Arcane-Strike
git lfs pull            # without this, models are tiny placeholder files and scenes look empty
```

1. Open the folder in Unity Hub with **6000.3.27f1**. The first import takes a while.
2. Run **Arcane Strike → Put Scenes In Story Order**. This sets the build to play Attic → Temple → Monster World → Attic Wake → Bedroom.
3. Open `Assets/_Project/Scenes/Attic.unity` and press **Play**.

The first Play on a new machine shows everything in cyan while Unity compiles the shaders. Wait for it to finish.

### Building for Quest

1. Go to **File → Build Profiles → Android** and choose **Switch Platform**.
2. Connect the headset, with developer mode on.
3. Choose **Build And Run**.

The build targets 60 fps or more on a Quest 3S.

### Playing without a headset

- **Keyboard and mouse:** every story step and move has a key. Click into the Game view, then press **F1** for the full list.
- **Meta XR Simulator (Windows/macOS):** set Left and Right input to **Hand**. Controllers also work, because each one acts as a tracked hand.
- **Webcam bridge (any OS, dev only):** `Tools/hand_bridge` tracks your hands and upper body with MediaPipe and streams them to the editor. See [Tools/hand_bridge/README.md](Tools/hand_bridge/README.md).

| Key | Action |
|---|---|
| W A S D, right mouse button | Walk, look around |
| E | Interact / do the next story step (in the temple: walk to the cauldron and touch it) |
| N | Skip a line or a step |
| P | Raise both palms (hold) |
| 1 / 2 | Choose a dialogue answer |
| Left click | Slash |
| F / Q / B / U / I / V | Thrust / overhead / rising slash / cleave / lunge / whirlwind |
| Left Shift (hold) | Guard / parry |
| R (hold), then click | Charge the blade, then release a Jyoti Wave |
| ← → ↓, Space | Dodge left / right, duck, jump |
| F9 | Destroy every enemy on the field (debug) |
| F10 or End | Win the Monster World fight and go to Chapter 2 (debug, for demos) |

On Linux the editor's menu bar can take F10, so use **End** there. You can also use the menu **Arcane Strike → Demo Video → Win the Monster World Fight**.

### Recording a demo (editor only)

| Key | Action |
|---|---|
| F6 | Record a take of the current scene (F6 again stops it). It is filmed live as well. |
| F7 | Replay the latest take smoothly and film it at 60 fps |
| F8 | Change the camera: first person / over the shoulder / trailer shots |

Videos are saved to `Recordings/Video` (WebM, converted to MP4 when `ffmpeg` is installed). `Tools/video/make_demo.py` cuts the submission video from those captures.

## Project layout

```
Assets/_Project/
  Scenes/       Attic, AscensionCave (the temple), MonsterWorld, Chapter2_AtticWake, Chapter2_Bedroom
  Scripts/
    Input/      hand sources (Quest hands, webcam, scripted hands), gestures, locomotion, keyboard map
    Combat/     sword, arms, spells, Kalakh enemies and their moves
    Story/      Attic, Rift (Ascension, arena), Void (Ember), Chapter2, voice and title cards
    Demo/       take recording, replay and cinematic cameras for the demo video
    Core/       room setup (MRUK), power unlocks per chapter, Quest performance settings
    Editor/     scene and character builders (Arcane Strike menu), video capture, Quest build
  Art/          characters, environments, materials and textures
  Resources/    shaders and data loaded at runtime (fire, Seal, voice lines)
Tools/
  blender/      Python scripts that build the models (run headless: blender -b -P <script>)
  textures/     procedural texture generators
  sky/          the night sky panorama
  tts/          voice line generation
  video/        demo video cutting
  hand_bridge/  webcam hand and body tracking for testing in the editor
Docs/           design document and team setup guides
```

Most environments and characters are made by scripts. To change one:

1. Edit its script in `Tools/blender`.
2. Run the script again.
3. Run the matching **Arcane Strike** menu item in Unity, for example **Apply Attic Model** (which also rebuilds the Weaver), **Create Ascension Temple Scene** or **Create Monster World Scene**.

## Team setup guides

- [Ubuntu (tech laptop) setup](Docs/UBUNTU_TECH_SETUP.md) · [Windows (art laptop) setup](Docs/WINDOWS_ART_SETUP.pdf)
- [Two-laptop setup](Docs/SETUP_GUIDE_TWO_LAPTOPS.md) · [Keeping both laptops in sync](Docs/SYNC_GUIDE.md)

## Credits

- Textures: CC0 materials from [ambientCG](https://ambientcg.com).
- Furniture and props: CC0 models from [Poly Haven](https://polyhaven.com/models).
- Voices: recorded lines, plus lines generated with the open-weight [Kokoro](https://huggingface.co/hexgrad/Kokoro-82M) TTS model (Apache 2.0).
- Everything else (the Weaver and his outfit, Ember, the Kalakh, the temple, Monster World, the effects and the story) was made for this project.

Full asset lists are in `Assets/_Project/Art/Environment/Attic/Textures/CREDITS.txt`.
