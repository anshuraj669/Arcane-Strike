#!/usr/bin/env python3
"""Cuts the hackathon demo video out of the full replay captures in Recordings/Video.

    python3 Tools/video/make_demo.py            -> Recordings/Video/ArcaneStrike_Demo.mp4

Every cut point was picked by hand from the capture (frame sheets + a word-level transcript): no cut lands inside a
spoken line, and no shot shows a keyboard hint ("press E", "hold P") or the "You have fallen" screen. In the suit-up
the take skipped through Ember's lines (they overlap and cut off), so that stretch gets her clean recordings instead.
"""
import os, re, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VID = os.path.join(ROOT, "Recordings", "Video")
VO = os.path.join(ROOT, "Assets", "_Project", "Resources", "VO", "Ember")
MAIN = os.path.join(VID, "2026-11-18_19-28-59_Attic_19-29-53.mp4")
CH2 = os.path.join(VID, "2026-11-18_20-12-09_Chapter2_AtticWake_20-12-27.mp4")
OUT = os.path.join(VID, "ArcaneStrike_Demo.mp4")
TMP = sys.argv[1] if len(sys.argv) > 1 else os.path.join(VID, ".demo_build")
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FONT_B = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"

# (source, start, end) in seconds; consecutive pieces of one beat play back to back.
# The whole film must stay under 3:00 (the judges stop there; see the GDD's submission checklist).
CH1 = [
    ("title",    0.9,   5.6),   # ARCANE STRIKE, a hands-first VR adventure
    ("witness",  8.0,  14.4),   # "Be still, child. You're not dreaming..."
    ("witness", 21.8,  30.3),   # "Your blood is the Agnivansh... Your people forgot."
    ("witness", 32.3,  34.8),   # the white flash (the take skipped the rest of that line)
    ("ch1",     36.2,  39.7),   # CHAPTER 1, The Lantern
    ("attic",   54.5,  57.5),
    ("attic",   59.8,  63.8),   # hands on the chest
    ("attic",   69.5,  80.6),   # the lantern wakes in the hand
    ("temple",  81.0,  84.0),
    ("temple",  87.5,  97.0),   # the blue fire rises and takes the room
    "SUITUP",                   # soul in the fire + suit-up (custom voice track)
    ("talk",   139.4, 144.3),   # "Before we go, do you want to try that blade first?"
    ("practice", 421.6, 432.35),# the shadow to practise on: "...the deeper it bites."
    ("practice", 442.8, 448.5), # "It's weak now... The bridge is waiting. Let's break the way open again."
    ("bridge", 154.6, 165.0),   # "There's your way out, but that bridge doesn't lead home..."
    ("bridge", 170.9, 172.4),   # "Follow the light, Weaver."
    ("portal", 176.0, 179.4),   # through the portal: "This is their world, Weaver. The Kalakh's world."
    ("fight",  189.2, 198.2),   # "...stay close to the light" / "Hold your blade across to parry"
    ("fight",  214.0, 231.0, True),  # combo cuts, parry, riposte
]
# Chapter 2 teaser (times in CH2). That take spent two minutes on the roof, so only the card and the wake-up.
CH2_CUTS = [
    ("ch2",      0.8,   5.0),   # CHAPTER 2, First Ignition
    ("wake",   176.6, 188.0),   # "Who's there? Show yourself..." / the Seal burns: "These runes... glowing like fire."
    ("ember",  190.6, 194.6, True),   # "Stand down. There is no enemy before you. It is me, Ember."
]

# Soul + suit-up: picture from two stretches, Ember's clean lines laid over the fire.
SUIT_A = (99.5, 112.0)
SUIT_B = (118.6, 134.6)
SUIT_VOICE = [("many_years", 5.0), ("my_name_is_ember", 9.9), ("jyoti_seal", 13.6), ("youre_a_weaver", 17.7)]

ENC = ["-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-r", "60",
       "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2"]


def run(args):
    r = subprocess.run(["ffmpeg", "-v", "error", "-y"] + args)
    if r.returncode: sys.exit(f"ffmpeg failed: {' '.join(args)}")


def mean_db(path):
    out = subprocess.run(["ffmpeg", "-i", path, "-af", "volumedetect", "-f", "null", "-"], capture_output=True, text=True).stderr
    return float(re.search(r"mean_volume: (-?[\d.]+)", out).group(1))


