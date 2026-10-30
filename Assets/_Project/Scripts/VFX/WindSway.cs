using UnityEngine;

namespace ArcaneStrike.VFX
{
    /// <summary>
    /// The night wind, shared by everything that moves in it: a steady direction and gusts that roll across the
    /// garden (a gust reaches trees further downwind a moment later). Gust(position) is 0 (still) to 1 (strong).
    /// </summary>
    public static class Wind
    {
        public static Vector3 Direction = new Vector3(1f, 0f, 0.35f).normalized;
        public static float Strength = 1f; // 0 still .. 2 stormy
        /// <summary>How fast a gust travels across the ground (m/s).</summary>
        public static float GustSpeed = 7f;

        public static float Gust(Vector3 position)
        {
            float t = Time.time - Vector3.Dot(position, Direction) / GustSpeed;
            float slow = Mathf.PerlinNoise(t * 0.22f, 0.37f);           // the wind rising and falling
            float gusts = Mathf.PerlinNoise(t * 0.9f, 4.1f);            // sharper gusts on top
            return Mathf.Clamp01(slow * 0.6f + gusts * gusts * 0.7f) * Strength;
        }
    }

    /// <summary>
    /// Sways a plant or tree in the wind: it leans away from the wind as gusts come through, with a little
    /// flutter. The pivot must be at the base (it bends there). Cheap: one rotation per object, no shader.
    /// </summary>
    public class WindSway : MonoBehaviour
    {
        [Tooltip("Lean in the strongest gust (degrees).")]
        [SerializeField] float lean = 3.5f;
        [Tooltip("Quick back-and-forth flutter (degrees).")]
        [SerializeField] float flutter = 0.8f;

        Quaternion rest;
        float phase;

        void Start()
        {
            rest = transform.rotation;
            phase = Random.value * 100f;
        }

        void Update()
        {
            float gust = Wind.Gust(transform.position);
            float t = Time.time + phase;
            float angle = lean * (0.25f + 0.75f * gust) + flutter * (0.3f + gust) * Mathf.Sin(t * 2.3f) * Mathf.Sin(t * 0.7f + 1f);
            Vector3 axis = Vector3.Cross(Vector3.up, Wind.Direction); // bend downwind
            transform.rotation = Quaternion.AngleAxis(angle, axis) * rest;
        }
    }
}
