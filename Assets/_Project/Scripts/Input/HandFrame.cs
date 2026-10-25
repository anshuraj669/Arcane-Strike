using UnityEngine;

namespace ArcaneStrike.HandInput
{
    public enum HandSide { Left = 0, Right = 1 }

    /// <summary>
    /// The 21 joints shared by every hand source. The order matches MediaPipe Hands,
    /// and each one has an equivalent joint on the Meta Interaction SDK hand skeleton.
    /// </summary>
    public enum HandJoint
    {
        Wrist = 0,
        ThumbMetacarpal, ThumbProximal, ThumbDistal, ThumbTip,
        IndexProximal, IndexIntermediate, IndexDistal, IndexTip,
        MiddleProximal, MiddleIntermediate, MiddleDistal, MiddleTip,
        RingProximal, RingIntermediate, RingDistal, RingTip,
        PinkyProximal, PinkyIntermediate, PinkyDistal, PinkyTip,
    }

    public enum Finger { Thumb = 0, Index, Middle, Ring, Pinky }

    /// <summary>
    /// One hand at one moment, in world space. Every gesture is computed from this,
    /// so the same recognizer works with the webcam bridge and with a real Quest.
    /// </summary>
    public class HandFrame
    {
        public const int JointCount = 21;

        public HandSide Side;
        public bool IsTracked;
        public float Confidence;
        public float Time;
        public readonly Vector3[] Joints = new Vector3[JointCount];

        public Vector3 this[HandJoint joint] => Joints[(int)joint];

        public Vector3 Wrist => Joints[(int)HandJoint.Wrist];

        public Vector3 PalmCenter =>
            (Joints[(int)HandJoint.Wrist] + Joints[(int)HandJoint.IndexProximal] +
             Joints[(int)HandJoint.MiddleProximal] + Joints[(int)HandJoint.PinkyProximal]) * 0.25f;

        /// <summary>Points out of the palm (the direction the palm "faces").</summary>
        public Vector3 PalmNormal
        {
            get
            {
                Vector3 toIndex = this[HandJoint.IndexProximal] - Wrist;
                Vector3 toPinky = this[HandJoint.PinkyProximal] - Wrist;
                Vector3 n = Side == HandSide.Right ? Vector3.Cross(toPinky, toIndex) : Vector3.Cross(toIndex, toPinky);
                return n.normalized;
            }
        }

        /// <summary>From the wrist towards the middle knuckle.</summary>
        public Vector3 HandForward => (this[HandJoint.MiddleProximal] - Wrist).normalized;

        /// <summary>
        /// 1 = finger fully straight, about 0.4 or less = curled into the palm.
        /// It's a ratio of lengths, so hand size and tracking scale don't affect it.
        /// </summary>
        public float Straightness(Finger finger)
        {
            int b = finger == Finger.Thumb ? (int)HandJoint.ThumbMetacarpal : 1 + (int)finger * 4;
            Vector3 j0 = Joints[b], j1 = Joints[b + 1], j2 = Joints[b + 2], j3 = Joints[b + 3];
            float path = Vector3.Distance(j0, j1) + Vector3.Distance(j1, j2) + Vector3.Distance(j2, j3);
            return path < 1e-5f ? 0f : Vector3.Distance(j0, j3) / path;
        }

        public void CopyFrom(HandFrame other)
        {
            Side = other.Side;
            IsTracked = other.IsTracked;
            Confidence = other.Confidence;
            Time = other.Time;
            System.Array.Copy(other.Joints, Joints, JointCount);
        }
    }

    public interface IHandSource
    {
        /// <summary>True when this source currently has live data for at least one hand.</summary>
        bool IsActive { get; }

        /// <summary>Fills <paramref name="frame"/> with the latest data. Returns false if that hand isn't tracked.</summary>
        bool TryGetHand(HandSide side, HandFrame frame);
    }
}
