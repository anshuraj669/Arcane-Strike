using ArcaneStrike.HandInput;
using ArcaneStrike.VFX;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Makes the old house at midnight feel real: a starry sky, a full virtual world (passthrough and the real
    /// room's mesh switched off), dust hanging in the air and glittering in the moonbeam from the attic window,
    /// a candle flame, wind at the window, crickets and an owl outside, the old roof creaking, floorboards
    /// creaking underfoot, the chest lid groaning open, and dim, hazy light. The moon itself (the scene's
    /// directional light, with shadows, and the night skybox) is set up by Apply Attic Model.
    /// Sounds are placeholders made from code; assign real clips to replace them. Sits on the attic model and
    /// works in its space (attic floor at y 0, player start at the origin facing +Z).
    /// </summary>
    public class AtticAtmosphere : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] AtticGameManager manager;

        [Header("Room (local space)")]
        [SerializeField] Vector3 roomMin = new Vector3(-2.4f, 0.05f, -1.7f);
        [SerializeField] Vector3 roomMax = new Vector3(2.4f, 3.0f, 4.1f);

        [Header("Dust in the air")]
        [SerializeField] bool dust = true;
        [Tooltip("Motes per second drifting around the room.")]
        [SerializeField] float dustRate = 18f;
        [SerializeField] Color dustColor = new Color(0.7f, 0.72f, 0.8f, 0.22f);

        [Header("Moonbeam through the attic window")]
        [SerializeField] bool lightShaft = true;
        [Tooltip("Centre of the window pane.")]
        [SerializeField] Vector3 windowCenter = new Vector3(0f, 2.775f, 4.17f);
        [SerializeField] Vector2 windowSize = new Vector2(0.6f, 0.55f);
        [Tooltip("Which way the moonlight comes in: from the window straight onto the chest.")]
        [SerializeField] Vector3 lightDirection = new Vector3(0f, -0.588f, -0.809f);
        [SerializeField] Color shaftColor = new Color(0.65f, 0.78f, 1f, 0.07f);
        [Tooltip("Optional. Found by name (WindowLight) when empty; it dims with the shaft as clouds pass.")]
        [SerializeField] Light windowLight;

        [Header("Candle")]
        [Tooltip("Optional. Found by name (Grab_Candle_1) when empty.")]
        [SerializeField] Transform candle;
        [SerializeField] Color flameColor = new Color(1f, 0.62f, 0.25f, 1f);

        [Header("Mood")]
        [Tooltip("Midnight, lit like a film night: cold blue ambient light, bright enough to see by, and a little haze.")]
        [SerializeField] bool darkenAmbient = true;
        [SerializeField] Color ambient = new Color(0.10f, 0.12f, 0.20f);
        [SerializeField] bool haze = true;
        [SerializeField] Color hazeColor = new Color(0.04f, 0.05f, 0.08f);
        [SerializeField] float hazeDensity = 0.018f;
        [Tooltip("Without a headset (the editor), use a normal screen field of view: the headset's 90° looks like a fisheye on a wide Game view.")]
        [SerializeField] float desktopFieldOfView = 60f;

        [Header("Night (full VR)")]
        [Tooltip("Switch passthrough and the real room's mesh off and show the night sky: this is a virtual world.")]
        [SerializeField] bool virtualWorld = true;
        [SerializeField] int stars = 900;
        [Tooltip("Where the crickets are (the yard in front of the house).")]
        [SerializeField] Vector3 yard = new Vector3(0f, -3.6f, -14f);
        [Tooltip("Front of the house: floorboards only creak inside it.")]
        [SerializeField] float houseFront = -7.4f;
        [SerializeField, Range(0f, 1f)] float cricketVolume = 0.35f;

        [Header("Wind outside")]
        [Tooltip("Leaves blowing across the yard and a gusting wind, following the same gusts that sway the garden.")]
        [SerializeField] bool windOutside = true;
        [SerializeField] Vector3 yardMin = new Vector3(-12f, -3.6f, -26f);
        [SerializeField] Vector3 yardMax = new Vector3(12f, -1.2f, 12f);
        [SerializeField] float leavesPerSecond = 30f;
        [SerializeField, Range(0f, 1f)] float outdoorWindVolume = 0.5f;

        [Header("Sound")]
        [Tooltip("Optional real clips; placeholders are made from code when empty.")]
        [SerializeField] AudioClip windClip;
        [SerializeField] AudioClip[] creakClips;
        [SerializeField, Range(0f, 1f)] float windVolume = 0.3f;
        [SerializeField, Range(0f, 1f)] float creakVolume = 0.35f;
        [Tooltip("Seconds between creaks of the roof (random in this range).")]
        [SerializeField] Vector2 creakInterval = new Vector2(6f, 16f);
        [Tooltip("Walking this far (m) may make a floorboard creak.")]
        [SerializeField] float stepDistance = 0.6f;

        ParticleSystem motes, glitter, flame, starField, leaves;
        AudioSource windowWind, yardWind;
        Light porchLight;
        Material shaftMaterial;
        Light flameLight;
        float windowLightBase, nextCreak, walked, startTime;
        Vector3 lastHead;
        bool hasHead;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (manager == null) manager = FindAnyObjectByType<AtticGameManager>();
        }

        void OnEnable()
        {
            if (manager != null) manager.OnBeatChanged += HandleBeat;
        }

        void OnDisable()
        {
            if (manager != null) manager.OnBeatChanged -= HandleBeat;
        }

        void Start()
        {
            startTime = Time.time;
            if (creakClips == null || creakClips.Length == 0)
                creakClips = new[] { Creak(1, 0.7f, 55f, 520f), Creak(2, 0.9f, 40f, 380f), Creak(3, 0.5f, 80f, 650f) };
            nextCreak = Time.time + Random.Range(creakInterval.x, creakInterval.y);

            if (dust)
            {
                motes = StoryFx.CreateParticles(transform, "DustMotes", false, 0.002f, 500);
                glitter = StoryFx.CreateParticles(transform, "ShaftGlitter", true, 0.001f, 300);
            }
            if (lightShaft) BuildShaft();
            if (windowLight == null) windowLight = FindLight("WindowLight");
            if (windowLight != null) windowLightBase = windowLight.intensity;
            if (candle == null) candle = FindDeep(transform, "Grab_Candle_1");
            if (candle != null) LightCandle();

            if (darkenAmbient)
            {
                // Moonlit sky above, dark ground below: walls facing away from the moon still read.
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = ambient * 2f;
                RenderSettings.ambientEquatorColor = ambient;
                RenderSettings.ambientGroundColor = ambient * 0.3f;
            }
            LightPorchLamp();
            if (haze)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = hazeColor;
                RenderSettings.fogDensity = hazeDensity;
            }

            if (virtualWorld) EnterVirtualWorld();
            if (!UnityEngine.XR.XRSettings.isDeviceActive && Camera.main != null) Camera.main.fieldOfView = desktopFieldOfView;
            StartNightSounds();

            var wind = new GameObject("Wind");
            wind.transform.SetParent(transform, false);
            wind.transform.localPosition = windowCenter;
            AudioClip windSound = windClip != null ? windClip : Wind();
            windowWind = StoryFx.CreateSource(wind, windSound, true, 1f);
            windowWind.minDistance = 1.5f;
            windowWind.maxDistance = 10f;
            windowWind.volume = windVolume;
            windowWind.Play();

            if (windOutside)
            {
                var outside = new GameObject("YardWind");
                outside.transform.SetParent(transform, false);
                outside.transform.localPosition = yard + Vector3.up * 2f;
                yardWind = StoryFx.CreateSource(outside, windSound, true, 1f);
                yardWind.minDistance = 8f;
                yardWind.maxDistance = 35f;
                yardWind.time = windSound.length * 0.5f; // out of step with the window's wind
                yardWind.Play();
                leaves = StoryFx.CreateParticles(transform, "BlowingLeaves", false, 0.05f, 600);
            }
        }

        void Update()
        {
            float clouds = 0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 0.08f, 3.1f); // passing clouds
            if (shaftMaterial != null) shaftMaterial.SetColor("_BaseColor", StoryFx.WithAlpha(shaftColor, shaftColor.a * clouds));
            if (windowLight != null) windowLight.intensity = windowLightBase * clouds;
            if (Time.time - startTime > 1f) EmitDust(clouds); // after the set has been placed in front of the player
            UpdateFlame();
            BlowWind();
            if (porchLight != null) porchLight.intensity = 2.2f * (0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 6f, 7.7f));

            if (Time.time >= nextCreak)
            {
                nextCreak = Time.time + Random.Range(creakInterval.x, creakInterval.y);
                float x = Random.Range(roomMin.x, roomMax.x);
                float roofY = Mathf.Lerp(roomMax.y + 0.2f, 1f, Mathf.Abs(x) / Mathf.Max(roomMax.x, 0.01f));
                PlayCreak(transform.TransformPoint(new Vector3(x, roofY, Random.Range(roomMin.z, roomMax.z))), creakVolume, 1f);
            }
            FloorCreaks();
        }

        // ---------------------------------------------------------------- dust

        void EmitDust(float clouds)
        {
            if (motes == null) return;
            int n = Mathf.FloorToInt(dustRate * Time.deltaTime + Random.value);
            for (int i = 0; i < n; i++)
            {
                Vector3 local = new Vector3(Random.Range(roomMin.x, roomMax.x), Random.Range(roomMin.y, roomMax.y - 0.4f), Random.Range(roomMin.z, roomMax.z));
                StoryFx.Emit(motes, transform.TransformPoint(local), Random.insideUnitSphere * 0.015f,
                             Random.Range(0.004f, 0.009f), Random.Range(6f, 12f), dustColor);
            }
            if (!lightShaft) return;
            // Motes inside the window beam catch the light and glitter.
            int m = Mathf.FloorToInt(14f * clouds * Time.deltaTime + Random.value);
            Vector3 dir = lightDirection.normalized;
            float length = ShaftLength();
            for (int i = 0; i < m; i++)
            {
                Vector3 across = new Vector3((Random.value - 0.5f) * windowSize.x, (Random.value - 0.5f) * windowSize.y, 0f);
                Vector3 local = windowCenter + across * 1.2f + dir * Random.Range(0.2f, length);
                StoryFx.Emit(glitter, transform.TransformPoint(local), Random.insideUnitSphere * 0.01f,
                             Random.Range(0.004f, 0.008f), Random.Range(3f, 6f), new Color(0.8f, 0.88f, 1f, 0.7f));
            }
        }

        // ---------------------------------------------------------------- light shaft

        float ShaftLength()
        {
            Vector3 dir = lightDirection.normalized;
            return dir.y < -0.01f ? windowCenter.y / -dir.y : 3f;
        }

        // Three nested, open-ended boxes from the window to the floor, brightest at the window, fading out.
        void BuildShaft()
        {
            shaftMaterial = SparkHands.CreateGlowMaterial(Texture2D.whiteTexture);
            shaftMaterial.name = "WindowShaft";
            shaftMaterial.SetFloat("_Cull", 0f);
            shaftMaterial.SetColor("_BaseColor", shaftColor);

            var go = new GameObject("WindowShaft");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = ShaftMesh();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = shaftMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        Mesh ShaftMesh()
        {
            Vector3 dir = lightDirection.normalized;
            Vector3 far = dir * ShaftLength();
            var verts = new System.Collections.Generic.List<Vector3>();
            var colors = new System.Collections.Generic.List<Color>();
            var tris = new System.Collections.Generic.List<int>();
            foreach (float scale in new[] { 1f, 0.75f, 0.5f })
            {
                Vector2 near = windowSize * 0.5f * scale;
                Vector2 wide = near * 1.35f; // a little spread towards the floor
                Vector3[] a =
                {
                    new Vector3(-near.x, -near.y, 0f), new Vector3(near.x, -near.y, 0f),
                    new Vector3(near.x, near.y, 0f), new Vector3(-near.x, near.y, 0f),
                };
                for (int side = 0; side < 4; side++)
                {
                    Vector3 p0 = a[side], p1 = a[(side + 1) % 4];
                    Vector3 q0 = new Vector3(p0.x / near.x * wide.x, p0.y / near.y * wide.y, 0f);
                    Vector3 q1 = new Vector3(p1.x / near.x * wide.x, p1.y / near.y * wide.y, 0f);
                    int b = verts.Count;
                    verts.Add(windowCenter + p0);
                    verts.Add(windowCenter + p1);
                    verts.Add(windowCenter + q1 + far);
                    verts.Add(windowCenter + q0 + far);
                    colors.Add(Color.white);
                    colors.Add(Color.white);
                    colors.Add(new Color(1f, 1f, 1f, 0f));
                    colors.Add(new Color(1f, 1f, 1f, 0f));
                    tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                }
            }
            var mesh = new Mesh { name = "WindowShaft" };
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------------------------------------------------------- candle

        void LightCandle()
        {
            var filter = candle.GetComponent<MeshFilter>();
            float top = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds.max.y : 0.06f;
            var wick = new GameObject("Flame").transform;
            wick.SetParent(candle, false);
            wick.localPosition = new Vector3(0f, top + 0.012f, 0f);
            flame = StoryFx.CreateParticles(wick, "FlameParticles", true, -0.05f, 120);
            flameLight = wick.gameObject.AddComponent<Light>();
            flameLight.type = LightType.Point;
            flameLight.color = flameColor;
            flameLight.range = 1.5f;
            flameLight.shadows = LightShadows.None;
        }

        void UpdateFlame()
        {
            if (flame == null) return;
            Transform wick = flame.transform.parent;
            float flicker = Mathf.PerlinNoise(Time.time * 9f, 0.5f);
            flameLight.intensity = 0.5f + 0.5f * flicker;
            int n = Mathf.FloorToInt(40f * Time.deltaTime + Random.value);
            for (int i = 0; i < n; i++)
                StoryFx.Emit(flame, wick.position + Random.insideUnitSphere * 0.003f, Vector3.up * Random.Range(0.04f, 0.09f),
                             Random.Range(0.012f, 0.022f), Random.Range(0.15f, 0.3f),
                             Color.Lerp(flameColor, new Color(1f, 0.9f, 0.6f), Random.value));
        }

        // ---------------------------------------------------------------- creaks

        void FloorCreaks()
        {
            Transform head = StoryFx.Head(input);
            if (head == null) return;
            Vector3 p = head.position;
            p.y = 0f;
            if (!hasHead)
            {
                lastHead = p;
                hasHead = true;
                return;
            }
            float moved = Vector3.Distance(p, lastHead);
            lastHead = p;
            if (moved > 0.5f) return; // the set was just placed, not a step
            Vector3 local = transform.InverseTransformPoint(head.position);
            if (local.z < houseFront || Mathf.Abs(local.x) > roomMax.x + 0.2f) return; // outside on the grass
            walked += moved;
            if (walked < stepDistance) return;
            walked = 0f;
            if (Random.value > 0.45f) return;
            Vector3 foot = transform.TransformPoint(new Vector3(0f, 0.02f, 0f));
            PlayCreak(new Vector3(head.position.x, foot.y, head.position.z), creakVolume * 1.4f, Random.Range(0.75f, 0.95f));
        }

        void HandleBeat(AtticGameManager.Beat beat)
        {
            // The chest lid groans open (the beat after ChestClosed).
            if (beat == AtticGameManager.Beat.LanternFound && manager.chest != null)
                PlayCreak(manager.chest.transform.position + Vector3.up * 0.4f, Mathf.Min(1f, creakVolume * 2.5f), 0.7f);
        }

        /// <summary>An old-wood creak at a point (floorboards, doors, the roof).</summary>
        public void PlayCreak(Vector3 at, float volume, float pitch)
        {
            var go = new GameObject("Creak");
            go.transform.position = at;
            AudioClip clip = creakClips[Random.Range(0, creakClips.Length)];
            AudioSource source = StoryFx.CreateSource(go, clip, false, 1f);
            source.volume = volume;
            source.pitch = pitch * Random.Range(0.9f, 1.1f);
            source.Play();
            Destroy(go, clip.length / source.pitch + 0.1f);
        }

        // ---------------------------------------------------------------- night

        void EnterVirtualWorld()
        {
            Core.RoomSetup.VirtualWorld = true;
            foreach (var b in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (b != null && b.GetType().Name == "OVRPassthroughLayer") b.enabled = false; // by name: no Meta reference here
            Camera cam = Camera.main;
            if (cam != null && RenderSettings.skybox != null)
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.backgroundColor = Color.black;
            }

            // Stars: tiny points on a far sphere above the horizon.
            starField = StoryFx.CreateParticles(transform, "Stars", true, 0f, stars + 10);
            var main = starField.main;
            main.startLifetime = 1e6f;
            var size = starField.sizeOverLifetime;
            size.enabled = false;
            var fade = starField.colorOverLifetime;
            fade.enabled = false;
            for (int i = 0; i < stars; i++)
            {
                Vector3 d = Random.onUnitSphere;
                d.y = Mathf.Abs(d.y) * 0.9f + 0.05f;
                float b = Random.value;
                StoryFx.Emit(starField, transform.position + d.normalized * 180f, Vector3.zero, Mathf.Lerp(0.25f, 0.9f, b * b),
                             1e6f, new Color(0.85f, 0.9f, 1f, Mathf.Lerp(0.3f, 1f, b)));
            }
        }

        // Gusts: the wind sound swells and leaves skitter across the yard, in step with the swaying garden.
        void BlowWind()
        {
            float gust = VFX.Wind.Gust(transform.TransformPoint(yard));
            if (windowWind != null) windowWind.volume = windVolume * (0.4f + 0.6f * gust);
            if (yardWind != null) yardWind.volume = outdoorWindVolume * (0.3f + 0.7f * gust);
            if (leaves == null) return;
            int n = Mathf.FloorToInt(leavesPerSecond * (0.15f + gust) * Time.deltaTime + Random.value);
            for (int i = 0; i < n; i++)
            {
                Vector3 local = new Vector3(Random.Range(yardMin.x, yardMax.x), Random.Range(yardMin.y, yardMax.y), Random.Range(yardMin.z, yardMax.z));
                if (Mathf.Abs(local.x) < roomMax.x + 0.4f && local.z > houseFront - 2f && local.z < roomMax.z + 0.4f) continue; // not inside the house
                Vector3 v = VFX.Wind.Direction * Random.Range(1.5f, 2f + 3f * gust) + Random.insideUnitSphere * 0.6f;
                Color c = Random.value < 0.5f ? new Color(0.22f, 0.3f, 0.14f, 0.9f) : new Color(0.33f, 0.24f, 0.13f, 0.9f);
                StoryFx.Emit(leaves, transform.TransformPoint(local), v, Random.Range(0.03f, 0.06f), Random.Range(3f, 6f), c);
            }
        }

        // The lamp by the front door: a warm, flickering light to walk towards in the dark.
        void LightPorchLamp()
        {
            Transform lamp = FindDeep(transform, "House_PorchLamp");
            if (lamp == null) return;
            var go = new GameObject("PorchLampLight");
            go.transform.SetParent(lamp, false);
            porchLight = go.AddComponent<Light>();
            porchLight.type = LightType.Point;
            porchLight.color = new Color(1f, 0.68f, 0.35f);
            porchLight.range = 7f;
            porchLight.intensity = 2.2f;
            porchLight.shadows = LightShadows.None;
            LightYardLamp();
        }

        // The lamp post by the gate, on the path to the door.
        void LightYardLamp()
        {
            Transform lamp = FindDeep(transform, "House_YardLamp");
            if (lamp == null) return;
            var go = new GameObject("YardLampLight");
            go.transform.SetParent(lamp, false);
            go.transform.localPosition = Vector3.up * 0.17f;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.68f, 0.35f);
            l.range = 7f;
            l.intensity = 1.6f;
            l.shadows = LightShadows.None;
        }

        void StartNightSounds()
        {
            var crickets = new GameObject("Crickets");
            crickets.transform.SetParent(transform, false);
            crickets.transform.localPosition = yard;
            AudioSource source = StoryFx.CreateSource(crickets, Crickets(), true, 1f);
            source.minDistance = 6f;
            source.maxDistance = 30f;
            source.volume = cricketVolume;
            source.Play();
            StartCoroutine(Owl());
        }

        System.Collections.IEnumerator Owl()
        {
            AudioClip hoot = Hoot();
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(14f, 30f));
                var go = new GameObject("Owl");
                go.transform.position = transform.TransformPoint(yard + new Vector3(Random.Range(-15f, 15f), 6f, Random.Range(-10f, 4f)));
                AudioSource s = StoryFx.CreateSource(go, hoot, false, 1f);
                s.minDistance = 5f;
                s.maxDistance = 40f;
                s.volume = 0.35f;
                s.pitch = Random.Range(0.92f, 1.05f);
                s.Play();
                Destroy(go, hoot.length + 0.2f);
            }
        }

        // ---------------------------------------------------------------- placeholder sounds

        /// <summary>A field of crickets: short bursts of a high chirp at slightly different rates (6 s loop).</summary>
        static AudioClip Crickets()
        {
            var rng = new System.Random(9);
            int length = Rate * 6;
            var data = new float[length];
            for (int c = 0; c < 5; c++)
            {
                float freq = 4200f + c * 260f, rate = 2f + c * 0.5f, phase = (float)rng.NextDouble();
                for (int i = 0; i < length; i++)
                {
                    float t = (float)i / Rate;
                    float cycle = (t * rate + phase) % 1f;
                    float burst = cycle < 0.18f ? Mathf.Sin(Mathf.PI * cycle / 0.18f) : 0f;
                    float pulses = Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 32f * t)); // the rasp
                    data[i] += Mathf.Sin(2f * Mathf.PI * freq * t) * burst * pulses * 0.12f;
                }
            }
            AudioClip clip = AudioClip.Create("Crickets (placeholder)", length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A tawny owl: a soft "hoo", a pause, then a long wavering "hoo-oo-oo".</summary>
        static AudioClip Hoot()
        {
            int length = (int)(Rate * 2.2f);
            var data = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / Rate;
                float env = 0f, f = 380f;
                if (t < 0.35f) env = Mathf.Sin(Mathf.PI * t / 0.35f);
                else if (t > 0.8f && t < 2.1f)
                {
                    float u = (t - 0.8f) / 1.3f;
                    env = Mathf.Sin(Mathf.PI * u) * (0.7f + 0.3f * Mathf.Sin(t * 40f));
                    f = 400f - 40f * u;
                }
                data[i] = (Mathf.Sin(2f * Mathf.PI * f * t) + 0.2f * Mathf.Sin(4f * Mathf.PI * f * t)) * env * 0.5f;
            }
            AudioClip clip = AudioClip.Create("Owl (placeholder)", length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        const int Rate = 44100;

        /// <summary>Wood under strain: stick-slip clicks (the rate glides up and back) ringing through a resonance.</summary>
        static AudioClip Creak(int seed, float length, float clickRate, float resonance)
        {
            var rng = new System.Random(seed);
            var data = new float[(int)(Rate * length)];
            float phase = 0f, y1 = 0f, y2 = 0f, peak = 1e-4f;
            float w = 2f * Mathf.PI * resonance / Rate, r = 0.992f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / data.Length;
                float rate = clickRate * (0.7f + 0.8f * Mathf.Sin(Mathf.PI * t) + 0.15f * Mathf.Sin(t * 37f));
                phase += rate / Rate;
                float x = ((float)rng.NextDouble() * 2f - 1f) * 0.05f;
                if (phase >= 1f)
                {
                    phase -= 1f;
                    x += 1f;
                }
                float y = x + 2f * r * Mathf.Cos(w) * y1 - r * r * y2;
                y2 = y1;
                y1 = y;
                float envelope = Mathf.Min(1f, t * 12f) * Mathf.Pow(1f - t, 0.6f);
                data[i] = y * envelope;
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            for (int i = 0; i < data.Length; i++) data[i] *= 0.8f / peak;
            AudioClip clip = AudioClip.Create($"Creak {seed} (placeholder)", data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Wind outside the window: soft low noise in slow gusts, with a faint whistle (8 s loop).</summary>
        static AudioClip Wind()
        {
            var rng = new System.Random(5);
            int length = Rate * 8;
            var data = new float[length];
            float low = 0f, band = 0f, peak = 1e-4f;
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / length; // 0..1 over the loop: whole cycles keep it seamless
                float gust = 0.45f + 0.35f * Mathf.Sin(2f * Mathf.PI * t * 2f) + 0.2f * Mathf.Sin(2f * Mathf.PI * t * 5f + 1f);
                float white = (float)rng.NextDouble() * 2f - 1f;
                low += (white - low) * 0.02f;
                band += (low - band) * 0.3f;
                float whistle = Mathf.Sin(2f * Mathf.PI * (650f * i / Rate + 3f * Mathf.Sin(2f * Mathf.PI * t * 3f))) * Mathf.Pow(Mathf.Max(0f, gust - 0.5f), 2f) * 0.15f;
                data[i] = (low * 3f - band * 0.5f) * gust + whistle;
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            int fade = Rate / 4; // blend the end into the start so the noise doesn't click at the loop
            for (int i = 0; i < fade; i++)
            {
                float a = (float)i / fade;
                data[i] = data[i] * a + data[length - fade + i] * (1f - a);
            }
            var loop = new float[length - fade];
            for (int i = 0; i < loop.Length; i++) loop[i] = data[i] * 0.7f / peak;
            AudioClip clip = AudioClip.Create("Wind (placeholder)", loop.Length, 1, Rate, false);
            clip.SetData(loop, 0);
            return clip;
        }

        // ---------------------------------------------------------------- helpers

        Light FindLight(string name)
        {
            Transform t = FindDeep(transform, name);
            return t != null ? t.GetComponent<Light>() : null;
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
    }
}
