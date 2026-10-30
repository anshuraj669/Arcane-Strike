using System.Collections;
using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The way into the Rift from the attic: a tear (<see cref="RiftTear"/>) standing on the floor in front of the
    /// player after the Ascension. Walking into it (or <see cref="Enter"/>, E or N on the keyboard) pulls the player through:
    /// light is sucked into the tear, the view goes violet and black, and the Rift scene loads.
    /// </summary>
    public class RiftGate : MonoBehaviour
    {
        [Tooltip("The scene on the other side (must be in Build Settings: Arcane Strike → Create Shadow Rift Scene adds it).")]
        public string riftScene = "ShadowRift";
        [Tooltip("How close (m, across the floor) the player's head must come to walk in.")]
        [SerializeField] float enterDistance = 0.6f;

        /// <summary>The open gate in this scene, if any.</summary>
        public static RiftGate Current { get; private set; }
        public RiftTear Tear { get; private set; }

        Transform head;
        bool entering;

        /// <summary>Tears a gate open on the floor at `position`, its face turned along `facing`.</summary>
        public static RiftGate Open(Vector3 position, Quaternion facing)
        {
            var go = new GameObject("RiftGate");
            go.transform.SetPositionAndRotation(position, facing);
            var gate = go.AddComponent<RiftGate>();
            gate.Tear = RiftTear.Create(position, facing, 2.3f, 1.1f);
            gate.Tear.transform.SetParent(go.transform, true);
            gate.StartCoroutine(gate.Tear.Open(1.8f));
            Current = gate;
            return gate;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        void Update()
        {
            if (entering || Tear == null || Tear.Openness < 0.8f) return;
            if (head == null) head = StoryFx.Head(FindAnyObjectByType<HandInputService>());
            if (head == null) return;
            Vector3 d = head.position - transform.position;
            d.y = 0f;
            if (d.magnitude < enterDistance) Enter();
        }

        public void Enter()
        {
            if (entering) return;
            entering = true;
            StartCoroutine(Through());
        }

        IEnumerator Through()
        {
            Debug.Log("[Attic] Into the rift");
            var locomotion = FindAnyObjectByType<PlayerLocomotion>();
            if (locomotion != null) locomotion.Paused = true;
            yield return ScreenFade.To(new Color(0.35f, 0.1f, 0.5f, 0.6f), 0.7f);
            if (!string.IsNullOrEmpty(riftScene) && Application.CanStreamedLevelBeLoaded(riftScene))
            {
                ScreenFade.LoadScene(riftScene, new Color(0.05f, 0f, 0.08f, 1f), 0.7f);
                yield break;
            }
            Debug.LogWarning($"[Attic] Scene '{riftScene}' isn't in Build Settings yet: run Arcane Strike → Create Shadow Rift Scene.");
            yield return ScreenFade.To(new Color(0.05f, 0f, 0.08f, 0f), 1f);
            if (locomotion != null) locomotion.Paused = false; // (the gate stays shut until the scene exists)
        }
    }
}
