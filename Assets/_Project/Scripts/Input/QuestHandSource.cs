using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace ArcaneStrike.HandInput
{
    /// <summary>
    /// Reads the real tracked hands from the Meta Interaction SDK (the Interactions Rig)
    /// and converts them to the shared 21-joint <see cref="HandFrame"/>.
    /// </summary>
    public class QuestHandSource : MonoBehaviour, IHandSource
    {
        [Tooltip("Optional. Leave empty to find the rig's tracked left hand automatically.")]
        [SerializeField, Interface(typeof(IHand))] Object leftHand;

        [Tooltip("Optional. Leave empty to find the rig's tracked right hand automatically.")]
        [SerializeField, Interface(typeof(IHand))] Object rightHand;

        // Same order as HandJoint. These names exist in both the OpenXR and the legacy OVR hand skeletons.
        static readonly HandJointId[] JointMap =
        {
            HandJointId.HandWristRoot,
            HandJointId.HandThumb1, HandJointId.HandThumb2, HandJointId.HandThumb3, HandJointId.HandThumbTip,
            HandJointId.HandIndex1, HandJointId.HandIndex2, HandJointId.HandIndex3, HandJointId.HandIndexTip,
            HandJointId.HandMiddle1, HandJointId.HandMiddle2, HandJointId.HandMiddle3, HandJointId.HandMiddleTip,
            HandJointId.HandRing1, HandJointId.HandRing2, HandJointId.HandRing3, HandJointId.HandRingTip,
            HandJointId.HandPinky1, HandJointId.HandPinky2, HandJointId.HandPinky3, HandJointId.HandPinkyTip,
        };

        readonly IHand[] hands = new IHand[2];

        public bool IsActive => IsTracked(Get(HandSide.Left)) || IsTracked(Get(HandSide.Right));

        void Awake()
        {
            hands[0] = leftHand as IHand;
            hands[1] = rightHand as IHand;
        }

        IHand Get(HandSide side)
        {
            int i = (int)side;
            if (hands[i] == null || (hands[i] is MonoBehaviour mb && mb == null))
                hands[i] = FindTrackedHand(side == HandSide.Left ? Handedness.Left : Handedness.Right);
            return hands[i];
        }

        // The rig has several Hand components per side (tracked hand, synthetic hand, ...).
        // Gestures must use the real tracked data, so skip the synthetic ones.
        static IHand FindTrackedHand(Handedness handedness)
        {
            IHand fallback = null;
            foreach (Hand hand in FindObjectsByType<Hand>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (hand.Handedness != handedness) continue;
                if (hand is SyntheticHand || hand.name.Contains("Synthetic"))
                {
                    fallback ??= hand;
                    continue;
                }
                return hand;
            }
            return fallback;
        }

        static bool IsTracked(IHand hand) => hand != null && hand.IsConnected && hand.IsTrackedDataValid;

        public bool TryGetHand(HandSide side, HandFrame frame)
        {
            IHand hand = Get(side);
            if (!IsTracked(hand)) return false;

            for (int j = 0; j < HandFrame.JointCount; j++)
            {
                if (!hand.GetJointPose(JointMap[j], out Pose pose)) return false;
                frame.Joints[j] = pose.position;
            }

            frame.Side = side;
            frame.IsTracked = true;
            frame.Confidence = hand.IsHighConfidence ? 1f : 0.3f;
            frame.Time = Time.time;
            return true;
        }
    }
}
