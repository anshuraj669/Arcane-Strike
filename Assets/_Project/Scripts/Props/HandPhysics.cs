using ArcaneStrike.HandInput;
using ArcaneStrike.Story;
using UnityEngine;

namespace ArcaneStrike.Props
{
    /// <summary>
    /// Solid hands: small kinematic spheres on the palm and fingertips that shove physics objects, so a swipe
    /// knocks a jar off the shelf. They only push while the hand is open and moving with some speed: a slow
    /// reach to pick something up (see <see cref="Grabbable"/>) doesn't nudge it away, and a closed hand that
    /// is holding something doesn't fight it.
    /// </summary>
    public class HandPhysics : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [Tooltip("The hand must move at least this fast (m/s) to push things.")]
        [SerializeField] float pushSpeed = 0.35f;
        [SerializeField] float palmRadius = 0.035f;
        [SerializeField] float fingertipRadius = 0.012f;
        [Tooltip("Webcam depth is rough, so its hands push things by accident. Off by default.")]
        [SerializeField] bool withWebcamHands;

        static readonly HandJoint[] Tips = { HandJoint.ThumbTip, HandJoint.IndexTip, HandJoint.MiddleTip, HandJoint.RingTip, HandJoint.PinkyTip };

        readonly Rigidbody[,] bodies = new Rigidbody[2, 6];
        readonly Vector3[] lastPalm = new Vector3[2];
        readonly float[] speed = new float[2];
        readonly bool[] wasTracked = new bool[2];

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            for (int hand = 0; hand < 2; hand++)
                for (int i = 0; i < 6; i++)
                {
                    var go = new GameObject($"{(HandSide)hand} {(i == 0 ? "Palm" : Tips[i - 1].ToString())}");
                    go.transform.SetParent(transform, false);
                    var sphere = go.AddComponent<SphereCollider>();
                    sphere.radius = i == 0 ? palmRadius : fingertipRadius;
                    var body = go.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    body.useGravity = false;
                    body.interpolation = RigidbodyInterpolation.Interpolate;
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    bodies[hand, i] = body;
                    go.SetActive(false);
                }
        }

        void FixedUpdate()
        {
            if (input == null) return;
            bool webcam = input.ActiveSource is WebcamHandSource;
            for (int hand = 0; hand < 2; hand++)
            {
                HandFrame f = input.Get((HandSide)hand);
                Vector3 palm = f.PalmCenter;
                // Tracking just came back: the hand "moved" from where it was lost in one step. That's not a swipe,
                // and must not knock everything near it flying.
                if (f.IsTracked && !wasTracked[hand])
                {
                    lastPalm[hand] = palm;
                    speed[hand] = 0f;
                }
                wasTracked[hand] = f.IsTracked;
                speed[hand] = Mathf.Lerp(speed[hand], Vector3.Distance(palm, lastPalm[hand]) / Time.fixedDeltaTime, 0.5f);
                lastPalm[hand] = palm;

                bool closed = StoryFx.IsFist(f) || Vector3.Distance(f[HandJoint.ThumbTip], f[HandJoint.IndexTip]) < 0.03f;
                bool push = f.IsTracked && (withWebcamHands || !webcam) && !closed && speed[hand] > pushSpeed;
                for (int i = 0; i < 6; i++)
                {
                    Rigidbody body = bodies[hand, i];
                    Vector3 target = i == 0 ? palm : f[Tips[i - 1]];
                    if (push && !body.gameObject.activeSelf)
                    {
                        body.gameObject.SetActive(true);
                        body.position = target; // appear where the hand is, without sweeping through things
                    }
                    else if (!push && body.gameObject.activeSelf)
                    {
                        body.gameObject.SetActive(false);
                    }
                    if (push) body.MovePosition(target);
                }
            }
        }
    }
}
