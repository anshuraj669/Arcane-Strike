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

        public HandFrame Left => frames[0];
        public HandFrame Right => frames[1];
        public HandFrame Get(HandSide side) => frames[(int)side];

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
                f.Side = (HandSide)i;
            }
        }

        IHandSource PickSource()
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
