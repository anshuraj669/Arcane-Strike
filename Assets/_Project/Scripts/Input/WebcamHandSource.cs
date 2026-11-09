using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcaneStrike.HandInput
{
    /// <summary>
    /// DEV ONLY. Receives hand joints from Tools/hand_bridge/hand_bridge.py (webcam + MediaPipe)
    /// over UDP and places them in front of the player's head, so gestures can be tested
    /// without a headset. It is never used in device builds.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class WebcamHandSource : MonoBehaviour, IHandSource, IUpperBodySource
    {
        [Tooltip("Must match --port in hand_bridge.py.")]
        [SerializeField] int port = 5065;

        [Tooltip("The player's head. Leave empty to use Camera.main.")]
        [SerializeField] Transform head;

        [Tooltip("How far the webcam is from your eyes, in metres. Press C while holding your hands at a comfortable casting distance to calibrate.")]
        [SerializeField] float cameraDistance = 0.75f;
        [Tooltip("On a screen (no headset): lift the hands and body this much (m) and bring them this far closer, so hands " +
                 "held at chest height show in the lower part of the view. A headset looks down at them instead, so it is 0 there.")]
        [SerializeField] Vector2 screenLift = new Vector2(0.24f, -0.06f);

        [Tooltip("Where your hands sit in front of the head when you press C.")]
        [SerializeField] float calibratedReach = 0.35f;

        [Tooltip("Extra offset from the head, in head space (webcams usually sit above the hands).")]
        [SerializeField] Vector3 headOffset = new Vector3(0f, -0.15f, 0f);

        [Header("Steady hands")]
        [Tooltip("How still a hand at rest is held (Hz): lower is steadier but slower to follow slow moves. One Euro filter.")]
        [SerializeField] float handSteadiness = 1.0f;
        [Tooltip("How quickly the filter lets fast moves through (per m/s of hand speed): higher keeps punches and swipes sharp.")]
        [SerializeField] float handResponse = 2.5f;
        [Tooltip("How quickly the drawn hand glides to the latest webcam pose (per second), so it moves every frame, not 30 times a second.")]
        [SerializeField] float handGlide = 28f;
        [Tooltip("How quickly a finger bone's length settles (0..1 per packet): bones keep one length instead of stretching with the noise.")]
        [Range(0.01f, 1f)]
        [SerializeField] float boneSettle = 0.06f;

        [Tooltip("The body: 0 = no smoothing, 0.9 = very smooth but laggy. Webcam data is jittery; 0.4–0.6 works well.")]
        [Range(0f, 0.95f)]
        [SerializeField] float smoothing = 0.5f;

        [Tooltip("Smoothing fades out when the wrist moves this far (m) in one packet, so fast punches and swipes aren't flattened.")]
        [SerializeField] float fastMove = 0.04f;

        [Tooltip("A hand counts as lost after this many seconds without data.")]
        [SerializeField] float timeout = 0.25f;

        [Tooltip("Calibrate automatically after you've been tracked for a second, so you don't have to press C. " +
                 "Press C any time to recalibrate (e.g. after moving closer or further).")]
        [SerializeField] bool autoCalibrate = true;

        [Tooltip("Where your eyes are relative to your nose (the bridge tracks the nose), in bridge space.")]
        [SerializeField] Vector3 eyeFromNose = new Vector3(0f, 0.04f, 0.03f);

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] class Packet { public float t; public HandPacket[] hands; public BodyPacket body; }
        [Serializable] class HandPacket { public string side; public float score; public float[] p; }
        [Serializable] class BodyPacket { public float score; public float[] p; public float[] v; }
#pragma warning restore 0649

        readonly HandFrame[] hands = { new HandFrame { Side = HandSide.Left }, new HandFrame { Side = HandSide.Right } };
        readonly float[] lastSeen = { -1f, -1f };
        readonly Vector3[][] rawDepth = { new Vector3[HandFrame.JointCount], new Vector3[HandFrame.JointCount] };

        readonly UpperBodyFrame body = new UpperBodyFrame();
        readonly Vector3[] rawBody = new Vector3[UpperBodyFrame.JointCount];
        float bodyLastSeen = -1f;
        float trackedSince = -1f;

        // After calibration, webcam space is pinned to where the head was at that moment, held in the rig's space:
        // walking and turning carry the hands along, but the head following your real head (WebcamHeadDriver's
        // lean, HeadFollowOffset) doesn't drag them with it.
        Transform rig;
        Matrix4x4 anchorInRig;
        bool anchored;
        Vector3 calibratedNose;
        Vector3 handsOnlyHeadOffset;

        /// <summary>Increases every time C calibrates. Lets other components react to a new calibration.</summary>
        public int CalibrationCount { get; private set; }
        /// <summary>True when the last calibration used the tracked body (hands sit at their real offset from your head).</summary>
        public bool CalibratedFromBody { get; private set; }
        /// <summary>Where the tracked nose was at the last body calibration, in today's world (it moves with the rig).</summary>
        public Vector3 CalibratedBodyHead => ToWorld(calibratedNose);
        /// <summary>How far the rig has been moved to follow your real head (WebcamHeadDriver); taken back out of every point.</summary>
        public Vector3 HeadFollowOffset { get; set; }

        UdpClient udp;
        Thread thread;
        volatile bool running;
        string pendingJson;
        readonly object pendingLock = new object();
        float lastPacketTime = -1f;

        public bool IsActive => lastPacketTime >= 0f && Time.time - lastPacketTime < 0.5f;
        public int Port => port;

        void Awake() => handsOnlyHeadOffset = headOffset;

        void OnEnable()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            try
            {
                udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                running = true;
                thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "WebcamHandSource" };
                thread.Start();
            }
            catch (SocketException e)
            {
                Debug.LogWarning($"[WebcamHandSource] Can't listen on UDP {port}: {e.Message}");
            }
        }

        void OnDisable()
        {
            running = false;
            udp?.Close();
            udp = null;
            thread?.Join(200);
            thread = null;
        }

        void ReceiveLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    byte[] data = udp.Receive(ref any);
                    string json = Encoding.UTF8.GetString(data);
                    lock (pendingLock) pendingJson = json;
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { return; }
            }
        }

        void Update()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;

            string json;
            lock (pendingLock) { json = pendingJson; pendingJson = null; }
            if (json != null) Apply(json);

            if (GameInput.Pressed(GameAction.CalibrateWebcam)) Calibrate();

            float glide = 1f - Mathf.Exp(-handGlide * Time.deltaTime);
            for (int i = 0; i < 2; i++)
            {
                hands[i].IsTracked = lastSeen[i] >= 0f && Time.time - lastSeen[i] < timeout;
                if (!hands[i].IsTracked || head == null) continue;
                // Every frame, in webcam space then out to the world: walking and turning carry the hand at once.
                for (int j = 0; j < HandFrame.JointCount; j++)
                {
                    shown[i][j] = Vector3.Lerp(shown[i][j], steady[i][j], glide);
                    hands[i].Joints[j] = ToWorld(shown[i][j]);
                }
            }
            body.IsTracked = bodyLastSeen >= 0f && Time.time - bodyLastSeen < timeout;
            if (autoCalibrate && CalibrationCount == 0) AutoCalibrate();
        }

        // Without calibration, someone standing back from the webcam gets their hands placed behind the camera.
        void AutoCalibrate()
        {
            bool ready = (body.IsTracked && body.Visibility[(int)UpperBodyJoint.Head] >= 0.5f) ||
                         hands[0].IsTracked || hands[1].IsTracked;
            if (!ready) trackedSince = -1f;
            else if (trackedSince < 0f) trackedSince = Time.time;
            else if (Time.time - trackedSince > 1f) Calibrate();
        }

        /// <summary>Bridge space (x right, y up, z = distance from the webcam) → world.</summary>
        Vector3 ToWorld(Vector3 raw)
        {
            Vector3 local = new Vector3(raw.x, raw.y, cameraDistance - raw.z) + headOffset;
            if (!UnityEngine.XR.XRSettings.isDeviceActive) local += new Vector3(0f, screenLift.x, screenLift.y);
            return anchored && rig != null ? (rig.localToWorldMatrix * anchorInRig).MultiplyPoint3x4(local) - HeadFollowOffset : head.TransformPoint(local);
        }

        void Apply(string json)
        {
            Packet packet;
            try { packet = JsonUtility.FromJson<Packet>(json); }
            catch (Exception) { return; }
            if (packet?.hands == null || head == null) return;

            lastPacketTime = Time.time;
            foreach (HandPacket hp in packet.hands)
            {
                if (hp.p == null || hp.p.Length < HandFrame.JointCount * 3) continue;
                int i = hp.side == "Left" ? 0 : 1;
                HandFrame frame = hands[i];
                bool fresh = !(lastSeen[i] >= 0f && Time.time - lastSeen[i] < timeout);
                float t = packet.t > 0f ? packet.t : Time.time;
                Steady(i, hp.p, t, fresh);
                if (fresh)
                    for (int j = 0; j < HandFrame.JointCount; j++) shown[i][j] = steady[i][j];

                frame.Confidence = hp.score;
                frame.Time = Time.time;
                lastSeen[i] = Time.time;
            }

            if (packet.body != null && packet.body.p != null && packet.body.p.Length >= UpperBodyFrame.JointCount * 3)
                ApplyBody(packet.body);
        }

        void ApplyBody(BodyPacket bp)
        {
            bool fresh = !(bodyLastSeen >= 0f && Time.time - bodyLastSeen < timeout);
            float k = fresh ? 0f : smoothing;
            for (int j = 0; j < UpperBodyFrame.JointCount; j++)
            {
                var raw = new Vector3(bp.p[j * 3], bp.p[j * 3 + 1], bp.p[j * 3 + 2]);
                rawBody[j] = raw;
                Vector3 world = ToWorld(raw);
                // Same rule as the hands: smooth jitter, but let fast movement (punches, dodges) through.
                float kj = fresh ? 0f : k * Mathf.Clamp01(1f - Vector3.Distance(world, body.Joints[j]) / fastMove);
                body.Joints[j] = Vector3.Lerp(world, body.Joints[j], kj);
                body.Visibility[j] = bp.v != null && j < bp.v.Length ? bp.v[j] : 1f;
            }
            body.Confidence = bp.score;
            body.Time = Time.time;
            bodyLastSeen = Time.time;
        }

        /// <summary>
        /// With the upper body visible: maps your real eyes onto the game head, so hands, arms and head line up
        /// at whatever distance you stand. Otherwise: makes the hands' webcam distance map to
        /// <see cref="calibratedReach"/> in front of the head.
        /// </summary>
        public void Calibrate()
        {
            if (head == null) return;
            if (body.IsTracked && body.Visibility[(int)UpperBodyJoint.Head] >= 0.5f)
            {
                Vector3 eye = rawBody[(int)UpperBodyJoint.Head] + eyeFromNose;
                cameraDistance = eye.z;
                headOffset = new Vector3(-eye.x, -eye.y, 0f);
                Anchor();
                CalibratedFromBody = true;
                calibratedNose = rawBody[(int)UpperBodyJoint.Head];
                CalibrationCount++;
                Debug.Log($"[WebcamHandSource] Calibrated from body: you are {eye.z:F2} m from the webcam. Hands and arms now line up with your head.");
                return;
            }

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < 2; i++)
            {
                if (!hands[i].IsTracked) continue;
                sum += rawDepth[i][(int)HandJoint.Wrist].z;
                count++;
            }
            if (count == 0)
            {
                Debug.Log("[WebcamHandSource] Calibrate: no hands visible. Hold your hands up in front of the webcam and press C again.");
                return;
            }
            cameraDistance = sum / count + calibratedReach;
            headOffset = handsOnlyHeadOffset;
            Anchor();
            CalibratedFromBody = false;
            CalibrationCount++;
            Debug.Log($"[WebcamHandSource] Calibrated: cameraDistance = {cameraDistance:F2} m (hands only; step back so your upper body is visible for full calibration)");
        }

        void Anchor()
        {
            rig = head.root;
            anchorInRig = rig.worldToLocalMatrix * head.localToWorldMatrix;
            HeadFollowOffset = Vector3.zero;
            anchored = true;
        }

        public bool TryGetBody(UpperBodyFrame frame)
        {
            if (!body.IsTracked) return false;
            frame.CopyFrom(body);
            return true;
        }

        // ---------------------------------------------------------------- steady hands

        // Each joint's parent along its finger (the wrist for the first bone of each).
        static readonly int[] Parent = { -1, 0, 1, 2, 3, 0, 5, 6, 7, 0, 9, 10, 11, 0, 13, 14, 15, 0, 17, 18, 19 };

        readonly Vector3[][] steady = { new Vector3[HandFrame.JointCount], new Vector3[HandFrame.JointCount] }; // filtered, webcam space
        readonly Vector3[][] shown = { new Vector3[HandFrame.JointCount], new Vector3[HandFrame.JointCount] };  // gliding to it
        readonly float[][] boneLength = { new float[HandFrame.JointCount], new float[HandFrame.JointCount] };
        readonly OneEuro[][] filters = { NewFilters(), NewFilters() };

        static OneEuro[] NewFilters()
        {
            var f = new OneEuro[HandFrame.JointCount];
            for (int j = 0; j < f.Length; j++) f[j] = new OneEuro();
            return f;
        }

        // The wrist is filtered where it is; every other joint relative to the wrist (the hand's shape), so the shape stays
        // still while the hand moves. Then each finger is rebuilt bone by bone at its settled length, in the filtered directions.
        void Steady(int i, float[] p, float t, bool fresh)
        {
            Vector3 Raw(int j) => new Vector3(p[j * 3], p[j * 3 + 1], p[j * 3 + 2]);
            OneEuro[] f = filters[i];
            Vector3[] relative = new Vector3[HandFrame.JointCount];
            Vector3 wrist = Raw(0);
            for (int j = 0; j < HandFrame.JointCount; j++)
            {
                rawDepth[i][j] = Raw(j);
                if (fresh) f[j].Reset();
                f[j].minCutoff = j == 0 ? handSteadiness * 0.8f : handSteadiness;
                f[j].beta = handResponse;
                relative[j] = j == 0 ? f[0].Filter(wrist, t) : f[j].Filter(Raw(j) - wrist, t);
            }
            Vector3[] outJoints = steady[i];
            float[] lengths = boneLength[i];
            outJoints[0] = relative[0];
            for (int j = 1; j < HandFrame.JointCount; j++)
            {
                Vector3 from = Parent[j] == 0 ? Vector3.zero : relative[Parent[j]];
                Vector3 bone = relative[j] - from;
                float length = bone.magnitude;
                lengths[j] = fresh || lengths[j] <= 0f ? length : Mathf.Lerp(lengths[j], length, boneSettle);
                Vector3 parentAt = Parent[j] == 0 ? outJoints[0] : outJoints[Parent[j]];
                outJoints[j] = parentAt + (length > 1e-5f ? bone / length : Vector3.up) * lengths[j];
            }
        }

        /// <summary>The One Euro filter (Casiez et al.): steady at rest, quick when moving.</summary>
        class OneEuro
        {
            public float minCutoff = 1f, beta = 2f, derivativeCutoff = 1f;
            Vector3 value, speed;
            float last;
            bool started;

            public void Reset() => started = false;

            public Vector3 Filter(Vector3 x, float t)
            {
                if (!started)
                {
                    started = true;
                    value = x;
                    speed = Vector3.zero;
                    last = t;
                    return x;
                }
                float dt = Mathf.Clamp(t - last, 1e-3f, 0.25f);
                last = t;
                speed = Vector3.Lerp(speed, (x - value) / dt, Alpha(derivativeCutoff, dt));
                value = Vector3.Lerp(value, x, Alpha(minCutoff + beta * speed.magnitude, dt));
                return value;
            }

            static float Alpha(float cutoff, float dt)
            {
                float tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 1e-3f));
                return 1f / (1f + tau / dt);
            }
        }

        public bool TryGetHand(HandSide side, HandFrame frame)
        {
            HandFrame src = hands[(int)side];
            if (!src.IsTracked) return false;
            frame.CopyFrom(src);
            return true;
        }
    }
}
