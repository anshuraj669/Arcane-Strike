using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The monster world's still black lake: gives the ArcaneStrike/MoonLake shader its ripple noise (the same texture
    /// the lava makes). The sky it mirrors is on the material. The water burns like lava (a surface-only
    /// <see cref="LavaFlow"/> on the same object marks it for <see cref="LavaHazard"/>).
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class MoonLake : MonoBehaviour
    {
        public const string ShaderName = "ArcaneStrike/MoonLake";

        void Start()
        {
            var r = GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.material.SetTexture("_NoiseTex", LavaFlow.SharedNoise);
        }
    }
}
