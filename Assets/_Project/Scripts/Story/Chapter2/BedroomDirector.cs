using System.Collections;
using ArcaneStrike.Combat;
using ArcaneStrike.HandInput;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Chapter 2, scene 2: the bedroom at midnight (the user's Chapter 2 script, played first-person):
    /// - pitch black but for pale moonlight through the blinds; the player sits on the edge of the bed; distant thunder;
    /// - unnatural shadows ripple across the floorboards towards the corner by the wardrobe, where orange embers gather
    ///   into a flame figure; a voice, inside the head: "Do not look away, child.";
    /// - the player jumps up ("Who's there?!"); a searing hiss, and the Jyoti Seal burns itself into the back of the left
    ///   wrist, line by line, molten gold and blood red;
    /// - Ember shows itself and the dialogue runs, with one choice ([A] "those monsters were real?" / [B] "leave me
    ///   alone!"; B gets one extra answer from Ember, then the scene goes on as written); the room's shadows warp
    ///   while Ember tells of the Agni-vansh;
    /// - the prompt: the edges of the view glow with heat and a ring marks the Seal: touch it with two fingers of the
    ///   right hand (or E). A boom, a shockwave of gold fire from the wrist, the walls crack and shatter like glass
    ///   into an abyss of swirling violet and ash;
    /// - QUEST ACCEPTED: FIRST IGNITION, then <see cref="nextScene"/> when it exists.
    /// Lines play recordings from Resources/VO/Chapter2/&lt;key&gt; when there are any; otherwise they are subtitles
    /// (and Ember's crackling voice). Lives on the bedroom model (its space: floor y 0, the player facing +Z).
    /// Keys: N skips a line, 1 / 2 choose, E touches the Seal.
    /// </summary>
    public class BedroomDirector : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] string nextScene = "Chapter2_Breach";

        [Header("Places (room space)")]
        [SerializeField] Vector3 seat = new Vector3(-1.1f, 0f, 1.45f);
        [SerializeField] Vector3 corner = new Vector3(1.7f, 0f, 3.1f);
        [Tooltip("Eye heights without a headset: sitting on the bed, then standing.")]
        [SerializeField] float seatedEyes = 1.15f, standingEyes = 1.6f;

        [Header("Look")]
        [SerializeField] Color emberText = new Color(1f, 0.72f, 0.38f, 1f);
        [SerializeField] Color heat = new Color(1f, 0.35f, 0.05f, 1f);
        [SerializeField] Color fray = new Color(0.35f, 0.05f, 0.55f, 1f);
        [SerializeField] float desktopFieldOfView = 60f;

        Transform head, rig;
        PlayerLocomotion locomotion;
        InnerVoice voice;
        JyotiSealBrand seal;
        EmberFlame ember;
        StorySpeech emberSpeech;
        RoomShatter room;
        Material shadowFloor;
        Light moon;
        float eyes, moonBase, shadowStrength, shadowReach = 0.4f;
        bool skip;

        bool HeadsetActive => head != null && rig != null && head.position.y - rig.position.y > 0.3f;
        Vector3 World(Vector3 local) => transform.TransformPoint(local);

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            locomotion = FindAnyObjectByType<PlayerLocomotion>();
            room = GetComponent<RoomShatter>();
            if (room == null) room = gameObject.AddComponent<RoomShatter>();
        }

        IEnumerator Start()
        {
            ScreenFade.Set(Color.black);
            yield return null;
            head = StoryFx.Head(input);
            if (head == null)
            {
                Debug.LogError("[Chapter2] No camera / head found.");
                yield break;
            }
            rig = head.root;
            if (locomotion != null) locomotion.Paused = true;
            Night();
            voice = InnerVoice.Create(input);
            seal = JyotiSealBrand.Create(input);
            RegisterRoom();
            BuildShadowFloor();

            if (!HeadsetActive) head.localRotation = Quaternion.identity;
            eyes = seatedEyes;
            PlaceHead(World(seat));
            Face(transform.forward);
            StartCoroutine(Thunder());
            Debug.Log("[Chapter2] Scene 2: the bedroom. N skips a line, 1 / 2 choose, E touches the Seal.");

            // Pitch black, then the moonlight through the blinds.
            yield return Wait(1.2f);
            yield return ScreenFade.To(new Color(0f, 0f, 0f, 0f), 3f);
            yield return Wait(2.5f);

            // Shadows ripple across the floor to the corner; embers gather there into a figure.
            StartCoroutine(Shadows(1f, 3.2f, 5f));
            yield return Wait(2f);
            yield return GatherEmber(4.5f);

            // A voice inside the head.
            emberSpeech.SetSpatial(0f);
            yield return EmberSays("c2_e01", "Do not look away, child.", 1.2f);

            // Up off the bed.
            StartCoroutine(StandUp(0.45f));
            yield return voice.Say("Who's there?! Show yourself!", "c2_me01", 1.0f);

            // The Seal sears into the left wrist.
            yield return Brand();
            emberSpeech.SetSpatial(1f);
            yield return EmberSays("c2_e02", "Stand down. There is no enemy before you. It is me—Ember.", 1.4f);
            StartCoroutine(EmberComesCloser(2.5f));
            if (locomotion != null) locomotion.Paused = false;

            yield return voice.Say("Ember?! Where are you? I can't see anything—and what did you just do to my arm?!", "c2_me03", 1.2f);
            yield return EmberSays("c2_e03", "Calm your pulse. The flame upon your flesh is the Jyoti Seal. It does not consume; it awakens.", 1.6f);
            yield return voice.Say("Awakens what? What are you talking about?", "c2_me04", 1.0f);
            yield return EmberSays("c2_e04", "The storm that is coming. The horrors you witnessed in the dark were not a trick of the mind. The veil between realms has cracked.", 1.6f);

            // The choice.
            var choice = DialogueChoice.Create(input);
            yield return choice.Ask("“You mean... those monsters were real?”", "“I'm not cut out for this. Leave me alone!”");
            if (choice.Chosen == 1)
            {
                yield return voice.Say("I'm not cut out for this. Leave me alone!", "c2_me05b", 1.0f);
                yield return EmberSays("c2_e05b", "Leave you? The dark will not leave you, child. It has already caught your scent.", 1.6f);
            }
            yield return voice.Say("Wait... all those things I saw? The claws? The blood? That was real?!", "c2_me05", 1.2f);
            yield return EmberSays("c2_e05", "Every terror of it. Entities from the lower rifts are tearing into this mortal plane through dimensional fissures. Normal eyes cannot see them—but yours can.", 1.6f);

            // Reality frays: the shadows climb, the edges of the view bruise violet, the moonlight stutters.
            StartCoroutine(Shadows(1.4f, 4.5f, 3f));
            StartCoroutine(Fray(true));
            yield return EmberSays("c2_e06", "Throughout history, a chosen bloodline has stood against the breach to keep your world from burning. The Agni-vansh—the lineage of sacred fire. And now, the mantle has chosen you.", 1.8f);
            yield return voice.Say("Agni-vansh? Portals? This is insane. This is way too much to drop on someone in the middle of the night!", "c2_me06", 1.2f);
            if (ember != null) ember.SetMood(EmberFlame.Mood.Excited);
            yield return EmberSays("c2_e07", "Time is not a luxury granted to the chosen. A rift has opened nearby, and the beasts are already scenting your spark. If you do not cross over and sever the rupture from inside, it will tear this city apart.", 1.6f);
            if (ember != null) ember.SetMood(EmberFlame.Mood.Calm);
            yield return voice.Say("Cross over?! How am I supposed to get inside a rift?!", "c2_me07", 1.0f);
            yield return EmberSays("c2_e08", "Channel your intent. Touch the Jyoti Seal upon your wrist, and break the boundary.", 1.2f);
            StartCoroutine(Fray(false));

            yield return IgnitePrompt();
            yield return Breach();
        }

        void Update()
        {
            if (GameInput.Pressed(GameAction.Skip))
            {
                skip = true;
                if (emberSpeech != null) emberSpeech.SkipLine();
                if (voice != null) voice.SkipLine();
            }
            if (shadowFloor != null)
            {
                shadowFloor.SetFloat("_Strength", shadowStrength);
                shadowFloor.SetFloat("_Reach", shadowReach);
            }
        }

        IEnumerator Wait(float seconds)
        {
            skip = false;
            for (float t = 0f; t < seconds && !skip; t += Time.deltaTime) yield return null;
            skip = false;
        }

        IEnumerator EmberSays(string key, string line, float hold)
        {
            AudioClip clip = Resources.Load<AudioClip>("VO/Chapter2/" + key);
            yield return emberSpeech.Say(line, clip, hold);
        }

        // ---------------------------------------------------------------- the night

        void Night()
        {
            Core.RoomSetup.VirtualWorld = true;
            foreach (var b in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (b != null && b.GetType().Name == "OVRPassthroughLayer") b.enabled = false;
            Camera cam = Camera.main;
            if (cam != null && RenderSettings.skybox != null)
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.backgroundColor = Color.black;
            }
            if (cam != null && !UnityEngine.XR.XRSettings.isDeviceActive) cam.fieldOfView = desktopFieldOfView;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.07f, 0.08f, 0.14f);
            RenderSettings.ambientEquatorColor = new Color(0.035f, 0.04f, 0.07f);
            RenderSettings.ambientGroundColor = new Color(0.01f, 0.01f, 0.015f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.02f, 0.025f, 0.04f);
            RenderSettings.fogDensity = 0.02f;
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) moon = l;
            if (moon != null) moonBase = moon.intensity;
        }

        IEnumerator Thunder()
        {
            AudioClip rumble = StoryFx.Rumble();
            var go = new GameObject("DistantThunder");
            go.transform.position = World(new Vector3(14f, 4f, 1f)); // far outside the window
            var source = StoryFx.CreateSource(go, rumble, false, 0.6f);
            source.maxDistance = 60f;
            source.minDistance = 10f;
            yield return new WaitForSeconds(2f);
            while (!room.Shattered)
            {
                source.pitch = Random.Range(0.6f, 0.9f);
                source.PlayOneShot(rumble, Random.Range(0.5f, 0.9f));
                yield return new WaitForSeconds(Random.Range(7f, 13f));
            }
        }

        // ---------------------------------------------------------------- the shadows and the embers

        void BuildShadowFloor()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "CreepingShadow";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = new Vector3(0f, 0.012f, 1.1f);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(4.4f, 5f, 1f);
            shadowFloor = new Material(Shader.Find("ArcaneStrike/CreepingShadow")) { name = "CreepingShadow" };
            shadowFloor.SetTexture("_NoiseTex", Resources.Load<Texture2D>("FX/T_FireNoise"));
            Vector3 c = World(corner);
            shadowFloor.SetVector("_Target", new Vector4(c.x, c.z, 0f, 0f));
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = shadowFloor;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        IEnumerator Shadows(float strength, float reach, float seconds)
        {
            float s0 = shadowStrength, r0 = shadowReach;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / seconds);
                shadowStrength = Mathf.Lerp(s0, strength, u);
                shadowReach = Mathf.Lerp(r0, reach, u);
                yield return null;
            }
        }

        // Faint embers flicker in the dark corner and swirl together into Ember's figure.
        IEnumerator GatherEmber(float seconds)
        {
            Vector3 c = World(corner);
            var go = new GameObject("Ember");
            go.transform.position = c;
            emberSpeech = go.AddComponent<StorySpeech>();
            emberSpeech.Configure("Ember", emberText, 0.8f, Vector3.up * 1.75f);
            var sparks = StoryFx.CreateParticles(go.transform, "GatheringEmbers", true, -0.05f, 800);
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float u = t / seconds;
                int count = Mathf.RoundToInt(Mathf.Lerp(1f, 6f, u));
                for (int i = 0; i < count; i++)
                {
                    if (Random.value > 0.5f) continue;
                    float a = Random.value * Mathf.PI * 2f, r = Mathf.Lerp(1.2f, 0.4f, u) * Random.Range(0.6f, 1f), y = Random.Range(0.1f, 1.6f);
                    Vector3 p = c + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                    Vector3 inward = (new Vector3(c.x, p.y + 0.2f, c.z) - p) * 0.9f;
                    Vector3 swirl = Vector3.Cross(Vector3.up, inward) * 0.8f;
                    StoryFx.Emit(sparks, p, inward + swirl, Random.Range(0.012f, 0.03f), Random.Range(0.8f, 1.4f),
                                 new Color(1f, Random.Range(0.3f, 0.55f), 0.08f, Random.Range(0.5f, 1f)));
                }
                yield return null;
            }
            ember = go.AddComponent<EmberFlame>();
            ember.Height = 1.3f;
            ember.SetMood(EmberFlame.Mood.Calm);
            ember.LookAt(head);
            Debug.Log("[Chapter2] Ember takes shape in the corner.");
        }

        IEnumerator EmberComesCloser(float seconds)
        {
            if (ember == null) yield break;
            Vector3 from = ember.transform.position;
            Vector3 me = new Vector3(head.position.x, from.y, head.position.z);
            Vector3 to = me + (from - me).normalized * 1.7f;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                ember.transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
                yield return null;
            }
        }

        IEnumerator Fray(bool on)
        {
            float from = on ? 0f : 0.4f, to = on ? 0.4f : 0f;
            for (float t = 0f; t < 2f; t += Time.deltaTime)
            {
                float a = Mathf.Lerp(from, to, t / 2f);
                ComfortVignette.Tint(fray, a * (0.85f + 0.15f * Mathf.Sin(Time.time * 3f)));
                if (moon != null) moon.intensity = moonBase * (on ? (Random.value < 0.06f ? 0.35f : 1f) : 1f);
                yield return null;
            }
            if (on)
                while (on && emberSpeech != null && emberSpeech.IsSpeaking)
                {
                    ComfortVignette.Tint(fray, 0.4f * (0.85f + 0.15f * Mathf.Sin(Time.time * 3f)));
                    if (moon != null) moon.intensity = moonBase * (Random.value < 0.04f ? 0.35f : 1f);
                    yield return null;
                }
            if (moon != null) moon.intensity = moonBase;
        }

        // ---------------------------------------------------------------- the player

        IEnumerator StandUp(float seconds)
        {
            CombatFx.Rumble(null, 0.4f, 0.1f);
            float from = eyes;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                eyes = Mathf.Lerp(from, standingEyes, 1f - Mathf.Pow(1f - t / seconds, 2f));
                if (!HeadsetActive) PlaceHead(new Vector3(head.position.x, World(seat).y, head.position.z));
                yield return null;
            }
            eyes = standingEyes;
            if (!HeadsetActive) PlaceHead(new Vector3(head.position.x, World(seat).y, head.position.z));
        }

        // A crimson spark on the left wrist, a hiss, and the Seal burns in, line by line.
        IEnumerator Brand()
        {
            var hiss = new GameObject("Hiss").AddComponent<AudioSource>();
            hiss.spatialBlend = 0f;
            hiss.PlayOneShot(Chapter2Sounds.Hiss(0.6f), 1f);
            var sparks = StoryFx.CreateParticles(seal.transform, "BrandSparks", true, 0.4f, 400);
            StoryFx.Burst(sparks, seal.Position, 40, 0.8f, 0.012f, 0.5f, new Color(1f, 0.1f, 0.05f, 1f));
            CombatFx.Rumble(HandSide.Left, 0.9f, 0.3f);
            ScreenFade.Set(new Color(0.6f, 0f, 0f, 0.25f));
            StartCoroutine(ScreenFade.To(new Color(0.6f, 0f, 0f, 0f), 0.4f));
            StartCoroutine(voice.Say("Agh! What the—!", "c2_me02", 0.8f));
            if (!LookingAt(seal.Position)) voice.Hint("Look at your left wrist");

            hiss.clip = Chapter2Sounds.Hiss(4.5f);
            hiss.Play();
            const float burn = 4.5f;
            for (float t = 0f; t < burn; t += Time.deltaTime)
            {
                seal.Reveal = t / burn;
                seal.Pulse = 1.2f + 0.3f * Mathf.Sin(t * 20f);
                if (Random.value < 0.5f)
                    StoryFx.Emit(sparks, seal.Position + Random.insideUnitSphere * 0.02f, seal.Normal * 0.15f + Random.insideUnitSphere * 0.2f,
                                 Random.Range(0.004f, 0.01f), Random.Range(0.3f, 0.7f), Random.value < 0.5f ? new Color(1f, 0.7f, 0.2f, 1f) : new Color(0.9f, 0.1f, 0.03f, 1f));
                if (Random.value < 0.08f) CombatFx.Rumble(HandSide.Left, 0.3f, 0.05f);
                if (LookingAt(seal.Position)) voice.Hint("");
                yield return null;
            }
            seal.Reveal = 1f;
            voice.Hint("");
            StartCoroutine(SealThrob());
            Destroy(hiss.gameObject, 5f);
        }

        IEnumerator SealThrob()
        {
            while (seal != null && !room.Shattered)
            {
                seal.Pulse = 1f + 0.25f * Mathf.Sin(Time.time * 2.4f);
                yield return null;
            }
        }

        bool LookingAt(Vector3 p) => Vector3.Angle(head.forward, p - head.position) < 30f;

        // ---------------------------------------------------------------- the breach

        IEnumerator IgnitePrompt()
        {
            seal.Target(1f);
            Debug.Log("[Chapter2] ▶ Touch the Jyoti Seal on your left wrist with two fingers of your right hand (E).");
            voice.Hint("[ INTERACT ]   Touch the Seal with two fingers  (E)\n« ENTER THE BREACH »");
            float t = 0f;
            while (!Touching() && !GameInput.Pressed(GameAction.Interact))
            {
                t += Time.deltaTime;
                ComfortVignette.Tint(heat, 0.3f + 0.12f * Mathf.Sin(t * 4f));
                seal.Pulse = 1.2f + 0.5f * Mathf.Sin(t * 6f);
                yield return null;
            }
            voice.Hint("");
            seal.Target(0f);
        }

        // The index and middle fingertips of the right hand on the Seal.
        bool Touching()
        {
            HandFrame right = input != null ? input.Get(HandSide.Right) : null;
            if (right == null || !right.IsTracked) return false;
            Vector3 s = seal.Position;
            return Vector3.Distance(right.Joints[8], s) < 0.06f && Vector3.Distance(right.Joints[12], s) < 0.07f;
        }

        IEnumerator Breach()
        {
            Vector3 wrist = seal.Position;
            Vector3 feet = new Vector3(head.position.x, World(Vector3.zero).y, head.position.z);
            Debug.Log("[Chapter2] The Seal ignites: the boundary breaks.");

            // The boom, the ringing, and a shockwave of gold fire out of the wrist.
            var boom = new GameObject("Boom").AddComponent<AudioSource>();
            boom.spatialBlend = 0f;
            boom.PlayOneShot(Ascension.Boom(), 1f);
            boom.PlayOneShot(Chapter2Sounds.ArcaneRing(), 0.8f);
            CombatFx.Rumble(null, 1f, 0.6f);
            seal.Pulse = 3f;
            ComfortVignette.Tint(heat, 0f);
            StartCoroutine(ScreenFade.Pulse(new Color(1f, 0.75f, 0.3f, 0.85f), 0.08f, 0.9f));
            StartCoroutine(Shockwave(wrist));
            yield return room.Crack(wrist, 0.7f);
            yield return new WaitForSeconds(0.25f);

            // The room breaks like glass.
            boom.PlayOneShot(Ascension.Boom(), 0.7f);
            room.Shatter(wrist, feet, StoryFx.Flat(head.forward));
            shadowStrength = 0f;
            if (moon != null) StartCoroutine(Dim(moon, 2f));
            RenderSettings.fog = false;
            RenderSettings.ambientSkyColor = new Color(0.25f, 0.1f, 0.35f);
            RenderSettings.ambientEquatorColor = new Color(0.12f, 0.04f, 0.18f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.01f, 0.07f);
            if (ember != null) ember.SetMood(EmberFlame.Mood.Excited);
            if (locomotion != null) locomotion.Paused = true;
            yield return new WaitForSeconds(4f);

            yield return voice.Caption("QUEST ACCEPTED: FIRST IGNITION\n\nObjective: Traverse the Spatial Rupture\nand eliminate the Void Incursion.", 0.8f, 4.5f, 0.8f);
            if (!string.IsNullOrEmpty(nextScene) && Application.CanStreamedLevelBeLoaded(nextScene))
                ScreenFade.LoadScene(nextScene, Color.black, 1.5f, 1.5f);
            else
                Debug.Log($"[Chapter2] Scene 2 done. The next scene ('{nextScene}') isn't built yet.");
        }

        IEnumerator Shockwave(Vector3 at)
        {
            var ring = StoryFx.CreateParticles(null, "GoldShockwave", true, 0f, 1200);
            for (int i = 0; i < 360; i++)
            {
                float a = i * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(a), Random.Range(-0.08f, 0.08f), Mathf.Sin(a));
                StoryFx.Emit(ring, at, d * Random.Range(5f, 7f), Random.Range(0.05f, 0.12f), Random.Range(0.5f, 0.9f),
                             Random.value < 0.7f ? new Color(1f, 0.72f, 0.25f, 1f) : new Color(1f, 0.35f, 0.08f, 1f));
            }
            StoryFx.Burst(ring, at, 120, 2.5f, 0.03f, 0.8f, new Color(1f, 0.85f, 0.5f, 1f));
            yield return new WaitForSeconds(3f);
            Destroy(ring.gameObject);
        }

        static IEnumerator Dim(Light l, float seconds)
        {
            float from = l.intensity;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                l.intensity = Mathf.Lerp(from, 0f, t / seconds);
                yield return null;
            }
            l.intensity = 0f;
        }

        // ---------------------------------------------------------------- the room

        void RegisterRoom()
        {
            const float w = 4.4f, d = 5f, h = 2.6f, mid = 1.1f;
            Surface("Room_Floor", new Vector3(0f, 0f, mid), Vector3.right, Vector3.forward, w, d, 1.1f);
            Surface("Room_Ceiling", new Vector3(0f, h, mid), Vector3.right, Vector3.back, w, d, 1.8f);
            Surface("Room_WallLeft", new Vector3(-2.2f, h / 2, mid), Vector3.forward, Vector3.up, d, h, 1.5f);
            Surface("Room_WallRight", new Vector3(2.2f, h / 2, mid), Vector3.back, Vector3.up, d, h, 1.5f);
            Surface("Room_WallFront", new Vector3(0f, h / 2, 3.6f), Vector3.right, Vector3.up, w, h, 1.5f);
            Surface("Room_WallBack", new Vector3(0f, h / 2, -1.4f), Vector3.left, Vector3.up, w, h, 1.5f);
            foreach (Transform t in GetComponentsInChildren<Transform>())
                if (t.parent != null && (t.name.StartsWith("Prop_") || t.name.StartsWith("Decor_")) && !t.parent.name.StartsWith("Prop_") && !t.parent.name.StartsWith("Decor_"))
                    room.AddDrifting(t);
        }

        void Surface(string name, Vector3 centre, Vector3 right, Vector3 up, float width, float height, float tile)
        {
            Transform t = FindDeep(transform, name);
            if (t == null)
            {
                Debug.LogWarning($"[Chapter2] No {name} in the bedroom model: it won't shatter.");
                return;
            }
            room.AddSurface(t.GetComponent<Renderer>(), centre, right, up, width, height, tile);
        }

        // ---------------------------------------------------------------- helpers

        void PlaceHead(Vector3 feet)
        {
            Vector3 p = rig.position;
            p.x += feet.x - head.position.x;
            p.z += feet.z - head.position.z;
            p.y = feet.y + (HeadsetActive ? 0f : eyes);
            rig.position = p;
        }

        void Face(Vector3 direction)
        {
            Vector3 now = StoryFx.Flat(head.forward), want = StoryFx.Flat(direction);
            rig.RotateAround(head.position, Vector3.up, Vector3.SignedAngle(now, want, Vector3.up));
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
            ComfortVignette.Tint(heat, 0f);
        }
    }
}
