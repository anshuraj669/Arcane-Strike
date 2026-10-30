using System.Collections.Generic;
using UnityEngine;

namespace ArcaneStrike.Combat
{
    /// <summary>
    /// A body pose for the Kalakh: how far each bone turns from its rest pose, in the monster's own frame (x = its right,
    /// y = up, z = forward; degrees: +x pitches forward/down, +y turns right, +z rolls right), plus how far the whole body
    /// drops (crouch, in heights) and pitches. Poses blend (<see cref="Lerp"/>) and mirror left/right.
    /// </summary>
    public sealed class KalakhPose
    {
        public const int Count = 22;
        public enum B
        {
            Hips, Spine, Chest, Neck, Head,
            UpperArmL, ForearmL, HandL, UpperArmR, ForearmR, HandR,
            ThighL, ShinL, FootL, ThighR, ShinR, FootR,
            Tail1, Tail2, Tail3, Tail4, Tail5,
        }
        public static readonly string[] BoneNames =
        {
            "Hips", "Spine", "Chest", "Neck", "Head",
            "UpperArm.L", "Forearm.L", "Hand.L", "UpperArm.R", "Forearm.R", "Hand.R",
            "Thigh.L", "Shin.L", "Foot.L", "Thigh.R", "Shin.R", "Foot.R",
            "Tail1", "Tail2", "Tail3", "Tail4", "Tail5",
        };

        public readonly Vector3[] bones = new Vector3[Count];
        /// <summary>The body drops this much (in heights) and pitches forward this much (degrees).</summary>
        public float crouch, pitch;

        public KalakhPose Set(B bone, float x, float y = 0f, float z = 0f)
        {
            bones[(int)bone] = new Vector3(x, y, z);
            return this;
        }

        /// <summary>The same angles on both arms (or legs), mirrored: z and y flip for the left side.</summary>
        public KalakhPose Arms(float x, float outward = 0f, float forearm = 0f, float hand = 0f)
        {
            Set(B.UpperArmR, x, 0f, outward).Set(B.UpperArmL, x, 0f, -outward);
            Set(B.ForearmR, forearm).Set(B.ForearmL, forearm);
            return Set(B.HandR, hand).Set(B.HandL, hand);
        }

        public KalakhPose Arm(bool right, float x, float outward = 0f, float forearm = 0f, float twist = 0f)
        {
            Set(right ? B.UpperArmR : B.UpperArmL, x, right ? twist : -twist, right ? outward : -outward);
            return Set(right ? B.ForearmR : B.ForearmL, forearm);
        }

        public KalakhPose Leg(bool right, float thigh, float shin, float foot = 0f, float outward = 0f)
        {
            Set(right ? B.ThighR : B.ThighL, thigh, 0f, right ? outward : -outward);
            Set(right ? B.ShinR : B.ShinL, shin);
            return Set(right ? B.FootR : B.FootL, foot);
        }

        public KalakhPose Legs(float thigh, float shin, float foot = 0f) => Leg(true, thigh, shin, foot).Leg(false, thigh, shin, foot);

        public KalakhPose Body(float spine, float chest, float twist = 0f, float lean = 0f)
        {
            Set(B.Spine, spine, twist * 0.4f, lean * 0.5f);
            return Set(B.Chest, chest, twist * 0.6f, lean * 0.5f);
        }

        public KalakhPose Look(float neck, float head, float turn = 0f)
        {
            Set(B.Neck, neck, turn * 0.4f);
            return Set(B.Head, head, turn * 0.6f);
        }

        /// <summary>Spreads a curl through the tail: each joint a little more (x lifts, y swings right).</summary>
        public KalakhPose Tail(float lift, float swing)
        {
            for (int i = 0; i < 5; i++) Set(B.Tail1 + i, lift * (0.5f + i * 0.25f) / 1.5f, swing * (0.5f + i * 0.25f) / 1.5f);
            return this;
        }

        /// <summary>Each tail joint lifted on its own (base to tip): a big lift at the base arches it up over the back.</summary>
        public KalakhPose TailEach(float t1, float t2, float t3, float t4, float t5)
        {
            Set(B.Tail1, t1).Set(B.Tail2, t2).Set(B.Tail3, t3).Set(B.Tail4, t4);
            return Set(B.Tail5, t5);
        }

