using System;
using ArcaneStrike.HandInput;
using ArcaneStrike.Story;
using UnityEngine;

namespace ArcaneStrike.Combat
{
    /// <summary>
    /// The player's health in the Rift, and the readout low in the view: a health bar (it beats red when low), the
    /// power-ups, the wave and the monsters left. A claw that lands flashes the view red and rumbles both controllers;
    /// health comes back slowly after a few seconds without being hit. At zero, <see cref="OnDown"/> (in the monster
    /// world the Weaver dies and wakes back in the cave; in the shadow waves the arena gets them back up).
    /// </summary>
    public class RiftPlayer : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] float maxHealth = 100f;
        [SerializeField] float regenDelay = 4f;
        [SerializeField] float regenPerSecond = 4f;
        [SerializeField] Color hurtColor = new Color(0.6f, 0f, 0.05f, 0.45f);

        public static RiftPlayer Instance { get; private set; }
        public float Health { get; private set; }
        public float MaxHealth => maxHealth;
        public bool Down { get; private set; }
        /// <summary>Health reached zero.</summary>
        public event Action OnDown;
        /// <summary>Shown under the health (the arena sets it: wave, shadows left).</summary>
        public string Status { get; set; } = "";
        /// <summary>Off while the Ascension plays (no health bar floating in the light).</summary>
        public bool HudHidden { get; set; }

        Transform head;
        TextMesh hud;
        float lastHurt, hurtFlash;
        Color flashShown;
        // What the readout shows now: rebuilt only when one of these changes (no new strings every frame).
        int shownHealth = -1, shownSmash = -1, shownWhirl = -1;
        string shownStatus;

        void Awake()
        {
            Instance = this;
            Health = maxHealth;
            if (input == null) input = FindAnyObjectByType<HandInputService>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (hudRoot != null) Destroy(hudRoot.gameObject);
        }

        public void TakeDamage(float amount, Vector3 from)
        {
            if (Down || !(amount > 0f)) return; // (nothing, a heal, or NaN: not a hit)
            Health = Mathf.Max(0f, Health - amount);
            lastHurt = Time.time;
            hurtFlash = 1f;
            CombatFx.Rumble(null, 1f, 0.2f);
            CombatAudio.Play(CombatAudio.Hit, from, 0.9f, 0.3f);
            Debug.Log($"[Rift] Hit for {amount:F0}: health {Health:F0}/{maxHealth:F0}");
            if (Health <= 0f)
            {
                Down = true;
                OnDown?.Invoke();
            }
        }

        /// <summary>Burning (lava): a steady drain, a little each frame, with no hit sound each time.</summary>
        public void Burn(float amount)
        {
            if (Down || !(amount > 0f)) return;
            Health = Mathf.Max(0f, Health - amount);
            lastHurt = Time.time;
            hurtFlash = Mathf.Max(hurtFlash, 0.5f);
            if (Health <= 0f)
            {
                Debug.Log("[Rift] Burned down by the lava");
                Down = true;
                OnDown?.Invoke();
            }
        }

        /// <summary>Health back (up to the full bar): a kill's reward. Returns how much was gained.</summary>
        public float Heal(float amount)
        {
            if (Down || !(amount > 0f)) return 0f;
            float before = Health;
            Health = Mathf.Min(maxHealth, Health + amount);
            float gained = Health - before;
            if (gained > 0.5f)
            {
                trailK = Mathf.Max(trailK, Health / Mathf.Max(maxHealth, 1f));
                if (head != null) CombatFx.Popup($"+{Mathf.RoundToInt(gained)} HEALTH", head.position + StoryFx.Flat(head.forward) * 1.2f + Vector3.down * 0.15f,
                                                 new Color(0.4f, 1f, 0.5f), 0.8f);
                CombatAudio.Play(CombatAudio.Wave, head != null ? head.position : transform.position, 0.6f, 0.05f);
                CombatFx.Rumble(null, 0.3f, 0.15f);
                Debug.Log($"[Rift] Healed {gained:F0}: health {Health:F0}/{maxHealth:F0}");
            }
            return gained;
        }

        /// <summary>Back on your feet with full health.</summary>
        public void Revive()
        {
            Down = false;
            Health = maxHealth;
            lastHurt = Time.time;
            shownHealth = -1;
        }

        void Update()
        {
            if (head == null) head = StoryFx.Head(input);
            if (head == null) return;
            if (!Down && Time.time - lastHurt > regenDelay) Health = Mathf.Min(maxHealth, Health + regenPerSecond * Time.deltaTime);
            if (hurtFlash > 0f)
            {
                hurtFlash = Mathf.Max(0f, hurtFlash - Time.unscaledDeltaTime * 2.5f);
                // Only over a clear view (or our own flash): never over, or clearing away, a fade or a scene's wash.
                Color now = ScreenFade.Current;
                bool ours = now.a < 0.02f || now == flashShown;
                if (!Down && ours && !ScreenFade.Busy)
                {
                    flashShown = StoryFx.WithAlpha(hurtColor, hurtColor.a * hurtFlash);
                    ScreenFade.Set(flashShown);
                }
            }
            UpdateHud();
        }

        // The sword's power-ups: a filling meter each, or READY.
        static string PowerUps()
        {
            WeaverSword sword = WeaverSword.Instance;
            if (sword == null || !sword.Drawn) return "";
            static string Meter(float k) => k >= 1f ? "READY" : new string('|', Mathf.FloorToInt(k * 8f)) + new string('.', 8 - Mathf.FloorToInt(k * 8f));
            return $"\nSmash {Meter(sword.OverheadPower)}    Whirlwind {Meter(sword.WhirlwindPower)}";
        }

        // A meter's step (0 .. 8), -1 with no sword out: what decides whether the readout changes.
        static int Step(float k) => WeaverSword.Instance == null || !WeaverSword.Instance.Drawn ? -1 : Mathf.FloorToInt(Mathf.Clamp01(k) * 8f);

        // ---------------------------------------------------------------- the readout

        // A health bar low in front of the eyes: it trails the view lazily (turning with the body, not snapping with
        // every glance, so it never feels glued to the face), with the power-ups and the status under it. Lost
        // health drains away in pale behind the bar; under a third it beats red.
        const float BarWidth = 0.34f, BarHeight = 0.028f;
        Transform hudRoot, fill, trail;
        Material fillMaterial;
        float trailK = 1f, trailHold;
        Vector3 hudForward;

        void UpdateHud()
        {
            if (hudRoot == null) BuildHud();
            if (hudRoot.gameObject.activeSelf == HudHidden) hudRoot.gameObject.SetActive(!HudHidden);
            if (HudHidden) return;

            // Where it floats: 0.85 m out, 0.3 m below the eyes, following the head's heading.
            Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(head.up, Vector3.up);
            flat.Normalize();
            if (hudForward == Vector3.zero) hudForward = flat;
            float dt = Time.unscaledDeltaTime;
            hudForward = Vector3.Slerp(hudForward, flat, 1f - Mathf.Exp(-3f * dt)).normalized;
            Vector3 at = head.position + hudForward * 0.85f + Vector3.down * 0.3f;
            hudRoot.position = Vector3.Distance(hudRoot.position, at) > 1.5f ? at : Vector3.Lerp(hudRoot.position, at, 1f - Mathf.Exp(-10f * dt));
            Vector3 look = hudRoot.position - head.position;
            if (look.sqrMagnitude > 1e-4f) hudRoot.rotation = Quaternion.LookRotation(look);

            float k = Mathf.Clamp01(Health / Mathf.Max(maxHealth, 1f));
            Bar(fill, k);
            if (k >= trailK) { trailK = k; trailHold = Time.unscaledTime + 0.5f; }
            else if (Time.unscaledTime > trailHold) trailK = Mathf.MoveTowards(trailK, k, dt * 0.6f);
            Bar(trail, trailK);
            Color c = k > 0.5f ? Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(0.25f, 0.95f, 0.35f), (k - 0.5f) * 2f)
                               : Color.Lerp(new Color(1f, 0.12f, 0.08f), new Color(1f, 0.85f, 0.2f), k * 2f);
            if (k < 0.34f && !Down) c *= 0.7f + 0.5f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4.2f)); // a heartbeat
            c.a = 0.95f;
            fillMaterial.SetColor(BaseColorId, c);

            WeaverSword sword = WeaverSword.Instance;
            int healthNow = Mathf.CeilToInt(Health), smash = Step(sword != null ? sword.OverheadPower : 0f), whirl = Step(sword != null ? sword.WhirlwindPower : 0f);
            if (healthNow == shownHealth && smash == shownSmash && whirl == shownWhirl && Status == shownStatus) return;
            shownHealth = healthNow;
            shownSmash = smash;
            shownWhirl = whirl;
            shownStatus = Status;
            label.text = $"HEALTH  {healthNow} / {Mathf.CeilToInt(maxHealth)}";
            label.color = k < 0.34f ? new Color(1f, 0.45f, 0.4f, 0.95f) : new Color(1f, 0.92f, 0.75f, 0.9f);
            hud.text = (PowerUps() + (string.IsNullOrEmpty(Status) ? "" : "\n" + Status)).TrimStart('\n');
        }

        TextMesh label;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // A bar filled to `k`, from its left end.
        static void Bar(Transform bar, float k)
        {
            k = Mathf.Clamp01(k);
            bar.localScale = new Vector3(Mathf.Max(BarWidth * k, 0.0001f), BarHeight, 1f);
            bar.localPosition = new Vector3(-BarWidth * 0.5f + BarWidth * k * 0.5f, 0f, bar.localPosition.z);
        }

        void BuildHud()
        {
            hudRoot = new GameObject("RiftHud").transform;
            hudRoot.position = head.position + head.forward * 0.85f;
            Quad("Frame", new Color(0.02f, 0.02f, 0.03f, 0.6f), 0.004f, 4000).localScale = new Vector3(BarWidth + 0.012f, BarHeight + 0.012f, 1f);
            trail = Quad("Lost", new Color(1f, 0.95f, 0.85f, 0.75f), 0.002f, 4001);
            fill = Quad("Health", Color.green, 0f, 4002);
            fillMaterial = fill.GetComponent<Renderer>().sharedMaterial;
            label = Text("Label", new Vector3(-BarWidth * 0.5f, 0.032f, 0f), TextAnchor.LowerLeft, 0.0042f);
            hud = Text("Info", new Vector3(0f, -0.03f, 0f), TextAnchor.UpperCenter, 0.0038f);
            hud.color = new Color(1f, 0.85f, 0.55f, 0.85f);
        }

        Transform Quad(string name, Color color, float back, int queue)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(hudRoot, false);
            go.transform.localPosition = new Vector3(0f, 0f, back);
            Material m = ArcaneStrike.VFX.AegisShield.CreateFaceMaterial(color);
            m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always); // never hidden in rock or a monster
            m.renderQueue = queue;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        TextMesh Text(string name, Vector3 at, TextAnchor anchor, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(hudRoot, false);
            go.transform.localPosition = at;
            var t = go.AddComponent<TextMesh>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var m = new Material(t.font.material) { renderQueue = 4010 };
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            t.anchor = anchor;
            t.alignment = TextAlignment.Center;
            t.fontSize = 48;
            t.characterSize = size;
            return t;
        }

        void OnDisable()
        {
            if (hudRoot != null) hudRoot.gameObject.SetActive(false);
        }
    }
}
