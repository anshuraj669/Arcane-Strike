using System.Collections;
using ArcaneStrike.Combat;
using ArcaneStrike.VFX;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The mini boss at the far end of the Rift: a hulking Kalakh twice the player's height with a crown of horns, a
    /// cluster of burning eyes and arms that drag on the ground, wreathed in smoke, with shards of dark stone
    /// circling its head. It doesn't fight yet: it steps out of the far tear, walks up to the player, bends down until
    /// they are face to face, and roars. A placeholder figure built from shapes, unless a model is given to
    /// <see cref="Create"/> (an Animator gets "Speed" and "Roar" if it has them).
    /// </summary>
    public class RiftBoss : MonoBehaviour
    {
        const float Height = 3.4f;

        Transform visual, headBone, crown;
        Transform[] arms = new Transform[2];
        Animator animator;
        Material eyes;
        float breathe;

        public Vector3 Eyes => headBone != null ? headBone.position : transform.position + Vector3.up * Height * 0.85f;

        public static RiftBoss Create(Vector3 position, Quaternion facing, GameObject model = null)
        {
            var go = new GameObject("Kalakh Mini Boss");
            go.transform.SetPositionAndRotation(position, facing);
            var boss = go.AddComponent<RiftBoss>();
            boss.Build(model);
            return boss;
        }

        void Build(GameObject model)
        {
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            if (model != null)
            {
                GameObject m = Instantiate(model, visual);
                var renderers = m.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
                    if (b.size.y > 1e-3f) m.transform.localScale *= Height / b.size.y;
                }
                animator = m.GetComponentInChildren<Animator>();
                headBone = new GameObject("Head").transform;
                headBone.SetParent(visual, false);
                headBone.localPosition = new Vector3(0f, Height * 0.85f, 0.2f);
                return;
            }

            var dark = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "BossShadow" };
            dark.SetColor("_BaseColor", new Color(0.025f, 0.015f, 0.035f));
            dark.SetFloat("_Smoothness", 0.5f);
            eyes = AegisShield.CreateFaceMaterial(new Color(1f, 0.15f, 0.3f, 1f));

            Shape(PrimitiveType.Capsule, visual, new Vector3(0f, 1.15f, 0f), new Vector3(1.3f, 1.15f, 0.9f), Vector3.right * 15f, dark); // body
            Shape(PrimitiveType.Sphere, visual, new Vector3(0f, 2.35f, 0.15f), new Vector3(2.1f, 0.9f, 1.2f), Vector3.zero, dark); // shoulders
            headBone = new GameObject("Head").transform;
            headBone.SetParent(visual, false);
            headBone.localPosition = new Vector3(0f, 2.75f, 0.55f);
            Shape(PrimitiveType.Sphere, headBone, Vector3.zero, new Vector3(0.62f, 0.55f, 0.7f), Vector3.zero, dark);
            Shape(PrimitiveType.Cube, headBone, new Vector3(0f, -0.2f, 0.22f), new Vector3(0.45f, 0.18f, 0.35f), new Vector3(15f, 0f, 0f), dark); // jaw
            // Horns: four tapering segments sweeping up and out each side, and two short ones in front.
            foreach (float side in new[] { -1f, 1f })
            {
                for (int k = 0; k < 4; k++)
                {
                    float s = 0.12f - k * 0.025f;
                    var at = new Vector3(side * (0.22f + 0.13f * k), 0.25f + 0.2f * k - 0.03f * k * k, -0.05f * k);
                    Shape(PrimitiveType.Capsule, headBone, at, new Vector3(s, 0.15f, s), new Vector3(-10f * k, 0f, -side * (45f - 12f * k)), dark);
                }
                Shape(PrimitiveType.Capsule, headBone, new Vector3(side * 0.1f, 0.25f, 0.2f), new Vector3(0.05f, 0.12f, 0.05f), new Vector3(-30f, 0f, -side * 10f), dark);
            }
            // A cluster of six eyes.
            for (int i = 0; i < 6; i++)
            {
                float x = (i % 3 - 1) * 0.12f, y = i < 3 ? 0.06f : -0.06f;
                var eye = Shape(PrimitiveType.Sphere, headBone, new Vector3(x, y, 0.33f - Mathf.Abs(x) * 0.4f), Vector3.one * (i < 3 ? 0.07f : 0.05f), Vector3.zero, eyes);
                eye.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // Long arms down to the ground, with claws.
            for (int a = 0; a < 2; a++)
            {
                float side = a == 0 ? -1f : 1f;
                var shoulder = new GameObject(a == 0 ? "ArmL" : "ArmR").transform;
                shoulder.SetParent(visual, false);
                shoulder.localPosition = new Vector3(side * 0.95f, 2.35f, 0.2f);
                Shape(PrimitiveType.Capsule, shoulder, new Vector3(0f, -0.55f, 0f), new Vector3(0.32f, 0.6f, 0.32f), Vector3.zero, dark);
                Shape(PrimitiveType.Capsule, shoulder, new Vector3(0f, -1.55f, 0.1f), new Vector3(0.25f, 0.55f, 0.25f), new Vector3(-8f, 0f, 0f), dark);
                for (int k = -1; k <= 1; k++)
                    Shape(PrimitiveType.Capsule, shoulder, new Vector3(k * 0.1f, -2.2f, 0.25f), new Vector3(0.05f, 0.22f, 0.05f), new Vector3(35f, 0f, k * 10f), dark);
                shoulder.localEulerAngles = new Vector3(0f, 0f, side * 10f);
                arms[a] = shoulder;
            }
            // Shards of stone circling the head.
            crown = new GameObject("Crown").transform;
            crown.SetParent(headBone, false);
            for (int i = 0; i < 9; i++)
            {
                float angle = i * 40f;
                Vector3 p = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 0.75f + Vector3.up * (0.35f + (i % 2) * 0.12f);
                Shape(PrimitiveType.Cube, crown, p, new Vector3(0.06f, 0.28f, 0.06f), new Vector3(Random.Range(-20f, 20f), angle, Random.Range(-25f, 25f)), dark);
            }
        }

        static Transform Shape(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Vector3 euler, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.transform.localEulerAngles = euler;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        void Update()
        {
            breathe += Time.deltaTime;
            if (crown != null) crown.localRotation = Quaternion.Euler(0f, breathe * 25f, 0f);
            if (visual != null && animator == null) visual.localScale = new Vector3(1f, 1f + Mathf.Sin(breathe * 1.4f) * 0.015f, 1f);
            if (Random.value < 0.8f)
                CombatFx.SmokePuff(transform.position + new Vector3(Random.Range(-1f, 1f), Random.Range(0f, Height), Random.Range(-0.6f, 0.6f)), 0.4f, 1, 0.3f);
        }

        /// <summary>Walks to `to` (on the floor) at `speed`, swaying, arms dragging.</summary>
        public IEnumerator WalkTo(Vector3 to, float speed)
        {
            Vector3 from = transform.position;
            float distance = Vector3.Distance(from, to), time = distance / Mathf.Max(speed, 0.1f);
            SetSpeed(speed);
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                float k = t / time;
                transform.position = Vector3.Lerp(from, to, k);
                float step = Mathf.Sin(t * 5f);
                visual.localRotation = Quaternion.Euler(6f, 0f, step * 4f);
                visual.localPosition = Vector3.up * Mathf.Abs(step) * 0.06f;
                if (Mathf.Abs(step) < 0.08f) CombatFx.Rumble(null, 0.5f, 0.06f); // every footfall
                for (int a = 0; a < 2; a++)
                    if (arms[a] != null) arms[a].localEulerAngles = new Vector3(Mathf.Sin(t * 5f + a * Mathf.PI) * 12f, 0f, (a == 0 ? -1f : 1f) * 10f);
                yield return null;
            }
            transform.position = to;
            SetSpeed(0f);
        }

        /// <summary>Bends down until its eyes are level with `face` (the player's eyes) and very close to them.</summary>
        public IEnumerator Lean(Transform face, float seconds)
        {
            Vector3 startHead = headBone.localPosition;
            Quaternion startBody = visual.localRotation;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                // Hunch forward and drop the head towards the player's eye level.
                visual.localRotation = Quaternion.Slerp(startBody, Quaternion.Euler(28f, 0f, 0f), k);
                if (face != null)
                {
                    float drop = Mathf.Max(0f, Eyes.y - face.position.y);
                    headBone.localPosition = Vector3.Lerp(startHead, startHead + new Vector3(0f, -drop * 0.5f, 0.35f), k);
                    Vector3 look = face.position - headBone.position;
                    if (look.sqrMagnitude > 1e-4f) headBone.rotation = Quaternion.Slerp(headBone.rotation, Quaternion.LookRotation(look), k);
                }
                yield return null;
            }
        }

        /// <summary>A roar into the player's face: the head rears and shakes, its eyes blaze, smoke blasts out.</summary>
        public IEnumerator Roar()
        {
            CombatAudio.Play(CombatAudio.Roar, Eyes, 1f, 0f);
            if (animator != null)
                foreach (var p in animator.parameters)
                    if (p.name == "Roar" && p.type == AnimatorControllerParameterType.Trigger) animator.SetTrigger("Roar");
            CombatFx.Rumble(null, 1f, 1.5f);
            Quaternion rest = headBone.localRotation;
            for (float t = 0f; t < 2.6f; t += Time.deltaTime)
            {
                float k = Mathf.Sin(Mathf.Clamp01(t / 2.6f) * Mathf.PI);
                headBone.localRotation = rest * Quaternion.Euler(-12f * k + Mathf.Sin(t * 40f) * 3f * k, Mathf.Sin(t * 31f) * 4f * k, 0f);
                if (eyes != null) eyes.SetColor("_BaseColor", Color.Lerp(new Color(1f, 0.15f, 0.3f, 1f), Color.white, k * 0.6f));
                if (Random.value < 0.9f)
                    CombatFx.SmokePuff(Eyes + headBone.forward * 0.4f, 0.15f, 2, 0.1f);
                yield return null;
            }
            headBone.localRotation = rest;
        }

        /// <summary>Dragged backwards into the dark as the rift seals.</summary>
        public IEnumerator PulledBack(Vector3 to, float seconds)
        {
            Vector3 from = transform.position;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = t / seconds;
                transform.position = Vector3.Lerp(from, to, k * k);
                transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.6f, k);
                yield return null;
            }
            gameObject.SetActive(false);
        }

        void SetSpeed(float speed)
        {
            if (animator == null) return;
            foreach (var p in animator.parameters)
                if (p.name == "Speed" && p.type == AnimatorControllerParameterType.Float) animator.SetFloat("Speed", speed);
        }
    }
}