        public KalakhPose Drop(float crouchHeights, float pitchDegrees = 0f)
        {
            crouch = crouchHeights;
            pitch = pitchDegrees;
            return this;
        }

        public KalakhPose Clone()
        {
            var p = new KalakhPose { crouch = crouch, pitch = pitch };
            bones.CopyTo(p.bones, 0);
            return p;
        }

        /// <summary>Left for right: an attack with the other hand.</summary>
        public KalakhPose Mirrored()
        {
            var p = new KalakhPose { crouch = crouch, pitch = pitch };
            for (int i = 0; i < Count; i++)
            {
                int j = Mirror(i);
                Vector3 e = bones[j];
                p.bones[i] = new Vector3(e.x, -e.y, -e.z);
            }
            return p;
        }

        static int[] mirror;

        // Each bone's other-side twin (worked out once: Mirrored() runs for every left-handed blow).
        static int Mirror(int i)
        {
            if (mirror == null)
            {
                var map = new int[Count];
                for (int k = 0; k < Count; k++)
                {
                    string n = ((B)k).ToString();
                    map[k] = n.EndsWith("L") ? (int)System.Enum.Parse(typeof(B), n.Substring(0, n.Length - 1) + "R")
                           : n.EndsWith("R") ? (int)System.Enum.Parse(typeof(B), n.Substring(0, n.Length - 1) + "L") : k;
                }
                mirror = map;
            }
            return mirror[i];
        }

        public static KalakhPose Lerp(KalakhPose a, KalakhPose b, float t, KalakhPose into = null)
        {
            into ??= new KalakhPose();
            for (int i = 0; i < Count; i++) into.bones[i] = Vector3.LerpUnclamped(a.bones[i], b.bones[i], t);
            into.crouch = Mathf.LerpUnclamped(a.crouch, b.crouch, t);
            into.pitch = Mathf.LerpUnclamped(a.pitch, b.pitch, t);
            return into;
        }
    }

    /// <summary>
    /// The Kalakh's body brought to life on its skeleton (Tools/blender/rig_kalakh.py): no animation clips, everything
    /// by code. Each frame it blends the action pose the fight asks for (<see cref="Act"/>: a claw raised, a slam, a
    /// guard...) over a living base: a hunched, breathing stance; a prowling walk cycle that grows with speed (legs
    /// stepping, arms swinging, hips rolling, tail swaying); the head turning to keep its eyes on the player; and hit
    /// reactions (<see cref="Flinch"/>). A model without the bones falls back to moving as a whole.
    /// </summary>
    public class KalakhBody : MonoBehaviour
    {
        public bool Rigged { get; private set; }
        /// <summary>World position of the jaws (projectiles and breath come from here).</summary>
        public Vector3 Mouth => head != null ? head.position + transform.forward * height * 0.08f : transform.position + Vector3.up * height * 0.8f;
        public Vector3 Chest => chest != null ? chest.position : transform.position + Vector3.up * height * 0.6f;
        public Vector3 HandR => bones[(int)KalakhPose.B.HandR] != null ? bones[(int)KalakhPose.B.HandR].position : Chest;
        public Vector3 HandL => bones[(int)KalakhPose.B.HandL] != null ? bones[(int)KalakhPose.B.HandL].position : Chest;
        public Vector3 TailTip => bones[(int)KalakhPose.B.Tail5] != null ? bones[(int)KalakhPose.B.Tail5].position : transform.position - transform.forward * height * 0.6f;
        public Transform Head => head;
        /// <summary>Where a foot is (on the ground under the body without the bones).</summary>
        public Vector3 Foot(bool right)
        {
            Transform f = bones[(int)(right ? KalakhPose.B.FootR : KalakhPose.B.FootL)];
            return f != null ? f.position : transform.position + transform.right * (right ? 0.12f : -0.12f) * height;
        }
        /// <summary>A foot comes down while walking (true: the right) — dust, and the ground shaking under the big ones.</summary>
        public event System.Action<bool> OnFootfall;
        /// <summary>How far the whole body has sunk into the ground (dying), in metres.</summary>
        public float Sink { get; set; }

        /// <summary>How fast it's walking (m/s) — the gait follows.</summary>
        public float Speed { get; set; }
        /// <summary>Where to look (null: straight ahead).</summary>
        public Transform LookTarget { get; set; }

