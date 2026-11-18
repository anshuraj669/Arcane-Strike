"""
Makes the voice clips for every spoken line that has no hand-made recording, so the game speaks them instead of
showing subtitles. Offline: Kokoro (open model, runs on this machine; nothing is sent anywhere).

    ~/.cache/arcane-tts/.venv/bin/python Tools/tts/make_voices.py          (from the Unity project folder)
    ... make_voices.py --list                                                (only list the lines it found)

Writes Assets/_Project/Resources/VO/Lines/<Speaker>/<key>.wav, where key = VoiceBank.Key(line) (FNV-1a of the
UTF-8 words, see Assets/_Project/Scripts/Story/VoiceBank.cs). Existing clips are kept (delete one to remake it).

Where the lines come from:
- the Editor logs: every "[Ember] ...", "[You] ..." line the game has printed (the exact words, as played),
- the scripts: the words in Line(...), Say(...), SayAt(...), EmberSays(...), lesson `how = "..."`, `=> "..."`,
- the scenes: serialized line lists (RiftArena's arrivalLines).
Lines that already have real recordings (Ember's Ascension lines via Voiced(...), the Witness) are skipped.
"""

import os
import re
import sys
import glob
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
SCRIPTS = os.path.join(PROJECT, "Assets/_Project/Scripts/Story")
SCENES = os.path.join(PROJECT, "Assets/_Project/Scenes")
OUT = os.path.join(PROJECT, "Assets/_Project/Resources/VO/Lines")
MODEL = os.path.expanduser("~/.cache/arcane-tts")
LOGS = [os.path.expanduser("~/.config/unity3d/Editor.log"), os.path.expanduser("~/.config/unity3d/Editor-prev.log")]

# Speaker → (Kokoro voice, speed, pitch factor). Ember's own recordings are a bright young woman's voice (~270 Hz).
VOICES = {
    "Ember": ("af_heart", 1.0, 1.0),
    "You": ("am_puck", 1.02, 1.0),
    "Kalakh": ("am_onyx", 0.85, 0.72),
}


def key(line):
    h = 2166136261
    for b in line.strip().encode("utf-8"):
        h ^= b
        h = (h * 16777619) & 0xFFFFFFFF
    return "l%08x" % h


def unescape(s):
    return re.sub(r'\\(u[0-9a-fA-F]{4}|.)', lambda m: chr(int(m.group(1)[1:], 16)) if m.group(1)[0] == "u"
                  else {"n": "\n", "t": "\t"}.get(m.group(1), m.group(1)), s)


LITERAL = r'(?<![$@])"((?:[^"\\]|\\.)*)"'


def from_scripts():
    found = []
    for path in glob.glob(os.path.join(SCRIPTS, "**", "*.cs"), recursive=True):
        name = os.path.basename(path)
        if name in ("CosmicPrologue.cs", "LanternVoidDirector.cs"):
            continue  # the Witness is recorded; the lantern void is retired
        for row in open(path, encoding="utf-8"):
            code = row
            if code.lstrip().startswith("//"):
                continue  # a comment
            if "Debug.Log" in code or "Voiced(" in code:
                continue
            speaker = None
            if "SayAt(" in code:
                speaker = "Kalakh"
            elif re.search(r'\bvoice\.Say\(', code) and "Chapter2" in path:
                speaker = "You"
            elif re.search(r'\b(Line|Say|Speak|EmberSays)\(', code) or re.search(r'\bhow\s*=\s*"', code) \
                    or re.search(r'=>\s*"', code):
                speaker = "Ember"
            if speaker is None:
                continue
            for lit in re.findall(LITERAL, code):
                text = unescape(lit).strip()
                if re.match(r"^c2_\w+$", text) or len(text) < 4 or not re.search(r"[A-Za-z]", text):
                    continue  # (a clip key, not words)
                found.append((speaker, text))
    return found


def from_scenes():
    found = []
    for path in glob.glob(os.path.join(SCENES, "*.unity")):
        lines = open(path, encoding="utf-8", errors="replace").read().split("\n")
        for i, row in enumerate(lines):
            if row.strip() == "arrivalLines:":
                for item in lines[i + 1:]:
                    m = re.match(r"\s*- (.*)$", item)
                    if not m:
                        break
                    text = m.group(1).strip()
                    if text.startswith(("'", '"')):
                        text = text[1:-1].replace("''", "'")
                    found.append(("Ember", text))
    return found


def from_logs():
    found = []
    for log in LOGS:
        if not os.path.exists(log):
            continue
        for row in open(log, encoding="utf-8", errors="replace"):
            m = re.match(r"^\[(Ember|You)\] (.+)$", row.rstrip("\n"))
            if m:
                found.append((m.group(1), m.group(2).strip()))
    return found


def recorded():
    """The words already recorded by hand (Ember's Voiced(...) lines): never made again."""
    words = set()
    for path in glob.glob(os.path.join(SCRIPTS, "**", "*.cs"), recursive=True):
        for row in open(path, encoding="utf-8"):
            m = re.search(r'Voiced\("\w+",\s*' + LITERAL, row)
            if m:
                words.add(unescape(m.group(1)).strip())
    return words


def spoken(text):
    """What the voice reads: the words, not the typography."""
    t = text.replace("—", ", ").replace("–", ", ").replace("...", "… ").replace("Hii", "Hi")
    t = t.replace("Kalakh", "Kaa-lakh").replace("Jyoti", "Jyothi")
    return re.sub(r"\s+", " ", t).strip()


def write_wav(path, samples, rate):
    pcm = np.clip(samples, -1.0, 1.0)
    pcm = (pcm * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(pcm.tobytes())


def main():
    skip = recorded()
    lines, seen = [], set()
    for speaker, text in from_logs() + from_scripts() + from_scenes():
        if text in skip or (speaker, text) in seen:
            continue
        if not re.search(r"[.!?…]$", text) or "(Mouse" in text:
            continue  # not a whole spoken sentence (a cut-off log line, a label, an on-screen prompt)
        seen.add((speaker, text))
        lines.append((speaker, text))
    lines.sort()
    if "--list" in sys.argv:
        for speaker, text in lines:
            print(f"{speaker:7s} {key(text)}  {text}")
        print(f"{len(lines)} lines")
        return

    from kokoro_onnx import Kokoro
    tts = Kokoro(os.path.join(MODEL, "kokoro-v1.0.onnx"), os.path.join(MODEL, "voices-v1.0.bin"))
    made = 0
    for speaker, text in lines:
        folder = os.path.join(OUT, speaker)
        os.makedirs(folder, exist_ok=True)
        path = os.path.join(folder, key(text) + ".wav")
        if os.path.exists(path):
            continue
        voice, speed, pitch = VOICES[speaker]
        samples, rate = tts.create(spoken(text), voice=voice, speed=speed, lang="en-us")
        samples = np.asarray(samples, dtype=np.float32)
        if pitch != 1.0:  # deeper and slower (resampled), for the Kalakh
            n = int(len(samples) / pitch)
            samples = np.interp(np.linspace(0, len(samples) - 1, n), np.arange(len(samples)), samples).astype(np.float32)
        peak = float(np.max(np.abs(samples))) or 1.0
        samples = samples / peak * 0.89  # about -1 dB
        tail = np.zeros(int(rate * 0.12), dtype=np.float32)
        write_wav(path, np.concatenate([samples, tail]), rate)
        made += 1
        print(f"[voice] {speaker}: {text}")
    print(f"[voice] made {made} clips ({len(lines)} lines) in {OUT}")


if __name__ == "__main__":
    main()
