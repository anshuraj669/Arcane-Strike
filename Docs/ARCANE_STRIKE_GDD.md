# ARCANE STRIKE — Game Design Document & Full Script

> **Pitch:** Portals tear open in the walls of your real room. Shadow creatures crawl through. You have no wand and no gun — your bare hands are the weapon. Cast fire, lightning and shields with real gestures and defend your home in 8-minute battles.

| Item | Value |
|---|---|
| Contest | Meta VR Start Developer Competition 2026 |
| Track | **Gaming** |
| Division | New Experience |
| Platform | Meta Quest 3 / 3S (Mixed Reality) |
| Input | Hands only (controllers optional, never required) |
| Play posture | Seated, every interaction within ~2 ft |
| Session length | 6–9 minutes per chapter |
| Engine | Unity 6 + Meta XR SDK (All-in-One) |
| Age rating target | 13+ (fantasy violence, no blood) |
| Deadline | **November 18, 2026, 12:00 PM PT** |

---

## 1. How Arcane Strike Fits the Judging Criteria

| Rule / Criterion | How we meet it |
|---|---|
| **Hands-first** (required) | Every spell, menu and action is a hand gesture. No controller is ever needed. |
| **Seated / airplane-seat test** | Player never moves. Enemies come *to* you. Shield, cast, and dodge-lean all fit in a 2 ft radius. |
| **One bus-stop test** | One chapter = 3 waves + boss ≈ 8 minutes. Instant resume from the exact wave. |
| **Passthrough must matter** | Portals spawn on *your* real walls (Scene API). Enemies hide behind *your* real sofa (Depth API occlusion). Every room = a different battlefield. Turning passthrough off would remove the core idea. |
| **Original, not a wrapper** | 100% our own game, art, story and code. No third-party service. |
| **Innovation (25%)** | Gesture-spell combat + room-aware portals + persistent "Ward Stones" anchored in your home. |
| **Experience Design (25%)** | 60-second onboarding, clear spell icons, comfort-first seated design, a daily "Rift Report" habit loop. |
| **Repeat usage** | Daily rift, persistent home wards that level up, unlockable spells, high-score rank. |

---

## 2. Story & World

### 2.1 The World
Beneath ordinary life exists **The Veil** — an invisible membrane separating our world from **The Hollow**, a dead dimension of shadow. For centuries, a secret order called the **Lanternbearers** kept the Veil sealed. Their magic needed no wands: it was drawn from the human hand itself — "the oldest tool, the first spell."

Twelve years ago the last Lanternbearer stronghold fell. The Veil has been thinning ever since. Now it is tearing — not in castles or forests, but in **ordinary homes**: living rooms, bedrooms, dorms. Wherever people live, the Hollow pushes through.

### 2.2 The Player — "The Spark"
You are an ordinary person who discovers you carry the **Spark** — the dormant gift of the Lanternbearers. You are not chosen by prophecy. You are simply the only one awake when the rift opens in your room.

### 2.3 Characters

| Character | Role | Personality |
|---|---|---|
| **Ember** | Your mentor. A small floating flame-spirit — the last surviving familiar of the Lanternbearers. Lives in a lantern on your desk. | Witty, warm, sarcastic, secretly terrified of losing another student. |
| **Warden Isolde Vey** | Voice of the last Lanternbearer, preserved inside an echo-crystal. Appears as a blue hologram. | Calm, stern, sacrificial. |
| **The Hollow King (Morrath)** | Main villain. A former Lanternbearer who opened the Veil to bring back his lost family — and was consumed. | Cold, grieving, believes he is saving the world. |
| **Shades** | Enemy creatures — fragments of people lost to the Hollow. | Mindless, hungry, afraid of light. |

### 2.4 Theme
*Light is not a weapon you are given — it's something you choose to hold.*
The story is about protecting your home, and about grief: Morrath tore the Veil because he couldn't let go. The Spark wins not by being stronger, but by sealing the wound.

### 2.5 Enemy Origin
Shades are made from **lost memories** pulled into the Hollow. When defeated, they dissolve into small floating light motes — "freed memories" — which the player collects as currency (**Motes**).

---

## 3. FULL GAME SCRIPT

**Format notes:**
- `[MR]` = mixed-reality event using the player's real room
- `[GESTURE]` = the player must perform this gesture to continue
- `EMBER:` = voiced line, spatial audio from the lantern's position
- All dialogue has English subtitles (contest requirement)
- Each chapter = Intro (≤45 s) → 3 Waves → Boss/Finale → Outro (≤30 s)

---

### PROLOGUE — "The Crack in the Wall" (Tutorial, ~4 min)

**[MR] Passthrough on. The player sees their real room. Silence. A low hum.**

*A hairline crack of violet light appears on the player's nearest real wall. It slowly widens.*

*A small antique lantern materializes on the player's real desk/table surface with a soft "clink".*

**EMBER** *(muffled, from inside the lantern):* Hello? ...Hello?! Oh, thank the stars — someone's awake. Open the lantern. Quickly!

**[GESTURE: Pinch the lantern lid and pull up]**

*The lid pops. A tiny flame-spirit with two bright eyes bursts out and circles the player's head.*

