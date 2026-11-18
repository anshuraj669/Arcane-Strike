using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcaneStrike.HandInput
{
    /// <summary>
    /// DEV ONLY. The game head follows your real head as the webcam sees it, so you can test without a headset:
    /// - leaning and ducking move the view (and DodgeDetector sees them);
    /// - turning your head left or right turns the view, and nodding up or down tilts it. Your eyes have to stay on
    ///   the screen, so the turn is multiplied (turn 20° and the view turns about 50°).
    /// Starts after a body calibration (press C with your upper body in view; it also calibrates by itself), and
    /// measures from where you were facing then. Works in the plain editor and in the Meta XR Simulator (there it
    /// turns the rig; up and down stay with the Simulator's mouse look). Never runs in device builds, where the
    /// headset moves the head itself. Added by itself to every scene that has the webcam bridge.
    /// </summary>
    [DefaultExecutionOrder(-45)]
    public class WebcamHeadDriver : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] WebcamHandSource webcam;
        [Tooltip("The camera rig root to move. Leave empty to use the root of Camera.main.")]
        [SerializeField] Transform rig;

        [Header("Lean and duck")]
        [Range(0f, 1f)]
        [Tooltip("1 = the game head moves exactly as much as your real head.")]
        [SerializeField] float followStrength = 1f;
        [Tooltip("Ignore head moves further than this (m) from where you calibrated (bad tracking frames).")]
        [SerializeField] float maxOffset = 0.6f;
        [Tooltip("Head moves smaller than this (m) are ignored: the webcam's jitter would otherwise shake the whole view.")]
        [SerializeField] float leanDeadZone = 0.02f;
        [Tooltip("How still the view holds while your head is still (Hz). One Euro filter: lower is steadier.")]
        [SerializeField] float steadiness = 0.5f;

        [Header("Turn and nod")]
        [SerializeField] bool turnWithHead = true;
        [Tooltip("How much more the view turns than your head (your eyes must stay on the screen).")]
        [SerializeField] float turnGain = 2.5f;
        [SerializeField] float nodGain = 1f;
        [Tooltip("A held tilt up or down eases back to level over about this many seconds, so the view settles on wherever " +
                 "you look at the screen from (you may have been looking at the keyboard when it calibrated). 0 = never.")]
        [SerializeField] float nodSettle = 3f;
        [Tooltip("Head turns smaller than this (degrees) are ignored, so the view holds still while you look at the screen.")]
        [SerializeField] float deadZone = 5f;
        [Tooltip("How quickly the view catches up with your head (per second). Lower is smoother but lags.")]
        [SerializeField] float smoothing = 5f;
        [SerializeField] float maxTurn = 120f;
        [SerializeField] float maxNod = 30f;

        // The nose sits further forward of the ears than half their spacing: this turns its sideways shift into an angle.
        const float NoseReach = 1.33f;

        Vector3 applied; // how far the rig is moved now for the real head's offset
        int seenCalibration;
        /// <summary>Running totals of everything this driver has done to the view (for recording demo takes): how
        /// far it moved the rig, how far it turned it, and how far it tipped the camera (degrees).</summary>
        public Vector3 TotalLean { get; private set; }
        public float TotalTurn { get; private set; }
        public float TotalNod { get; private set; }

        float baseTurn, baseNod, turn, nod, appliedTurn, appliedNod, lastBodyTime = -1f, rawTurn, rawNod;
        bool hasBase, hasAngles;
        Vector3 leanTarget;
        readonly OneEuro3 leanFilter = new OneEuro3();
        readonly OneEuro1 turnFilter = new OneEuro1(), nodFilter = new OneEuro1();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            if (!Application.isEditor) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            AddToScene();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AddToScene();

        static void AddToScene()
        {
            var source = FindAnyObjectByType<WebcamHandSource>();
            if (source != null && FindAnyObjectByType<WebcamHeadDriver>() == null) source.gameObject.AddComponent<WebcamHeadDriver>();
        }

        void Awake()
        {
            if (!Application.isEditor) { enabled = false; return; }
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (webcam == null) webcam = FindAnyObjectByType<WebcamHandSource>();
        }

        void Update()
        {
            if (rig == null && Camera.main != null) rig = Camera.main.transform.root;
            if (webcam == null || rig == null || input == null) return;

            if (webcam.CalibrationCount != seenCalibration)
            {
                seenCalibration = webcam.CalibrationCount;
                applied = Vector3.zero; // (the rig where it is now is the calibrated centre)
                hasBase = false;        // (and the way you face now is straight ahead)
                turn = nod = appliedTurn = appliedNod = 0f;
                leanTarget = Vector3.zero;
                hasAngles = false;
                leanFilter.Reset();
                turnFilter.Reset();
                nodFilter.Reset();
            }
            if (!webcam.CalibratedFromBody || !webcam.IsActive) return;

            UpperBodyFrame body = input.Body;
            if (!body.IsVisible(UpperBodyJoint.Head)) return;

            // Only a new webcam frame is new information: filter once per frame from the webcam, not per game frame.
            bool fresh = body.Time != lastBodyTime;
            lastBodyTime = body.Time;
            if (fresh)
            {
                Vector3 offset = body[UpperBodyJoint.Head] - webcam.CalibratedBodyHead;
                if (offset.magnitude <= maxOffset)
                {
                    leanFilter.minCutoff = steadiness;
                    leanFilter.beta = 1.5f;
                    Vector3 steady = leanFilter.Filter(offset, body.Time);
                    // Small moves are the webcam's jitter, not the player's: hold still inside the dead zone.
                    float size = steady.magnitude;
                    leanTarget = size > leanDeadZone ? steady * ((size - leanDeadZone) / size) * followStrength : Vector3.zero;
                }
                if (turnWithHead) MeasureFacing(body);
            }
            // Move by the change only: walking (PlayerLocomotion) moves the rig too, and setting it outright would undo it.
            Vector3 want = Vector3.Lerp(applied, leanTarget, 1f - Mathf.Exp(-smoothing * 2f * Time.deltaTime));
            rig.position += want - applied;
            TotalLean += want - applied;
            applied = want;
            webcam.HeadFollowOffset = applied;

            if (turnWithHead && hasAngles) Turn();
        }

        // Which way the head faces, from where the nose sits between the ears: turning shifts it towards one ear,
        // nodding drops or lifts it below or above them. Relative to the facing at calibration.
        void MeasureFacing(UpperBodyFrame body)
        {
            if (!body.IsVisible(UpperBodyJoint.LeftEar, 0.3f) || !body.IsVisible(UpperBodyJoint.RightEar, 0.3f)) return;
            Vector3 left = body[UpperBodyJoint.LeftEar], right = body[UpperBodyJoint.RightEar], nose = body[UpperBodyJoint.Head];
            Vector3 across = left - right;
            float half = across.magnitude * 0.5f;
            if (half < 0.02f) return;
            Vector3 fromMiddle = nose - (left + right) * 0.5f;
            // Towards the left ear = turned to the player's left = a negative turn in Unity.
            float measuredTurn = -Mathf.Asin(Mathf.Clamp(Vector3.Dot(fromMiddle, across / (half * 2f)) / half / NoseReach, -1f, 1f)) * Mathf.Rad2Deg;
            // The nose dropping below the ears = looking down = a positive nod in Unity.
            float measuredNod = -Mathf.Asin(Mathf.Clamp(fromMiddle.y / half / NoseReach, -1f, 1f)) * Mathf.Rad2Deg;
            turnFilter.minCutoff = nodFilter.minCutoff = steadiness;
            turnFilter.beta = nodFilter.beta = 0.03f; // (per degree per second)
            rawTurn = turnFilter.Filter(measuredTurn, body.Time);
            rawNod = nodFilter.Filter(measuredNod, body.Time);
            hasAngles = true;
            if (!hasBase)
            {
                baseTurn = rawTurn;
                baseNod = rawNod;
                hasBase = true;
            }
        }

        // Turns the view towards the measured facing, smoothly, every game frame.
        void Turn()
        {
            float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            turn = Mathf.Lerp(turn, Mathf.Clamp(Beyond(rawTurn - baseTurn) * turnGain, -maxTurn, maxTurn), k);
            // Up and down re-centres itself: only a nod tilts the view, not the angle you happen to sit at.
            if (nodSettle > 0f) baseNod = Mathf.Lerp(baseNod, rawNod, 1f - Mathf.Exp(-Time.deltaTime / nodSettle));
            nod = Mathf.Lerp(nod, Mathf.Clamp(Beyond(rawNod - baseNod) * nodGain, -maxNod, maxNod), k);

            Transform head = input.Head != null ? input.Head : Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            // Turn by the change only, round the eyes (snap turns and walking turn the rig as well).
            rig.RotateAround(head.position, Vector3.up, turn - appliedTurn);
            TotalTurn += turn - appliedTurn;
            appliedTurn = turn;
            // Up and down: on the camera itself without a headset. With one (the Simulator) the headset owns the
            // camera, and tipping the whole rig would tip the floor, so there the Simulator's mouse look does it.
            if (!Headset(head))
            {
                head.localRotation = Quaternion.Euler(nod - appliedNod, 0f, 0f) * head.localRotation;
                TotalNod += nod - appliedNod;
                appliedNod = nod;
            }
        }

        float Beyond(float angle) => Mathf.Sign(angle) * Mathf.Max(0f, Mathf.Abs(angle) - deadZone);

        // Is a headset (or the Simulator) tracking the head? Measured as the rest of the game does: eyes well above the rig.
        bool Headset(Transform head) => head.position.y - rig.position.y > 0.3f;
    }
}
