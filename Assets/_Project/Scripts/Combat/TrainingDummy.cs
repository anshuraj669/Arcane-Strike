using UnityEngine;

namespace ArcaneStrike.Combat
{
    /// <summary>
    /// DEV / tutorial target. Flashes and jolts when hit and logs the damage, so hand combat can be
    /// tuned before real enemies exist. Needs a Collider (any shape) on this object or a child.
    /// </summary>
    public class TrainingDummy : MonoBehaviour, IHittable
    {
        [SerializeField] float maxHealth = 100f;
        [SerializeField] Color hitColor = new Color(1f, 0.79f, 0.29f); // Spark gold
        [SerializeField] float flashTime = 0.15f;
        [SerializeField] float joltDistance = 0.05f;

        float health;
        float flashUntil;
        Vector3 home;
        Vector3 jolt;
        Renderer rend;
        MaterialPropertyBlock block;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            health = maxHealth;
            home = transform.localPosition;
            rend = GetComponentInChildren<Renderer>();
            block = new MaterialPropertyBlock();
        }

        public void TakeHit(HitInfo hit)
        {
            health -= hit.Damage;
            flashUntil = Time.time + flashTime;
            jolt = hit.Direction * joltDistance;
            Debug.Log($"[Dummy] {hit.Hand} hit for {hit.Damage:F0} at {hit.Speed:F1} m/s, health {Mathf.Max(health, 0f):F0}/{maxHealth:F0}");
            if (health <= 0f) health = maxHealth; // a dummy never dies
        }

        void Update()
        {
            jolt = Vector3.Lerp(jolt, Vector3.zero, Time.deltaTime * 12f);
            transform.localPosition = home + jolt;

            if (rend == null) return;
            rend.GetPropertyBlock(block);
            if (Time.time < flashUntil) block.SetColor(BaseColor, hitColor);
            else block.Clear();
            rend.SetPropertyBlock(block);
        }
    }
}
