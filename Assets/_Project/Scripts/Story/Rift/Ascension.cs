using System.Collections.Generic;
using System;
using System.Collections;
using ArcaneStrike.Combat;
using ArcaneStrike.Core;
using ArcaneStrike.HandInput;
using ArcaneStrike.VFX;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The Ascension, inside the Rift (the ShadowRift scene, started by <see cref="RiftArena"/> as the player
    /// arrives). The attic's rift has just pulled the player in, the lantern tumbling through ahead of them.
    /// Here the lantern blazes blue and floods the dark with light, the void above splits open, a column of light
    /// strikes down and lifts the player into the air, and the light turns white with a boom and a chime. Up there
    /// the soul slips out of the body: the view drifts out and looks back at the boy hanging in the light. Ember
    /// rises from the lantern, introduces itself to the soul, has the player raise their palms (the Jyoti Seal
    /// wakes with a heartbeat), and then the soul watches the suit-up: the suit wraps onto the body in one wave from
    /// the chest out, ribbons of light winding down the limbs, the body arched back in the light, the hood last and
    /// the mask's eyes lighting (<see cref="WarriorOutfit.SuitUp"/>). Ember explains the lantern, the runes, the
    /// Weaver's task and the powers, and the katana forms and slides into its sheath on the back. Then the soul is
    /// drawn back in, the white fades, and the player floats down to the Rift's floor wearing it. Ember stays
    /// (<see cref="Ember"/>) for the fight.
    /// In a headset the view is only ever moved slowly and smoothly (eased in and out), with a dark ring round the
    /// edge of the view while it moves (<see cref="ComfortVignette"/>); the soul's view stays steady while the body
    /// bobs; flashes rise and fall softly; every jump of the view happens behind solid white.
    /// </summary>
    /// In the temple (AscensionCave scene, <see cref="relic"/> set): the player arrives, walks into the shaft of
    /// light and touches the bronze cauldron; a shockwave rings out of it, and then the Ascension
    /// happens. The sword forms out of the light with the suit-up and goes to its sheath on the back.
    /// Afterwards a Kalakh rift opens in the cave wall (<see cref="exitScene"/>), and walking into it starts the fight.
    public class Ascension : MonoBehaviour
    {
        [SerializeField] Color blueFire = new Color(0.1f, 0.42f, 1f, 1f);
        [Tooltip("The heart of the fire: what the view fills with as the flames take the player.")]
        [SerializeField] Color blueBlaze = new Color(0.55f, 0.84f, 1f, 1f);
        [SerializeField] Color paleBlue = new Color(0.45f, 0.78f, 1f, 1f);
        [Tooltip("How high the light lifts the player (m).")]
        [SerializeField] float liftHeight = 1.3f;
        [Tooltip("The character: his body in the light, and the outfit formed on it. Empty: found in the scene.")]
        [SerializeField] WarriorOutfit outfit;
        [Header("Arriving")]
        [Tooltip("The tear the player is pulled out of (behind them). Empty: the arena's entrance.")]
        public RiftTear entrance;
        [Tooltip("How far (m) the pull carries the player out of the tear as they arrive.")]
        [SerializeField] float arrivalDrift = 1f;
        [Header("Out of body")]
        [Tooltip("Up in the light the soul slips out of the body: the view drifts out to this distance (m) and looks back " +
                 "at the body hanging in the light, where the outfit forms while Ember talks.")]
        [SerializeField] float soulDistance = 2.2f;
        [Tooltip("The soul's eyes relative to the body's (m).")]
        [SerializeField] float soulHeight = -0.1f;
        [Tooltip("Seconds the soul takes to drift out of the body.")]
        [SerializeField] float soulDrift = 3.5f;
        [Tooltip("\"Hold them up\": without tracked hands (a desktop), carry on after this many seconds (N also skips it).")]
        [SerializeField] float palmsTimeout = 8f;
        [Tooltip("\"Hold them up\": with tracked hands that never quite face the eyes (and no N key in a headset), carry on after this many seconds.")]
        [SerializeField] float palmsGiveUp = 20f;
        [Tooltip("How white the view stays while the player floats in the light and Ember talks (0 clear .. 1 white).")]
        [SerializeField, Range(0f, 0.8f)] float hoverWash = 0.15f;
        [Tooltip("Seconds the suit takes to wrap onto the body.")]
        [SerializeField] float suitUpTime = 10f;
        [SerializeField] int burstSparks = 160;

        [Header("The cave (empty: the Ascension starts as the player arrives)")]
        [Tooltip("The bronze cauldron: walking up and touching it sets off the shockwave and the Ascension.")]
        public Renderer relic;
        [Tooltip("The shaft of light onto the altar (it flares with the shockwave).")]
        public LightShaft shaft;
        [Tooltip("How close (m) a fingertip or palm must come to the hilt to touch it.")]
        [SerializeField] float touchReach = 0.14f;
        [Tooltip("Afterwards the rock pile at the back of the cave collapses and this tunnel opens the way out into the " +
                 "monster world. Empty: a rift opens in the wall into Exit Scene instead.")]
        public CavePassage passage;
        [Tooltip("After the Ascension Ember offers optional sword practice (SwordPractice) before the way out opens.")]
        [SerializeField] bool offerPractice = true;
        [Tooltip("The fire sets the player down this far (m) from the cauldron, not beside it.")]
        [SerializeField] float landDistance = 3.6f;
        [Tooltip("Seconds of standing still after Ember's last line before Ember leads the player out of the cave.")]
        [SerializeField] float leadAfter = 3f;
        [Tooltip("Afterwards a rift opens here into this scene (the fight). Empty: none.")]
        public string exitScene = "";
        [Tooltip("Where that rift opens, facing into the cave. Empty: to the player's right.")]
        public Transform exitPoint;
        [Tooltip("Grandfather's lantern from the attic (hidden in the scene): it comes out of the rift with the player " +
                 "when the scene is played on its own. From the attic, the lantern carried through the rift is used instead.")]
        public Transform lanternModel;
        [Tooltip("Where the player arrives (the rig is put here as the scene starts). Empty: wherever the rig is.")]
        public Transform playerStart;
        [Tooltip("Start as the scene starts (the cave), rather than when RiftArena asks.")]
        [SerializeField] bool runOnStart;
        [Tooltip("The tear the player came out of seals behind them.")]
        [SerializeField] bool closeEntrance;

        /// <summary>The soul is back, the outfit on, the player on the floor.</summary>
        public event Action OnAscended;
        public bool IsRunning { get; private set; }
        /// <summary>Ember, out of the lantern (handed on to the fight).</summary>
        public EmberFlame Ember => ember;
        public StorySpeech EmberSpeech => emberSpeech;

        ParticleSystem burst;
        Transform lantern;
        Light lanternLight;

        Vector3 LanternCenter => (lantern != null ? lantern.position : transform.position) + Vector3.up * 0.12f;

        /// <summary>The Weaver fell in the monster world and is sent back to the cave: no second Ascension, they wake
        /// there suited and armed and Ember offers the training again (or straight back out).</summary>
        public static bool ReturnAfterFall;
        bool returned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ReturnAfterFall = false;

        void Awake()
        {
            burst = StoryFx.CreateParticles(transform, "AscensionSparks", true, -0.05f, 800);
            if (runOnStart && relic != null)
            {
                returned = ReturnAfterFall;
                ReturnAfterFall = false;
                // Otherwise the cave always plays the Ascension: never start it already dressed.
                if (!returned) WarriorOutfit.ForgetDressed();
                if (GetComponent<CaveAmbience>() == null) gameObject.AddComponent<CaveAmbience>(); // (scenes built before it existed)
            }
        }

        void Start()
        {
            if (runOnStart) StartCoroutine(Run());
        }

        public IEnumerator Run()
        {
            if (returned)
            {
                yield return WakeAfterFall();
                if (passage != null) yield return WayOut();
                yield break;
            }
            IsRunning = true;
            var look = relic != null ? CaveLook.Take() : null; // the cave as it was before the light came
            yield return Rise();
            IsRunning = false;
            if (look != null) look.Restore();
            if (relic != null) StandOnFloor();
            if (passage != null) yield return WayOut();
            else if (!string.IsNullOrEmpty(exitScene)) yield return OpenExit();
        }

        // Back in the body on a screen: eyes at standing height above the cave floor, whatever the light left behind.
        void StandOnFloor()
        {
            Transform head = StoryFx.Head(FindAnyObjectByType<HandInputService>());
            if (head == null) return;
            Transform rig = head.root;
            if (Headset(head, rig)) return;
            RaycastHit[] below = Physics.RaycastAll(head.position + Vector3.up * 1f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
            float floor = float.MinValue;
            foreach (RaycastHit h in below)
                if (h.collider.attachedRigidbody == null && !h.collider.transform.IsChildOf(rig) && h.normal.y > 0.6f && h.point.y < head.position.y + 0.5f)
                    floor = Mathf.Max(floor, h.point.y);
            if (floor == float.MinValue) return;
            rig.position += Vector3.up * (floor + 1.6f - head.position.y);
            head.localRotation = Quaternion.identity;
        }

        // Everything the Ascension's light changes about how the cave looks: the ambient light, the fog, the sky and
        // every light in the scene. Taken before, put back exactly after, so the cave looks as it did.
        class CaveLook
        {
            UnityEngine.Rendering.AmbientMode mode;
            Color sky, equator, ground, ambient, fogColor;
            float ambientIntensity, fogDensity;
            bool fog;
            Material skybox;
            readonly List<(Light light, bool on, float intensity, Color color, float range)> lights = new List<(Light, bool, float, Color, float)>();

            public static CaveLook Take()
            {
                var l = new CaveLook
                {
                    mode = RenderSettings.ambientMode, sky = RenderSettings.ambientSkyColor, equator = RenderSettings.ambientEquatorColor,
                    ground = RenderSettings.ambientGroundColor, ambient = RenderSettings.ambientLight, ambientIntensity = RenderSettings.ambientIntensity,
                    fog = RenderSettings.fog, fogColor = RenderSettings.fogColor, fogDensity = RenderSettings.fogDensity, skybox = RenderSettings.skybox
                };
                foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    l.lights.Add((light, light.enabled, light.intensity, light.color, light.range));
                return l;
            }

            public void Restore()
            {
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                RenderSettings.ambientGroundColor = ground;
                if (mode == UnityEngine.Rendering.AmbientMode.Flat) RenderSettings.ambientLight = ambient;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.fog = fog;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.skybox = skybox;
                foreach (var (light, on, intensity, color, range) in lights)
                {
                    if (light == null || light.GetComponentInParent<LightShaft>() != null) continue; // (the shaft keeps itself)
                    light.enabled = on;
                    light.intensity = intensity;
                    light.color = color;
                    light.range = range;
                }
                ScreenFade.Set(new Color(0f, 0f, 0f, 0f));
                ComfortVignette.Set(0f);
            }
        }

        void Update()
        {
            if (runOnStart && GameInput.Pressed(GameAction.Skip)) Skip(); // (in the Rift, RiftArena passes N on)
        }

        /// <summary>N: the current line (or the "hold them up" wait).</summary>
        public void Skip()
        {
            if (waitingForPalms) palmsSkipped = true;
            else if (emberSpeech != null && emberSpeech.IsSpeaking) emberSpeech.SkipLine();
        }

        // ---------------------------------------------------------------- arriving

        // Thrown out of the tear: the rig glides out of it as the dark clears, slowing to a stop, and the lantern
        // tumbles out past the player and hangs in the air ahead, glowing.
        IEnumerator Arrive(Transform head, Transform rig)
        {
            Vector3 fwd = StoryFx.Flat(head.forward);
            if (entrance != null) fwd = StoryFx.Flat(entrance.transform.forward);
            if (Vector3.Dot(fwd, StoryFx.Flat(head.forward)) < 0f) fwd = -fwd;
            Vector3 start = rig.position - fwd * arrivalDrift;
            Vector3 end = rig.position;
            rig.position = start;

            lantern = CarriedLantern.Take();
            if (lantern != null)
            {
                if (lanternModel != null) lanternModel.gameObject.SetActive(false);
            }
            else if (lanternModel != null)
            {
                lantern = lanternModel;
                lantern.SetParent(null, true);
                lantern.gameObject.SetActive(true);
            }
            else lantern = CarriedLantern.Placeholder();
            Vector3 hover = head.position + (end - start) + fwd * 1.2f - Vector3.up * 0.35f;
            Vector3 from = head.position + Vector3.up * 0.2f - fwd * 0.3f;
            lantern.position = from;
            lanternLight = lantern.GetComponentInChildren<Light>();
            if (lanternLight == null)
            {
                lanternLight = new GameObject("LanternLight").AddComponent<Light>();
                lanternLight.transform.SetParent(lantern, false);
                lanternLight.transform.localPosition = Vector3.up * 0.12f;
                lanternLight.range = 3f;
                lanternLight.shadows = LightShadows.None;
            }
            lanternLight.enabled = true;
            lanternLight.color = blueFire;
            lanternLight.intensity = 1.5f;
            Quaternion spin = Random.rotation;

            CombatAudio.Play(CombatAudio.Tear, head.position - fwd, 0.6f, 0f);
            if (ScreenFade.Current.a < 0.5f) ScreenFade.Set(new Color(0.05f, 0f, 0.08f, 1f)); // played straight here: start in the dark too
            StartCoroutine(ScreenFade.To(new Color(0.05f, 0f, 0.08f, 0f), 2.2f));
            ComfortVignette.Set(0.8f);
            for (float t = 0f, d = 2.6f; t < d; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / d); // eased in and out: no sudden push
                rig.position = Vector3.Lerp(start, end, k);
                float l = Mathf.SmoothStep(0f, 1f, t / d);
                lantern.position = Vector3.Lerp(from, hover, l) + Vector3.up * Mathf.Sin(l * Mathf.PI) * 0.3f;
                lantern.rotation = Quaternion.Slerp(spin, Quaternion.Euler(0f, Quaternion.LookRotation(-fwd).eulerAngles.y, 0f), l);
                if (Random.value < 0.4f) StoryFx.Emit(burst, LanternCenter, Random.insideUnitSphere * 0.2f, 0.03f, 0.7f, blueFire);
                yield return null;
            }
            rig.position = end;
            ComfortVignette.Set(0f);
            StartCoroutine(Bob(lantern, hover));
            if (closeEntrance && entrance != null) StartCoroutine(entrance.Close(4f));
        }

        AscensionFire fire;
        bool lanternBobs = true;
        int bobber;

        IEnumerator Bob(Transform t, Vector3 at)
        {
            int me = ++bobber; // a newer Bob takes over
            for (float time = 0f; lanternBobs && t != null && me == bobber; time += Time.deltaTime)
            {
                t.position = at + Vector3.up * Mathf.Sin(time * 1.6f) * 0.04f;
                t.rotation *= Quaternion.Euler(0f, 12f * Time.deltaTime, 0f);
                yield return null;
            }
        }

        // ---------------------------------------------------------------- the cave: the sword in the stone

        // The lantern drifts over to hang beside the cauldron; it glows and hums louder as the player walks up to it; a
        // touch anywhere on its rim (or E close by) sets off the shockwave.
        IEnumerator TouchTheSword(HandInputService input, Transform head, PlayerLocomotion locomotion)
        {
            Bounds b = relic.bounds;
            Vector3 pommel = new Vector3(b.center.x, b.max.y, b.center.z);
            Vector3 guard = new Vector3(b.center.x, b.min.y + b.size.y * 0.45f, b.center.z);
            float rim = Mathf.Min(b.extents.x, b.extents.z) * 0.8f; // (a hand on its side counts, not only over its middle)
            Vector3 toPlayer = StoryFx.Flat(head.position - guard);
            Vector3 side = Vector3.Cross(Vector3.up, toPlayer);
            StartCoroutine(MoveLantern(pommel + side.normalized * 1.0f + toPlayer.normalized * 0.2f - Vector3.up * 0.1f, 3f));

            var glow = new GameObject("SwordGlow").AddComponent<Light>();
            glow.transform.position = (guard + pommel) * 0.5f + toPlayer * 0.15f;
            glow.type = LightType.Point;
            glow.color = Color.Lerp(blueFire, blueBlaze, 0.4f);
            glow.range = 2.5f;
            glow.shadows = LightShadows.None;
            var hum = new GameObject("SwordHum");
            hum.transform.position = guard;
            AudioSource tone = StoryFx.CreateSource(hum, StoryFx.Hum(), true, 1f);
            tone.pitch = 1.35f;
            tone.Play();

            if (locomotion != null) locomotion.Paused = false;
            Debug.Log($"[Cave] Walk into the light and touch the cauldron ({GameInput.KeyName(GameAction.Interact)} when close)");
            float nextHint = Time.time + 2.5f, reach = 0f;
            while (true)
            {
                float near = new Vector2(head.position.x - guard.x, head.position.z - guard.z).magnitude; // across the floor
                float k = 1f - Mathf.Clamp01((near - 0.8f) / 4f);
                // A hand reaching for the hilt: the blade answers it, the hum rising and tightening, light gathering
                // from the hilt towards the fingers (the last half metre).
                float hand = HandToHilt(input, guard, pommel, out Vector3 fingers) - rim;
                reach = Mathf.MoveTowards(reach, 1f - Mathf.Clamp01((hand - touchReach) / 0.45f), Time.deltaTime * 3f);
                glow.intensity = Mathf.Lerp(0.6f, 3f, k) * (1f + 0.15f * Mathf.Sin(Time.time * (3f + 9f * reach))) * (1f + 0.6f * reach);
                tone.volume = 0.05f + 0.25f * k + 0.2f * reach;
                tone.pitch = 1.35f + 0.35f * reach;
                if (Random.value < (0.2f + 0.6f * k) * 72f * Time.deltaTime)
                    StoryFx.Emit(burst, Vector3.Lerp(guard + Vector3.down * 0.5f, pommel, Random.value) + Random.insideUnitSphere * 0.05f,
                                 Vector3.up * 0.15f + Random.insideUnitSphere * 0.05f, Random.Range(0.008f, 0.02f), 0.9f, blueFire);
                if (reach > 0.05f && Random.value < reach * 40f * Time.deltaTime)
                {
                    Vector3 from = Vector3.Lerp(guard, pommel, Random.value);
                    StoryFx.Emit(burst, from, (fingers - from) * 2.2f, Random.Range(0.006f, 0.014f), 0.45f, Color.Lerp(blueFire, blueBlaze, 0.5f));
                }
                if (Time.time > nextHint)
                {
                    CombatFx.Popup(near > 1.8f ? "Walk into the light" : "Touch the cauldron", pommel + Vector3.up * 0.3f + toPlayer.normalized * 0.2f,
                                   new Color(0.7f, 0.85f, 1f), 0.55f);
                    nextHint = Time.time + 8f;
                }
                if (hand < touchReach) break;
                if (GameInput.Pressed(GameAction.Interact))
                {
                    if (near < 2.2f) break;
                    CombatFx.Popup("Closer...", pommel + Vector3.up * 0.3f, new Color(0.7f, 0.85f, 1f), 0.5f);
                }
                yield return null;
            }
            if (locomotion != null) locomotion.Paused = true;
            yield return Shockwave(guard, head);
            Destroy(hum);
            StartCoroutine(FadeLight(glow, 1.5f));
        }

        // How close (m) the nearest tracked fingertip or palm is to the hilt (from guard to pommel), and where it is.
        static float HandToHilt(HandInputService input, Vector3 guard, Vector3 pommel, out Vector3 nearest)
        {
            nearest = pommel;
            float best = float.MaxValue;
            if (input == null) return best;
            for (int side = 0; side < 2; side++)
            {
                HandFrame f = side == 0 ? input.Left : input.Right;
                if (f == null || !f.IsTracked) continue;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 p = i == 0 ? f.PalmCenter : i == 1 ? f[HandJoint.IndexTip] : f[HandJoint.MiddleTip];
                    Vector3 ab = pommel - guard;
                    float t = Mathf.Clamp01(Vector3.Dot(p - guard, ab) / ab.sqrMagnitude);
                    float d = (p - (guard + ab * t)).magnitude;
                    if (d < best)
                    {
                        best = d;
                        nearest = p;
                    }
                }
            }
            return best;
        }

        // The touch: the sword answers with a shockwave. A ring of light sweeps out through the cave at the hilt's
        // height and another along the floor, a boom, the hands rumble, the shaft and the mushrooms flare, dust sifts
        // down from the roof. The view itself never shakes (a headset), and the flash rises and falls softly.
        IEnumerator Shockwave(Vector3 at, Transform head)
        {
            Debug.Log("[Cave] The cauldron is touched: a shockwave rings out of it");
            var ears = new GameObject("ShockwaveSound");
            ears.transform.position = at;
            AudioSource boom = StoryFx.CreateSource(ears, Boom(), false, 0.5f);
            boom.volume = 1f;
            boom.Play();
            CombatAudio.Play(CombatAudio.Clang, at, 0.8f, 0f);
            CombatFx.Rumble(null, 1f, 0.6f);
            if (shaft != null) shaft.Flare(2.5f, 2f);
            StartCoroutine(ScreenFade.Pulse(StoryFx.WithAlpha(Color.Lerp(blueFire, blueBlaze, 0.6f), 0.35f), 0.12f, 1f));
            float floor = Headset(head, head.root) ? head.root.position.y : head.position.y - 1.6f;
            GroundLightning.Strike(this, new Vector3(at.x, floor + 0.05f, at.z), 9f, 2.2f); // gold lightning cracks out across the floor
            StartCoroutine(Ring(at, 14f, 1.2f, 0.3f));
            StartCoroutine(Ring(new Vector3(at.x, floor + 0.05f, at.z), 12f, 1.5f, 0.45f));
            for (int i = 0; i < 160; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), Random.Range(-0.05f, 0.15f), Mathf.Sin(a));
                StoryFx.Emit(burst, at, dir * Random.Range(4f, 8f), Random.Range(0.015f, 0.04f), Random.Range(0.6f, 1.2f), Color.Lerp(blueFire, blueBlaze, 0.5f));
            }
            for (int i = 0; i < 70; i++) // dust shaken loose from the roof
                StoryFx.Emit(burst, at + new Vector3(Random.Range(-5f, 5f), Random.Range(3.5f, 5.5f), Random.Range(-5f, 5f)),
                             Vector3.down * Random.Range(0.6f, 1.4f), Random.Range(0.02f, 0.05f), Random.Range(2f, 3.5f), new Color(0.6f, 0.6f, 0.65f, 0.5f));
            var flames = FindAnyObjectByType<CauldronFlames>();
            if (flames != null) flames.Flare(1.2f); // the fire roars up at the touch
            var ambience = GetComponent<CaveAmbience>();
            if (ambience != null) ambience.Flare(3f, 1.8f); // (it drives the mushrooms' glow)
            else
                foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.name.StartsWith("MushroomGlow")) StartCoroutine(FlareLight(l, 3f, 1.8f));
            yield return Wait(1.8f);
            Destroy(ears, 3f);
        }

        // A ring of light racing outward from a point (flat), thinning and fading as it goes.
        IEnumerator Ring(Vector3 centre, float radius, float seconds, float width)
        {
            var go = new GameObject("ShockRing");
            var line = go.AddComponent<LineRenderer>();
            line.loop = true;
            line.positionCount = 72;
            line.useWorldSpace = true;
            Texture2D soft = VFX.SparkHands.CreateSoftLine(64);
            line.sharedMaterial = VFX.SparkHands.CreateGlowMaterial(soft);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = t / seconds;
                float r = Mathf.Lerp(0.2f, radius, 1f - (1f - k) * (1f - k));
                for (int i = 0; i < 72; i++)
                {
                    float a = i / 72f * Mathf.PI * 2f;
                    line.SetPosition(i, centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                }
                line.widthMultiplier = width * (1f - k * 0.7f);
                Color c = StoryFx.WithAlpha(Color.Lerp(blueBlaze, blueFire, k), 0.9f * (1f - k));
                line.startColor = line.endColor = c;
                yield return null;
            }
            Destroy(line.sharedMaterial); // (made for this ring alone)
            Destroy(soft);
            Destroy(go);
        }

        static IEnumerator FlareLight(Light l, float times, float seconds)
        {
            float base_ = l.intensity;
            for (float t = 0f; t < seconds && l != null; t += Time.deltaTime)
            {
                l.intensity = base_ * (1f + (times - 1f) * Mathf.Sin(Mathf.Min(1f, t / seconds) * Mathf.PI) * (1f - t / seconds * 0.5f));
                yield return null;
            }
            if (l != null) l.intensity = base_;
        }

        static IEnumerator FadeLight(Light l, float seconds)
        {
            float from = l.intensity;
            for (float t = 0f; t < seconds && l != null; t += Time.deltaTime)
            {
                l.intensity = Mathf.Lerp(from, 0f, t / seconds);
                yield return null;
            }
            if (l != null) Destroy(l.gameObject);
        }

        // The lantern glides (eased) to a new spot and bobs there.
        IEnumerator MoveLantern(Vector3 to, float seconds)
        {
            if (lantern == null) yield break;
            bobber++; // stop the old bob
            Vector3 from = lantern.position;
            for (float t = 0f; t < seconds && lantern != null; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                lantern.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.25f;
                yield return null;
            }
            if (lantern != null) StartCoroutine(Bob(lantern, to));
        }

        // ---------------------------------------------------------------- after: the way out of the cave

        // Back on the cave floor, suited and armed: the mountain rumbles, the rock pile at the back comes down, and
        // the tunnel behind it glows with the light of the Kalakh's world. Ember sends the Weaver out through it and
        // keeps beside them.
        IEnumerator WayOut()
        {
            Transform head = StoryFx.Head(FindAnyObjectByType<HandInputService>());
            if (head == null) yield break;
            if (offerPractice)
            {
                // First, if the player wants it: practice with the blade (SwordPractice), or straight on.
                practice = GetComponent<SwordPractice>();
                if (practice == null) practice = gameObject.AddComponent<SwordPractice>();
                bool offering = true;
                StartCoroutine(EmberAlong(head, () => offering));
                yield return practice.Offer(head, Line);
                offering = false;
            }
            if (ember != null) ember.SetMood(EmberFlame.Mood.Excited);
            yield return Line(returned ? "The bridge is waiting. Let's break the way open again." : "...Do you hear that? The temple felt the Weaver wake.", 1f);
            StartCoroutine(passage.Open());
            if (ember != null && passage.rocks != null) ember.LookAt(passage.rocks.transform);
            yield return Wait(2.5f);
            if (ember != null && passage.threshold != null) ember.PointAt(passage.threshold.transform, 3f);
            if (returned) yield return Line("Back into their world. This time, watch their wind-ups, and watch your back.", 1.4f);
            else
            {
                yield return Line("There's your way out. But that bridge doesn't lead home...", 1.4f);
                yield return Line("It leads into their world. The Kalakh's world. Bare rock and dead moonlight, as far as you can see.", 1.6f);
                if (ember != null) ember.SetMood(EmberFlame.Mood.Calm);
                yield return Line("They'll come for that blade. Better we meet them out there than let them in here. Follow the light, Weaver.", 1.5f);
            }
            if (ember != null) ember.SetMood(EmberFlame.Mood.Calm);
            // (in a headset the walk out moves the view, so Ember asks first: standing still is the answer)
            yield return Line("Walk on across the bridge... or stand still a moment, and I'll lead you there.", 0.6f);
            Debug.Log($"[Cave] Walk through the tunnel at the back into the light, or stand still and Ember leads you out ({GameInput.KeyName(GameAction.Interact)} to go now)");

            // The player can walk out themselves; if they stand still a few seconds (or press E), Ember leads them:
            // they walk out of the cave, up the tunnel and through the threshold into the monster world.
            float still = 0f;
            Vector3 last = head.position;
            bool led = false;
            while (this != null && !passage.Entering)
            {
                Vector3 moved = head.position - last;
                moved.y = 0f;
                still = moved.magnitude > 0.3f * Time.deltaTime ? 0f : still + Time.deltaTime; // (slower than 0.3 m/s: standing)
                last = head.position;
                if (!led && passage.IsOpen && (still > leadAfter || GameInput.Pressed(GameAction.Interact)))
                {
                    led = true;
                    if (ember != null) ember.SetMood(EmberFlame.Mood.Excited);
                    StartCoroutine(Line("This way. Stay close to me.", 0.6f));
                    StartCoroutine(passage.WalkOut(head)); // (Ember keeps beside them on the way)
                }
                if (ember != null) FollowPlayer(head);
                yield return null;
            }
            while (this != null)
            {
                if (ember != null) FollowPlayer(head);
                yield return null;
            }
        }

        // Ember keeps beside the player while `still` holds.
        IEnumerator EmberAlong(Transform head, Func<bool> still)
        {
            while (still() && this != null)
            {
                if (ember != null) FollowPlayer(head);
                yield return null;
            }
        }

        // ---------------------------------------------------------------- after: the way into the Rift

        // The Kalakh tear a rift open in the cave wall; Ember sends the Weaver through. Walking into it (or E) loads
        // the fight; Ember keeps beside the player meanwhile.
        IEnumerator OpenExit()
        {
            Transform head = StoryFx.Head(FindAnyObjectByType<HandInputService>());
            if (head == null) yield break;
            Vector3 at;
            Quaternion facing;
            if (exitPoint != null)
            {
                at = exitPoint.position;
                facing = Quaternion.LookRotation(StoryFx.Flat(new Vector3(head.position.x, at.y, head.position.z) - at));
            }
            else
            {
                Vector3 right = Vector3.Cross(Vector3.up, StoryFx.Flat(head.forward));
                at = new Vector3(head.position.x, Headset(head, head.root) ? head.root.position.y : head.position.y - 1.6f, head.position.z) + right * 3f;
                facing = Quaternion.LookRotation(-right);
            }
            if (ember != null) ember.SetMood(EmberFlame.Mood.Excited);
            yield return Line("...Wait. Do you feel that?", 0.8f);
            RiftGate gate = RiftGate.Open(at, facing);
            gate.riftScene = exitScene;
            Debug.Log($"[Cave] A Kalakh rift tears open in the wall: walk into it ({GameInput.KeyName(GameAction.Interact)}) for {exitScene}");
            if (ember != null) ember.PointAt(gate.Tear.transform, 3f);
            yield return Line("The Kalakh. They followed the light all the way down here and tore a way in.", 1.4f);
            yield return Line("If we leave that open, they'll pour through. We close it from their side.", 1.4f);
            if (ember != null) ember.SetMood(EmberFlame.Mood.Calm);
            yield return Line("Walk into the rift, Weaver. I'll be right behind you.", 1.5f);
            while (gate != null)
            {
                if (ember != null) FollowPlayer(head);
                if (GameInput.Pressed(GameAction.Interact)) gate.Enter();
                yield return null;
            }
        }

        // ---------------------------------------------------------------- the Ascension

        IEnumerator Rise()
        {
            var input = FindAnyObjectByType<HandInputService>();
            Transform head = StoryFx.Head(input);
            Transform rig = head != null ? head.root : null;
            if (head == null || rig == null) yield break;
            var locomotion = FindAnyObjectByType<PlayerLocomotion>();
            if (locomotion != null) locomotion.Paused = true;
            yield return PlacePlayer(head, rig);
            if (outfit == null) outfit = FindAnyObjectByType<WarriorOutfit>();
            if (outfit == null) Debug.LogWarning("[Rift] No WarriorOutfit in the scene: Create Shadow Rift Scene adds it.");
            else StartCoroutine(outfit.Load()); // the character model loads while the player arrives

            yield return Arrive(head, rig);
            if (relic != null) yield return TouchTheSword(input, head, locomotion);
            Debug.Log("[Rift] Pulled through: the Ascension begins (N skips a line)");

            // The body's eyes: the player's head until the soul leaves; then the body hangs where it was and the
            // light lifts it (lift) while the soul floats out in front of it.
            character = new GameObject("BodyEyes").transform;
            character.SetPositionAndRotation(head.position, Quaternion.Euler(0f, head.eulerAngles.y, 0f));
            float baseY = rig.position.y;
            lift = 0f;
            bool ascending = true;
            StartCoroutine(Body());
            IEnumerator Body()
            {
                while (ascending)
                {
                    if (soulOut)
                    {
                        character.position = bodyEyes + Vector3.up * lift;
                        if (outfit != null) outfit.SetBodyPose(character.position, character.eulerAngles.y);
                        FollowSoul(head, rig);
                        SoulCord(head);
                    }
                    else
                    {
                        rig.position = new Vector3(rig.position.x, baseY + lift, rig.position.z);
                        character.position = head.position;
                    }
                    yield return null;
                }
            }

            // 1. The flames come up round the player: the Ascension happens inside the fire.
            Debug.Log("[Rift] The fire rises round the player");
            fire = gameObject.GetComponent<AscensionFire>();
            if (fire == null) fire = gameObject.AddComponent<AscensionFire>();
            fire.Centre = character;
            fire.Target = 0.55f;
            StoryFx.Burst(burst, LanternCenter, burstSparks, 2f, 0.05f, 1.2f, blueFire);
            var roomLight = new GameObject("AscensionLight").AddComponent<Light>();
            roomLight.transform.position = LanternCenter + Vector3.up * 0.3f;
            roomLight.type = LightType.Point;
            roomLight.color = blueFire;
            roomLight.shadows = LightShadows.None;
            var ears = new GameObject("AscensionSound");
            ears.transform.SetParent(head, false);
            AudioSource rumble = StoryFx.CreateSource(ears, StoryFx.Rumble(), false, 0f);
            rumble.volume = 0.8f;
            rumble.Play();

            Color sky = RenderSettings.ambientSkyColor, equator = RenderSettings.ambientEquatorColor, ground = RenderSettings.ambientGroundColor;
            Color fog = RenderSettings.fogColor;
            for (float t = 0f, d = Dur(2.5f); t < d; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / d);
                roomLight.range = Mathf.Lerp(1f, 14f, k);
                roomLight.intensity = Mathf.Lerp(0.5f, 6f, k);
                lanternLight.intensity = Mathf.Lerp(1.5f, 5f, k);
                RenderSettings.ambientSkyColor = Color.Lerp(sky, blueFire * 0.8f, k);
                RenderSettings.ambientEquatorColor = Color.Lerp(equator, blueFire * 0.5f, k);
                RenderSettings.fogColor = Color.Lerp(fog, blueFire * 0.25f, k);
                ScreenFade.Set(StoryFx.WithAlpha(blueFire, 0.22f * k));
                if (Random.value < 0.5f) StoryFx.Burst(burst, LanternCenter, 4, 1f + 2f * k, 0.04f, 0.9f, blueFire);
                yield return null;
            }

            // 2. The void above splits open.
            Debug.Log("[Rift] The dark above splits open");
            Vector3 player = head.position;
            AudioSource boom = StoryFx.CreateSource(ears, Boom(), false, 0f);
            boom.volume = 1f;
            boom.Play();
            StoryFx.Burst(burst, player + Vector3.up * 25f, 90, 6f, 1.2f, 2f, StoryFx.WithAlpha(blueBlaze, 0.9f));
            StartCoroutine(ScreenFade.Pulse(StoryFx.WithAlpha(Color.Lerp(blueFire, blueBlaze, 0.5f), 0.45f), 0.15f, Dur(0.9f)));
            yield return Wait(Dur(0.8f));

            // 3. A column of flame roars up round the player.
            Debug.Log("[Rift] The column of fire takes the player");
            fire.Target = 1f;
            Vector3 feet = new Vector3(player.x, Headset(head, rig) ? baseY : player.y - 1.6f, player.z);
            if (shaft != null) shaft.gameObject.SetActive(false); // (one column of fire: not the altar's light as well)
            Renderer core = Beam(feet, blueFire, 0.3f);
            var skyLight = new GameObject("SkyLight").AddComponent<Light>();
            skyLight.type = LightType.Spot;
            skyLight.transform.SetPositionAndRotation(player + Vector3.up * 8f, Quaternion.LookRotation(Vector3.down));
            skyLight.spotAngle = 30f;
            skyLight.range = 20f;
            skyLight.intensity = 0f;
            skyLight.color = blueFire;
            boom.Play();
            Color strike = Color.Lerp(blueFire, blueBlaze, 0.6f);
            yield return ScreenFade.To(StoryFx.WithAlpha(strike, 0.75f), Dur(0.18f)); // bright, but rising: no hard flash
            for (float t = 0f, d = Dur(0.8f); t < d; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / d);
                core.transform.localScale = new Vector3(1.8f * k, core.transform.localScale.y, 1.8f * k);
                skyLight.intensity = 40f * k;
                ScreenFade.Set(StoryFx.WithAlpha(Color.Lerp(strike, blueFire, k), Mathf.Lerp(0.75f, 0.25f, k)));
                yield return null;
            }

            // 4. The fire lifts the player into the air, burning brighter, with a boom and a chime.
            Debug.Log("[Rift] The player rises in the fire");
            ring = StoryFx.CreateSource(ears, Ringing(), true, 0f);
            ring.Play();
            ComfortVignette.Set(0.6f); // the player is being lifted: narrow the view's edge while they rise
            for (float t = 0f, d = Dur(4.5f); t < d; t += Time.deltaTime)
            {
                float k = t / d;
                lift = liftHeight * Mathf.SmoothStep(0f, 1f, k);
                Color white = Color.Lerp(blueFire, blueBlaze, Mathf.SmoothStep(0f, 1f, k));
                SetBeamColor(core, StoryFx.WithAlpha(white, 0.3f));
                skyLight.color = white;
                roomLight.color = white;
                ring.volume = 0.3f * k;
                ScreenFade.Set(StoryFx.WithAlpha(white, Mathf.Lerp(0.25f, 0.6f, k * k)));
                RisingMotes(character, white);
                yield return null;
            }
            ComfortVignette.Set(0f);
            Debug.Log("[Rift] The fire fills the view");
            AudioSource chime = StoryFx.CreateSource(ears, Chime(), false, 0f);
            chime.volume = 0.9f;
            chime.Play();
            boom.Play();
            ring.volume = 0.08f;
            yield return ScreenFade.To(blueBlaze, Dur(0.5f));
            yield return Wait(Dur(0.3f)); // the view turns round behind solid white

            // 5. The soul slips out of the body: behind the white the view turns round, and as the white clears the
            // soul drifts back out of the body, which hangs in the light in front of it.
            bool hovering = true;
            StartCoroutine(Hover());
            IEnumerator Hover()
            {
                for (float t = 0f; hovering; t += Time.deltaTime)
                {
                    lift = liftHeight + Mathf.Sin(t * 1.3f) * 0.04f;
                    RisingMotes(character, blueBlaze);
                    // Once Ember has arrived (awake), it floats between the soul and the body.
                    if (ember != null && ember.CurrentMood != EmberFlame.Mood.Asleep) FollowPlayer(head);
                    yield return null;
                }
            }
            LeaveBody(head, rig);
            // The soul must see the suit go on: the fire parts round the body, the column and the cauldron die down.
            var cauldronFire = FindAnyObjectByType<CauldronFlames>();
            if (cauldronFire != null) cauldronFire.Damp = 0.25f;
            fire.ClearView = true;
            SetBeamColor(core, StoryFx.WithAlpha(blueBlaze, 0.07f));
            ComfortVignette.Set(0.6f); // the soul drifts back out of the body
            StartCoroutine(ScreenFade.To(StoryFx.WithAlpha(blueBlaze, hoverWash), Dur(2.5f)));
            for (float t = 0f, d = Dur(soulDrift); t < d; t += Time.deltaTime)
            {
                soulAway = Mathf.Lerp(0.15f, soulReach, Mathf.SmoothStep(0f, 1f, t / d));
                if (!Headset(head, rig)) LookAtBody(head, Mathf.SmoothStep(0f, 1f, t / d * 2f)); // on a screen, turn the view onto the body
                yield return null;
            }
            soulAway = soulReach;
            ComfortVignette.Set(0f);

            // 6. Ember comes out of the lantern, finds the soul, and explains everything while the light weaves the
            // outfit onto the body, part by part.
            yield return EmberSpeaks(input, head);

            // 7. Back into the body: the soul is drawn in, a last flash, and the light lets go behind the white.
            Debug.Log("[Rift] The soul returns to the body");
            if (outfit != null && !outfit.IsComplete) outfit.Equip(1f);
            float away = soulAway;
            ComfortVignette.Set(0.7f);
            for (float t = 0f, d = Dur(2.4f); t < d; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / d);
                soulAway = Mathf.Lerp(away, 0.4f, k); // drawn back towards the body, slowing: the white covers the rest
                ScreenFade.Set(StoryFx.WithAlpha(blueBlaze, Mathf.Lerp(hoverWash, 0.85f, k)));
                yield return null;
            }
            yield return ScreenFade.To(blueBlaze, Dur(0.4f));
            yield return null; // a solid white frame before the view jumps back
            ReturnToBody(head, rig);
            SetDownClear(head, rig); // (behind the solid fire: the player sees no jump)
            fire.ClearView = false;
            if (cauldronFire != null) cauldronFire.Damp = 1f;
            hovering = false;
            chime.Play();
            Destroy(core.sharedMaterial); // (its own)
            Destroy(core.gameObject);
            if (shaft != null) shaft.gameObject.SetActive(true);
            Destroy(skyLight.gameObject);
            lanternBobs = false;
            if (lantern != null) Destroy(lantern.gameObject); // its flame is Ember now
            yield return Wait(Dur(1f));
            float lifted = lift;
            fire.Target = 0f; // the flames sink as the player comes down
            ComfortVignette.Set(0.6f); // floating down
            for (float t = 0f, d = Dur(4f); t < d; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / d);
                lift = Mathf.Lerp(lifted, 0f, k);
                if (ember != null) FollowPlayer(head);
                ScreenFade.Set(StoryFx.WithAlpha(blueBlaze, 1f - k));
                roomLight.intensity = Mathf.Lerp(6f, 0f, k);
                ring.volume = 0.08f * (1f - k);
                // The Rift's own dark creeps back as the light lets go.
                RenderSettings.ambientSkyColor = Color.Lerp(blueFire * 0.8f, sky, k);
                RenderSettings.ambientEquatorColor = Color.Lerp(blueFire * 0.5f, equator, k);
                RenderSettings.ambientGroundColor = ground;
                RenderSettings.fogColor = Color.Lerp(blueFire * 0.25f, fog, k);
                yield return null;
            }
            lift = 0f;
            ComfortVignette.Set(0f);
            yield return null; // the body puts the player down
            ascending = false;
            ScreenFade.Set(new Color(1f, 1f, 1f, 0f));
            RenderSettings.fogColor = fog;
            Destroy(roomLight.gameObject);
            ring.Stop();
            if (locomotion != null) locomotion.Paused = false;
            if (ember != null) ember.SetMood(EmberFlame.Mood.Calm);
            Destroy(ears, 5f);
            Destroy(character.gameObject, 5f);
            OnAscended?.Invoke();
        }

        static float Dur(float seconds) => seconds; // (one place to shorten every beat while testing)

        IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        // ---------------------------------------------------------------- out of body

        Transform character; // the body's eyes
        float lift;
        AudioSource ring;
        bool soulOut;
        Vector3 bodyEyes, soulDirection, soulAnchor, rigPosition;
        Quaternion rigRotation, headRotation;
        float soulAway, soulReach;
        readonly RaycastHit[] viewHits = new RaycastHit[8];

        // The body stays where it hangs (the figure appears there, in the player's own clothes); the view turns round
        // to face it, with as much open space behind the soul as there is.
        void LeaveBody(Transform head, Transform rig)
        {
            if (outfit == null) return;
            soulOut = true;
            float yaw = head.eulerAngles.y;
            character.SetPositionAndRotation(head.position, Quaternion.Euler(0f, yaw, 0f));
            bodyEyes = head.position - Vector3.up * lift;
            rigPosition = rig.position;
            rigRotation = rig.rotation;
            headRotation = head.localRotation;

            // The first of these directions (from the body) with room for the soul: in front, then round the sides.
            // The body then turns to face that way, so the soul always sees it from the front.
            Vector3 chest = character.position - Vector3.up * 0.3f;
            float best = -1f;
            foreach (float angle in new[] { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 180f })
            {
                Vector3 dir = Quaternion.Euler(0f, yaw + angle, 0f) * Vector3.forward;
                float free = soulDistance;
                int n = Physics.SphereCastNonAlloc(chest, 0.25f, dir, viewHits, soulDistance, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                    if (viewHits[i].distance > 0f && !viewHits[i].collider.transform.IsChildOf(rig))
                        free = Mathf.Min(free, viewHits[i].distance - 0.15f);
                if (free > best)
                {
                    best = free;
                    soulDirection = dir;
                }
                if (free >= soulDistance * 0.85f) break;
            }
            soulReach = Mathf.Max(1.2f, best);
            float facing = Quaternion.LookRotation(soulDirection).eulerAngles.y;
            character.rotation = Quaternion.Euler(0f, facing, 0f);
            outfit.ShowBody(character.position, facing);

            // Turn the view round to look back at the body, and start the soul right at its eyes.
            rig.RotateAround(head.position, Vector3.up, Vector3.SignedAngle(StoryFx.Flat(head.forward), -soulDirection, Vector3.up));
            soulAway = 0.15f;
            soulAnchor = SoulSpot();
            rig.position += soulAnchor - head.position;
            Debug.Log($"[Rift] The soul leaves the body ({soulReach:F1} m of room)");
        }

        // The soul's spot is fixed to where the body hangs, not to its bob in the light: the view stays steady while
        // the body gently rises and falls in front of it.
        Vector3 SoulSpot() => bodyEyes + Vector3.up * liftHeight + soulDirection * soulAway + Vector3.up * soulHeight;

        // The soul drifts with the body as it bobs in the light; the rig moves by how much the spot moved, so a
        // headset's own head movement still counts.
        void FollowSoul(Transform head, Transform rig)
        {
            Vector3 spot = SoulSpot();
            rig.position += spot - soulAnchor;
            soulAnchor = spot;
        }

        // On a screen (no head tracking) tilt the view down onto the body's chest.
        void LookAtBody(Transform head, float amount)
        {
            if (outfit == null) return;
            Vector3 look = outfit.BodyChest - head.position;
            float pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
            head.localRotation = Quaternion.Slerp(headRotation, Quaternion.Euler(pitch, 0f, 0f), amount);
        }

        // The soul snaps back (behind the white): the body goes (the player is in it, wearing the outfit), the rig
        // returns to where the body hangs, facing the way it faced.
        // Moves the player out from the cauldron, the way they were already standing from it, to land at a distance.
        void SetDownClear(Transform head, Transform rig)
        {
            if (relic == null) return;
            Vector3 centre = relic.bounds.center;
            Vector3 away = StoryFx.Flat(head.position - centre);
            if (away.sqrMagnitude < 0.01f) away = StoryFx.Flat(-head.forward);
            Vector3 spot = centre + away.normalized * landDistance;
            Vector3 shift = spot - head.position;
            shift.y = 0f;
            rig.position += shift;
            if (character != null) character.position += shift;
        }

        void ReturnToBody(Transform head, Transform rig)
        {
            if (!soulOut) return;
            soulOut = false;
            if (outfit != null) outfit.HideBody();
            rig.SetPositionAndRotation(rigPosition, rigRotation);
            if (!Headset(head, rig)) head.localRotation = headRotation;
        }

        IEnumerator PlacePlayer(Transform head, Transform rig)
        {
            if (playerStart == null && !(runOnStart && entrance != null)) yield break;
            // Stand the player at the arrival spot (or just out in front of the tear they come through): in a
            // headset the rig is on the floor, on a screen at eye level. Whatever the scene's rig was left at.
            // The headset's pose only arrives with the first frames (until then the head sits on the rig and it
            // looks like a screen): wait for it in the dark the scene starts in.
            if (ScreenFade.Current.a < 0.5f) ScreenFade.Set(new Color(0.05f, 0f, 0.08f, 1f));
            for (float t = 0f; t < 0.5f && head.localPosition.sqrMagnitude < 1e-6f; t += Time.deltaTime) yield return null;
            Vector3 spot = playerStart != null ? playerStart.position
                         : entrance.transform.position + StoryFx.Flat(entrance.transform.forward) * 1.2f;
            float yaw = playerStart != null ? playerStart.eulerAngles.y : entrance.transform.eulerAngles.y;
            bool headset = Headset(head, rig);
            rig.SetPositionAndRotation(spot + Vector3.up * (headset ? 0f : 1.6f), Quaternion.Euler(0f, yaw, 0f));
            if (!headset) head.localRotation = Quaternion.identity; // looking straight ahead
            else
            {
                // In a headset the player stands wherever they are in their room, facing wherever: turn and shift
                // the rig (still in the dark) so their own head is over the spot, facing the altar.
                rig.rotation = Quaternion.Euler(0f, rig.eulerAngles.y + Mathf.DeltaAngle(head.eulerAngles.y, yaw), 0f);
                Vector3 off = head.position - rig.position;
                rig.position = new Vector3(spot.x - off.x, spot.y, spot.z - off.z);
            }
            Physics.SyncTransforms();
            Debug.Log($"[Cave] The player arrives at {spot} (eyes {head.position})");
        }

        // Back from a fall in the monster world: the Weaver wakes on the cave floor in the dark, still suited (the
        // outfit was carried), the sword gone from its stone and on their back; Ember is there, and the choice comes
        // again: train, or straight back out through the tunnel.
        IEnumerator WakeAfterFall()
        {
            var input = FindAnyObjectByType<HandInputService>();
            Transform head = StoryFx.Head(input);
            Transform rig = head != null ? head.root : null;
            if (head == null || rig == null) yield break;
            ScreenFade.Set(Color.black);
            yield return PlacePlayer(head, rig);
            StandOnFloor();
            if (lanternModel != null) lanternModel.gameObject.SetActive(false); // (its flame is Ember)
            if (outfit == null) outfit = FindAnyObjectByType<WarriorOutfit>();
            if (outfit != null && !outfit.IsComplete) outfit.DressNow();
            Debug.Log("[Cave] Back from the fall: the Weaver wakes in the cave, suited and armed");

            var go = new GameObject("Ember");
            go.transform.position = EmberSpot(head);
            ember = go.AddComponent<EmberFlame>();
            ember.Height = 0.6f;
            emberSpeech = go.AddComponent<StorySpeech>();
            emberSpeech.TextOffset = Vector3.up * 0.9f;
            ember.LookAt(head);
            ember.SetMood(EmberFlame.Mood.Calm);

            while (ScreenFade.Busy) yield return null; // (the scene change's own fade in)
            if (ScreenFade.Current.a > 0.01f) yield return ScreenFade.To(new Color(0f, 0f, 0f, 0f), 1.5f);
            ember.Burst(60);
            yield return Line("There you are. Breathe, Weaver... you're back in the temple. The light carried you home.", 1.2f);
            ember.SetMood(EmberFlame.Mood.Excited);
            yield return Line("They knocked you down. They didn't put you out. Your blade's still on your back.", 1.2f);
            ember.SetMood(EmberFlame.Mood.Calm);
        }

        // A headset tracks the real eye height above the rig; on a screen the eyes sit at the rig.
        static bool Headset(Transform head, Transform rig) => head.position.y - rig.position.y > 0.3f;

        // The thread between soul and body: specks of light drifting along a sagging line from the soul's chest to
        // the body's.
        void SoulCord(Transform head)
        {
            if (outfit == null || !outfit.BodyShown) return;
            Vector3 from = head.position - Vector3.up * 0.45f, to = outfit.BodyChest;
            int n = Random.value < 0.6f ? 2 : 1;
            for (int i = 0; i < n; i++)
            {
                float u = Random.value;
                Vector3 p = Vector3.Lerp(from, to, u) - Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.12f + Random.insideUnitSphere * 0.015f;
                StoryFx.Emit(burst, p, (to - from).normalized * 0.05f, Random.Range(0.01f, 0.025f), 0.5f,
                             StoryFx.WithAlpha(new Color(0.75f, 0.88f, 1f), 0.8f));
            }
        }

        // ---------------------------------------------------------------- Ember

        EmberFlame ember;
        StorySpeech emberSpeech;
        SwordPractice practice;

        // Ember rises out of the lantern's wick, drifts over to the soul and (after introducing itself) explains it
        // all: the lantern, the runes, what the Weaver must do and the powers. Each part of the outfit forms on the
        // body as Ember gets to it; the Jyoti Seal wakes when the player holds up their hands.
        IEnumerator EmberSpeaks(HandInputService input, Transform head)
        {
            var go = new GameObject("Ember");
            go.transform.position = LanternCenter;
            ember = go.AddComponent<EmberFlame>();
            ember.Height = 0.6f;
            emberSpeech = go.AddComponent<StorySpeech>();
            emberSpeech.TextOffset = Vector3.up * 0.9f;
            ember.LookAt(head);
            if (lanternLight != null) lanternLight.color = paleBlue;
            Vector3 from = go.transform.position;
            for (float t = 0f; t < 2.5f; t += Time.deltaTime) // out of the wick and across to the soul
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 2.5f);
                go.transform.position = Vector3.Lerp(from, EmberSpot(head), k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.4f;
                if (Random.value < 0.5f) StoryFx.Emit(burst, go.transform.position, Random.insideUnitSphere * 0.3f, 0.04f, 0.8f, paleBlue);
                yield return null;
            }
            ember.SetMood(EmberFlame.Mood.Calm);
            ember.Burst(60);
            Debug.Log("[Rift] Ember meets the soul (N skips a line)");

            // Who are you? (Ember, curious, circling the soul.)
            yield return Voiced("many_years", "It's been many years since someone has been here. You have no idea...", 0.6f);
            yield return Voiced("introductions", "...Right. Introductions.", 0.2f);
            ember.SetMood(EmberFlame.Mood.Excited);
            yield return Voiced("my_name_is_ember", "Hii! My name is Ember.", 0.2f);
            ember.SetMood(EmberFlame.Mood.Calm);
            ember.PointAt(head, 2.5f);
            yield return Voiced("and_you", "And you...", 1.2f);
            ember.SetMood(EmberFlame.Mood.Excited);
            ember.PointAt(head, 4f);
            yield return Voiced("you_have_it", "Wait a minute. Oh. Oh, my, my... You have it.", 1f);
            ember.SetMood(EmberFlame.Mood.Calm);
            yield return Voiced("hold_them_up", "Look at your hands. Go on... hold them up.", 0.4f);

            // Everything waits for the player to raise both palms to their face; then the Seal answers with a heartbeat.
            yield return WaitForPalms(input, head);
            yield return Heartbeat(input, head);
            var powers = FindAnyObjectByType<PowerUnlocks>();
            Debug.Log("[Rift] The Jyoti Seal awakens on the player's palms");
            ember.SetMood(EmberFlame.Mood.Excited);
            yield return Voiced("jyoti_seal", "I don't believe it... the Jyoti Seal.", 0.8f);
            yield return Voiced("youre_a_weaver", "Do you have any idea what this means, kid? You aren't just a tourist. You're a Weaver.", 1f);

            // The suit-up: Ember moves aside and the soul watches the suit wrap onto the body.
            ember.SetMood(EmberFlame.Mood.Calm);
            yield return SuitUp(head);
            ember.SetMood(EmberFlame.Mood.Excited);
            Gear(); // the sword: it forms out of the light before the chest, turns, and slides into the sheath on the back
            yield return Wait(Dur(3f)); // (time to watch the sword form)
            ember.SetMood(EmberFlame.Mood.Calm);
            if (powers == null) Debug.Log("[Rift] (no PowerUnlocks in the scene: every power is allowed)");
        }

        // Ember says a line in its recorded voice (Resources/VO/Ember/`clip`), with no subtitle; N (Skip) ends it at once.
        IEnumerator Voiced(string clip, string text, float hold)
        {
            AudioClip voice = Resources.Load<AudioClip>($"VO/Ember/{clip}");
            if (voice == null || emberSpeech == null)
            {
                Debug.LogWarning($"[Ember] No voice clip '{clip}': {text}");
                yield break;
            }
            yield return emberSpeech.Speak(voice, text, hold);
        }

        // The suit-up, watched by the soul: Ember drifts out of the way, the white thins so the body is clear, and
        // a soft glow (never a hard flash) rises with the wave and settles as the eyes light.
        IEnumerator SuitUp(Transform head)
        {
            if (outfit == null) yield break;
            Color wash = ScreenFade.Current;
            suiting = true;
            StartCoroutine(ScreenFade.To(StoryFx.WithAlpha(blueBlaze, 0f), 1.2f)); // nothing tints the view while the suit forms
            var glow = new GameObject("SuitUpGlow").AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = paleBlue;
            glow.range = 3f;
            glow.shadows = LightShadows.None;
            if (fire != null) fire.Target = 1f;
            GroundLightning.Strike(this, outfit.BodyChest + Vector3.down * 0.3f, 2.6f, 1.6f); // the fire answers the suit
            Coroutine suit = StartCoroutine(outfit.SuitUp(suitUpTime));
            for (float t = 0f; t < suitUpTime; t += Time.deltaTime)
            {
                float u = t / suitUpTime;
                glow.transform.position = outfit.BodyChest + Vector3.up * 0.2f;
                glow.intensity = 2.5f * Mathf.Sin(u * Mathf.PI);
                yield return null;
            }
            yield return suit;
            Destroy(glow.gameObject);
            yield return ScreenFade.Pulse(StoryFx.WithAlpha(blueBlaze, 0.35f), 0.3f, 0.8f); // the eyes light: a soft swell
            yield return ScreenFade.To(wash, 1f);
            suiting = false;
        }

        bool suiting;

        // The next part of the outfit forms, with a little flare from Ember.
        void Gear()
        {
            if (outfit == null || !outfit.EquipNext(2f)) return;
            if (ember != null) ember.Burst(30);
        }

        bool waitingForPalms, palmsSkipped;

        // "Hold them up": waits until both palms face the player's eyes for a moment. The light's ringing hushes
        // while it waits. Without tracked hands (a desktop) it carries on after a while; N skips it.
        IEnumerator WaitForPalms(HandInputService input, Transform head)
        {
            waitingForPalms = true;
            palmsSkipped = false;
            float ringVolume = ring != null ? ring.volume : 0f;
            if (emberSpeech != null) emberSpeech.ShowText($"(Raise both palms towards your face: hold {GameInput.KeyName(GameAction.RaisePalms)})");
            Debug.Log($"[Rift] Waiting: raise both palms towards your face (hold {GameInput.KeyName(GameAction.RaisePalms)}, or N skips)");
            float held = 0f, untracked = 0f, waited = 0f;
            while (!palmsSkipped)
            {
                waited += Time.deltaTime;
                if (waited > palmsGiveUp)
                {
                    Debug.Log("[Rift] The palms never quite faced the eyes: carrying on");
                    break;
                }
                if (ring != null) ring.volume = Mathf.MoveTowards(ring.volume, 0f, Time.deltaTime * 0.1f);
                bool anyTracked = input != null && ((input.Left != null && input.Left.IsTracked) || (input.Right != null && input.Right.IsTracked));
                untracked = anyTracked || GameInput.Held(GameAction.RaisePalms) ? 0f : untracked + Time.deltaTime;
                if (untracked > palmsTimeout)
                {
                    Debug.Log("[Rift] No tracked hands: carrying on");
                    break;
                }
                held = PalmsToFace(input, head) || GameInput.Held(GameAction.RaisePalms) ? held + Time.deltaTime : 0f;
                if (held > 0.4f) break;
                yield return null;
            }
            waitingForPalms = false;
            if (emberSpeech != null) emberSpeech.ClearText();
            if (ring != null) ring.volume = ringVolume;
        }

        static bool PalmsToFace(HandInputService input, Transform head)
        {
            if (input == null || head == null) return false;
            for (int side = 0; side < 2; side++)
            {
                HandFrame f = side == 0 ? input.Left : input.Right;
                if (f == null || !f.IsTracked) return false;
                Vector3 toEyes = head.position - f.PalmCenter;
                if (toEyes.magnitude > 0.6f || Vector3.Dot(head.forward, -toEyes) < 0.08f) return false; // up in front of the face
                if (Vector3.Dot(f.PalmNormal, toEyes.normalized) < 0.5f) return false; // palm turned to the eyes
            }
            return true;
        }

        // A heavy heartbeat through the hands (controller rumble on Quest, a thud everywhere) and the Seal lighting up
        // on the palms with each beat; the golden pulses swell and ebb.
        IEnumerator Heartbeat(HandInputService input, Transform head)
        {
            var go = new GameObject("Heartbeat");
            go.transform.SetParent(head, false);
            AudioSource beat = StoryFx.CreateSource(go, HeartbeatClip(), false, 0f);
            beat.volume = 1f;
            for (int i = 0; i < 3; i++)
            {
                beat.Play();
                for (int thump = 0; thump < 2; thump++)
                {
                    CombatFx.Rumble(null, thump == 0 ? 1f : 0.6f, 0.1f);
                    SealOnPalms(input, 40 + i * 30);
                    yield return ScreenFade.To(StoryFx.WithAlpha(paleBlue, (0.18f + 0.06f * i) * (thump == 0 ? 1f : 0.7f)), 0.09f);
                    yield return ScreenFade.To(StoryFx.WithAlpha(blueBlaze, hoverWash), thump == 0 ? 0.19f : 0.58f);
                }
            }
            StartCoroutine(SealGlow(input));
            Destroy(go, 2f);
        }

        // The Seal on each palm: a ring of golden sparks, burst outward.
        void SealOnPalms(HandInputService input, int count)
        {
            if (input == null) return;
            for (int side = 0; side < 2; side++)
            {
                HandFrame f = side == 0 ? input.Left : input.Right;
                if (f == null || !f.IsTracked) continue;
                Vector3 n = f.PalmNormal, a = Vector3.Cross(n, f.HandForward).normalized, b = Vector3.Cross(n, a);
                for (int i = 0; i < count; i++)
                {
                    float angle = Random.value * Mathf.PI * 2f;
                    Vector3 dir = a * Mathf.Cos(angle) + b * Mathf.Sin(angle);
                    StoryFx.Emit(burst, f.PalmCenter + dir * 0.035f + n * 0.01f, dir * 0.25f + n * 0.1f, Random.Range(0.006f, 0.014f), 0.6f, paleBlue);
                }
            }
        }

        // Afterwards the mark keeps glowing softly on the palms while the soul is out.
        IEnumerator SealGlow(HandInputService input)
        {
            while (soulOut)
            {
                if (Random.value < 0.5f) SealOnPalms(input, 2);
                yield return null;
            }
        }

        // Ember floats beside the line between the soul and the body, a little below the soul's eyes.
        Vector3 EmberSpot(Transform head)
        {
            if (practice != null && practice.Practising)
            {
                // Out of the sword's way: off to the left, a little ahead, watching the lesson.
                Vector3 fwd = StoryFx.Flat(head.forward);
                return head.position + fwd * 0.9f - Vector3.Cross(Vector3.up, fwd) * 1.3f - Vector3.up * 0.25f;
            }
            Vector3 body = outfit != null && outfit.BodyShown ? character.position : head.position + StoryFx.Flat(head.forward) * 2f;
            Vector3 across = Vector3.Cross(Vector3.up, StoryFx.Flat(body - head.position));
            Vector3 spot = Vector3.Lerp(head.position, body, suiting ? 0.25f : 0.4f) + across * (suiting ? 0.9f : 0.55f);
            spot.y = head.position.y - (suiting ? 0.9f : 0.75f);
            return spot;
        }

        void FollowPlayer(Transform head)
        {
            Transform t = ember.transform;
            t.position = Vector3.Lerp(t.position, EmberSpot(head), 1f - Mathf.Exp(-3f * Time.deltaTime));
        }

        // Ember says a line; N (Skip) finishes it at once.
        IEnumerator Line(string text, float hold)
        {
            if (emberSpeech == null)
            {
                Debug.Log($"[Ember] {text}");
                yield break;
            }
            yield return emberSpeech.Say(text, hold);
        }

        // ---------------------------------------------------------------- effects

        // Specks of light drifting up past the player inside the column.
        void RisingMotes(Transform around, Color color)
        {
            if (around == null || Random.value > 0.7f) return;
            StoryFx.Emit(burst, around.position + new Vector3(Random.Range(-0.6f, 0.6f), -1.5f, Random.Range(-0.6f, 0.6f)),
                         Vector3.up * Random.Range(1.5f, 3f), Random.Range(0.02f, 0.06f), 1.2f, color);
        }

        // A column of light from high above down to the floor, visible from inside too.
        static Renderer Beam(Vector3 feet, Color color, float alpha)
        {
            const float height = 80f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "SkyBeam";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = feet + Vector3.up * height * 0.5f;
            go.transform.localScale = new Vector3(0f, height * 0.5f, 0f); // the cylinder mesh is 2 m tall
            Material m = AegisShield.CreateFaceMaterial(StoryFx.WithAlpha(color, alpha));
            m.name = "SkyBeam";
            m.SetFloat("_Cull", 0f);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        static void SetBeamColor(Renderer beam, Color color) => beam.sharedMaterial.SetColor("_BaseColor", color);

        /// <summary>A heavy heartbeat: two low thumps, lub-dub (1 s).</summary>
        static AudioClip HeartbeatClip()
        {
            const int rate = 44100;
            var data = new float[rate];
            foreach (float start in new[] { 0f, 0.28f })
            {
                float level = start == 0f ? 1f : 0.7f;
                for (int i = (int)(start * rate); i < data.Length; i++)
                {
                    float t = (float)i / rate - start;
                    if (t > 0.35f) break;
                    data[i] += Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(65f, 40f, t * 3f) * t) * Mathf.Exp(-t * 14f) * level * Mathf.Min(1f, t * 400f);
                }
            }
            AudioClip clip = AudioClip.Create("Heartbeat (placeholder)", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A deep boom: a falling sub-bass thump with a crack of noise on top (3 s).</summary>
        public static AudioClip Boom()
        {
            const int rate = 44100;
            var rng = new System.Random(11);
            var data = new float[rate * 3];
            float phase = 0f, low = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / rate;
                phase += 2f * Mathf.PI * Mathf.Lerp(70f, 28f, Mathf.Min(1f, t * 1.5f)) / rate;
                low += (((float)rng.NextDouble() * 2f - 1f) - low) * 0.08f;
                float crack = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 25f);
                data[i] = Mathf.Clamp((Mathf.Sin(phase) * 0.8f + low * 0.9f) * Mathf.Exp(-t * 1.6f) + crack * 0.5f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("Boom (placeholder)", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A temple-bell chime: bright inharmonic partials ringing out (5 s).</summary>
        static AudioClip Chime()
        {
            const int rate = 44100;
            var data = new float[rate * 5];
            float[] partials = { 1f, 2.0f, 2.76f, 4.07f, 5.4f };
            float[] levels = { 0.5f, 0.3f, 0.25f, 0.12f, 0.08f };
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / rate;
                float s = 0f;
                for (int k = 0; k < partials.Length; k++)
                    s += Mathf.Sin(2f * Mathf.PI * 523f * partials[k] * t) * levels[k] * Mathf.Exp(-t * (0.7f + k * 0.6f));
                data[i] = s * 0.6f * Mathf.Min(1f, t * 200f);
            }
            AudioClip clip = AudioClip.Create("Chime (placeholder)", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>The ringing in the ears in the light: two close high tones beating (2 s loop).</summary>
        public static AudioClip Ringing()
        {
            const int rate = 44100;
            var data = new float[rate * 2];
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / rate;
                data[i] = (Mathf.Sin(2f * Mathf.PI * 3200f * t) + Mathf.Sin(2f * Mathf.PI * 3202.5f * t)) * 0.15f;
            }
            AudioClip clip = AudioClip.Create("Ringing (placeholder)", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
