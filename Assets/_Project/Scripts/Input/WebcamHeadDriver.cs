using UnityEngine;

namespace ArcaneStrike.HandInput
{
    /// <summary>
    /// DEV ONLY. Moves the camera rig so the game head follows your real head as seen by the webcam,
    /// so leaning and ducking work in the editor like they will with a headset (and DodgeDetector sees them).
    /// Starts after a body calibration (press C with your upper body in view). Never runs in device builds,
    /// where the headset moves the head itself.
    /// </summary>
    [DefaultExecutionOrder(-45)]
    public class WebcamHeadDriver : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] WebcamHandSource webcam;
        [Tooltip("The camera rig root to move. Leave empty to use the root of Camera.main.")]
        [SerializeField] Transform rig;

        [Range(0f, 1f)]
        [Tooltip("1 = the game head moves exactly as much as your real head.")]
        [SerializeField] float followStrength = 1f;
        [Tooltip("Ignore head moves further than this (m) from where you calibrated (bad tracking frames).")]
        [SerializeField] float maxOffset = 0.6f;

        Vector3 applied; // how far the rig is moved now for the real head's offset
        int seenCalibration;

        void Awake()
        {
            if (!Application.isEditor) { enabled = false; return; }
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (webcam == null) webcam = FindAnyObjectByType<WebcamHandSource>();
            if (rig == null && Camera.main != null) rig = Camera.main.transform.root;
        }

        void Update()
        {
            if (webcam == null || rig == null || input == null) return;

            if (webcam.CalibrationCount != seenCalibration)
            {
                seenCalibration = webcam.CalibrationCount;
                applied = Vector3.zero; // (the rig where it is now is the calibrated centre)
            }
            if (!webcam.CalibratedFromBody || !webcam.IsActive) return;

            UpperBodyFrame body = input.Body;
            if (!body.IsVisible(UpperBodyJoint.Head)) return;

            Vector3 offset = body[UpperBodyJoint.Head] - webcam.CalibratedBodyHead;
            if (offset.magnitude > maxOffset) return;
            // Move by the change only: walking (PlayerLocomotion) moves the rig too, and setting it outright would undo it.
            Vector3 want = offset * followStrength;
            rig.position += want - applied;
            applied = want;
        }
    }
}
