using System;
using Random = UnityEngine.Random;
using System.Collections;
using System.Collections.Generic;
using ArcaneStrike.Core;
using ArcaneStrike.HandInput;
using ArcaneStrike.VFX;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The blood has touched the lantern.
    /// Into The Rift (the story's ending now): the runes blaze blue, the attic shakes and things drift up, and space
    /// and time tear open in front of the player. The rift swallows the lantern, then pulls the player in after it,
    /// and the AscensionCave scene loads: the Ascension (<see cref="Ascension"/>) happens there.
    /// Into The Lantern (an earlier ending): the wick lights by itself, golden light swells until it swallows the
    /// player, the view whites out, and the Lantern Void scene loads.
    /// Portal (the first ending): blue energy burst → whisper voices → books float → the attic shakes → a portal.
    /// Either way the lantern slips out of the player's hand and floats.
    /// </summary>
    public class LanternAwakening : MonoBehaviour
    {
        public enum Ending { IntoTheLantern, Portal, IntoTheRift }

        [Header("Ending")]
        [SerializeField] Ending ending = Ending.IntoTheRift;
        [Tooltip("Into The Lantern: the scene the golden light carries the player into (must be in Build Settings).")]
        public string lanternScene = "LanternVoid";
        [SerializeField] Color goldColor = new Color(1f, 0.79f, 0.29f, 1f);
        [Tooltip("Seconds from the wick lighting to the whiteout.")]
        [SerializeField] float engulfTime = 4f;

        [Header("Into the Rift")]
        [SerializeField] Color ascensionBlue = new Color(0.25f, 0.55f, 1f, 1f);
        [SerializeField] Color riftColor = new Color(0.55f, 0.25f, 1f, 1f);
        [Tooltip("How far in front of the player the rift tears open (m; closer if a wall is in the way).")]
        [SerializeField] float riftDistance = 2f;
        [Tooltip("Seconds the rift takes to pull the player in.")]
        [SerializeField] float pullTime = 3f;
        [Tooltip("The other side of the rift: the cave where the Ascension happens (Arcane Strike → Create Ascension Cave Scene adds it to Build Settings).")]
        [SerializeField] string riftScene = "AscensionCave";

        [Header("Lantern")]
        [SerializeField] Transform lantern;
        [SerializeField] LanternPickup pickup;
        [SerializeField] LanternGlow glow;
        [SerializeField] RuneGlow runes;
        [SerializeField] LanternSound sound;
        [SerializeField] Color energyColor = new Color(0.3f, 0.6f, 1f, 1f);
        [SerializeField] int burstSparks = 160;

        [Header("The attic reacts")]
        [SerializeField] WhisperVoices whispers;
        [Tooltip("Optional root that is switched on when the books float (like the original floatingSystem).")]
        public GameObject floatingSystem;
        public FloatingObject[] floatingObjects;
        [Tooltip("Everything else that drifts up as the attic reacts (jars, scrolls, the cloth...), quickly one after another.")]
        public FloatingObject[] floatingProps;
        [SerializeField] float propStagger = 0.08f;
        [SerializeField] AtticShake shake;
        [SerializeField] PortalController portal;

        [Header("Timing (seconds after the blood touches the lantern)")]
        [SerializeField] float whispersAt = 1.5f;
        [SerializeField] float booksAt = 3f;
        [Tooltip("Gap between one book lifting and the next.")]
        [SerializeField] float bookStagger = 0.25f;
        [SerializeField] float shakeAt = 6f;
        [SerializeField] float portalAt = 8.5f;

        public event Action OnPortalOpened;
        /// <summary>Into The Lantern: the light has swallowed the player (just before the next scene loads).</summary>
        public event Action OnEngulfed;
        /// <summary>Into The Rift: the rift has pulled the player through (just before the AscensionCave scene loads).</summary>
        public event Action OnPulledIn;
        public bool IsAwakened { get; private set; }

        ParticleSystem burst;
        bool skip;

        Vector3 LanternCenter => (lantern != null ? lantern.position : transform.position) + Vector3.up * 0.12f;

        void Start()
        {
            burst = StoryFx.CreateParticles(transform, "EnergyBurst", true, -0.05f, 800);
        }

        public void TriggerAwakening()
        {
            if (IsAwakened) return;
            IsAwakened = true;
            StartCoroutine(ending == Ending.IntoTheRift ? IntoTheRift() : ending == Ending.IntoTheLantern ? IntoTheLantern() : Awaken());
        }

        /// <summary>Debug: run the remaining beats straight away.</summary>
        public void Skip() => skip = true;

        IEnumerator Awaken()
        {
            float start = Time.time;

            // Blue energy burst
            Debug.Log("[Attic] The lantern awakens: blue energy burst");
            Vector3 c = LanternCenter;
            StoryFx.Burst(burst, c, burstSparks, 2.2f, 0.06f, 1.2f, energyColor);
            StoryFx.Burst(burst, c, burstSparks / 4, 0.6f, 0.25f, 0.5f, StoryFx.WithAlpha(Color.white, 0.8f));
            if (glow != null)
            {
                glow.Flash(energyColor, 3f);
                glow.SetColor(energyColor);
                glow.SetLevel(1f);
            }
            if (runes != null) runes.Activate(energyColor, 6f);
            if (sound != null)
            {
                sound.SetIntensity(1f);
                sound.Swell();
            }
            if (pickup != null) pickup.Release();
            var lanternFloat = lantern != null ? lantern.GetComponent<FloatingObject>() : null;
            if (lanternFloat != null) lanternFloat.Activate();

            yield return Until(start + whispersAt);
            if (whispers != null) whispers.Play();

            yield return Until(start + booksAt);
            Debug.Log("[Attic] Books float");
            if (floatingSystem != null) floatingSystem.SetActive(true);
            if (floatingProps != null && floatingProps.Length > 0) StartCoroutine(FloatProps());
            if (floatingObjects != null)
                foreach (FloatingObject book in floatingObjects)
                {
                    if (book == null) continue;
                    book.Activate();
                    if (!skip) yield return new WaitForSeconds(bookStagger);
                }

            yield return Until(start + shakeAt);
            if (shake != null) shake.Shake();

            yield return Until(start + portalAt);
            if (portal != null) portal.Open();
            OnPortalOpened?.Invoke();
        }

        IEnumerator FloatProps()
        {
            yield return new WaitForSeconds(skip ? 0f : 0.5f);
            foreach (FloatingObject prop in floatingProps)
            {
                if (prop == null) continue;
                prop.Activate();
                if (!skip) yield return new WaitForSeconds(propStagger);
            }
        }

        IEnumerator IntoTheLantern()
        {
            Debug.Log("[Attic] The blood touches the lantern: the wick lights by itself");
            Vector3 c = LanternCenter;
            if (glow != null)
            {
                glow.SetColor(goldColor);
                glow.Flash(goldColor, 1.5f);
                glow.SetLevel(1f); // the flame in the lantern grows with the glow
            }
            StoryFx.Burst(burst, c, burstSparks / 3, 0.7f, 0.05f, 1.2f, goldColor);
            if (runes != null) runes.Activate(goldColor, 8f);
            if (sound != null)
            {
                sound.SetIntensity(1f);
                sound.Swell();
            }
            if (pickup != null) pickup.Release();
            var lanternFloat = lantern != null ? lantern.GetComponent<FloatingObject>() : null;
            if (lanternFloat != null) lanternFloat.Activate();

            float start = Time.time;
            yield return Until(start + 1.5f);

            // The golden light swells until it fills the attic and swallows the player.
            Debug.Log("[Attic] Golden light engulfs the player");
            Light light = glow != null ? glow.lanternLight : null;
            float baseRange = light != null ? light.range : 0f;
            var ringer = new GameObject("Ringing");
            ringer.transform.SetParent(Camera.main != null ? Camera.main.transform : transform, false);
            AudioSource ring = StoryFx.CreateSource(ringer, Ascension.Ringing(), true, 0f);
            ring.Play();
            float swell = skip ? 0.3f : engulfTime;
            for (float t = 0f; t < swell; t += Time.deltaTime)
            {
                float k = t / swell;
                if (light != null)
                {
                    light.color = goldColor;
                    light.range = Mathf.Lerp(baseRange, 18f, k);
                    light.intensity = Mathf.Lerp(light.intensity, 40f * k + 3f, 1f - Mathf.Exp(-4f * Time.deltaTime));
                }
                if (Random.value < k) StoryFx.Burst(burst, LanternCenter, 6, 1.5f + 3f * k, 0.04f + 0.08f * k, 0.8f, goldColor);
                ring.volume = 0.5f * k;
                ScreenFade.Set(StoryFx.WithAlpha(goldColor, k * k * 0.9f));
                yield return null;
            }
            yield return ScreenFade.To(Color.white, 0.7f);
            OnEngulfed?.Invoke();

            if (!string.IsNullOrEmpty(lanternScene) && Application.CanStreamedLevelBeLoaded(lanternScene))
            {
                ScreenFade.LoadScene(lanternScene, Color.white, 0f);
                yield break;
            }
            Debug.Log($"[Attic] Scene '{lanternScene}' isn't in Build Settings yet (Arcane Strike → Create Lantern Void Scene adds it).");
            ring.Stop();
            yield return ScreenFade.To(new Color(1f, 1f, 1f, 0f), 1.5f);
        }

        // The blood wakes the lantern and time itself tears: the runes blaze blue and the attic shakes, loose things
        // drift up, and a rift splits open in the air in front of the player. The lantern is torn from the air into
        // it, then the rift pulls the player in after it, faster and faster, until they're through (the Ascension
        // happens on the other side, in the AscensionCave scene).
        IEnumerator IntoTheRift()
        {
            var input = FindAnyObjectByType<HandInputService>();
            Transform head = StoryFx.Head(input);
            Transform rig = head != null ? head.root : null;
            var locomotion = FindAnyObjectByType<PlayerLocomotion>();
            if (locomotion != null) locomotion.Paused = true;

            // 1. The runes blaze blue and blue light floods the room; the attic shakes and things drift up.
            Debug.Log("[Attic] The runes awaken: blue light floods the room");
            Vector3 c = LanternCenter;
            if (glow != null)
            {
                glow.SetColor(ascensionBlue);
                glow.Flash(ascensionBlue, 2f);
                glow.SetLevel(1f);
            }
            if (runes != null) runes.Activate(ascensionBlue, 8f);
            if (sound != null)
            {
                sound.SetIntensity(1f);
                sound.Swell();
            }
            if (pickup != null) pickup.Release();
            var lanternFloat = lantern != null ? lantern.GetComponent<FloatingObject>() : null;
            if (lanternFloat != null) lanternFloat.Activate();
            StoryFx.Burst(burst, c, burstSparks, 2f, 0.05f, 1.2f, ascensionBlue);
            if (whispers != null) whispers.Play();

            var roomLight = new GameObject("AwakeningLight").AddComponent<Light>();
            roomLight.transform.position = c + Vector3.up * 0.3f;
            roomLight.type = LightType.Point;
            roomLight.color = ascensionBlue;
            roomLight.shadows = LightShadows.None;
            var ears = new GameObject("AwakeningSound");
            ears.transform.SetParent(head != null ? head : transform, false);
            AudioSource rumble = StoryFx.CreateSource(ears, StoryFx.Rumble(), false, 0f);
            rumble.volume = 0.8f;
            rumble.Play();
            if (shake != null) shake.Shake();
            if (floatingProps != null && floatingProps.Length > 0) StartCoroutine(FloatProps());
            if (floatingObjects != null)
                foreach (FloatingObject book in floatingObjects)
                    if (book != null) book.Activate();

            Color sky = RenderSettings.ambientSkyColor, equator = RenderSettings.ambientEquatorColor;
            for (float t = 0f, d = Dur(2.5f); t < d; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / d);
                roomLight.range = Mathf.Lerp(1f, 10f, k);
                roomLight.intensity = Mathf.Lerp(0.5f, 4f, k);
                RenderSettings.ambientSkyColor = Color.Lerp(sky, ascensionBlue * 0.6f, k);
                RenderSettings.ambientEquatorColor = Color.Lerp(equator, ascensionBlue * 0.4f, k);
                ScreenFade.Set(StoryFx.WithAlpha(ascensionBlue, 0.18f * k));
                if (Random.value < 0.5f) StoryFx.Burst(burst, LanternCenter, 4, 1f + 2f * k, 0.04f, 0.9f, ascensionBlue);
                yield return null;
            }
            if (head == null || rig == null)
            {
                // No view to pull (no camera at all): don't leave the player frozen and tinted.
                Debug.LogWarning("[Attic] No head/camera found: the rift can't open in front of the player.");
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                ScreenFade.Set(new Color(0f, 0f, 0f, 0f));
                if (locomotion != null) locomotion.Paused = false;
                yield break;
            }

            // 2. Space and time split: a rift tears open in the air in front of the player.
            Vector3 fwd = StoryFx.Flat(head.forward);
            float floorY = FloorUnder(head.position, rig);
            Vector3 chest = new Vector3(head.position.x, floorY + 1.1f, head.position.z);
            float distance = riftDistance;
            if (Physics.SphereCast(chest, 0.4f, fwd, out RaycastHit hit, distance + 0.6f, ~0, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(1.2f, hit.distance - 0.6f);
            Vector3 at = new Vector3(head.position.x, floorY + 0.15f, head.position.z) + fwd * distance;
            RiftTear tear = RiftTear.Create(at, Quaternion.LookRotation(-fwd), 2.4f, 1.2f);
            Debug.Log("[Attic] Space-time tears open: a rift in front of the player");
            AudioSource boom = StoryFx.CreateSource(ears, Ascension.Boom(), false, 0f);
            boom.volume = 0.9f;
            boom.Play();
            Combat.CombatAudio.Play(Combat.CombatAudio.Tear, tear.Centre, 0.9f, 0f);
            if (shake != null) shake.Shake();
            StartCoroutine(ScreenFade.Pulse(StoryFx.WithAlpha(riftColor, 0.4f), 0.15f, Dur(0.9f)));
            yield return tear.Open(Dur(2f));

            // 3. The lantern is torn out of the air and sucked into the rift (a copy goes through with the player).
            Debug.Log("[Attic] The rift swallows the lantern");
            if (lantern != null)
            {
                CarriedLantern.Keep(lantern);
                yield return Suck(lantern, tear.Centre, Dur(1.4f), true);
                StoryFx.Burst(burst, tear.Centre, 80, 1.4f, 0.05f, 0.8f, ascensionBlue);
                lantern.gameObject.SetActive(false);
            }
            yield return Until(Time.time + Dur(0.4f));

            // 4. Then the player: the rift pulls, slowly and then all at once; loose things fly in around them.
            // Scenes saved before the cave existed still say "ShadowRift": the Ascension now happens in the cave first.
            if (riftScene == "ShadowRift" && Application.CanStreamedLevelBeLoaded("AscensionCave")) riftScene = "AscensionCave";
            if (!Application.CanStreamedLevelBeLoaded(riftScene))
            {
                string fix = riftScene == "ShadowRift" ? "Arcane Strike → Create Shadow Rift Scene" : "Arcane Strike → Create Ascension Cave Scene";
                Debug.LogWarning($"[Attic] Scene '{riftScene}' isn't in Build Settings yet ({fix}): the rift can't pull the player through.");
                Combat.CombatFx.Popup($"No '{riftScene}' scene yet\nStop Play, then {fix}",
                                      head.position + fwd * 1.2f, new Color(1f, 0.6f, 0.6f), 0.7f);
                // Stay playable: the room's colour, light and the player's legs come back.
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                Destroy(roomLight.gameObject);
                yield return ScreenFade.To(new Color(0f, 0f, 0f, 0f), 1f);
                if (locomotion != null) locomotion.Paused = false;
                yield break;
            }
            Debug.Log("[Attic] The rift pulls the player in");
            AudioSource wind = StoryFx.CreateSource(ears, Combat.CombatAudio.Drone, true, 0f);
            wind.Play();
            foreach (FloatingObject f in AllFloating())
                StartCoroutine(Suck(f.transform, tear.Centre + Random.insideUnitSphere * 0.3f, Dur(Random.Range(1.2f, pullTime)), false));
            Vector3 from = head.position, into = tear.Centre;
            into.y = from.y; // straight across, at eye height
            for (float t = 0f, d = Dur(pullTime); t < d; t += Time.deltaTime)
            {
                float k = Mathf.Pow(t / d, 1.8f); // a gentle tug that grows into a rush
                // Headset comfort: the view's edge darkens as the pull grows, and the rift's colour fills the rest
                // before the rush is at its fastest.
                ComfortVignette.Set(Mathf.Lerp(0.4f, 1f, Mathf.Sqrt(t / d)));
                Vector3 want = Vector3.Lerp(from, into, k);
                Vector3 move = want - head.position;
                move.y = 0f;
                rig.position += move;
                wind.volume = 0.2f + 0.8f * k;
                wind.pitch = 0.8f + 0.6f * k;
                if (Random.value < 0.3f + k) Combat.CombatFx.Rumble(null, 0.3f + 0.7f * k, 0.05f);
                ScreenFade.Set(StoryFx.WithAlpha(Color.Lerp(ascensionBlue, riftColor, k), Mathf.Lerp(0.18f, 0.95f, k)));
                // Streaks of light rushing past into the rift.
                for (int i = 0; i < 1 + (int)(k * 6f); i++)
                {
                    Vector3 p = head.position + Random.insideUnitSphere * 1.2f;
                    StoryFx.Emit(burst, p, (into - p).normalized * (2f + 6f * k), Random.Range(0.01f, 0.03f), 0.4f, StoryFx.WithAlpha(riftColor, 0.8f));
                }
                yield return null;
            }
            OnPulledIn?.Invoke();
            ComfortVignette.Set(0f); // eases off under the wash, so it doesn't linger into the cave
            ScreenFade.LoadScene(riftScene, new Color(0.05f, 0f, 0.08f, 1f), Dur(0.25f));
        }

        IEnumerable<FloatingObject> AllFloating()
        {
            if (floatingObjects != null)
                foreach (FloatingObject f in floatingObjects)
                    if (f != null && f.isActiveAndEnabled) yield return f;
            if (floatingProps != null)
                foreach (FloatingObject f in floatingProps)
                    if (f != null && f.isActiveAndEnabled) yield return f;
        }

        // Pulls a thing into the rift: it lets go of its float, speeds up as it goes, tumbles, and shrinks away.
        IEnumerator Suck(Transform thing, Vector3 into, float seconds, bool trail)
        {
            if (thing == null) yield break;
            var floating = thing.GetComponent<FloatingObject>();
            if (floating != null) floating.enabled = false;
            var body = thing.GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = true;
            Vector3 from = thing.position, size = thing.localScale;
            Quaternion turn = Quaternion.Euler(Random.Range(-200f, 200f), Random.Range(-200f, 200f), Random.Range(-200f, 200f));
            for (float t = 0f; t < seconds && thing != null; t += Time.deltaTime)
            {
                float k = Mathf.Pow(t / seconds, 2f);
                thing.position = Vector3.Lerp(from, into, k);
                thing.rotation = Quaternion.Slerp(Quaternion.identity, turn, Time.deltaTime) * thing.rotation;
                thing.localScale = size * Mathf.Lerp(1f, 0.05f, Mathf.Clamp01((k - 0.6f) / 0.4f));
                if (trail && Random.value < 0.6f) StoryFx.Emit(burst, thing.position, Random.insideUnitSphere * 0.2f, 0.03f, 0.6f, ascensionBlue);
                yield return null;
            }
            if (thing != null && thing != lantern) thing.gameObject.SetActive(false);
        }

        float Dur(float seconds) => skip ? Mathf.Min(seconds, 0.2f) : seconds;

        // The floor under the player: the highest solid surface below their head (not their own rig, not loose
        // props). The rig's own height won't do: on a screen the rig sits at eye level, in a headset on the floor.
        static float FloorUnder(Vector3 head, Transform rig)
        {
            float best = float.MinValue;
            foreach (RaycastHit h in Physics.RaycastAll(head, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.attachedRigidbody == null && !h.collider.transform.IsChildOf(rig) && h.normal.y > 0.6f &&
                    h.point.y < head.y - 0.5f && h.point.y > best)
                    best = h.point.y;
            return best > float.MinValue ? best : head.y - 1.6f;
        }

        IEnumerator Until(float time)
        {
            while (Time.time < time && !skip) yield return null;
        }
    }
}
