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
        [SerializeField] bool activateOnStart;

        Vector3 startPos, hoverPos;
        float activatedAt = -1f, phase;
        Quaternion spinAxis;

        public bool IsFloating => activatedAt >= 0f;

        void Start()
        {
            if (activateOnStart) Activate();
        }

        public void Activate()
        {
            if (IsFloating) return;
            startPos = transform.position;
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
            transform.rotation = Quaternion.AngleAxis(spin * Time.deltaTime * eased, spinAxis * Vector3.up) * transform.rotation;
        }
    }
}
