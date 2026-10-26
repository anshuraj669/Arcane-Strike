using UnityEngine;

namespace ArcaneStrike.HandInput
{
    /// <summary>Upper-body joints shared by every body source. Order matches the webcam bridge packet.</summary>
    public enum UpperBodyJoint
    {
        Head = 0, // nose
        LeftEar, RightEar,
        LeftShoulder, RightShoulder,
        LeftElbow, RightElbow,
        LeftWrist, RightWrist,
        LeftHip, RightHip,
    }

    /// <summary>
    /// The player's upper body at one moment, in world space. Comes from the webcam bridge in the editor;
    /// on Quest it can come from the Movement SDK's body tracking later. Gameplay that must work everywhere
    /// should prefer the head (HMD) and hands, and use this for extras like forearm blocks.
    /// </summary>
    public class UpperBodyFrame
    {
        public const int JointCount = 11;

        public bool IsTracked;
        public float Confidence;
        public float Time;
        public readonly Vector3[] Joints = new Vector3[JointCount];
        public readonly float[] Visibility = new float[JointCount];

        public Vector3 this[UpperBodyJoint joint] => Joints[(int)joint];

        public bool IsVisible(UpperBodyJoint joint, float min = 0.5f) => IsTracked && Visibility[(int)joint] >= min;

        public Vector3 ShoulderCenter => (this[UpperBodyJoint.LeftShoulder] + this[UpperBodyJoint.RightShoulder]) * 0.5f;

        public float ShoulderWidth => Vector3.Distance(this[UpperBodyJoint.LeftShoulder], this[UpperBodyJoint.RightShoulder]);

        public Vector3 Shoulder(HandSide side) => this[side == HandSide.Left ? UpperBodyJoint.LeftShoulder : UpperBodyJoint.RightShoulder];
        public Vector3 Elbow(HandSide side) => this[side == HandSide.Left ? UpperBodyJoint.LeftElbow : UpperBodyJoint.RightElbow];
        public Vector3 Wrist(HandSide side) => this[side == HandSide.Left ? UpperBodyJoint.LeftWrist : UpperBodyJoint.RightWrist];

        public void CopyFrom(UpperBodyFrame other)
        {
            IsTracked = other.IsTracked;
            Confidence = other.Confidence;
            Time = other.Time;
            System.Array.Copy(other.Joints, Joints, JointCount);
            System.Array.Copy(other.Visibility, Visibility, JointCount);
        }
    }

    public interface IUpperBodySource
    {
        /// <summary>Fills <paramref name="frame"/> with the latest body. Returns false if no body is tracked.</summary>
        bool TryGetBody(UpperBodyFrame frame);
    }
}