        readonly Transform[] bones = new Transform[KalakhPose.Count];
        readonly Quaternion[] rest = new Quaternion[KalakhPose.Count]; // each bone's rest rotation relative to the body
        readonly int[] parent = new int[KalakhPose.Count];
        readonly Quaternion[] delta = new Quaternion[KalakhPose.Count];
        Transform visual, head, chest;
        Vector3 visualRest;
        float height;

        KalakhPose from = Idle(), target = Idle(), current = Idle(), blended = new KalakhPose();
        float blendTime = 0.3f, blendAge = 1f;
        bool snapBlend;
        float gait, stride, breath;
        Vector3 flinch, flinchVelocity;
        float lookYaw, lookPitch;
        float hitchUntil;

        // The stance: a little more hunched than the model's rest, claws forward and ready, knees bent.
        public static KalakhPose Idle() => new KalakhPose().Body(6f, 5f).Look(-5f, -6f).Arms(-12f, 6f, -14f).Legs(-6f, 10f, -4f).Tail(-4f, 0f);

        /// <summary>Finds the bones under `model` (the Visual holder) and measures the body.</summary>
        public void Setup(Transform visualRoot, float bodyHeight)
        {
            visual = visualRoot;
            visualRest = visual.localPosition;
            height = bodyHeight;
            var all = new Dictionary<string, Transform>();
            foreach (Transform t in visual.GetComponentsInChildren<Transform>(true)) all[t.name] = t;
            Rigged = true;
            for (int i = 0; i < KalakhPose.Count; i++)
            {
                all.TryGetValue(KalakhPose.BoneNames[i], out bones[i]);
                if (bones[i] == null) Rigged = false;
            }
            if (!Rigged) return;
            for (int i = 0; i < KalakhPose.Count; i++)
            {
                rest[i] = Quaternion.Inverse(transform.rotation) * bones[i].rotation;
                parent[i] = -1;
                for (Transform p = bones[i].parent; p != null && p != visual; p = p.parent)
                {
                    int k = System.Array.IndexOf(bones, p);
                    if (k >= 0)
                    {
                        parent[i] = k;
                        break;
                    }
                }
            }
            head = bones[(int)KalakhPose.B.Head];
            chest = bones[(int)KalakhPose.B.Chest];
            foreach (var a in visual.GetComponentsInChildren<Animator>(true)) a.enabled = false; // the code moves the bones
        }

        /// <summary>Blends to an action pose over `seconds` (snappy for strikes, slow for wind-ups).</summary>
        public void Act(KalakhPose pose, float seconds, bool snap = false)
        {
            from = current.Clone();
            target = pose ?? Idle();
            blendTime = Mathf.Max(0.01f, seconds);
            blendAge = 0f;
            snapBlend = snap;
        }

        /// <summary>Back to the plain stance.</summary>
        public void Relax(float seconds = 0.35f) => Act(Idle(), seconds);

        /// <summary>A blow lands: the body freezes for a split second (its weight taking the hit), then reels.</summary>
        public void Hitch(float seconds) => hitchUntil = Mathf.Max(hitchUntil, Time.time + seconds);

