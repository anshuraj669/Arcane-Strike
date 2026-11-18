using System;
using System.Collections.Generic;
using System.IO;
using ArcaneStrike.HandInput;
using UnityEngine;

namespace ArcaneStrike.Demo
{
    /// <summary>
    /// One recorded "take" for the demo video: everything the player did, frame by frame, from the moment the scene
    /// started: both hands (21 joints each, in the player rig's space), the upper body, the webcam head moves (lean,
    /// turn, nod, as running totals), the mouse, and every key / controller action. Saved raw to Recordings/Takes;
    /// <see cref="Smoothed"/> makes the cleaned-up copy that is replayed: after the take is over, each moment can be
    /// smoothed with what came just before AND after it, so the jitter goes with no lag at all (which live filtering
    /// can never do), and short tracking drop-outs are filled in.
    /// </summary>
    public class DemoTake
    {
        const string Magic = "ARCANE-TAKE";
        const int FormatVersion = 2; // 2: + the scene changes
        public const int Actions = 64; // GameAction values recorded (bits of a ulong)

        public class Frame
        {
            public float time;
            public readonly bool[] handTracked = new bool[2];
            public readonly float[] handConfidence = new float[2];
            public readonly Vector3[][] hands = { new Vector3[HandFrame.JointCount], new Vector3[HandFrame.JointCount] };
            public bool bodyTracked;
            public float bodyConfidence;
            public readonly Vector3[] body = new Vector3[UpperBodyFrame.JointCount];
            public readonly float[] bodyVisibility = new float[UpperBodyFrame.JointCount];
            public Vector3 lean;         // running total of how far the webcam head driver moved the rig
            public float turn, nod;      // ... and turned it / tipped the camera (degrees)
            public Vector2 mouse;        // mouse movement this frame (pixels)
            public ulong held, pressed, released;

            public Frame Copy()
            {
                var f = new Frame
                {
                    time = time, bodyTracked = bodyTracked, bodyConfidence = bodyConfidence,
                    lean = lean, turn = turn, nod = nod, mouse = mouse, held = held, pressed = pressed, released = released,
                };
                for (int h = 0; h < 2; h++)
                {
                    f.handTracked[h] = handTracked[h];
                    f.handConfidence[h] = handConfidence[h];
                    Array.Copy(hands[h], f.hands[h], HandFrame.JointCount);
                }
                Array.Copy(body, f.body, body.Length);
                Array.Copy(bodyVisibility, f.bodyVisibility, bodyVisibility.Length);
                return f;
            }
        }

        public string scene;
        public int seed;
        public string recordedAt;
        public bool webcam; // played with webcam hands
        public readonly List<Frame> frames = new List<Frame>();

        /// <summary>A scene that loaded during the take, and when (take time): the replay lines each scene up with it.</summary>
        public struct SceneMark
        {
            public float time;
            public string scene;
        }
        public readonly List<SceneMark> sceneMarks = new List<SceneMark>();

        public float Duration => frames.Count == 0 ? 0f : frames[frames.Count - 1].time;

        // ---------------------------------------------------------------- files

