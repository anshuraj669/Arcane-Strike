using System;
using System.Collections;
using System.Collections.Generic;
using ArcaneStrike.HandInput;
using ArcaneStrike.VFX;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The Weaver: the main character's body, his outfit (the plague assassin's hooded leather coat, beak mask, gloves
    /// and boots) and his sword (Resources/Characters/Weaver.prefab, from Tools/blender/build_weaver.py). It is a separate model so the attic loads light: <see cref="Load"/> brings
    /// it in when the Ascension begins (or the first time anything needs it).
    /// <see cref="EquipNext"/> forms the next part (sleeves and gloves, boots, coat, the hood and mask, the coat's long
    /// back, then the sword) in a shower of
    /// golden sparks; <see cref="Equip"/> forms whatever is left.
    /// Two views of it:
    /// - Worn (first person): Outfit_* pieces follow the player every frame: the sleeves on the forearms (from the
    ///   tracked hands; without tracking they rest low in front, so they show on a desktop too), the rest and the
    ///   sheathed sword below the head, turning with the body.
    /// - The body (<see cref="ShowBody"/>): while the player's soul is out of it in the Ascension, the whole boy hangs
    ///   in the light in his hoodie and joggers (Body_*). <see cref="SuitUp"/> wraps the suit onto him in one wave
    ///   (Avatar_*): it spreads out from his chest over the arms, legs and boots, eating his own clothes as it goes,
    ///   with ribbons of light spiralling down each limb ahead of it, his body arching back in the light; the hood
    ///   wraps up over his head last and the mask's eyes light. The katana then appears before his chest, turns, and
    ///   slides into the sheath on his back.
    /// Appearances tint the outfit's leather (coat, gloves, boots, back of the coat, hood); O cycles them. The look and
    /// whether it is on carry over to the next scene. <see cref="DrawSword"/> hands the sword to whoever wields it;
    /// <see cref="SheathedPose"/> says where it hangs on the back, to draw it from there.
    /// </summary>
    [DefaultExecutionOrder(200)] // after the hands have been read
    public class WarriorOutfit : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [Tooltip("The character prefab, under a Resources folder (Arcane Strike → Build Weaver Character makes it).")]
        [SerializeField] string prefabPath = "Characters/Weaver";
        [Tooltip("Chest centre below the eyes (m).")]
        [SerializeField] float chestBelowEyes = 0.38f;
        [Tooltip("Chest centre behind the eyes (m).")]
        [SerializeField] float chestBehindEyes = 0.08f;
        [Tooltip("How quickly the body turns after the head (larger = tighter).")]
        [SerializeField] float bodyTurn = 3f;
        [SerializeField] Color sparkColor = new Color(1f, 0.8f, 0.35f, 1f);
        [Tooltip("The body: a part flares this colour as it forms, before it turns to cloth.")]
        [SerializeField] Color formingColor = new Color(1f, 0.92f, 0.72f, 0.85f);
        [Tooltip("The body: seconds a forming part glows before it turns to cloth.")]
        [SerializeField] float formingGlow = 0.6f;

        [Serializable]
        public class Appearance
        {
            public string name;
            public Color coat, trim, boots, cape, scarf;
        }

        [Header("Appearances")]
        [SerializeField] Appearance[] appearances =
        {
            // Tints over the assassin's own (near-black) leather texture: white keeps it as it is.
            new Appearance { name = "Plague Shadow", coat = Color.white, trim = Color.white, boots = Color.white, cape = Color.white, scarf = Color.white },
            new Appearance { name = "Ember Crimson", coat = new Color(1f, 0.55f, 0.5f), trim = new Color(1f, 0.8f, 0.6f),
                             boots = Color.white, cape = new Color(1f, 0.4f, 0.35f), scarf = new Color(1f, 0.55f, 0.5f) },
            new Appearance { name = "Midnight Indigo", coat = new Color(0.6f, 0.65f, 1f), trim = new Color(0.8f, 0.85f, 1f),
                             boots = new Color(0.8f, 0.8f, 0.9f), cape = new Color(0.5f, 0.55f, 1f), scarf = new Color(0.6f, 0.65f, 1f) },
            new Appearance { name = "Saffron Weaver", coat = new Color(1f, 0.8f, 0.5f), trim = new Color(1f, 0.85f, 0.5f),
                             boots = new Color(1f, 0.9f, 0.75f), cape = new Color(1f, 0.75f, 0.3f), scarf = new Color(1f, 0.8f, 0.5f) },
            new Appearance { name = "Forest Ranger", coat = new Color(0.7f, 0.85f, 0.6f), trim = new Color(0.9f, 0.85f, 0.65f),
                             boots = new Color(0.9f, 0.85f, 0.75f), cape = new Color(0.6f, 0.8f, 0.55f), scarf = new Color(0.7f, 0.85f, 0.6f) },
        };
        [SerializeField] int appearance;
        [Tooltip("Keyboard (GameInput): O switches to the next appearance.")]
        [SerializeField] bool keyboardFallback = true;
        [Tooltip("Wear the whole outfit from the start (scenes after the Ascension). It is also put on by itself when an " +
                 "earlier scene finished dressing the player.")]
        [SerializeField] bool dressedAtStart;

        // Carried from scene to scene: the look chosen and whether the outfit is on.
        static int carriedAppearance = -1;
        static bool carriedDressed;

        public int AppearanceCount => appearances.Length;
        public string AppearanceName => appearances.Length > 0 ? appearances[appearance].name : "";

        public bool IsLoaded => character != null;
        public bool IsWorn { get; private set; }
        /// <summary>Every part is on.</summary>
        public bool IsComplete => nextStep >= Steps.Length;
        /// <summary>Puts the whole outfit on as the scene starts (ticked, or already won in an earlier scene).</summary>
        public bool ArrivesDressed => dressedAtStart || carriedDressed;
        /// <summary>The body is shown (the soul is out of it).</summary>
        public bool BodyShown => bodyShown;
        /// <summary>Middle of the body's chest (while it is shown).</summary>
        public Vector3 BodyChest => AnchorPosition(bodyEyes, BodyFacing);

        // The order the parts form in, and the boy's own clothes each one replaces on the body.
        static readonly string[] Steps = { "Sleeve", "Boots", "Coat", "Scarf", "Cape", "Sword" };
        static readonly Dictionary<string, string> Replaces = new Dictionary<string, string>
        {
            { "Body_Arms", "Sleeve" }, { "Body_Feet", "Boots" }, { "Body_Torso", "Coat" }, { "Body_Head", "Scarf" }, // the hood and mask cover his head
        };

        // How the sheathed katana hangs, relative to the body's anchor: the same numbers as back_frame() in
        // build_weaver.py (diagonally across the back: the guard behind the right shoulder, the tip at the left hip).
        static readonly Vector3 SwordDown = new Vector3(-0.474f, -0.88f, -0.034f).normalized;
        static readonly Vector3 SwordGuard = new Vector3(0.119f, 1.396f, -0.172f) - new Vector3(0f, 1.22f, 0.1328f);
        const float SwordGripToGuard = 0.155f; // the bare sword's pivot (mid-grip) is this far above the guard

        int nextStep;

        enum Anchor { Wrist, Body }

        class Piece // worn in first person
        {
            public Transform transform;
            public Anchor anchor;
            public HandSide side;
            public Vector3 size;
            public float grow; // 0..1 while materialising
            public Renderer[] renderers;
        }

        class BodyPart // on the body seen from outside
        {
            public Transform transform;
            public Renderer[] renderers;
            public Material[][] cloth;
            public string step; // Avatar_*: the step that forms it; Body_*: the step that replaces it (or null: never)
            public bool isOutfit, shown;
        }

        readonly List<Piece> pieces = new List<Piece>();
        readonly List<BodyPart> body = new List<BodyPart>();
        GameObject character;
        Transform bareSword;
        Material forming;
        bool bodyShown, loading;
        Vector3 bodyEyes;
        float bodyEyesYaw;
        Transform head;
        ParticleSystem sparks;
        float bodyYaw;

        Quaternion BodyFacing => Quaternion.Euler(0f, bodyEyesYaw, 0f);

        // A fresh play session starts undressed (statics can outlive Play when domain reload is off).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCarried()
        {
            carriedAppearance = -1;
            carriedDressed = false;
        }

        /// <summary>The Weaver died: the outfit isn't carried over, so the next scene (the cave) plays the Ascension again.</summary>
        public static void ForgetCarried() => ResetCarried();

        /// <summary>The next scene starts undressed (the cave, where the Ascension always plays), keeping the look chosen.
        /// Call it before the outfit's Start.</summary>
        public static void ForgetDressed() => carriedDressed = false;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (carriedAppearance >= 0) appearance = carriedAppearance;
        }

        void Start()
        {
            sparks = StoryFx.CreateParticles(transform, "OutfitSparks", true, -0.1f, 800);
            if (GetComponent<Combat.WeaverArms>() == null) gameObject.AddComponent<Combat.WeaverArms>();
            if (dressedAtStart || carriedDressed) DressNow();
        }

        /// <summary>Puts every part on at once, with no sparks (arriving already dressed).</summary>
        public void DressNow()
        {
            LoadNow();
            BeginWearing();
            while (!IsComplete)
            {
                string step = Steps[nextStep++];
                foreach (Piece p in pieces)
                    if (p.transform.name.StartsWith("Outfit_" + step))
                    {
                        p.grow = 1f;
                        p.transform.gameObject.SetActive(true);
                        ShowPiece(p, !bodyShown);
                        Place(p);
                    }
            }
            carriedDressed = true;
        }

        void BeginWearing()
        {
            if (IsWorn) return;
            IsWorn = true;
            head = StoryFx.Head(input);
            if (head != null) bodyYaw = head.eulerAngles.y;
        }

        // ---------------------------------------------------------------- the sword in hand

        /// <summary>The sword is out of its scabbard (in the player's hand).</summary>
        public bool SwordDrawn { get; private set; }

        /// <summary>Takes the sword out: the hilt leaves the hip and the bare sword is returned for a hand to hold
        /// (its pivot is the middle of the grip, the blade along +Y). Null if the character has no sword.</summary>
        public Transform DrawSword()
        {
            LoadNow();
            if (bareSword == null) return null;
            SwordDrawn = true;
            foreach (Piece p in pieces)
                if (p.transform.name == "Outfit_Sword_Hilt") ShowPiece(p, false);
            bareSword.gameObject.SetActive(true);
            return bareSword;
        }

        Vector3 heldGrip;
        Quaternion heldRotation;
        int heldFrame = -10;

        /// <summary>The sword in an untracked right hand (keyboard swings) is here this frame: the right glove and
        /// sleeve close round its grip (the reverse of how a tracked hand holds it: palm against the flat, blade out of
        /// the thumb side).</summary>
        public void HoldSword(Vector3 grip, Quaternion rotation)
        {
            heldGrip = grip;
            heldRotation = rotation;
            heldFrame = Time.frameCount;
            if (!IsWorn || head == null) return;
            foreach (Piece p in pieces)
                if (p.anchor == Anchor.Wrist && p.side == HandSide.Right && p.transform.gameObject.activeSelf) Place(p);
        }

        /// <summary>Puts the sword back in the scabbard.</summary>
        public void SheatheSword()
        {
            if (!SwordDrawn) return;
            SwordDrawn = false;
            if (bareSword != null) bareSword.gameObject.SetActive(false);
            foreach (Piece p in pieces)
                if (p.transform.name == "Outfit_Sword_Hilt") ShowPiece(p, !bodyShown);
        }

        /// <summary>Where the bare sword would be if it were in its sheath on the back of the worn outfit (its pivot,
        /// the middle of the grip, and its rotation: blade along +Y). False before the outfit is worn.</summary>
        public bool SheathedPose(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (!IsWorn || head == null) return false;
            Quaternion facing = Quaternion.Euler(0f, bodyYaw, 0f);
            Sheathed(AnchorPosition(head.position, facing), facing, out position, out rotation);
            return true;
        }

        static void Sheathed(Vector3 anchor, Quaternion facing, out Vector3 position, out Quaternion rotation)
        {
            Vector3 down = facing * SwordDown;
            position = anchor + facing * SwordGuard - down * SwordGripToGuard;
            rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(facing * Vector3.back, down).normalized, down);
        }

        // A worn piece shows unless the soul is out of the body, or (the hilt) the sword is in the hand.
        void ShowPiece(Piece p, bool show)
        {
            if (SwordDrawn && p.transform.name == "Outfit_Sword_Hilt") show = false;
            if (armsTaken && p.anchor == Anchor.Wrist) show = false;
            foreach (Renderer r in p.renderers) r.enabled = show;
        }

        bool armsTaken;

        /// <summary>WeaverArms is showing whole arms: the worn sleeves step aside.</summary>
        public void TakeArms(bool taken)
        {
            armsTaken = taken;
            foreach (Piece p in pieces)
                if (p.anchor == Anchor.Wrist)
                    foreach (Renderer r in p.renderers) r.enabled = !taken && !bodyShown;
        }

        /// <summary>What the sleeves are made of (the coat's cloth if it can be found).</summary>
        public Material SleeveMaterial
        {
            get
            {
                Material first = null;
                foreach (Piece p in pieces)
                    if (p.anchor == Anchor.Wrist)
                        foreach (Renderer r in p.renderers)
                            foreach (Material m in r.sharedMaterials)
                            {
                                if (m == null) continue;
                                if (m.name.Contains("OutfitCoat")) return m;
                                if (first == null) first = m;
                            }
                return first;
            }
        }

        // ---------------------------------------------------------------- loading

        /// <summary>Loads the character in the background (start it a few seconds before it is needed).</summary>
        public IEnumerator Load()
        {
            if (character != null) yield break;
            if (loading)
            {
                while (loading) yield return null;
                yield break;
            }
            loading = true;
            ResourceRequest request = Resources.LoadAsync<GameObject>(prefabPath);
            yield return request;
            loading = false;
            if (character == null) Setup(request.asset as GameObject);
        }

        void LoadNow()
        {
            if (character == null) Setup(Resources.Load<GameObject>(prefabPath));
        }

        void Setup(GameObject prefab)
        {
            if (prefab == null)
            {
                Debug.LogWarning($"[Outfit] Resources/{prefabPath} not found: run Tools/blender/build_weaver.py, then Arcane Strike → Apply Attic Model.");
                character = new GameObject("Weaver (missing)");
                return;
            }
            character = Instantiate(prefab);
            character.name = "Weaver";
            // Everything moves on its own (with the player, or the body), so nothing is left under the root.
            var parts = new List<Transform>();
            foreach (Transform t in character.transform) parts.Add(t);
            foreach (Transform t in parts)
            {
                t.SetParent(null, true);
                string n = t.name;
                if (n == "Weaver_Sword")
                {
                    bareSword = t;
                    foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true)) r.materials = r.materials; // own copies, recoloured
                    t.gameObject.SetActive(false);
                }
                else if (n.StartsWith("Body_") || n.StartsWith("Avatar_"))
                {
                    bool outfit = n.StartsWith("Avatar_");
                    var part = new BodyPart
                    {
                        transform = t,
                        renderers = t.GetComponentsInChildren<Renderer>(true),
                        step = outfit ? n.Substring(7) : Replaces.TryGetValue(n, out string s) ? s : null,
                        isOutfit = outfit,
                    };
                    part.cloth = new Material[part.renderers.Length][];
                    for (int i = 0; i < part.renderers.Length; i++) part.cloth[i] = part.renderers[i].materials; // own copies
                    t.gameObject.SetActive(false);
                    body.Add(part);
                }
                else if (n.StartsWith("Outfit_"))
                {
                    var p = new Piece
                    {
                        transform = t,
                        anchor = n.StartsWith("Outfit_Sleeve") ? Anchor.Wrist : Anchor.Body,
                        side = n.EndsWith("_L") ? HandSide.Left : HandSide.Right,
                        size = t.lossyScale,
                        renderers = t.GetComponentsInChildren<Renderer>(true),
                    };
                    foreach (Renderer r in p.renderers)
                    {
                        r.materials = r.materials; // own copies, recoloured
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // right at the eyes
                    }
                    t.gameObject.SetActive(false);
                    pieces.Add(p);
                }
            }
            Debug.Log($"[Outfit] Loaded the Weaver: {pieces.Count} worn pieces, {body.Count} body parts{(bareSword != null ? ", the sword" : "")}.");
            SetAppearance(appearance);
        }

        // ---------------------------------------------------------------- appearances

        /// <summary>Recolours the outfit with one of the appearances.</summary>
        public void SetAppearance(int index)
        {
            if (appearances.Length == 0) return;
            appearance = (index % appearances.Length + appearances.Length) % appearances.Length;
            carriedAppearance = appearance;
            Appearance a = appearances[appearance];
            void Recolour(Material m)
            {
                if (m == null) return;
                string n = m.name;
                Color? c = n.Contains("OutfitCoat") ? a.coat : n.Contains("OutfitTrim") && !n.Contains("Sword") ? a.trim : n.Contains("OutfitBoots") ? a.boots
                         : n.Contains("OutfitCape") ? a.cape : n.Contains("OutfitScarf") ? a.scarf : (Color?)null;
                if (c.HasValue) m.SetColor("_BaseColor", c.Value);
            }
            foreach (Piece p in pieces)
                foreach (Renderer r in p.renderers)
                    foreach (Material m in r.sharedMaterials)
                        Recolour(m);
            foreach (BodyPart part in body)
                foreach (Material[] set in part.cloth)
                    foreach (Material m in set)
                        Recolour(m);
            if (bareSword != null)
                foreach (Renderer r in bareSword.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.sharedMaterials)
                        Recolour(m);
            if (IsWorn) Debug.Log($"[Outfit] Appearance: {a.name}");
        }

        public void NextAppearance()
        {
            SetAppearance(appearance + 1);
            if (sparks == null) return;
            if (bodyShown)
            {
                foreach (BodyPart part in body)
                    if (part.isOutfit && part.shown) Sparkle(part.renderers, 40);
                return;
            }
            foreach (Piece p in pieces)
                if (p.transform.gameObject.activeSelf) StoryFx.Burst(sparks, Center(p), 25, 0.6f, 0.02f, 0.7f, sparkColor);
        }

        void Update()
        {
            if (keyboardFallback && GameInput.Pressed(GameAction.NextAppearance))
                NextAppearance();
        }

        // ---------------------------------------------------------------- the body, seen from outside

        /// <summary>Shows the body with its eyes at `eyes`, facing `yaw`, wearing the parts formed so far, and hides
        /// the worn pieces.</summary>
        public void ShowBody(Vector3 eyes, float yaw)
        {
            LoadNow();
            if (body.Count == 0) return;
            bodyShown = true;
            SetBodyPose(eyes, yaw);
            if (forming == null)
            {
                forming = AegisShield.CreateFaceMaterial(formingColor);
                forming.name = "WeaverForming";
            }
            foreach (BodyPart part in body)
            {
                bool formed = part.step != null && Array.IndexOf(Steps, part.step) < nextStep;
                part.shown = part.isOutfit ? formed : !formed;
                part.transform.gameObject.SetActive(part.shown);
                Dress(part, false);
                PlacePart(part);
            }
            foreach (Piece p in pieces)
                foreach (Renderer r in p.renderers) r.enabled = false;
        }

        /// <summary>Where the body's eyes are, and which way it faces.</summary>
        public void SetBodyPose(Vector3 eyes, float yaw)
        {
            bodyEyes = eyes;
            bodyEyesYaw = yaw;
        }

        /// <summary>The soul is back: the body goes, the worn pieces show again.</summary>
        public void HideBody()
        {
            if (!bodyShown) return;
            bodyShown = false;
            foreach (BodyPart part in body) part.transform.gameObject.SetActive(false);
            if (bareSword != null && !SwordDrawn) bareSword.gameObject.SetActive(false);
            foreach (Piece p in pieces) ShowPiece(p, true);
        }

        Vector3 AnchorPosition(Vector3 eyes, Quaternion facing) =>
            eyes - Vector3.up * chestBelowEyes - facing * Vector3.forward * chestBehindEyes;

        // The body's pose: at its anchor, facing its way, arched back (and trembling) while the suit wraps it.
        Quaternion BodyRotation => BodyFacing * Quaternion.Euler(-14f * arch + tremble.x, tremble.y, tremble.z);
        float arch;
        Vector3 tremble;

        void PlacePart(BodyPart part) => part.transform.SetPositionAndRotation(AnchorPosition(bodyEyes, BodyFacing), BodyRotation);

        Vector3 BodyPoint(Vector3 local) => AnchorPosition(bodyEyes, BodyFacing) + BodyRotation * local;

        void Dress(BodyPart part, bool glowing)
        {
            for (int i = 0; i < part.renderers.Length; i++)
            {
                if (!glowing)
                {
                    part.renderers[i].sharedMaterials = part.cloth[i];
                    continue;
                }
                var glow = new Material[part.cloth[i].Length];
                for (int k = 0; k < glow.Length; k++) glow[k] = forming;
                part.renderers[i].sharedMaterials = glow;
            }
            foreach (Renderer r in part.renderers)
                r.shadowCastingMode = glowing ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
        }

        // A part of the outfit forms on the body: the boy's own clothes there scatter into sparks, and the new part
        // flares with light and settles into cloth.
        IEnumerator FormOnBody(string step)
        {
            foreach (BodyPart part in body)
                if (!part.isOutfit && part.step == step && part.shown)
                {
                    Sparkle(part.renderers, 90);
                    part.shown = false;
                    part.transform.gameObject.SetActive(false);
                }
            if (step == "Sword" && bareSword != null) yield return SwordAppears();
            var formed = new List<BodyPart>();
            foreach (BodyPart part in body)
                if (part.isOutfit && part.step == step && !part.shown && bodyShown)
                {
                    part.shown = true;
                    part.transform.gameObject.SetActive(true);
                    PlacePart(part);
                    Dress(part, true);
                    Sparkle(part.renderers, 140);
                    formed.Add(part);
                }
            yield return new WaitForSeconds(formingGlow);
            foreach (BodyPart part in formed)
                if (bodyShown)
                {
                    Dress(part, false);
                    Sparkle(part.renderers, 60);
                }
        }

        // The sword forms out of light in front of the body's chest, point down, turning slowly, then glides into the
        // scabbard at the left hip.
        /// <summary>Where the sword comes from at its step, if it stands somewhere in the world: it hides, and the bare
        /// sword tears itself out of there and flies to the body. Null (the temple): it forms out of light with the suit.</summary>
        public Renderer SwordSource { get; set; }

        // The katana pulls itself out of the stone, straight up, trembling, with a ring of steel and sparks; then it
        // flies (eased, arcing) to hang before the body's chest.
        IEnumerator SwordFromStone(Renderer stone)
        {
            Bounds b = stone.bounds;
            Vector3 pommel = new Vector3(b.center.x, b.max.y, b.center.z);
            Vector3 grip = pommel + Vector3.down * SwordGripToGuard;
            Quaternion upright = Quaternion.LookRotation(BodyFacing * Vector3.right, Vector3.down); // point down, as it stood
            stone.enabled = false;
            bareSword.SetPositionAndRotation(grip, upright);
            Combat.CombatAudio.Play(Combat.CombatAudio.Clang, grip, 0.7f, 0f);
            if (sparks != null) StoryFx.Burst(sparks, grip + Vector3.down * 0.5f, 60, 0.8f, 0.02f, 0.8f, sparkColor);
            const float pull = 1.4f, fly = 1.6f;
            for (float t = 0f; t < pull; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / pull);
                Vector3 shake = Random.insideUnitSphere * 0.004f * (1f - k);
                bareSword.SetPositionAndRotation(grip + Vector3.up * 0.95f * k + shake, upright);
                if (sparks != null && k < 0.7f && Random.value < 0.6f)
                    StoryFx.Emit(sparks, grip + Vector3.down * (0.55f - 0.95f * k), Random.insideUnitSphere * 0.4f, Random.Range(0.01f, 0.03f), 0.6f, sparkColor);
                yield return null;
            }
            Combat.CombatAudio.Play(Combat.CombatAudio.Swoosh, bareSword.position, 0.5f, 0.1f);
            Vector3 from = bareSword.position;
            for (float t = 0f; t < fly && bodyShown; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / fly);
                Quaternion facing = BodyFacing;
                Vector3 chest = AnchorPosition(bodyEyes, facing) + facing * Vector3.forward * 0.55f + Vector3.up * 0.1f;
                bareSword.SetPositionAndRotation(Vector3.Lerp(from, chest, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.5f,
                                                 Quaternion.Slerp(upright, Quaternion.LookRotation(facing * Vector3.right, Vector3.down), k));
                if (sparks != null && Random.value < 0.5f)
                    StoryFx.Emit(sparks, bareSword.position + bareSword.up * Random.Range(0f, 0.8f), Random.insideUnitSphere * 0.1f, 0.015f, 0.5f, sparkColor);
                yield return null;
            }
        }

        IEnumerator SwordAppears()
        {
            bareSword.gameObject.SetActive(true);
            Vector3 size = bareSword.localScale;
            Renderer stone = SwordSource;
            SwordSource = null;
            if (stone != null) yield return SwordFromStone(stone);
            float turnTime = stone != null ? 1.6f : 3f;
            const float glideTime = 1.2f;
            for (float t = 0f; t < turnTime && bodyShown; t += Time.deltaTime)
            {
                float k = t / turnTime;
                Quaternion facing = BodyFacing;
                Vector3 chest = AnchorPosition(bodyEyes, facing);
                // The grip at chest height, the blade hanging below it, turning one and a half times as it slows.
                Vector3 grip = chest + facing * Vector3.forward * 0.55f + Vector3.up * (0.1f + 0.04f * Mathf.Sin(t * 2.2f));
                Quaternion spin = Quaternion.AngleAxis(540f * k * (2f - k), Vector3.up);
                bareSword.SetPositionAndRotation(grip, spin * Quaternion.LookRotation(facing * Vector3.right, Vector3.down));
                bareSword.localScale = stone != null ? size : size * EaseOutBack(Mathf.Min(1f, k * 2.5f)); // (out of light: it grows)
                if (sparks != null && Random.value < 0.8f)
                    StoryFx.Emit(sparks, grip + bareSword.up * Random.Range(0f, 0.85f), Random.insideUnitSphere * 0.15f,
                                 Random.Range(0.01f, 0.03f), 0.7f, sparkColor);
                yield return null;
            }
            // Into the sheath: up behind the right shoulder, turning to hang down the back as it does there.
            Vector3 from = bareSword.position;
            Quaternion fromRotation = bareSword.rotation;
            bareSword.localScale = size;
            for (float t = 0f; t < glideTime && bodyShown; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / glideTime);
                Sheathed(AnchorPosition(bodyEyes, BodyFacing), BodyFacing, out Vector3 back, out Quaternion hang);
                // Over the shoulder: an arc rising above the straight line, so it doesn't pass through the body.
                Vector3 path = Vector3.Lerp(from, back, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.35f;
                bareSword.SetPositionAndRotation(path, Quaternion.Slerp(fromRotation, hang, k));
                yield return null;
            }
            bareSword.gameObject.SetActive(false);
        }

        void Sparkle(Renderer[] renderers, int count)
        {
            if (sparks == null || renderers.Length == 0) return;
            foreach (Renderer r in renderers)
            {
                Bounds b = r.bounds;
                for (int i = 0; i < count / renderers.Length; i++)
                {
                    var p = new Vector3(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y, b.max.y), Random.Range(b.min.z, b.max.z));
                    StoryFx.Emit(sparks, p, Random.insideUnitSphere * 0.3f + Vector3.up * 0.2f, Random.Range(0.015f, 0.04f), Random.Range(0.6f, 1.2f), sparkColor);
                }
            }
        }

        // ---------------------------------------------------------------- the suit-up

        // Where the wave starts: the middle of the chest (anchor space, the same frame as the meshes).
        static readonly Vector3 Core = new Vector3(0f, -0.06f, -0.13f);

        // The paths the light ribbons wind down (anchor space, from build_weaver.py's figure): each arm to the
        // fingertips, each leg to the ankle. Unity -x is his left.
        static readonly Vector3[][] Limbs =
        {
            new[] { Core, new Vector3(-0.221f, 0.031f, -0.158f), new Vector3(-0.366f, -0.105f, -0.158f), new Vector3(-0.477f, -0.233f, -0.107f), new Vector3(-0.57f, -0.403f, -0.056f) },
            new[] { Core, new Vector3(0.221f, 0.031f, -0.158f), new Vector3(0.366f, -0.105f, -0.158f), new Vector3(0.477f, -0.233f, -0.107f), new Vector3(0.57f, -0.403f, -0.056f) },
            new[] { Core, new Vector3(-0.119f, -0.42f, -0.133f), new Vector3(-0.125f, -0.78f, -0.12f), new Vector3(-0.136f, -1.135f, -0.133f) },
            new[] { Core, new Vector3(0.119f, -0.42f, -0.133f), new Vector3(0.125f, -0.78f, -0.12f), new Vector3(0.136f, -1.135f, -0.133f) },
        };
        const float HoodStart = 1.3f, HoodEnd = 1.72f; // the hood wraps after everything else, from the neck up

        // When the wave reaches a point of a part that forms at `step` (distance from the chest; the hood and the
        // head by height, last; the long back of the coat a little behind the rest).
        static float WaveKey(Vector3 p, string step)
        {
            if (step == "Scarf") return HoodStart + Mathf.Max(0f, p.y - 0.12f) * 1.2f;
            float d = (p - Core).magnitude;
            return step == "Cape" ? 0.45f + d * 0.8f : d;
        }

        // A part's triangles in the order the wave reaches them, shown (the suit) or hidden (his clothes, eaten
        // away) up to the wave front, by trimming a copy of its mesh each frame.
        class Reveal
        {
            public Mesh mesh;
            public Transform transform;
            public List<int>[] triangles;
            public float[][] keys;
            public Vector3[][] centres;
            public int[] shown;
            public bool eat;
        }

        Reveal[] BuildReveals(BodyPart part, string step, bool eat)
        {
            var result = new List<Reveal>();
            foreach (Renderer r in part.renderers)
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Mesh mesh = Instantiate(filter.sharedMesh); // (never the prefab's own)
                filter.sharedMesh = mesh;
                Matrix4x4 toAnchor = part.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Vector3[] vertices = mesh.vertices;
                var reveal = new Reveal
                {
                    mesh = mesh, transform = filter.transform, eat = eat,
                    triangles = new List<int>[mesh.subMeshCount], keys = new float[mesh.subMeshCount][],
                    centres = new Vector3[mesh.subMeshCount][], shown = new int[mesh.subMeshCount],
                };
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] tris = mesh.GetTriangles(sub);
                    int n = tris.Length / 3;
                    var order = new int[n];
                    var key = new float[n];
                    var centre = new Vector3[n];
                    for (int i = 0; i < n; i++)
                    {
                        order[i] = i;
                        centre[i] = (vertices[tris[i * 3]] + vertices[tris[i * 3 + 1]] + vertices[tris[i * 3 + 2]]) / 3f;
                        key[i] = WaveKey(toAnchor.MultiplyPoint3x4(centre[i]), step);
                    }
                    Array.Sort(order, (a, b) => eat ? key[b].CompareTo(key[a]) : key[a].CompareTo(key[b]));
                    var sorted = new List<int>(tris.Length);
                    reveal.keys[sub] = new float[n];
                    reveal.centres[sub] = new Vector3[n];
                    for (int i = 0; i < n; i++)
                    {
                        int t = order[i];
                        sorted.Add(tris[t * 3]);
                        sorted.Add(tris[t * 3 + 1]);
                        sorted.Add(tris[t * 3 + 2]);
                        reveal.keys[sub][i] = key[t];
                        reveal.centres[sub][i] = centre[t];
                    }
                    reveal.triangles[sub] = sorted;
                    reveal.shown[sub] = eat ? n : -1;
                }
                result.Add(reveal);
            }
            return result.ToArray();
        }

        // Shows (or, eating, keeps) the triangles the wave has reached; light sparks where the suit has just formed,
        // a little dust where his own clothes have just gone.
        void ApplyReveal(Reveal reveal, float front)
        {
            for (int sub = 0; sub < reveal.triangles.Length; sub++)
            {
                float[] keys = reveal.keys[sub];
                int count = 0;
                if (reveal.eat) while (count < keys.Length && keys[count] > front) count++;
                else while (count < keys.Length && keys[count] < front) count++;
                int before = reveal.shown[sub];
                if (count == before) continue;
                reveal.mesh.SetTriangles(reveal.triangles[sub], 0, count * 3, sub, false);
                reveal.shown[sub] = count;
                if (sparks == null || before < 0) continue;
                int from = Mathf.Min(before, count), to = Mathf.Max(before, count);
                for (int k = 0; k < 3 && to > from; k++)
                {
                    Vector3 at = reveal.transform.TransformPoint(reveal.centres[sub][Random.Range(from, to)]);
                    if (reveal.eat)
                        StoryFx.Emit(sparks, at, Random.insideUnitSphere * 0.15f, Random.Range(0.01f, 0.02f), 0.5f, new Color(0.8f, 0.85f, 1f, 0.5f));
                    else
                        StoryFx.Emit(sparks, at, Random.insideUnitSphere * 0.08f, Random.Range(0.012f, 0.03f), Random.Range(0.4f, 0.8f), sparkColor);
                }
            }
        }

        /// <summary>
        /// The suit-up (while the body is shown): every part up to the sword wraps onto the body in one wave over
        /// `seconds`. It spreads from the chest over the arms and legs to the gloves and boots, eating his own
        /// clothes, with ribbons of light spiralling down each limb just ahead of it; the body arches back and
        /// trembles; the hood wraps up over his head last, and the mask's eyes light. Without the body shown, the
        /// parts simply form one after another.
        /// </summary>
        public IEnumerator SuitUp(float seconds = 9f)
        {
            LoadNow();
            BeginWearing();
            int swordStep = Array.IndexOf(Steps, "Sword");
            if (!bodyShown)
            {
                while (nextStep < swordStep && EquipNext(seconds / swordStep)) yield return new WaitForSeconds(seconds / swordStep);
                yield break;
            }
            var forming = new HashSet<string>();
            for (int i = nextStep; i < swordStep; i++) forming.Add(Steps[i]);
            Debug.Log($"[Outfit] Suit-up: {string.Join(", ", forming)} wrap on together ({AppearanceName}).");

            var reveals = new List<Reveal>();
            var eaten = new List<BodyPart>();
            foreach (BodyPart part in body)
            {
                if (part.step == null || !forming.Contains(part.step)) continue;
                if (part.isOutfit)
                {
                    part.shown = true;
                    part.transform.gameObject.SetActive(true);
                    Dress(part, false);
                    PlacePart(part);
                    var r = BuildReveals(part, part.step, false);
                    foreach (Reveal x in r) ApplyReveal(x, -1f);
                    reveals.AddRange(r);
                }
                else if (part.shown)
                {
                    reveals.AddRange(BuildReveals(part, part.step, true));
                    eaten.Add(part);
                }
            }
            // The worn pieces (first person) are on from now; they show once the soul is back.
            for (int i = nextStep; i < swordStep; i++)
                foreach (Piece p in pieces)
                    if (p.transform.name.StartsWith("Outfit_" + Steps[i]))
                    {
                        p.grow = 1f;
                        p.transform.gameObject.SetActive(true);
                        ShowPiece(p, false);
                    }
            nextStep = Mathf.Max(nextStep, swordStep); // (never back: already dressed, the sword would form a second time)

            var ribbons = MakeRibbons();
            var hum = new GameObject("SuitUpHum");
            hum.transform.position = BodyPoint(Core);
            AudioSource tone = StoryFx.CreateSource(hum, StoryFx.Hum(), true, 0.6f);
            tone.Play();
            float nextSwoosh = 0f;
            bool eyesLit = false;
            float noise = Random.value * 10f;

            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float u = t / seconds;
                float front = Mathf.Lerp(-0.05f, HoodEnd + 0.1f, Mathf.SmoothStep(0f, 1f, u));
                // Arched back in the light, a fine tremble running through him (eased in and out: no jolts).
                arch = Mathf.SmoothStep(0f, 1f, u / 0.18f) * (1f - Mathf.SmoothStep(0f, 1f, (u - 0.82f) / 0.18f));
                float shake = 1.4f * arch;
                tremble = new Vector3(Mathf.PerlinNoise(noise, t * 6f) - 0.5f, Mathf.PerlinNoise(noise + 3f, t * 6f) - 0.5f,
                                      Mathf.PerlinNoise(noise + 7f, t * 6f) - 0.5f) * 2f * shake;
                foreach (BodyPart part in body)
                    if (part.shown) PlacePart(part);
                foreach (Reveal r in reveals) ApplyReveal(r, front);
                MoveRibbons(ribbons, front, t);

                tone.volume = 0.25f * Mathf.Sin(u * Mathf.PI);
                tone.pitch = Mathf.Lerp(0.9f, 1.5f, u);
                if (t > nextSwoosh && front < HoodEnd)
                {
                    Combat.CombatAudio.Play(Combat.CombatAudio.Swoosh, BodyPoint(Core), 0.25f, 0.4f);
                    nextSwoosh = t + Random.Range(0.35f, 0.7f);
                }
                if (!eyesLit && front > HoodEnd - 0.02f)
                {
                    eyesLit = true;
                    LightEyes();
                }
                yield return null;
            }
            arch = 0f;
            tremble = Vector3.zero;
            foreach (Reveal r in reveals) ApplyReveal(r, float.MaxValue);
            foreach (BodyPart part in eaten)
            {
                part.shown = false;
                part.transform.gameObject.SetActive(false);
            }
            foreach (TrailRenderer ribbon in ribbons)
                if (ribbon != null)
                {
                    ribbon.emitting = false;
                    Destroy(ribbon.gameObject, ribbon.time + 0.1f);
                }
            if (!eyesLit) LightEyes();
            Destroy(hum);
            if (IsComplete) carriedDressed = true;
        }

        // Two ribbons of light per limb (opposite each other round it) and two for the hood.
        List<TrailRenderer> MakeRibbons()
        {
            var list = new List<TrailRenderer>();
            Material glow = SparkHands.CreateGlowMaterial(SparkHands.CreateSoftLine(64));
            for (int i = 0; i < (Limbs.Length + 1) * 2; i++)
            {
                var go = new GameObject("SuitRibbon");
                go.transform.position = BodyPoint(Core);
                var trail = go.AddComponent<TrailRenderer>();
                trail.sharedMaterial = glow;
                trail.time = 0.7f;
                trail.minVertexDistance = 0.01f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.035f), new Keyframe(1f, 0f));
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(sparkColor, 1f) },
                          new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = g;
                trail.emitting = false;
                list.Add(trail);
            }
            return list;
        }

        // Each ribbon rides the wave front down its limb, winding round it (wide round the chest, close round the
        // wrist and ankle); the last two wind up round the head with the hood.
        void MoveRibbons(List<TrailRenderer> ribbons, float front, float time)
        {
            for (int i = 0; i < ribbons.Count; i++)
            {
                TrailRenderer ribbon = ribbons[i];
                if (ribbon == null) continue;
                int limb = i / 2;
                float phase = (i % 2) * Mathf.PI;
                Vector3 centre, along;
                float radius, travelled;
                bool active;
                if (limb < Limbs.Length)
                {
                    bool onLimb = PointAtDistance(Limbs[limb], front, out centre, out along, out travelled);
                    active = front > 0.02f && onLimb;
                    radius = Mathf.Lerp(0.2f, limb < 2 ? 0.06f : 0.085f, Mathf.Clamp01(travelled / 0.45f));
                }
                else
                {
                    float y = 0.12f + (front - HoodStart) / 1.2f;
                    active = front > HoodStart && front < HoodEnd + 0.02f;
                    centre = new Vector3(0f, Mathf.Min(y, 0.42f), -0.135f);
                    along = Vector3.up;
                    travelled = y;
                    radius = Mathf.Lerp(0.13f, 0.06f, Mathf.Clamp01((y - 0.12f) / 0.3f));
                }
                if (!active)
                {
                    ribbon.emitting = false;
                    continue;
                }
                Vector3 n1 = Vector3.Cross(along, Mathf.Abs(along.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
                Vector3 n2 = Vector3.Cross(along, n1);
                float angle = phase + travelled * 26f + time * 2f;
                Vector3 local = centre + (n1 * Mathf.Cos(angle) + n2 * Mathf.Sin(angle)) * radius;
                ribbon.transform.position = BodyPoint(local);
                ribbon.emitting = true;
            }
        }

        // The point on a path (from the chest out) as far from the chest as `distance`; false once past its end.
        static bool PointAtDistance(Vector3[] path, float distance, out Vector3 point, out Vector3 along, out float travelled)
        {
            travelled = 0f;
            for (int i = 0; i < path.Length - 1; i++)
            {
                Vector3 a = path[i], b = path[i + 1];
                float da = (a - Core).magnitude, db = (b - Core).magnitude;
                if (distance <= db || i == path.Length - 2)
                {
                    float k = Mathf.Clamp01(Mathf.InverseLerp(da, db, distance));
                    point = Vector3.Lerp(a, b, k);
                    along = (b - a).normalized;
                    travelled += (b - a).magnitude * k;
                    return distance <= db + 0.05f;
                }
                travelled += (b - a).magnitude;
            }
            point = path[path.Length - 1];
            along = Vector3.down;
            return false;
        }

        // The mask's eyes light up white (a soft flare, then a steady glow), on the hood so they go where it goes.
        void LightEyes()
        {
            BodyPart hood = body.Find(b => b.isOutfit && b.step == "Scarf");
            if (hood == null) return;
            Material glow = AegisShield.CreateFaceMaterial(new Color(0.92f, 0.96f, 1f, 1f));
            glow.name = "WeaverEyes";
            foreach (float x in new[] { -0.034f, 0.034f })
            {
                var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(eye.GetComponent<Collider>());
                eye.name = "WeaverEye";
                eye.transform.SetParent(hood.transform, false);
                eye.transform.localPosition = new Vector3(x, 0.325f, 0.012f);
                eye.GetComponent<Renderer>().sharedMaterial = glow;
                eye.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                StartCoroutine(Flare(eye.transform));
            }
            var light = new GameObject("EyeLight").AddComponent<Light>();
            light.transform.SetParent(hood.transform, false);
            light.transform.localPosition = new Vector3(0f, 0.325f, 0.06f);
            light.color = new Color(0.85f, 0.92f, 1f);
            light.range = 0.6f;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;
            if (sparks != null) StoryFx.Burst(sparks, hood.transform.TransformPoint(new Vector3(0f, 0.325f, 0.03f)), 30, 0.4f, 0.01f, 0.5f, Color.white);
        }

        static IEnumerator Flare(Transform eye)
        {
            for (float t = 0f; t < 0.8f && eye != null; t += Time.deltaTime)
            {
                float k = t / 0.8f;
                eye.localScale = Vector3.one * Mathf.Lerp(0.004f, 0.016f, Mathf.Sin(Mathf.Min(1f, k * 1.6f) * Mathf.PI * 0.5f)) * (1f + 0.6f * Mathf.Sin(k * Mathf.PI));
                yield return null;
            }
            if (eye != null) eye.localScale = Vector3.one * 0.016f;
        }

        // ---------------------------------------------------------------- forming the outfit

        /// <summary>Forms the next part of the outfit over `seconds`. Returns false when everything is already on.</summary>
        public bool EquipNext(float seconds = 1.5f)
        {
            if (IsComplete) return false;
            LoadNow();
            BeginWearing();
            string step = Steps[nextStep++];
            if (IsComplete) carriedDressed = true;
            Debug.Log($"[Outfit] {step} forms ({nextStep}/{Steps.Length}, {AppearanceName}).");
            if (bodyShown) StartCoroutine(FormOnBody(step));
            foreach (Piece p in pieces)
                if (p.transform.name.StartsWith("Outfit_" + step) && !p.transform.gameObject.activeSelf) StartCoroutine(Grow(p, seconds));
            return true;
        }

        /// <summary>Forms all the parts not on yet, one after another, over about `seconds`.</summary>
        public void Equip(float seconds = 2.5f) => StartCoroutine(EquipRest(seconds));

        IEnumerator EquipRest(float seconds)
        {
            float each = seconds / Steps.Length;
            while (EquipNext(each * 2f)) yield return new WaitForSeconds(each);
        }

        IEnumerator Grow(Piece p, float seconds)
        {
            p.grow = 0f;
            p.transform.gameObject.SetActive(true);
            ShowPiece(p, !bodyShown); // out of the body: they show once the soul is back
            Place(p);
            if (sparks != null && !bodyShown) StoryFx.Burst(sparks, Center(p), 40, 0.8f, 0.025f, 0.9f, sparkColor);
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                p.grow = t / seconds;
                if (sparks != null && !bodyShown && Random.value < 0.6f)
                    StoryFx.Emit(sparks, Center(p) + Random.insideUnitSphere * (p.anchor == Anchor.Body ? 0.35f : 0.12f), Random.insideUnitSphere * 0.2f,
                                 Random.Range(0.01f, 0.03f), 0.6f, sparkColor);
                yield return null;
            }
            p.grow = 1f;
        }

        static Vector3 Center(Piece p)
        {
            foreach (Renderer r in p.renderers)
                if (r.enabled) return r.bounds.center;
            return p.transform.position;
        }

        void LateUpdate()
        {
            if (bodyShown)
                foreach (BodyPart part in body)
                    if (part.shown) PlacePart(part);
            if (!IsWorn) return;
            if (head == null) head = StoryFx.Head(input);
            if (head == null) return;
            float yaw = head.eulerAngles.y;
            bodyYaw = Mathf.LerpAngle(bodyYaw, yaw, 1f - Mathf.Exp(-bodyTurn * Time.deltaTime)); // (the same at 72 or 90 fps)
            foreach (Piece p in pieces)
                if (p.transform.gameObject.activeSelf) Place(p);
        }

        void Place(Piece p)
        {
            if (head == null) return;
            // A little overshoot as each piece snaps into shape.
            float k = p.grow >= 1f ? 1f : EaseOutBack(p.grow);
            p.transform.localScale = p.size * k;

            if (p.anchor == Anchor.Body)
            {
                Quaternion facing = Quaternion.Euler(0f, bodyYaw, 0f);
                p.transform.SetPositionAndRotation(AnchorPosition(head.position, facing), facing);
                return;
            }

            HandFrame f = input != null ? input.Get(p.side) : null;
            Vector3 wrist, forward, back;
            if (f != null && f.IsTracked)
            {
                wrist = f.Wrist;
                forward = f.HandForward;
                back = -f.PalmNormal;
            }
            else if (SwordDrawn && p.side == HandSide.Right && Time.frameCount - heldFrame <= 1)
            {
                Vector3 blade = heldRotation * Vector3.up, palm = -(heldRotation * Vector3.forward);
                forward = Vector3.Cross(blade, palm).normalized; // knuckles towards the edge
                back = -palm;
                wrist = heldGrip - forward * 0.08f;
            }
            else
            {
                // No tracked hand: rest it low in front, palm down, so the piece still shows.
                Vector3 fwd = StoryFx.Flat(head.forward), right = Vector3.Cross(Vector3.up, fwd);
                forward = (fwd - Vector3.up * 0.35f).normalized;
                back = Vector3.Cross(forward, right).normalized;
                if (Vector3.Dot(back, Vector3.up) < 0f) back = -back;
                wrist = head.position + fwd * 0.3f - Vector3.up * 0.42f + right * (p.side == HandSide.Right ? 0.17f : -0.17f);
            }
            p.transform.SetPositionAndRotation(wrist, Quaternion.LookRotation(forward, back));
        }

        static float EaseOutBack(float t)
        {
            const float c = 1.7f;
            t -= 1f;
            return 1f + (c + 1f) * t * t * t + c * t * t;
        }
    }
}