**EMBER:** Ahh, air! Real air! Twelve years in a lamp, you've no idea... Right. Introductions. I'm Ember. And you — *(peers at the player's hands)* — oh. Oh my. You have it.

**EMBER:** Look at your hands. Go on — hold them up.

**[GESTURE: Raise both palms toward face]**

*Faint golden veins of light trace the player's real hand skeleton (hand-tracking overlay).*

**EMBER:** That glow is the Spark. Lanternbearer magic. Haven't seen it in a living hand since... well. Since before.

*The violet crack on the wall SPLITS with a sharp tearing sound. Cold blue mist pours out.*

**EMBER:** And THAT is why your timing is excellent. That's a rift. Something's coming through it. Small, I hope. Please be small.

*A single tiny Shade — a wisp with glowing white eyes — crawls out of the real wall and drifts slowly toward the player.*

**EMBER:** Small! Good. Lesson one. Point at it like you're aiming. Two fingers. Then flick — like flicking water off your fingertips.

**[GESTURE: FIREBOLT — point index + middle finger, flick wrist forward]**

*A bolt of fire streaks from the player's fingertips. The wisp bursts into golden motes.*

**EMBER:** HA! First try! Well, technically second if we count my demonstration in 1847, but who's counting.

*The motes drift to the player's hand and absorb with a chime.*

**EMBER:** Those are Motes — memories the Hollow stole. You just set one free. Keep them; they'll make you stronger.

*Two more wisps emerge. One flies fast and fires a shadow dart.*

**EMBER:** Incoming! Shield! Open your palm toward it — flat, like you're saying "stop!"

**[GESTURE: AEGIS — open palm facing forward, hold]**

*A hexagonal golden shield appears in front of the player's hand. The dart shatters against it.*

**EMBER:** Beautiful. Shield blocks, fire kills. That's ninety percent of magic, honestly. The other ten is not panicking.

**[Player defeats the remaining wisps with Firebolt.]**

*The rift flickers, unstable.*

**EMBER:** Now we close it. Put your hands together — then pull them apart slowly, like stretching a seal over the hole.

**[GESTURE: SEAL — both hands together, then pull apart horizontally; aim at rift]**

*Golden threads stitch the rift shut. It collapses with a deep "thoom". The room is quiet. A faint golden scar remains on the wall.*

**EMBER:** ...You did it. You sealed a rift. On your first night.

*(Pause. Ember's flame dims slightly.)*

**EMBER:** That's not normal, by the way. Rifts don't open in houses. Not unless something on the other side is pushing very, very hard.

*A blue hologram flickers to life above the lantern — a tall woman in a hooded cloak.*

**WARDEN VEY** *(echoing, ancient):* If this message plays, the Veil has begun to fail and a new Spark has woken. I am Isolde Vey, last Warden of the Lanternbearers. Listen carefully. The one who broke the Veil will return. He will open doors where you live. You must hold your home. Ember will teach you. Trust the light in your hands.

*The hologram fades.*

**EMBER:** *(quietly)* She recorded that the night the Citadel fell. Never thought anyone would hear it.

**EMBER:** *(brightening)* Right! You need rest. I need a snack. Do you have any candles? No? Tragic. Come back tomorrow — the rifts will.

**[MR] Ember places a glowing Ward Stone on the real surface where the rift was sealed. It stays there (Spatial Anchor).**

**SCREEN TEXT:** *PROLOGUE COMPLETE — Ward Stone placed. Your home is protected... for now.*

---

### CHAPTER 1 — "Uninvited Guests" (~7 min)

**INTRO**

*[MR] Ember is floating near the lantern. The Ward Stone from last session glows on the player's real surface.*

**EMBER:** You came back! I had a bet with myself. I lost, which means I also won. Anyway. Bad news: three rifts tonight.

*Three violet cracks appear on three different real walls (Scene API picks the walls).*

**EMBER:** They come from different directions now. Use your eyes — look at a rift and I'll mark it. You don't have to turn your whole body, just glance. Shades hate being watched.

**[MECHANIC INTRODUCED: GAZE TARGETING — look at an enemy for 0.3 s to lock-on; spells auto-curve toward the locked target]**

**WAVE 1** — 8 Wisps from two walls.
**EMBER** *(mid-wave):* Left side! No, your other left. Good!

**WAVE 2** — Wisps + 2 **Crawlers** (ground enemies that move along the real floor and hide behind real furniture).
**EMBER:** That one's hiding behind your — is that a couch? It's behind the couch! It thinks it's clever. Wait for it to peek.

*[MR] Depth API occlusion: the Crawler is genuinely hidden by the player's real furniture until it moves out.*

**WAVE 3** — Mixed swarm, 14 enemies, all three rifts active.
**EMBER:** This is the part where I'd normally hide in my lamp. I'm staying. Mostly.

**BOSS — "The Gnasher"** *(a large Crawler with a bone-armor shell)*
**EMBER:** Its shell's too thick for fire. Lightning cracks shells! Make a fist, then open it fast toward it — like throwing a flash bang!

**[NEW SPELL UNLOCKED: VOLT BURST — make a fist, then explode fingers open toward target]**

*Volt Burst shatters the shell. Firebolts finish it.*

**OUTRO**

**EMBER:** You used lightning. On your second day. The Lanternbearers needed a month for that.

*A whisper echoes from the last closing rift — a deep male voice.*

**MORRATH** *(whisper, from all directions):* ...another Spark... how bright... how brief...

**EMBER:** *(flame flares red)* ...No. No no no. That voice. That's not possible.

**EMBER:** Seal it. Seal it NOW.

**[GESTURE: SEAL]**

**SCREEN TEXT:** *CHAPTER 1 COMPLETE — New spell: Volt Burst. +1 Ward Stone.*

---

### CHAPTER 2 — "The Name in the Dark" (~7 min)

**INTRO**

**EMBER:** I owe you an explanation. That voice last night — his name is Morrath. He was a Lanternbearer. The best of us. Warden Vey's own apprentice.

**EMBER:** His wife and daughter were taken by a Shade swarm. He tried to pull them back out of the Hollow. He tore the Veil open to do it. And the Hollow... took him instead.

**EMBER:** He's not a man anymore. He's a door. And he's knocking on yours.

*The room's real lights seem to dim (passthrough darkened via shader). Frost creeps over the edges of the player's view.*

**EMBER:** He's making it cold. Shades love cold. We need light. Raise one hand above your head and open it — like you're holding up a lantern.

**[NEW SPELL UNLOCKED: LUMEN — raise hand above head, open palm upward. Brightens the room, reveals invisible Phantoms for 6 seconds]**

**WAVE 1** — Invisible **Phantoms** that only appear under Lumen.
**EMBER:** You can't see them? Good, that means they're Phantoms. Light reveals!

**WAVE 2** — Phantoms + Wisps. The player must alternate Lumen and Firebolt.

**WAVE 3** — **Spitters**: enemies that cling to the real ceiling (Scene API ceiling plane) and spit shadow bile.
**EMBER:** Up! On your ceiling! I'm never sleeping in this house again. I don't sleep. But still!

**BOSS — "The Weeping Veil"** *(a floating figure made of shadow fabric that splits into 4 copies)*
**EMBER:** Only one of them is real! Lumen — the real one casts a shadow!

*[MR] Under Lumen, only the real boss casts a shadow on the player's real wall.*

**OUTRO**

*The boss dissolves. Among the motes, one glows differently — it forms a brief image: a little girl laughing.*

**EMBER:** *(softly)* That was a memory. One of his. His daughter, I think.

**EMBER:** He's not just sending monsters. He's sending pieces of himself.

**SCREEN TEXT:** *CHAPTER 2 COMPLETE — New spell: Lumen. Memory Fragment 1/5 collected.*

---

### CHAPTER 3 — "Hold the Line" (~8 min)

**INTRO**

**EMBER:** Your Ward Stones are working. Look — they glow when a rift tries to open near them.

*[MR] The persistent Ward Stones from previous sessions glow on the player's real surfaces.*

**EMBER:** Tonight he's going to try to break them. If a Ward falls, its rift opens wider. Protect them.

**[MECHANIC INTRODUCED: WARD DEFENSE — enemies target Ward Stones; each Ward has HP shown as a ring]**

**[NEW SPELL UNLOCKED: TETHER — grab gesture (close hand) on an enemy, then pull back toward you to yank it out of the air; throw it into others]**

**EMBER:** Grab one! Close your hand on it and pull it to you. Then throw! Shades make wonderful bowling balls.

**WAVE 1** — Wisps rush the Wards.
**WAVE 2** — **Brutes**: slow tanky enemies that must be Tethered off the Ward.
**WAVE 3** — Everything at once, with 3 Wards to protect.

**EMBER** *(mid-wave):* Left Ward is cracking! I'll patch it — no, I won't, I'm a candle — YOU patch it!

**[GESTURE: Hold palm over a Ward for 2 s to restore it — costs 20 Motes]**

**BOSS — "Morrath's Hand"** *(a giant shadow hand that reaches out of a rift and tries to crush a Ward)*

**MORRATH** *(through the rift):* You protect walls. Stones. A *room*. I wanted to protect *everything*.

**EMBER:** Don't listen. Shield the Ward, then hit the palm when it opens!

*Combo: AEGIS to block the slam → VOLT BURST into the open palm → FIREBOLT barrage.*

**OUTRO**

**MORRATH** *(retreating):* Every wall you build... I have built before. They all fall.

**EMBER:** He's scared. You're actually scaring him.

**SCREEN TEXT:** *CHAPTER 3 COMPLETE — New spell: Tether. Memory Fragment 2/5.*

---

### CHAPTER 4 — "The Warden's Echo" (~7 min)

**INTRO**

*The Warden Vey hologram reappears, now flickering and damaged.*

**WARDEN VEY:** If you are hearing this second echo, the Spark has survived three nights. Then you are ready to learn the last spell I taught Morrath — the one I hoped would save him.

**WARDEN VEY:** Two hands. Draw a circle in the air together. Light will remember the shape.

**[NEW SPELL UNLOCKED: SUNWHEEL — both hands draw a circle together; summons a rotating ring of light that orbits the player for 10 s, damaging anything close. Ultimate — charges by collecting 100 Motes]**

**EMBER:** *(quietly)* She taught him that the night before he fell.

**WAVE 1** — **Mirror Shades**: they reflect Firebolt. The player must use Volt Burst or Tether.
**WAVE 2** — Swarm of 30 tiny **Mites**. Ideal for Sunwheel.
**EMBER:** That's a LOT of them. This is what the circle is for!

**WAVE 3** — Rifts open *in the floor* (Scene API floor plane). Enemies rise around the player's real chair.

**BOSS — "The Echo"** — a shadow copy of Warden Vey, twisted by Morrath.

**THE ECHO:** *(Vey's voice, distorted)* Give up the Spark, child. Light only shows you what you've lost.

**EMBER:** *(furious)* THAT is not her. Burn it.

**OUTRO**

*As The Echo dissolves, the real Vey hologram flickers one last time.*

**WARDEN VEY:** Morrath... if any part of you hears this... I'm sorry I couldn't hold you. *(to the player)* Close his door, Spark. Not with hatred. With light.

*The hologram fades permanently. Ember is silent for a long moment.*

**EMBER:** ...Right. Tomorrow we go to him.

**SCREEN TEXT:** *CHAPTER 4 COMPLETE — Ultimate unlocked: Sunwheel. Memory Fragment 3/5.*

---

### CHAPTER 5 — "Through the Wall" (~8 min)

**INTRO**

**EMBER:** He's pouring everything through one rift tonight. The big one. It'll open on your largest wall.

*[MR] The largest wall detected by the Scene API cracks open into a huge portal. Through it, the player sees The Hollow — a black-and-violet wasteland with a distant spire. The rest of the room stays real.*

**EMBER:** That's the Hollow. Don't stare too long. It stares back.

**WAVE 1** — Enemies flood from the giant portal; player defends from the chair.
**WAVE 2** — **Hollow Knights**: armored elite Shades with shields that face the player. Tether pulls their shield away, then Firebolt.
**WAVE 3** — All enemy types. Wards under attack. Sunwheel available.

**BOSS — "The Gatekeeper"** — a colossal creature whose head and arms reach *through* the portal into the player's room.

*[MR] The Gatekeeper's hands grip the edges of the player's real wall portal. Depth occlusion makes them wrap around real furniture.*

**EMBER:** Its hands! Hit the hands holding the portal open!

*Each hand destroyed shrinks the portal. Final phase: player casts SEAL with the Gatekeeper half-through.*

**OUTRO**

*The portal collapses to a pinhole. Through it, a voice — no longer menacing, just tired.*

**MORRATH:** ...You remind me of me. Before.

**EMBER:** He's at the spire. One more night. He'll open the door himself.

**SCREEN TEXT:** *CHAPTER 5 COMPLETE — Memory Fragment 4/5.*

---

### CHAPTER 6 — "The Hollow King" (FINALE, ~9 min)

**INTRO**

*[MR] Every Ward Stone in the player's home glows at once. Ember floats in front of the player, flame steady.*

**EMBER:** Whatever happens tonight — I'm glad it was you. Twelve years in a lamp, and the first person I met was brave. That's lucky. I'm not used to lucky.

*All the real walls crack simultaneously. The room fills with violet light. In the center of the room, three feet in front of the player, a tall figure assembles from smoke: MORRATH, the Hollow King — a crown of broken lanterns, a body of moving shadow, and one human eye still visible.*

**MORRATH:** You held your little room for six nights. I held the whole Veil for forty years. Do you know what it costs?

**MORRATH:** I only wanted them back. My Lira. My little Wren. The Hollow promised me. All I had to do was open the door.

**EMBER:** It lied to you, Morrath! It always lies!

**MORRATH:** *(roaring)* THEN LET IT TAKE EVERYTHING!

**PHASE 1 — "Crown of Lanterns"**
*Morrath floats in place. Broken lanterns orbit him, each a shield.*
**EMBER:** The lanterns are shielding him! Tether them out of the sky!

**PHASE 2 — "The Swarm King"**
*Morrath summons every enemy type from every wall.*
**EMBER:** Sunwheel! Now! Everything you've got!

**PHASE 3 — "The Last Memory"**
*Morrath is weakened, kneeling. The 4 collected Memory Fragments float out of the player's inventory and orbit him.*

**EMBER:** *(urgently)* Wait. Don't attack. Look — the memories. They're his. You collected them.

**EMBER:** Lumen. Show him.

**[GESTURE: LUMEN — raise hand, open palm]**

*The light shines through the Memory Fragments. On the player's real wall, projected like a shadow-play: a man laughing with a woman, a little girl on his shoulders.*

**MORRATH:** *(very quietly)* ...Wren.

**MORRATH:** I remember her laugh. I forgot it. The Hollow took it first.

*The final, fifth memory fragment forms in Morrath's hand. He holds it out to the player.*

**MORRATH:** Close the door, Spark. Please. Before I forget again.

**[FINAL CHOICE — GESTURE]**
- **SEAL** (hands together, pull apart) → *Morrath is sealed in light, at peace.* (**Canon ending: "The Lanternbearer"**)
- **FIREBOLT** → *Morrath is destroyed; the Veil seals but the room stays cold.* (**Alt ending: "The Hollow Victory"**)

---

**ENDING A — "The Lanternbearer" (Seal)**

*Golden threads wrap Morrath. His shadow fades; for a second, a normal man stands there, smiling. He dissolves into thousands of motes that drift up through the player's real ceiling like fireflies.*

**MORRATH** *(fading):* Thank you... Lanternbearer.

*All rifts across the room seal. The violet light dies. The player's real room returns — quiet, warm, ordinary. Every Ward Stone glows gold.*

**EMBER:** *(after a long silence)* ...He called you a Lanternbearer.

**EMBER:** You know what that makes you? The first one in twelve years. Which makes me — *(flame swells proudly)* — a familiar again!

**EMBER:** The Veil's still thin, you know. It'll tear again. Little rifts. Every day probably.

**EMBER:** Same time tomorrow?

**SCREEN TEXT:** *ARCANE STRIKE — THE END. Daily Rifts unlocked. Your home is protected by a Lanternbearer.*

---

**ENDING B — "The Hollow Victory" (Firebolt)**

*Morrath bursts into black ash. The rifts seal — but frost remains on the edges of the room, and Ember's flame is dim.*

**EMBER:** ...It's done. He's gone.

**EMBER:** I suppose that's what winning feels like. I thought it would be warmer.

**SCREEN TEXT:** *THE END? — Replay Chapter 6 to find another way.*

---

### POST-GAME — DAILY RIFTS (endless loop)

**Each day a new "Rift Report" from Ember:**

**EMBER (sample daily lines, randomized):**
- "Morning! Two rifts on the north wall and something smelly near your fridge. Possibly unrelated."
- "Today's rift is a Mirror day. Leave the fire at home, bring lightning."
- "Speed round! Seal three rifts in under 90 seconds and I'll stop making jokes for a whole hour. Probably."
- "You're on a 5-day streak. The Lanternbearers would be proud. I'm proud. Don't tell anyone."
- "Bad news: Brutes. Good news: you've thrown Brutes before. Fun news: they're bigger today."

---

## 4. Core Gameplay Loop

### 4.1 Session Loop (one chapter, ~8 min)
```
Launch (≤5 s cold start, room already scanned)
   ↓
Ember intro (≤45 s, skippable)
   ↓
Wave 1 (≈90 s) → Wave 2 (≈90 s) → Wave 3 (≈120 s)
   ↓                                       ↑
   └── Pause anytime (palm-up menu) ───────┘
   ↓
Boss (≈2 min)
   ↓
Seal the rift → Outro (≤30 s) → Rewards (Motes, spells, Ward Stone)
```

### 4.2 Meta Loop (days)
```
Play chapter / daily rift
   → earn Motes
   → upgrade spells & Ward Stones
   → Ward Stones stay anchored in your real home
   → next day: new Rift Report + streak bonus
   → come back
```

### 4.3 Scoring
| Action | Points |
|---|---|
| Enemy defeated | 10–150 (by type) |
| Gaze-lock kill | ×1.2 |
| Combo (3 spells in 4 s) | +50 |
| No damage wave | +200 |
| Ward Stone survives | +100 each |
| Rift seal speed bonus | up to +300 |

**Rank:** Spark → Ember → Flame → Lantern → Lanternbearer.

### 4.4 Player Health
- **Light Meter** (ring around left wrist): 100 HP.
- Shade hits drain 10–25. At 0 → "Your light flickers..." → restart wave (no full chapter restart).
- AEGIS blocks fully; regen 2 HP/s when not hit for 4 s.

---

## 5. Spell & Gesture System

### 5.1 Spell List

| # | Spell | Gesture | Effect | Cooldown / Cost | Unlock |
|---|---|---|---|---|---|
| 1 | **Firebolt** | Point index+middle finger, flick wrist forward | Fast fire projectile, 20 dmg, homes to gaze target | 0.35 s | Prologue |
| 2 | **Aegis** | Open palm facing forward, hold | Hex shield; blocks projectiles; reflect if timed in 0.2 s | Drains while held; 1.5 s recharge | Prologue |
| 3 | **Seal** | Both hands together, pull apart horizontally | Closes a weakened rift | Only when rift HP ≤ 0 | Prologue |
| 4 | **Volt Burst** | Fist → explode fingers open toward target | Lightning cone, 45 dmg, breaks armor | 2.5 s | Ch 1 |
| 5 | **Lumen** | Raise hand above head, palm up, open | Room brightens, reveals invisible enemies 6 s, stuns Phantoms | 8 s | Ch 2 |
| 6 | **Tether** | Close hand on gaze-locked enemy, pull back, release to throw | Grab and throw enemies (physics), 30 dmg on impact | 3 s | Ch 3 |
| 7 | **Mend** | Hold palm over a Ward Stone 2 s | Restores Ward HP 50% | 20 Motes | Ch 3 |
| 8 | **Sunwheel** (Ultimate) | Both index fingers draw a circle together | Orbiting ring of light 10 s, 15 dmg/tick | 100 Motes charge | Ch 4 |

### 5.2 Combos
| Combo | Sequence | Result |
|---|---|---|
| **Flashfire** | Lumen → Firebolt within 1 s | Firebolt splits into 3 |
| **Thunder Toss** | Tether → Volt Burst on thrown enemy | Chain lightning to 3 nearby |
| **Mirror Guard** | Perfect-timed Aegis | Reflected projectile deals ×2 |

### 5.3 Gesture Detection Approach
- **Static poses** (Aegis, Lumen palm, fist): Meta XR **Interaction SDK Hand Pose Detection** (Shape Recognizer + Transform Recognizer).
- **Motion gestures** (flick, explode-open, pull-apart, circle): custom `GestureRecognizer` tracking wrist/finger velocity over a 250 ms buffer.
- **Sunwheel circle:** sample index-tip positions, fit a circle (radius 10–35 cm, ≥300° coverage) — the $1 recognizer is a fallback option.
- **Microgestures** (optional bonus): thumb-tap on index = quick-cast last spell.
- **Anti-misfire rules:**
  - Each pose must be held ≥80 ms before triggering.
  - Only one hand casts an offensive spell at a time; the other may hold Aegis.
  - Hands near the face/body (within 15 cm of headset) ignore casting to prevent accidents.
  - Low-confidence hand tracking → spells are disabled + subtle "hand lost" glow.

### 5.4 Spell Feedback (every spell has all 4)
1. **Visual** — particle + light flash on the hand.
2. **Audio** — distinct spatial sound per spell.
3. **Hand glow** — the hand's vein-glow changes color per spell (fire orange, volt blue, lumen white).
4. **UI** — small spell icon pulses on the wrist.

---

## 6. Enemies & Bosses

### 6.1 Enemy Roster
| Enemy | HP | Movement | Attack | Weakness | First appears |
|---|---|---|---|---|---|
| **Wisp** | 20 | Floats slowly toward player | Contact 10 | Anything | Prologue |
| **Dart Wisp** | 20 | Floats, keeps distance | Shadow dart 10 | Aegis reflect | Prologue |
| **Crawler** | 40 | Walks on real floor, hides behind real furniture (depth occlusion) | Leap 15 | Firebolt when exposed | Ch 1 |
| **Phantom** | 30 | Invisible, drifts | Touch 20 | Lumen reveals | Ch 2 |
| **Spitter** | 35 | Clings to real ceiling | Bile arc 15 | Volt Burst | Ch 2 |
| **Brute** | 120 | Slow, walks to Ward Stones | Smash Ward 30 | Tether off, then burn | Ch 3 |
| **Mirror Shade** | 50 | Floats | Reflects Firebolt | Volt / Tether | Ch 4 |
| **Mite** | 5 | Swarm of 20–30 | Nibble 3 | Sunwheel | Ch 4 |
| **Hollow Knight** | 150 | Walks, shield faces player | Slash 25 | Tether shield away | Ch 5 |

### 6.2 Bosses
| Chapter | Boss | Mechanic |
|---|---|---|
| 1 | **The Gnasher** | Bone shell → needs Volt Burst |
| 2 | **The Weeping Veil** | Splits into 4; only the real one casts a shadow under Lumen |
| 3 | **Morrath's Hand** | Slams a Ward — Aegis, then Volt into open palm |
| 4 | **The Echo** | Copies your last spell back at you; must vary spells |
| 5 | **The Gatekeeper** | Hands hold the wall portal open; destroy hands, then Seal |
| 6 | **Morrath, the Hollow King** | 3 phases + narrative choice |

### 6.3 Spawn Rules
- Rifts spawn only on walls/ceiling/floor that the Scene API reports, ≥1.2 m from the player.
- Max 12 active enemies at once (performance + clarity).
- All enemies must approach within a **front 200° arc** by default (seated comfort). A "Full 360°" option exists for experienced players.
- Enemies never physically pass through the player's head; minimum approach distance 0.6 m.

---

## 7. Mixed Reality Features (Why Passthrough Matters)

| Feature | Meta API | Use in Arcane Strike |
|---|---|---|
| **Wall portals** | MR Utility Kit (MRUK) / Scene API | Rifts open on real walls; the largest wall gets the Ch 5 mega-portal |
| **Furniture cover** | Depth API (occlusion) + MRUK furniture volumes | Crawlers hide behind your real couch/table; you wait for them to peek |
| **Ceiling & floor** | MRUK planes | Spitters cling to your real ceiling; Ch 4 floor rifts |
| **Persistent Ward Stones** | Spatial Anchors | Wards stay on your real shelf/desk across sessions and visibly level up |
| **Dynamic lighting** | Passthrough color/brightness controls | Morrath dims your room; Lumen brightens it back |
| **Shadow projection** | Custom shader on MRUK wall mesh | Weeping Veil's real shadow on your wall; final memory shadow-play |
| **Gaze lock** | Eye tracking (Quest Pro) / head-gaze fallback (Quest 3/3S) | Glance to target an enemy |
| **Hand skeleton glow** | Hand Tracking + custom hand material | Golden Spark veins on your real hands |

**Fallback:** If no room scan exists, the game prompts the Space Setup once (≈30 s). If the scan fails, a "virtual room" fallback builds four walls 2.5 m away so the game is never blocked.

---

## 8. UX & Comfort

### 8.1 First-Time Onboarding (≤60 s before first spell)
1. Splash (3 s) → "Hold up your hands" prompt.
2. Room check: use existing scan or a quick Space Setup.
3. Lantern appears → Prologue teaches Firebolt within ~40 s.

### 8.2 Menus — Hands Only
- **Palm-up menu:** turn left palm toward face → a radial wrist menu appears (Resume, Spells, Settings, Quit).
- Poke buttons with the right index finger (Interaction SDK Poke).
- No laser pointers needed.

### 8.3 Pause / Resume (bus-stop test)
- Taking off the headset or the system menu = auto-pause.
- Game saves state at every wave start → resume restarts the current wave, not the chapter.
- Cold launch → "Continue" goes straight into the last wave in ≤10 s.

### 8.4 Seated Comfort
- Zero artificial locomotion; the player never moves.
- All targets are within reach or reachable by projectile; nothing requires standing or turning >100° by default.
- No camera shake; hits shown with a colored edge vignette instead.
- Option: "Front Arc Only" (enemies only within 120°) for airplane / tight spaces.

### 8.5 Accessibility
- Subtitles on by default (contest requires English subtitles).
- One-hand mode: Aegis becomes automatic for 1 s after each hit; all spells mappable to one hand.
- Colorblind-safe spell colors + distinct shapes per spell icon.
- Difficulty: Story / Normal / Lanternbearer.
- Gesture sensitivity slider.

---

## 9. Tech Stack & Architecture

### 9.1 Stack
| Layer | Choice |
|---|---|
| Engine | **Unity 6 LTS** (URP) |
| XR | **Meta XR All-in-One SDK** (Core, Interaction SDK, MR Utility Kit, Depth API, Spatial Anchors, Audio SDK) |
| Hand input | Interaction SDK hand poses + custom motion recognizer |
| Audio | Meta XR Audio SDK (spatial HRTF) |
| VFX | Unity VFX Graph / Particle System (mobile-friendly) |
| Save | JSON in `Application.persistentDataPath` + Spatial Anchor UUIDs |
| Build | Android APK → Meta Developer Dashboard, release channel **"Competition"** |
| Version control | Git + GitHub (LFS for assets) |

### 9.2 System Architecture
```
GameManager (state machine: Boot → Room → Chapter → Wave → Boss → Outro)
 ├── RoomService          (MRUK: walls, floor, ceiling, furniture, largest wall)
 ├── AnchorService        (save/load Ward Stone spatial anchors)
 ├── HandInputService     (raw hand data from Interaction SDK)
 │    └── GestureRecognizer (pose + motion → GestureEvent)
 ├── SpellSystem          (GestureEvent → Spell; cooldowns, Motes, combos)
 ├── TargetingService     (gaze/head-gaze lock-on)
 ├── WaveDirector         (reads ChapterData ScriptableObjects; spawns at rifts)
 │    ├── RiftController  (open/close/seal, HP)
 │    └── EnemyAI         (per-type behaviour; NavMesh on MRUK floor)
 ├── WardSystem           (Ward HP, Mend)
 ├── DialogueSystem       (Ember/Vey/Morrath lines, subtitles, triggers)
 ├── ScoreSystem
 ├── SaveSystem
 └── UI (wrist HUD, palm menu, subtitles)
```

### 9.3 Project Folder Structure
```
Assets/
  _Project/
    Scripts/
      Core/        GameManager, SaveSystem, Events
      Input/       HandInputService, GestureRecognizer, Poses/
      Spells/      SpellSystem, Spell_Firebolt, Spell_Aegis, ...
      Enemies/     EnemyBase, Wisp, Crawler, Brute, ...
      Bosses/
      MR/          RoomService, AnchorService, RiftPlacer, Occlusion
      Narrative/   DialogueSystem, DialogueData
      UI/
    Data/          ChapterData (ScriptableObjects), SpellData, EnemyData
    Prefabs/
    VFX/
    Audio/  (VO/, SFX/, Music/)
    Scenes/ Boot.unity, Game.unity
```

### 9.4 Performance Budget (Quest 3 / 3S)
- 72 FPS minimum target (90 preferred).
- ≤ 100 draw calls, ≤ 150k triangles on screen.
- Pooled projectiles & enemies; max 12 enemies, 40 projectiles.
- Baked lighting for the Hollow portal view; additive particles capped at ~2k.

---

## 10. Art & Audio Direction

### 10.1 Visual Style
- **Stylised, low-poly, glowing** — reads clearly over a busy real room.
- Palette: **Spark gold (#FFC94A)**, **Hollow violet (#6B3FD9)**, **Shade black-blue (#0E1630)**, **Volt cyan (#4FE3FF)**.
- Shades: soft dark silhouettes with bright white eyes (readable against any wall).
- Rifts: jagged violet tears with cold mist and drifting particles.
- Ember: a simple flame with big expressive eyes — the emotional anchor.

### 10.2 Audio
- **Spatial audio is gameplay:** every rift has a directional hum; you *hear* where enemies come from.
- Each spell has a signature sound (fire whoosh, volt crack, lumen choir hit).
- Music: low ambient tension → drums build per wave → full orchestral-synth on boss.
- VO: Ember (needs personality!), Vey, Morrath. Options: record team members, or use an AI voice tool whose license allows commercial use (check terms; never clone a real person's voice).

### 10.3 Asset Sources (all must be licensed)
- Unity Asset Store (check license), Kenney (CC0), Quaternius (CC0), Mixamo (animations), Freesound (check CC licence), self-made VFX.
- **No** recognizable brands, logos or real people (contest rules §17–19).

---

## 11. 4-Week Development Plan

**Today:** Oct 20, 2026 · **Submission deadline:** Nov 18, 2026 12:00 PM PT · **Plan freeze:** Nov 16

### Week 1 (Oct 20 – Oct 26) — "First Spark": Foundation & Core Prototype
| Task | Owner | Done |
|---|---|---|
| Unity 6 project, Meta XR All-in-One SDK, URP, Android build to Quest | Dev | ☐ |
| Passthrough + MRUK room loading working on device | Dev | ☐ |
| Hand tracking + Interaction SDK hand poses | Dev | ☐ |
| Gesture recognizer v1: **Firebolt, Aegis** | Dev | ☐ |
| Rift spawns on a real wall; Wisp flies toward player | Dev | ☐ |
| Firebolt kills Wisp; Aegis blocks dart | Dev | ☐ |
| Greybox VFX (placeholder) | Art | ☐ |
| Finalize script & record placeholder VO (phone mic OK) | Writer | ☐ |
| Join Devpost, set up Developer Dashboard app + "Competition" channel | Lead | ☐ |

**Milestone (Sun Oct 26):** *On a real Quest: a rift opens on my wall, a wisp comes out, I kill it with a finger flick and block a dart with my palm.*

### Week 2 (Oct 27 – Nov 2) — "The Spells": Combat Systems
| Task | Done |
|---|---|
| Gesture recognizer v2: **Volt Burst, Lumen, Tether, Seal** | ☐ |
| Spell system: cooldowns, Motes, wrist HUD | ☐ |
| Gaze / head-gaze targeting + homing | ☐ |
| Enemies: Wisp, Dart Wisp, Crawler (floor NavMesh), Phantom | ☐ |
| Depth API occlusion (Crawler behind real furniture) | ☐ |
| WaveDirector + ChapterData ScriptableObjects | ☐ |
| Player Light Meter, damage, wave restart | ☐ |
| Ember character model + floating animation | ☐ |
| Dialogue system + subtitles | ☐ |

**Milestone (Sun Nov 2):** *The Prologue + Chapter 1 are fully playable start to finish (with placeholder art).*

### Week 3 (Nov 3 – Nov 9) — "Hold the Line": Content & MR Magic
| Task | Done |
|---|---|
| Ward Stones + **Spatial Anchors** persistence | ☐ |
| Chapters 2 & 3 waves + bosses (Weeping Veil, Morrath's Hand) | ☐ |
| Spitter (ceiling), Brute, Mites; Sunwheel ultimate | ☐ |
| Dynamic passthrough dimming / Lumen brighten | ☐ |
| Palm-up menu, pause/resume, save system | ☐ |
| Final VFX for all spells, final rift effect | ☐ |
| Record final VO; spatial audio mix | ☐ |
| Playtest with 3+ people seated; fix gesture misfires | ☐ |

**Milestone (Sun Nov 9):** *Prologue → Ch 3 polished and playable; Wards persist after restart.*

### Week 4 (Nov 10 – Nov 18) — "The Hollow King": Finale, Polish & Submit
| Task | Done |
|---|---|
| **Chapter 6 finale** (Morrath, 3 phases, both endings) — *priority over Ch 4–5* | ☐ |
| Chapters 4–5 if time allows (see cut list) | ☐ |
| Daily Rift mode (randomized wave + Ember line) | ☐ |
| Performance pass: stable 72 FPS | ☐ |
| Onboarding polish; airplane-seat test & bus-stop test | ☐ |
| **Record demo video (≤3 min)** on device — Nov 13–14 | ☐ |
| Write Devpost description, screenshots, changelog | ☐ |
| Upload APK to **"Competition"** release channel; test the install on a clean headset | ☐ |
| **Submit on Devpost by Nov 16** (2-day safety buffer) | ☐ |

### Cut List (if behind schedule — cut in this order)
1. Chapter 4 & 5 → replace with one combined "Chapter 4: Through the Wall".
2. Ending B (keep only the Seal ending).
3. Mirror Shade, Hollow Knight enemies.
4. Combos.
5. Eye-tracking (keep head-gaze only).

**Must never cut:** Prologue, Ch 1–3, Finale, Ward anchors, wall portals, depth occlusion, hands-only menus.

### Team Roles (suggested for 2–4 people)
| Role | Responsibilities |
|---|---|
| **Lead / Gameplay Dev** | Gestures, spells, game manager, build & submission |
| **MR / Systems Dev** | MRUK, anchors, depth, enemies AI, waves |
| **Artist / VFX** | Ember, Shades, rifts, spell VFX, UI |
| **Narrative / Audio / Video** | VO, SFX, music, subtitles, demo video, Devpost write-up |

---

## 12. Demo Video Storyboard (2:50 total, judges stop at 3:00)

| Time | Shot | Audio / VO |
|---|---|---|
| 0:00–0:08 | Black → a real living room through passthrough. A violet crack splits the wall. | Low hum. Text: *"What if magic came to your room?"* |
| 0:08–0:20 | Lantern appears on real desk; player pinches it open; Ember bursts out. | EMBER: "Thank the stars — someone's awake!" |
| 0:20–0:35 | Player raises hands — golden Spark veins on real hands. | EMBER: "Look at your hands. You have it." |
| 0:35–0:55 | First wisp; **Firebolt** flick; **Aegis** blocks dart. Quick cuts. | Spell SFX; title card: **ARCANE STRIKE** |
| 0:55–1:20 | Crawler hides behind the real sofa (depth occlusion), peeks, gets blasted. | Text: *"Your room is the battlefield."* |
| 1:20–1:40 | Spell montage: Volt Burst shatters shell, Lumen reveals Phantoms, Tether throws a Brute. | Text overlay of each gesture. |
| 1:40–1:55 | Ward Stone on a real shelf — headset off/on — it's still there, glowing. | Text: *"Wards stay in your home. Every day."* |
| 1:55–2:20 | Ch 5: the largest wall opens into the Hollow; Gatekeeper's hands grip it. **Sunwheel**. | Music peaks. |
| 2:20–2:35 | Morrath kneels; memory shadow-play on the real wall; player casts Seal. | MORRATH: "Close the door, Spark." |
| 2:35–2:50 | Quiet room, Ember: "Same time tomorrow?" Feature bullets: Hands-only · Seated · 8-min chapters · Your room, your battlefield. | End card: logo + "Meta Quest 3 / 3S" |

**Filming tips:** record with the Quest's built-in recorder and casting; film one real-world shot of the player seated making gestures (picture-in-picture); keep cuts quick; add subtitles.

---

## 13. Submission Checklist & Risks

### 13.1 Submission Checklist
- ☐ All team members 18+, Meta developer accounts, START program membership
- ☐ APK uploaded to Developer Dashboard → release channel **"Competition"**
- ☐ App free; judges can access until Dec 11, 2026
- ☐ Demo video < 3:00, public on YouTube/Vimeo, shows on-device footage
- ☐ Devpost text: inspiration, how we built it, future plans, target launch date
- ☐ Track = **Gaming**, Division = **New Experience**
- ☐ English everywhere + subtitles
- ☐ No third-party brands, logos, real people; all assets licensed
- ☐ Content guidelines 13+ (fantasy violence, no gore)
- ☐ Tested: full playthrough without ever touching a controller
- ☐ Tested: airplane-seat test (2 ft radius) and bus-stop test (≤10 min chapter)
- ☐ Submitted **before Nov 16** (buffer before the Nov 18 deadline)

### 13.2 Risks & Mitigations
| Risk | Impact | Mitigation |
|---|---|---|
| Gesture misfires / false positives | High — breaks fun | Hold thresholds, confidence checks, "near-face" dead zone, sensitivity slider, playtest early (Week 2) |
| Hand-tracking loss when hands go behind the body | Medium | Keep all gestures in front of the chest; show "hand lost" feedback |
| Room scan missing / bad | Medium | Prompt Space Setup; virtual-room fallback |
| Performance drops with VFX | Medium | Object pooling, particle caps, profile on device weekly |
| Scope creep (6 chapters) | High | Cut list above; finale is prioritized over Ch 4–5 |
| VO quality | Low–Medium | Subtitles carry the story; Ember can be text + chirp sounds if needed |
| Last-minute upload issues | High | Submit 2 days early; test install from the release channel |

---

## 14. Post-Contest Roadmap (for the Devpost "future plans" section)
- **Co-op Mode:** two players in the same room defend shared Wards (Shared Spatial Anchors).
- **Seasonal Rifts:** new enemy types and story arcs monthly.
- **Ward Garden:** decorate and customize Ward Stones around your home.
- **Target Meta Horizon Store launch:** Q2 2027.

---
*Arcane Strike — an original game concept. All characters, names and lore are original creations.*