        public static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "Takes"));

        public static string Latest()
        {
            if (!Directory.Exists(Folder)) return null;
            string best = null;
            DateTime newest = DateTime.MinValue;
            foreach (string f in Directory.GetFiles(Folder, "*.take"))
            {
                DateTime t = File.GetLastWriteTimeUtc(f);
                if (t > newest)
                {
                    newest = t;
                    best = f;
                }
            }
            return best;
        }

        public string Save()
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{scene}.take");
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(Magic);
                w.Write(FormatVersion);
                w.Write(scene ?? "");
                w.Write(seed);
                w.Write(recordedAt ?? "");
                w.Write(webcam);
                w.Write(frames.Count);
                foreach (Frame f in frames)
                {
                    w.Write(f.time);
                    for (int h = 0; h < 2; h++)
                    {
                        w.Write(f.handTracked[h]);
                        w.Write(f.handConfidence[h]);
                        foreach (Vector3 v in f.hands[h]) Write(w, v);
                    }
                    w.Write(f.bodyTracked);
                    w.Write(f.bodyConfidence);
                    for (int j = 0; j < f.body.Length; j++)
                    {
                        Write(w, f.body[j]);
                        w.Write(f.bodyVisibility[j]);
                    }
                    Write(w, f.lean);
                    w.Write(f.turn);
                    w.Write(f.nod);
                    w.Write(f.mouse.x);
                    w.Write(f.mouse.y);
                    w.Write(f.held);
                    w.Write(f.pressed);
                    w.Write(f.released);
                }
                w.Write(sceneMarks.Count);
                foreach (SceneMark m in sceneMarks)
                {
                    w.Write(m.time);
                    w.Write(m.scene ?? "");
                }
            }
            return path;
        }

        public static DemoTake Load(string path)
        {
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                if (r.ReadString() != Magic) throw new InvalidDataException($"{path} isn't a demo take.");
                int version = r.ReadInt32();
                if (version < 1 || version > FormatVersion) throw new InvalidDataException($"{path}: take format {version}, expected 1 to {FormatVersion}.");
                var take = new DemoTake { scene = r.ReadString(), seed = r.ReadInt32(), recordedAt = r.ReadString() };
                take.webcam = r.ReadBoolean();
                int count = r.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var f = new Frame { time = r.ReadSingle() };
                    for (int h = 0; h < 2; h++)
                    {
                        f.handTracked[h] = r.ReadBoolean();
                        f.handConfidence[h] = r.ReadSingle();
                        for (int j = 0; j < HandFrame.JointCount; j++) f.hands[h][j] = Read(r);
                    }
                    f.bodyTracked = r.ReadBoolean();
                    f.bodyConfidence = r.ReadSingle();
                    for (int j = 0; j < f.body.Length; j++)
                    {
                        f.body[j] = Read(r);
                        f.bodyVisibility[j] = r.ReadSingle();
                    }
                    f.lean = Read(r);
                    f.turn = r.ReadSingle();
                    f.nod = r.ReadSingle();
                    f.mouse = new Vector2(r.ReadSingle(), r.ReadSingle());
                    f.held = r.ReadUInt64();
                    f.pressed = r.ReadUInt64();
                    f.released = r.ReadUInt64();
                    take.frames.Add(f);
                }
                if (version >= 2)
                {
                    int marks = r.ReadInt32();
                    for (int i = 0; i < marks; i++) take.sceneMarks.Add(new SceneMark { time = r.ReadSingle(), scene = r.ReadString() });
                }
                return take;
            }
        }

        static void Write(BinaryWriter w, Vector3 v)
        {
            w.Write(v.x);
            w.Write(v.y);
            w.Write(v.z);
        }

        static Vector3 Read(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        // ---------------------------------------------------------------- smoothing (after the take, both ways in time)

        /// <summary>How much to smooth: seconds of Gaussian blur for the hands (wrists), the fingers (relative to
        /// the wrist), the body, and the head (lean / turn / nod); and the longest tracking drop-out to fill in.</summary>
        [Serializable]
        public struct Smoothing
        {
            public float hands, fingers, body, head, fillGaps;

            public static Smoothing Preset(int level) => level switch
            {
                0 => new Smoothing { hands = 0f, fingers = 0f, body = 0f, head = 0f, fillGaps = 0f },          // raw
                1 => new Smoothing { hands = 0.035f, fingers = 0.03f, body = 0.05f, head = 0.12f, fillGaps = 0.25f },
                3 => new Smoothing { hands = 0.11f, fingers = 0.08f, body = 0.12f, head = 0.35f, fillGaps = 0.5f },
                _ => new Smoothing { hands = 0.065f, fingers = 0.05f, body = 0.08f, head = 0.22f, fillGaps = 0.35f }, // 2: medium
            };

            public static readonly string[] Names = { "Off (raw)", "Light", "Medium", "Strong" };
        }

        /// <summary>A cleaned-up copy: drop-outs filled, then everything smoothed with no lag.</summary>
        public DemoTake Smoothed(Smoothing s)
        {
            var copy = new DemoTake { scene = scene, seed = seed, recordedAt = recordedAt, webcam = webcam };
            copy.sceneMarks.AddRange(sceneMarks);
            foreach (Frame f in frames) copy.frames.Add(f.Copy());
            int n = copy.frames.Count;
            if (n < 3) return copy;
            var times = new float[n];
            for (int i = 0; i < n; i++) times[i] = copy.frames[i].time;

            for (int h = 0; h < 2; h++)
            {
                int hand = h;
                var tracked = new bool[n];
                for (int i = 0; i < n; i++) tracked[i] = copy.frames[i].handTracked[hand];
                FillGaps(times, tracked, s.fillGaps, (a, b, u, i) =>
                {
                    Frame fa = copy.frames[a], fb = copy.frames[b], fi = copy.frames[i];
                    for (int j = 0; j < HandFrame.JointCount; j++) fi.hands[hand][j] = Vector3.Lerp(fa.hands[hand][j], fb.hands[hand][j], u);
                    fi.handConfidence[hand] = Mathf.Lerp(fa.handConfidence[hand], fb.handConfidence[hand], u);
                    fi.handTracked[hand] = true;
                });
                for (int i = 0; i < n; i++) tracked[i] = copy.frames[i].handTracked[hand];

                // The wrist on its own; the fingers as offsets from the wrist (so a quick hand move doesn't smear them).
                var wrist = new Vector3[n];
                for (int i = 0; i < n; i++) wrist[i] = copy.frames[i].hands[hand][0];
                Vector3[] wristSmooth = Blur(times, wrist, tracked, s.hands);
                for (int j = 1; j < HandFrame.JointCount; j++)
                {
                    var offset = new Vector3[n];
                    for (int i = 0; i < n; i++) offset[i] = copy.frames[i].hands[hand][j] - wrist[i];
                    Vector3[] smooth = Blur(times, offset, tracked, s.fingers);
                    for (int i = 0; i < n; i++)
                        if (tracked[i]) copy.frames[i].hands[hand][j] = wristSmooth[i] + smooth[i];
                }
                for (int i = 0; i < n; i++)
                    if (tracked[i]) copy.frames[i].hands[hand][0] = wristSmooth[i];
            }

            var bodyTracked = new bool[n];
            for (int i = 0; i < n; i++) bodyTracked[i] = copy.frames[i].bodyTracked;
            FillGaps(times, bodyTracked, s.fillGaps, (a, b, u, i) =>
            {
                Frame fa = copy.frames[a], fb = copy.frames[b], fi = copy.frames[i];
                for (int j = 0; j < fi.body.Length; j++)
                {
                    fi.body[j] = Vector3.Lerp(fa.body[j], fb.body[j], u);
                    fi.bodyVisibility[j] = Mathf.Lerp(fa.bodyVisibility[j], fb.bodyVisibility[j], u);
                }
                fi.bodyTracked = true;
            });
            for (int i = 0; i < n; i++) bodyTracked[i] = copy.frames[i].bodyTracked;
            for (int j = 0; j < UpperBodyFrame.JointCount; j++)
            {
                var joint = new Vector3[n];
                for (int i = 0; i < n; i++) joint[i] = copy.frames[i].body[j];
                Vector3[] smooth = Blur(times, joint, bodyTracked, s.body);
                for (int i = 0; i < n; i++)
                    if (bodyTracked[i]) copy.frames[i].body[j] = smooth[i];
            }

            // The head moves are running totals, always present: smooth them as one continuous track.
            var all = new bool[n];
            for (int i = 0; i < n; i++) all[i] = true;
            var head = new Vector3[n];
            for (int i = 0; i < n; i++) head[i] = copy.frames[i].lean;
            Vector3[] leanSmooth = Blur(times, head, all, s.head);
            for (int i = 0; i < n; i++) head[i] = new Vector3(copy.frames[i].turn, copy.frames[i].nod, 0f);
            Vector3[] angleSmooth = Blur(times, head, all, s.head);
            for (int i = 0; i < n; i++)
            {
                copy.frames[i].lean = leanSmooth[i];
                copy.frames[i].turn = angleSmooth[i].x;
                copy.frames[i].nod = angleSmooth[i].y;
            }
            return copy;
        }

        // Short runs of "not tracked" between two tracked frames are bridged (fill(a, b, u, i) sets frame i, u along a → b).
        static void FillGaps(float[] times, bool[] tracked, float longest, Action<int, int, float, int> fill)
        {
            if (longest <= 0f) return;
            int n = times.Length, lastTracked = -1;
            for (int i = 0; i < n; i++)
            {
                if (!tracked[i]) continue;
                if (lastTracked >= 0 && i - lastTracked > 1 && times[i] - times[lastTracked] <= longest)
                    for (int k = lastTracked + 1; k < i; k++)
                        fill(lastTracked, i, Mathf.InverseLerp(times[lastTracked], times[i], times[k]), k);
                lastTracked = i;
            }
        }

        // A Gaussian blur over time (frames needn't be evenly spaced), within each tracked run so nothing bleeds in
        // from a drop-out. Centred: no lag.
        static Vector3[] Blur(float[] times, Vector3[] values, bool[] valid, float sigma)
        {
            int n = values.Length;
            var result = (Vector3[])values.Clone();
            if (sigma <= 1e-4f) return result;
            float reach = sigma * 3f, twoSigma2 = 2f * sigma * sigma;
            int start = 0;
            while (start < n)
            {
                if (!valid[start])
                {
                    start++;
                    continue;
                }
                int end = start;
                while (end + 1 < n && valid[end + 1]) end++;
                int lo = start;
                for (int i = start; i <= end; i++)
                {
                    while (times[i] - times[lo] > reach) lo++;
                    Vector3 sum = Vector3.zero;
                    float weights = 0f;
                    for (int k = lo; k <= end && times[k] - times[i] <= reach; k++)
                    {
                        float d = times[k] - times[i];
                        float w = Mathf.Exp(-d * d / twoSigma2);
                        sum += values[k] * w;
                        weights += w;
                    }
                    result[i] = weights > 0f ? sum / weights : values[i];
                }
                start = end + 1;
            }
            return result;
        }

        // ---------------------------------------------------------------- reading it back at any time

        /// <summary>The last frame at or before time `t` (-1 before the first).</summary>
        public int IndexAt(float t)
        {
            int lo = 0, hi = frames.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (frames[mid].time <= t)
                {
                    found = mid;
                    lo = mid + 1;
                }
                else hi = mid - 1;
            }
            return found;
        }
    }
}
