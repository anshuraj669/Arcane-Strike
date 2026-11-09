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

        [Header("Turn and nod")]
        [SerializeField] bool turnWithHead = true;
        [Tooltip("How much more the view turns than your head (your eyes must stay on the screen).")]
        [SerializeField] float turnGain = 2.5f;
        [SerializeField] float nodGain = 1.5f;
        [Tooltip("A held tilt up or down eases back to level over about this many seconds, so the view settles on wherever " +
                 "you look at the screen from (you may have been looking at the keyboard when it calibrated). 0 = never.")]
        [SerializeField] float nodSettle = 3f;
        [Tooltip("Head turns smaller than this (degrees) are ignored, so the view holds still while you look at the screen.")]
        [SerializeField] float deadZone = 4f;
        [Tooltip("How quickly the view catches up with your head (per second). Lower is smoother but lags.")]
        [SerializeField] float smoothing = 8f;
        [SerializeField] float maxTurn = 120f;
        [SerializeField] float maxNod = 40f;

        // The nose sits further forward of the ears than half their spacing: this turns its sideways shift into an angle.
        const float NoseReach = 1.33f;

        Vector3 applied; // how far the rig is moved now for the real head's offset
        int seenCalibration;
        float baseTurn, baseNod, turn, nod, appliedTurn, appliedNod;
        bool hasBase;

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
            }
            if (!webcam.CalibratedFromBody || !webcam.IsActive) return;

            UpperBodyFrame body = input.Body;
            if (!body.IsVisible(UpperBodyJoint.Head)) return;

            Vector3 offset = body[UpperBodyJoint.Head] - webcam.CalibratedBodyHead;
            if (offset.magnitude <= maxOffset)
            {
                // Move by the change only: walking (PlayerLocomotion) moves the rig too, and setting it outright would undo it.
                Vector3 want = offset * followStrength;
                rig.position += want - applied;
                applied = want;
                webcam.HeadFollowOffset = applied;
            }

            if (turnWithHead) Turn(body);
        }

        // Which way the head faces, from where the nose sits between the ears: turning shifts it towards one ear,
        // nodding drops or lifts it below or above them. Relative to the facing at calibration.
        void Turn(UpperBodyFrame body)
        {
            if (!body.IsVisible(UpperBodyJoint.LeftEar, 0.3f) || !body.IsVisible(UpperBodyJoint.RightEar, 0.3f)) return;
            Vector3 left = body[UpperBodyJoint.LeftEar], right = body[UpperBodyJoint.RightEar], nose = body[UpperBodyJoint.Head];
            Vector3 across = left - right;
            float half = across.magnitude * 0.5f;
            if (half < 0.02f) return;
            Vector3 fromMiddle = nose - (left + right) * 0.5f;
            // Towards the left ear = turned to the player's left = a negative turn in Unity.
            float rawTurn = -Mathf.Asin(Mathf.Clamp(Vector3.Dot(fromMiddle, across / (half * 2f)) / half / NoseReach, -1f, 1f)) * Mathf.Rad2Deg;
            // The nose dropping below the ears = looking down = a positive nod in Unity.
            float rawNod = -Mathf.Asin(Mathf.Clamp(fromMiddle.y / half / NoseReach, -1f, 1f)) * Mathf.Rad2Deg;
            if (!hasBase)
            {
                baseTurn = rawTurn;
                baseNod = rawNod;
                hasBase = true;
            }

            float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            turn = Mathf.Lerp(turn, Mathf.Clamp(Beyond(rawTurn - baseTurn) * turnGain, -maxTurn, maxTurn), k);
            // Up and down re-centres itself: only a nod tilts the view, not the angle you happen to sit at.
            if (nodSettle > 0f) baseNod = Mathf.Lerp(baseNod, rawNod, 1f - Mathf.Exp(-Time.deltaTime / nodSettle));
            nod = Mathf.Lerp(nod, Mathf.Clamp(Beyond(rawNod - baseNod) * nodGain, -maxNod, maxNod), k);

            Transform head = input.Head != null ? input.Head : Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            // Turn by the change only, round the eyes (snap turns and walking turn the rig as well).
            rig.RotateAround(head.position, Vector3.up, turn - appliedTurn);
            appliedTurn = turn;
            // Up and down: on the camera itself without a headset. With one (the Simulator) the headset owns the
            // camera, and tipping the whole rig would tip the floor, so there the Simulator's mouse look does it.
            if (!Headset(head))
            {
                head.localRotation = Quaternion.Euler(nod - appliedNod, 0f, 0f) * head.localRotation;
                appliedNod = nod;
            }
        }

        float Beyond(float angle) => Mathf.Sign(angle) * Mathf.Max(0f, Mathf.Abs(angle) - deadZone);

        // Is a headset (or the Simulator) tracking the head? Measured as the rest of the game does: eyes well above the rig.
        bool Headset(Transform head) => head.position.y - rig.position.y > 0.3f;
    }
}
