using System.Collections;
using ArcaneStrike.Combat;
using ArcaneStrike.HandInput;
using ArcaneStrike.Props;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Chapter 2, scene 1: waking in the attic. Back from the Kalakh's world, the player comes to on the attic floor
    /// where the rift was:
    /// - black, with a heavy heartbeat and panting breath, and the room's sounds muffled as if the ears are ringing;
    /// - the eyes blink open twice, and the player stumbles back against a wooden crate (a thud, dust);
    /// - the heartbeat slows and fades and the attic's own sounds come back (wind on the roof, creaking timber, dust settling);
    /// - they whisper "A dream... Just a damn hallucination. Get a grip.";
    /// - they walk back through the gate to the stairs, and are carried down them as the view fades to black;
    /// - the caption "23:42 PM — Midnight Hours", then the bedroom (<see cref="nextScene"/>).
    /// Lives on the attic model (attic space: floor y 0, the chest ahead at +Z). Keys: N skips the moment, E heads
    /// downstairs at once.
    /// </summary>
    public class AtticWakeDirector : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] string nextScene = "Chapter2_Bedroom";

        [Header("Places (attic space)")]
        [Tooltip("Where the player comes to (their feet), and the way they face: where the rift was, by the chest.")]
        [SerializeField] Vector3 wakeAt = new Vector3(-0.5f, 0f, 0.35f);
        [SerializeField] float wakeFacing = 35f;
        [Tooltip("How far they stumble backwards, into the crate.")]
        [SerializeField] float stumble = 0.45f;
        [Tooltip("The top of the stairs: reaching it takes the player down.")]
        [SerializeField] Vector3 stairTop = new Vector3(0.7f, 0f, -2.8f);
        [SerializeField] Vector3 stairFoot = new Vector3(0.7f, -3.2f, -6.1f);
        [SerializeField] float reachStairs = 0.8f;
        [Tooltip("Eye height used without a headset.")]
        [SerializeField] float desktopEyeHeight = 1.6f;

        [Header("Feel")]
        [SerializeField] Vector2 heartRate = new Vector2(118f, 66f);
        [Tooltip("Seconds for the heart to slow down and the ears to clear.")]
        [SerializeField] float calmDown = 16f;
        [SerializeField] Color pulseColor = new Color(0.45f, 0.02f, 0.02f, 1f);
        [SerializeField] string caption = "23:42 PM — Midnight Hours";

        Transform head, rig;
        PlayerLocomotion locomotion;
        AtticAtmosphere atmosphere;
        InnerVoice voice;
        AudioSource heart, breath;
        AudioLowPassFilter muffle;
        float startTime, lastBeat = -10f, panic = 1f;
        bool skip, leaving;

        bool HeadsetActive => head != null && rig != null && head.position.y - rig.position.y > 0.3f;
        Vector3 World(Vector3 local) => transform.TransformPoint(local);

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            locomotion = FindAnyObjectByType<PlayerLocomotion>();
            atmosphere = GetComponent<AtticAtmosphere>();
        }

        IEnumerator Start()
        {
            ScreenFade.Set(Color.black);
            yield return null; // let the rig and the hands start
            head = StoryFx.Head(input);
            if (head == null)
            {
                Debug.LogError("[Chapter2] No camera / head found.");
                yield break;
            }
            rig = head.root;
            if (locomotion != null) locomotion.Paused = true;
            voice = InnerVoice.Create(input);
            startTime = Time.time;
            OpenGate();

            Vector3 forward = Quaternion.AngleAxis(wakeFacing, Vector3.up) * transform.forward;
            if (!HeadsetActive) head.localRotation = Quaternion.identity;
            PlaceHead(World(wakeAt));
            Face(forward);
            Debug.Log("[Chapter2] Scene 1: waking in the attic. N skips a moment, E heads downstairs.");

            BeginBodySounds();
            SettleDust();
            StartCoroutine(Pulse());

            // Black: only the heart and the breath.
            yield return Wait(2.2f);

            // The eyes blink open twice, then stay open.
            yield return Blink(0.55f, 0.35f);
            yield return Wait(0.15f);
            yield return Blink(0.3f, 0.3f);
            yield return Wait(0.1f);
            StartCoroutine(ScreenFade.To(new Color(0f, 0f, 0f, 0f), 0.9f));

            // A step backwards, into the crate.
            yield return Wait(0.35f);
            yield return Stumble(-forward * stumble, 0.55f);
            yield return Wait(1.6f);

            // A whisper, between breaths.
            yield return voice.Say("A dream... Just a damn hallucination. Get a grip.", "c2_dream", 2.2f, true);
            yield return Wait(0.6f);

            // Free to move: back through the gate and down the stairs.
            if (locomotion != null) locomotion.Paused = false;
            voice.Hint("Go back downstairs");
            Debug.Log("[Chapter2] Walk back through the gate to the stairs (WASD / thumbstick). E goes down at once.");
            while (!AtStairs() && !GameInput.Pressed(GameAction.Interact) && !GameInput.Pressed(GameAction.Skip)) yield return null;
            yield return Descend();
        }

        void Update()
        {
            if (GameInput.Pressed(GameAction.Skip) && !leaving)
            {
                skip = true;
                if (voice != null) voice.SkipLine();
            }
            if (muffle != null)
            {
                // The ears clear: the room's own sounds come back from under the heartbeat.
                float u = Mathf.Clamp01((Time.time - startTime) / (calmDown * 0.6f));
                muffle.cutoffFrequency = Mathf.Lerp(600f, 22000f, u * u * u);
                if (u >= 1f)
                {
                    Destroy(muffle);
                    muffle = null;
                }
            }
        }

        // A short wait that N cuts short.
        IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds && !skip; t += Time.deltaTime) yield return null;
            skip = false;
        }

        // Half-open eyes: up from black to `open` (1 = fully clear) and shut again.
        IEnumerator Blink(float open, float seconds)
        {
            yield return ScreenFade.To(new Color(0f, 0f, 0f, 1f - open), seconds * 0.55f);
            yield return ScreenFade.To(Color.black, seconds * 0.45f);
        }

        // ---------------------------------------------------------------- the body

        void BeginBodySounds()
        {
            heart = gameObject.AddComponent<AudioSource>();
            heart.playOnAwake = false;
            heart.spatialBlend = 0f;
            breath = gameObject.AddComponent<AudioSource>();
            breath.playOnAwake = false;
            breath.spatialBlend = 0f;
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener != null)
            {
                muffle = listener.gameObject.AddComponent<AudioLowPassFilter>();
                muffle.cutoffFrequency = 600f;
            }
            breath.PlayOneShot(Chapter2Sounds.EarRing(), 0.5f);
            StartCoroutine(Heartbeat());
            StartCoroutine(Panting());
        }

        // The heart races, then slows and fades into the room's sounds.
        IEnumerator Heartbeat()
        {
            AudioClip beat = Chapter2Sounds.Heartbeat();
            while (true)
            {
                float u = Mathf.Clamp01((Time.time - startTime) / calmDown);
                panic = 1f - Mathf.SmoothStep(0f, 1f, u);
                float volume = Mathf.Lerp(0.05f, 1f, panic);
                if (u >= 1f) yield break;
                heart.PlayOneShot(beat, volume);
                lastBeat = Time.time;
                if (panic > 0.4f) CombatFx.Rumble(null, 0.25f * panic, 0.08f);
                yield return new WaitForSeconds(60f / Mathf.Lerp(heartRate.y, heartRate.x, panic));
            }
        }

        IEnumerator Panting()
        {
            var clips = new[] { Chapter2Sounds.Breath(1), Chapter2Sounds.Breath(2), Chapter2Sounds.Breath(3) };
            while (panic > 0.08f)
            {
                breath.pitch = Mathf.Lerp(0.95f, 1.3f, panic);
                breath.PlayOneShot(clips[Random.Range(0, clips.Length)], Mathf.Lerp(0.1f, 0.55f, panic));
                yield return new WaitForSeconds(Mathf.Lerp(2.4f, 0.75f, panic) * Random.Range(0.9f, 1.1f));
            }
        }

        // A dark-red throb round the edge of the view with every beat, fading as the heart calms.
        IEnumerator Pulse()
        {
            while (panic > 0.02f)
            {
                float throb = Mathf.Exp(-(Time.time - lastBeat) * 5f);
                ComfortVignette.Tint(pulseColor, panic * (0.25f + 0.35f * throb));
                yield return null;
            }
            ComfortVignette.Tint(pulseColor, 0f);
        }

        // Backwards, unsteady, into the crate: it ends in a knock, a creak and a little dust.
        IEnumerator Stumble(Vector3 move, float seconds)
        {
            Vector3 start = rig.position;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float u = 1f - Mathf.Pow(1f - t / seconds, 3f);
                rig.position = start + move * u;
                if (HeadsetActive) ComfortVignette.Moving(0.6f);
                yield return null;
            }
            rig.position = start + move;
            Vector3 crate = head.position - Vector3.up * 0.9f + move.normalized * 0.35f;
            AudioSource.PlayClipAtPoint(Chapter2Sounds.CrateThud(), crate, 1f);
            if (atmosphere != null) atmosphere.PlayCreak(crate, 0.7f, 0.8f);
            ImpactFx.Dust(crate + Vector3.up * 0.3f, 14);
            CombatFx.Rumble(null, 0.6f, 0.15f);
            if (!HeadsetActive) StartCoroutine(Jolt());
        }

        // On a screen only (it would be uncomfortable in a headset): the view jolts as the back hits the crate.
        IEnumerator Jolt()
        {
            float applied = 0f;
            for (float t = 0f; t < 0.7f; t += Time.deltaTime)
            {
                float a = -7f * Mathf.Sin(Mathf.PI * t / 0.7f) * Mathf.Exp(-t * 3f);
                head.localRotation = Quaternion.Euler(a - applied, 0f, 0f) * head.localRotation;
                applied = a;
                yield return null;
            }
            head.localRotation = Quaternion.Euler(-applied, 0f, 0f) * head.localRotation;
        }

        // The dust the rift shook loose drifts down from the roof.
        void SettleDust()
        {
            var ps = StoryFx.CreateParticles(transform, "SettlingDust", false, 0.012f, 400);
            for (int i = 0; i < 220; i++)
            {
                Vector3 p = World(new Vector3(Random.Range(-2.2f, 2.2f), Random.Range(1.2f, 2.9f), Random.Range(-1.5f, 3.8f)));
                StoryFx.Emit(ps, p, Random.insideUnitSphere * 0.03f, Random.Range(0.006f, 0.02f), Random.Range(7f, 14f),
                             new Color(0.75f, 0.74f, 0.72f, Random.Range(0.25f, 0.55f)));
            }
        }

        // ---------------------------------------------------------------- downstairs

        bool AtStairs()
        {
            Vector3 local = transform.InverseTransformPoint(head.position);
            return new Vector2(local.x - stairTop.x, local.z - stairTop.z).magnitude < reachStairs;
        }

        IEnumerator Descend()
        {
            leaving = true;
            voice.Hint("");
            if (locomotion != null) locomotion.Paused = true;
            Debug.Log("[Chapter2] Down the stairs.");
            if (!AtStairs())
            {
                // (E from across the room: straight to the top of the stairs, behind a quick fade.)
                yield return ScreenFade.To(Color.black, 0.4f);
                PlaceHead(World(stairTop));
                Face(transform.TransformDirection(stairFoot - stairTop));
                yield return ScreenFade.To(new Color(0f, 0f, 0f, 0f), 0.4f);
            }
            Vector3 from = new Vector3(head.position.x, World(stairTop).y, head.position.z), to = World(stairFoot);
            Face(to - from);
            StartCoroutine(FadeOutPartWay());
            float length = Vector3.Distance(from, to), stepped = 0f;
            for (float d = 0f; d < length; d += 0.8f * Time.deltaTime)
            {
                Vector3 feet = Vector3.Lerp(from, to, d / length);
                PlaceHead(feet);
                if (HeadsetActive) ComfortVignette.Moving(0.4f);
                stepped += 0.8f * Time.deltaTime;
                if (stepped > 0.45f)
                {
                    stepped = 0f;
                    ImpactFx.Play(Grabbable.Sound.Wood, feet, 0.15f, 3f);
                    if (atmosphere != null && Random.value < 0.4f) atmosphere.PlayCreak(feet, 0.4f, Random.Range(0.8f, 1.1f));
                }
                if (ScreenFade.Current.a >= 0.999f && d > length * 0.6f) break;
                yield return null;
            }
            yield return ScreenFade.To(Color.black, 0.3f);

            // FADE TO BLACK. The time, on the dark.
            yield return new WaitForSeconds(0.8f);
            yield return voice.Caption(caption, 1.2f, 2.8f, 1f);
            yield return new WaitForSeconds(0.4f);
            if (!string.IsNullOrEmpty(nextScene) && Application.CanStreamedLevelBeLoaded(nextScene))
                ScreenFade.LoadScene(nextScene, Color.black, 0.1f, 1.5f);
            else
                Debug.Log($"[Chapter2] Scene 1 done. The next scene ('{nextScene}') isn't built or in the Build Profile yet.");
        }

        IEnumerator FadeOutPartWay()
        {
            yield return new WaitForSeconds(1.2f);
            yield return ScreenFade.To(Color.black, 2.2f);
        }

        // ---------------------------------------------------------------- helpers

        void PlaceHead(Vector3 feet)
        {
            Vector3 p = rig.position;
            p.x += feet.x - head.position.x;
            p.z += feet.z - head.position.z;
            p.y = feet.y + (HeadsetActive ? 0f : desktopEyeHeight);
            rig.position = p;
        }

        // Turns the rig round the eyes to face `direction` (instantly: only ever done while the view is dark or still).
        void Face(Vector3 direction)
        {
            Vector3 now = StoryFx.Flat(head.forward), want = StoryFx.Flat(direction);
            rig.RotateAround(head.position, Vector3.up, Vector3.SignedAngle(now, want, Vector3.up));
        }

        // The gate between the store room and the loft stands open, as the player left it.
        void OpenGate()
        {
            foreach (string name in new[] { "Gate_Left", "Gate_Right" })
            {
                Transform door = FindDeep(transform, name);
                if (door == null) continue;
                Renderer r = door.GetComponentInChildren<Renderer>();
                Vector3 leaf = (r != null ? r.bounds.center : door.position + door.right) - door.position;
                Vector3 inside = World(new Vector3(0f, 1f, 1.5f)) - door.position;
                Vector3 plus = Quaternion.AngleAxis(90f, Vector3.up) * leaf, minus = Quaternion.AngleAxis(-90f, Vector3.up) * leaf;
                float sign = Vector3.Dot(plus, inside) > Vector3.Dot(minus, inside) ? 1f : -1f;
                Vector3 up = door.parent != null ? door.parent.InverseTransformDirection(Vector3.up) : Vector3.up;
                door.localRotation = Quaternion.AngleAxis(sign * 95f, up) * door.localRotation;
            }
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform hit = FindDeep(child, name);
                if (hit != null) return hit;
            }
            return null;
        }

        void OnDestroy()
        {
            if (muffle != null) Destroy(muffle);
            ComfortVignette.Tint(pulseColor, 0f);
        }
    }
}
