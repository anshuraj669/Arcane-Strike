using UnityEngine;

namespace ArcaneStrike.HandInput
{
    /// <summary>
    /// Single place the rest of the game reads hands from. Chooses between the real Quest hands
    /// and the webcam bridge, so spells, UI etc. never care where the data came from.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class HandInputService : MonoBehaviour
    {
        public enum SourceMode { Auto, QuestOnly, WebcamOnly }

        [Tooltip("Auto: use the webcam bridge while it is sending data (Editor only), otherwise the Quest hands.")]
        [SerializeField] SourceMode mode = SourceMode.Auto;

        [SerializeField] QuestHandSource questSource;
        [SerializeField] WebcamHandSource webcamSource;

        [Tooltip("The player's head (CenterEyeAnchor). Leave empty to use Camera.main.")]
        [SerializeField] Transform head;

        readonly HandFrame[] frames = { new HandFrame { Side = HandSide.Left }, new HandFrame { Side = HandSide.Right } };

        public Transform Head => head;
        public IHandSource ActiveSource { get; private set; }
        public string ActiveSourceName => ActiveSource == null ? "none" : ActiveSource.GetType().Name;

        /// <summary>Set while a recorded demo take replays: the hands (and body) come from it instead.</summary>
        public static IHandSource ReplaySource;
        /// <summary>Hands acted out for keyboard play (<see cref="ActedHands"/>): used while no real hands are tracked.</summary>
        public static IHandSource ActedSource;
        /// <summary>The replayed take was played with webcam hands (so gestures are judged as for the webcam).</summary>
        public static bool ReplayIsWebcam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ReplaySource = null;
            ReplayIsWebcam = false;
            ActedSource = null;
        }

        /// <summary>The hands come from the webcam bridge (live, or a replayed webcam take): rougher than Quest hands.</summary>
        public bool WebcamHands => ActiveSource is WebcamHandSource || (ActiveSource != null && ActiveSource == ReplaySource && ReplayIsWebcam);

        public HandFrame Left => frames[0];
        public HandFrame Right => frames[1];
        public HandFrame Get(HandSide side) => frames[(int)side];

        readonly UpperBodyFrame body = new UpperBodyFrame();

        /// <summary>
        /// Upper body (shoulders, elbows, wrists, hips) when the active source provides it: the webcam bridge
        /// in the editor. Not available from Quest hands yet (Movement SDK body tracking could add it), so
        /// core gameplay should rely on the head and hands and treat this as optional.
        /// </summary>
        public UpperBodyFrame Body => body;

        void Awake()
        {
            if (questSource == null) questSource = GetComponent<QuestHandSource>();
            if (webcamSource == null) webcamSource = GetComponent<WebcamHandSource>();

            // The webcam bridge is a development tool and must never ship.
            if (!Application.isEditor && webcamSource != null)
            {
                webcamSource.enabled = false;
                webcamSource = null;
            }
        }

        void Update()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;

            ActiveSource = PickSource();
            for (int i = 0; i < 2; i++)
            {
                HandFrame f = frames[i];
                f.IsTracked = ActiveSource != null && ActiveSource.TryGetHand((HandSide)i, f);
                // A hand the story is acting out (the wrist raised for the Jyoti Seal, say) stands in for that one hand
                // when the active source (a replay, or a webcam that lost it) doesn't have it, so it's never missing.
                if (!f.IsTracked && ActedSource != null && !ReferenceEquals(ActedSource, ActiveSource) && ActedSource.IsActive)
                    f.IsTracked = ActedSource.TryGetHand((HandSide)i, f);
                f.Side = (HandSide)i;
            }
            body.IsTracked = ActiveSource is IUpperBodySource bodySource && bodySource.TryGetBody(body);
        }

        IHandSource PickSource()
        {
            if (ReplaySource != null) return ReplaySource;
            IHandSource real = PickRealSource();
            if ((real == null || !real.IsActive) && ActedSource != null && ActedSource.IsActive) return ActedSource;
            return real;
        }

        IHandSource PickRealSource()
        {
            bool webcamOk = webcamSource != null && webcamSource.isActiveAndEnabled;
            bool questOk = questSource != null && questSource.isActiveAndEnabled;
            switch (mode)
            {
                case SourceMode.WebcamOnly: return webcamOk ? webcamSource : null;
                case SourceMode.QuestOnly: return questOk ? questSource : null;
                default:
                    if (webcamOk && webcamSource.IsActive) return webcamSource;
                    return questOk ? questSource : null;
            }
        }
    }
}
