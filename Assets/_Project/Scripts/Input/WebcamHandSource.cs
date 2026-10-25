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
    public class WebcamHandSource : MonoBehaviour, IHandSource
    {
        [Tooltip("Must match --port in hand_bridge.py.")]
        [SerializeField] int port = 5065;

        [Tooltip("The player's head. Leave empty to use Camera.main.")]
        [SerializeField] Transform head;

        [Tooltip("How far the webcam is from your eyes, in metres. Press C while holding your hands at a comfortable casting distance to calibrate.")]
        [SerializeField] float cameraDistance = 0.75f;

        [Tooltip("Where your hands sit in front of the head when you press C.")]
        [SerializeField] float calibratedReach = 0.35f;

        [Tooltip("Extra offset from the head, in head space (webcams usually sit above the hands).")]
        [SerializeField] Vector3 headOffset = new Vector3(0f, -0.15f, 0f);

        [Tooltip("0 = no smoothing, 0.9 = very smooth but laggy. Webcam data is jittery; 0.4–0.6 works well.")]
        [Range(0f, 0.95f)]
        [SerializeField] float smoothing = 0.5f;

        [Tooltip("Smoothing fades out when the wrist moves this far (m) in one packet, so fast punches and swipes aren't flattened.")]
        [SerializeField] float fastMove = 0.04f;

        [Tooltip("A hand counts as lost after this many seconds without data.")]
        [SerializeField] float timeout = 0.25f;

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] class Packet { public float t; public HandPacket[] hands; }
        [Serializable] class HandPacket { public string side; public float score; public float[] p; }
#pragma warning restore 0649

        readonly HandFrame[] hands = { new HandFrame { Side = HandSide.Left }, new HandFrame { Side = HandSide.Right } };
        readonly float[] lastSeen = { -1f, -1f };
        readonly Vector3[][] rawDepth = { new Vector3[HandFrame.JointCount], new Vector3[HandFrame.JointCount] };

        UdpClient udp;
        Thread thread;
        volatile bool running;
        string pendingJson;
        readonly object pendingLock = new object();
        float lastPacketTime = -1f;

        public bool IsActive => lastPacketTime >= 0f && Time.time - lastPacketTime < 0.5f;
        public int Port => port;

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

            if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) Calibrate();

            for (int i = 0; i < 2; i++)
                hands[i].IsTracked = lastSeen[i] >= 0f && Time.time - lastSeen[i] < timeout;
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
                float k = fresh ? 0f : smoothing;

                if (!fresh)
                {
                    // Smooth jitter when the hand is still, but let fast movement through.
                    var rawWrist = new Vector3(hp.p[0], hp.p[1], hp.p[2]);
                    Vector3 target = head.TransformPoint(new Vector3(rawWrist.x, rawWrist.y, cameraDistance - rawWrist.z) + headOffset);
                    float moved = Vector3.Distance(target, frame.Joints[(int)HandJoint.Wrist]);
                    k *= Mathf.Clamp01(1f - moved / fastMove);
                }

                for (int j = 0; j < HandFrame.JointCount; j++)
                {
                    // Bridge space: x = player's right, y = up, z = distance from the webcam.
                    var raw = new Vector3(hp.p[j * 3], hp.p[j * 3 + 1], hp.p[j * 3 + 2]);
                    rawDepth[i][j] = raw;
                    Vector3 local = new Vector3(raw.x, raw.y, cameraDistance - raw.z) + headOffset;
                    Vector3 world = head.TransformPoint(local);
                    frame.Joints[j] = Vector3.Lerp(world, frame.Joints[j], k);
                }

                frame.Confidence = hp.score;
                frame.Time = Time.time;
                lastSeen[i] = Time.time;
            }
        }

        /// <summary>Makes the hands' current webcam distance map to <see cref="calibratedReach"/> in front of the head.</summary>
        public void Calibrate()
        {
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
            Debug.Log($"[WebcamHandSource] Calibrated: cameraDistance = {cameraDistance:F2} m");
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