def piece(i, src, a, b, fade=False):
    out = os.path.join(TMP, f"p{i:02d}.mp4")
    d = b - a
    t = 0.7 if fade else 0.12  # (a beat that ends a chapter fades to black)
    vf = ["-vf", f"fade=t=out:st={d - t:.3f}:d={t}"] if fade else []
    run(["-ss", f"{a}", "-t", f"{d}", "-i", src, *vf,
         "-af", f"afade=t=in:d=0.08,afade=t=out:st={d - t:.3f}:d={t}", *ENC, out])
    return out


def suitup(i):
    out = os.path.join(TMP, f"p{i:02d}.mp4")
    a0, a1 = SUIT_A; b0, b1 = SUIT_B
    la, lb = a1 - a0, b1 - b0
    ins = ["-ss", f"{a0}", "-t", f"{la}", "-i", MAIN, "-ss", f"{b0}", "-t", f"{lb}", "-i", MAIN,
           "-ss", "120", "-t", f"{la - 4.5}", "-i", MAIN]
    voice = []
    for k, (clip, at) in enumerate(SUIT_VOICE):
        path = os.path.join(VO, clip + ".wav")
        ins += ["-i", path]
        g = -15.0 - mean_db(path)
        voice.append(f"[{3 + k}:a]aresample=48000,aformat=channel_layouts=stereo,volume={g:.1f}dB,"
                     f"adelay={int(at * 1000)}|{int(at * 1000)}[v{k}]")
    f = ";".join([
        "[0:v][1:v]concat=n=2:v=1:a=0[v]",
        # bed: the first 4.5 s as recorded (before the skipped lines start), then the fire's roar, then the suit-up
        f"[0:a]atrim=0:4.5,afade=t=out:st=4.2:d=0.3[a0]",
        f"[2:a]afade=t=in:d=0.3,afade=t=out:st={la - 4.5 - 0.3:.2f}:d=0.3[a1]",
        "[1:a]afade=t=in:d=0.2[a2]",
        "[a0][a1][a2]concat=n=3:v=0:a=1,volume=-5dB[bed]",
        *voice,
        "[bed]" + "".join(f"[v{k}]" for k in range(len(voice))) +
        f"amix=inputs={1 + len(voice)}:normalize=0:duration=first,afade=t=out:st={la + lb - 0.15:.2f}:d=0.15[a]",
    ])
    run(ins + ["-filter_complex", f, "-map", "[v]", "-map", "[a]", *ENC, out])
    return out


def endcard(i, tagline):
    out = os.path.join(TMP, f"p{i:02d}.mp4")
    # The game's own title card as the background, with what the judges need underneath.
    lines = [("Built for Meta Quest 3  ·  hand tracking, no controllers", 0.62, 44),
             (tagline, 0.695, 34)]
    dt = ",".join(
        f"drawtext=fontfile={FONT}:text='{t}':fontsize={s}:fontcolor=white@0.85:x=(w-tw)/2:y=h*{y}:"
        f"alpha='if(lt(t,1.0),0,min(1,(t-1.0)/0.8))'" for t, y, s in lines)
    run(["-ss", "1.5", "-t", "4", "-i", MAIN,
         "-vf", "tpad=stop_mode=clone:stop_duration=3," + dt + ",fade=t=out:st=6.0:d=1.0",
         "-af", "apad,atrim=0:7,afade=t=in:d=0.3,afade=t=out:st=3.0:d=1.0", *ENC, out])
    return out


def main():
    os.makedirs(TMP, exist_ok=True)
    parts = []
    for item in CH1:
        i = len(parts)
        parts.append(suitup(i) if item == "SUITUP" else piece(i, MAIN, *item[1:]))
        print(f"  {i:02d} {item if isinstance(item, str) else item[0]}", flush=True)
    for name, *cut in CH2_CUTS:
        parts.append(piece(len(parts), CH2, *cut)); print(f"  {len(parts) - 1:02d} {name}", flush=True)
    parts.append(endcard(len(parts), "Chapter 1 · The Lantern     Chapter 2 · First Ignition" if CH2_CUTS
                                     else "Chapter 1 · The Lantern"))
    lst = os.path.join(TMP, "list.txt")
    with open(lst, "w") as fh: fh.writelines(f"file '{p}'\n" for p in parts)
    joined = os.path.join(TMP, "joined.mp4")
    run(["-f", "concat", "-safe", "0", "-i", lst, "-c", "copy", joined])
    # one loudness for the whole film (-14 LUFS, YouTube's level), video untouched
    run(["-i", joined, "-c:v", "copy", "-af", "loudnorm=I=-14:TP=-1.5:LRA=11", "-c:a", "aac", "-b:a", "192k",
         "-ar", "48000", "-movflags", "+faststart", OUT])
    print("->", OUT)


if __name__ == "__main__":
    main()
