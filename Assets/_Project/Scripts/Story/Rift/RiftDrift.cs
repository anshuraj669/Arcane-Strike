using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>Rock floating in the Rift: bobs slowly up and down and turns, each at its own pace.</summary>
    public class RiftDrift : MonoBehaviour
    {
        [SerializeField] float bob = 0.25f;
        [SerializeField] float spin = 6f;

        Vector3 home, axis;
        float phase, rate;

        void Start()
        {
            home = transform.position;
            axis = Random.onUnitSphere;
            phase = Random.value * 10f;
            rate = Random.Range(0.25f, 0.6f);
        }

        void Update()
        {
            transform.position = home + Vector3.up * Mathf.Sin(Time.time * rate + phase) * bob;
            transform.Rotate(axis, spin * rate * Time.deltaTime, Space.World);
        }
    }
}
