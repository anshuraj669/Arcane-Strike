using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Makes an object (a book, the lantern) rise into the air and keep bobbing and slowly turning there.
    /// Sleeps until <see cref="Activate"/>, unless Activate On Start is ticked.
    /// </summary>
    public class FloatingObject : MonoBehaviour
    {
        [Tooltip("How high it rises before hovering (m).")]
        [SerializeField] float liftHeight = 0.8f;
        [SerializeField] float riseTime = 1.8f;
        [Tooltip("Bob up and down by this much (m).")]
        public float height = 0.05f;
        public float speed = 2f;
        [Tooltip("Slow turn while floating (degrees per second).")]
        [SerializeField] float spin = 15f;
        [Tooltip("Flutter: tips back and forth by up to this many degrees while floating.")]
        [SerializeField] float wobble = 6f;
        [SerializeField] bool activateOnStart;

        Vector3 startPos, hoverPos;
        float activatedAt = -1f, phase, spun;
        Quaternion spinAxis, startRot;

        public bool IsFloating => activatedAt >= 0f;

        void Start()
        {
            if (activateOnStart) Activate();
        }

        public void Activate()
        {
            if (IsFloating) return;
            startPos = transform.position;
            startRot = transform.rotation;
            spun = 0f;
            hoverPos = startPos + Vector3.up * liftHeight + Random.insideUnitSphere * 0.1f;
            phase = Random.value * Mathf.PI * 2f;
            spinAxis = Quaternion.Euler(Random.Range(-20f, 20f), 0f, Random.Range(-20f, 20f));
            activatedAt = Time.time;
        }

        void Update()
        {
            if (!IsFloating) return;
            float t = Mathf.Clamp01((Time.time - activatedAt) / Mathf.Max(riseTime, 0.01f));
            float eased = t * t * (3f - 2f * t);
            Vector3 bob = Vector3.up * Mathf.Sin(Time.time * speed + phase) * height * eased;
            transform.position = Vector3.Lerp(startPos, hoverPos, eased) + bob;
            spun += spin * Time.deltaTime * eased;
            float w = wobble * eased;
            Quaternion flutter = Quaternion.Euler(Mathf.Sin(Time.time * speed * 1.7f + phase) * w, 0f,
                                                  Mathf.Sin(Time.time * speed * 1.3f + phase * 2f) * w);
            transform.rotation = Quaternion.AngleAxis(spun, spinAxis * Vector3.up) * startRot * flutter;
        }
    }
}