        /// <summary>Knocked from `direction` (world): the body rocks away and springs back.</summary>
        public void Flinch(Vector3 direction, float strength)
        {
            Vector3 local = transform.InverseTransformDirection(direction);
            flinchVelocity += new Vector3(local.z, local.x, 0f) * 220f * strength;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime * (Time.time < hitchUntil ? 0.08f : 1f); // (a hit freezes it for a moment)
            blendAge += dt;
            float k = Mathf.Clamp01(blendAge / blendTime);
            k = snapBlend ? 1f - (1f - k) * (1f - k) * (1f - k) : k * k * (3f - 2f * k); // strikes whip in; the rest ease
            KalakhPose.Lerp(from, target, k, current);

            // The living base: breathing, the walk, the hit reaction, the look.
            breath += dt * 1.6f;
            gait = Mathf.MoveTowards(gait, Mathf.Clamp01(Speed / 1.6f), dt * 3f);
            float before = Mathf.Sin(stride);
            stride += dt * Mathf.Lerp(0f, 7f, Mathf.Sqrt(gait)) + dt * Speed * 1.5f;
            if (gait > 0.3f && Mathf.Sign(before) != Mathf.Sign(Mathf.Sin(stride))) OnFootfall?.Invoke(before < 0f);
            flinchVelocity += (-flinch * 160f - flinchVelocity * 14f) * dt; // a damped spring
            flinch += flinchVelocity * dt;

            float s = Mathf.Sin(stride), c = Mathf.Cos(stride);
            KalakhPose.Lerp(current, current, 0f, blended); // (a copy to add the living base onto)
            Add(blended, KalakhPose.B.Chest, -2.5f * Mathf.Sin(breath), 0f, 0f);
            Add(blended, KalakhPose.B.Neck, 1.5f * Mathf.Sin(breath + 0.6f), 0f, 0f);
            float g = gait;
            Add(blended, KalakhPose.B.ThighR, -28f * s * g, 0f, 0f);
            Add(blended, KalakhPose.B.ThighL, 28f * s * g, 0f, 0f);
            Add(blended, KalakhPose.B.ShinR, 34f * Mathf.Max(0f, c) * g, 0f, 0f);
            Add(blended, KalakhPose.B.ShinL, 34f * Mathf.Max(0f, -c) * g, 0f, 0f);
            Add(blended, KalakhPose.B.FootR, -14f * Mathf.Max(0f, -s) * g, 0f, 0f);
            Add(blended, KalakhPose.B.FootL, -14f * Mathf.Max(0f, s) * g, 0f, 0f);
            Add(blended, KalakhPose.B.UpperArmR, 18f * s * g, 0f, 0f);
            Add(blended, KalakhPose.B.UpperArmL, -18f * s * g, 0f, 0f);
            Add(blended, KalakhPose.B.Hips, 0f, 6f * s * g, 4f * c * g);
            Add(blended, KalakhPose.B.Chest, 0f, -8f * s * g, 0f);
            for (int i = 0; i < 5; i++)
                Add(blended, KalakhPose.B.Tail1 + i, 3f * Mathf.Sin(breath * 0.7f + i * 0.6f), (6f + 6f * g) * Mathf.Sin(stride * 0.5f + breath * 0.4f - i * 0.7f), 0f);
            Add(blended, KalakhPose.B.Spine, flinch.x * 0.5f, 0f, -flinch.y * 0.5f);
            Add(blended, KalakhPose.B.Chest, flinch.x * 0.7f, 0f, -flinch.y * 0.7f);
            Add(blended, KalakhPose.B.Head, flinch.x * 0.4f, 0f, 0f);

            // Eyes on the player: the neck and head take the turn (within reason).
            if (LookTarget != null && head != null)
            {
                Vector3 to = transform.InverseTransformPoint(LookTarget.position) - transform.InverseTransformPoint(head.position);
                float yaw = Mathf.Clamp(Mathf.Atan2(to.x, Mathf.Max(0.01f, to.z)) * Mathf.Rad2Deg, -55f, 55f);
                float pitch = Mathf.Clamp(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, -35f, 35f);
                lookYaw = Mathf.Lerp(lookYaw, yaw, 1f - Mathf.Exp(-6f * dt));
                lookPitch = Mathf.Lerp(lookPitch, pitch, 1f - Mathf.Exp(-6f * dt));
                Add(blended, KalakhPose.B.Neck, lookPitch * 0.4f, lookYaw * 0.4f, 0f);
                Add(blended, KalakhPose.B.Head, lookPitch * 0.6f, lookYaw * 0.6f, 0f);
            }

            Apply(blended, dt);
        }

        static void Add(KalakhPose p, KalakhPose.B b, float x, float y, float z) => p.bones[(int)b] += new Vector3(x, y, z);

        void Apply(KalakhPose pose, float dt)
        {
            float bob = Mathf.Abs(Mathf.Sin(stride)) * 0.035f * height * gait;
            if (visual != null)
            {
                visual.localPosition = visualRest + Vector3.down * (pose.crouch * height + Sink) + Vector3.up * bob;
                visual.localRotation = Quaternion.Euler(pose.pitch + flinch.x * 0.15f, 0f, -flinch.y * 0.15f);
            }
            if (!Rigged) return;
            Quaternion body = transform.rotation * Quaternion.Euler(pose.pitch, 0f, 0f);
            for (int i = 0; i < KalakhPose.Count; i++)
            {
                Quaternion own = Quaternion.Euler(pose.bones[i]);
                delta[i] = parent[i] >= 0 ? delta[parent[i]] * own : own;
                bones[i].rotation = body * delta[i] * rest[i];
            }
        }
    }
}
