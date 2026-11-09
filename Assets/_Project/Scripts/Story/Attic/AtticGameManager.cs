using System;
using System.Collections;
using ArcaneStrike.HandInput;
using ArcaneStrike.Props;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Runs the attic opening in story order:
    /// open the chest → find the lantern → pick it up → clean it (dust, soft hum, golden glow, runes, louder hum)
    /// → finger cut → blood touches the lantern → <see cref="LanternAwakening"/> (blue burst, whispers,
    /// books float, the attic shakes, the portal opens) → the player steps through.
    /// Each step only listens for the player once the previous one is done. Put this on the attic set's root:
    /// the set is laid out with the player at its origin facing +Z, and is moved in front of the player at start.
    /// </summary>
    public class AtticGameManager : MonoBehaviour
    {
        public enum Beat { ChestClosed, LanternFound, LanternHeld, Cleaning, Cleaned, FingerCut, Awakening, PortalOpen, Done }

        [SerializeField] HandInputService input;
        public ChestOpen chest;
        [SerializeField] LanternPickup pickup;
        [SerializeField] LanternCleaning cleaning;
        [SerializeField] FingerCutEvent fingerCut;
        public LanternAwakening awakening;
        [SerializeField] PortalController portal;

        public enum Placement { Auto, RoomScale, Desk }

        [Tooltip("Several boxes to open (AtticBoxes): the lantern turns up in the third or fourth one opened.")]
        [SerializeField] bool severalBoxes = true;
        AtticBoxes boxes;

        [Header("Placement")]
        [Tooltip("Move the set in front of the player at start.")]
        [SerializeField] bool placeInFrontOfPlayer = true;
        [Tooltip("RoomScale: the set stands on the real floor (headset). Desk: the chest is raised to just below the eyes " +
                 "and pulled close, so a seated webcam tester can see it and reach it. " +
                 "Auto: Desk when no headset is running or the hands come from the webcam bridge.")]
        [SerializeField] Placement placement = Placement.Auto;
        [Tooltip("Desk: how far below the eyes the set's floor goes (m). 0.6 puts the chest lid just below eye level.")]
        [SerializeField] float deskFloorBelowEyes = 0.6f;
        [Tooltip("Desk: distance from the eyes to the chest (m), within reach of webcam hands.")]
        [SerializeField] float deskChestDistance = 0.6f;
        [Tooltip("RoomScale without room data: assumed eye height above the floor (m).")]
        [SerializeField] float standingEyeHeight = 1.6f;
        [Tooltip("Wait this long for the headset pose (and the room) before placing the set.")]
        [SerializeField] float placeDelay = 0.75f;

        [Header("Debug")]
        [Tooltip("Keyboard (GameInput): E or N does the current step for you (open, pick up, clean, cut, skip ahead, enter). " +
                 "H places the set in front of you again.")]
        [SerializeField] bool keyboardFallback = true;

        [Tooltip("Fired on every new beat with its name: hook narration, Ember's lines or subtitles here.")]
        [SerializeField] UnityEvent<string> onBeat;

        public Beat Current { get; private set; } = Beat.ChestClosed;
        /// <summary>Raised on every new beat (StoryAnimations plays the artist's clips from this).</summary>
        public event Action<Beat> OnBeatChanged;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
        }

        void OnEnable()
        {
            if (pickup != null) pickup.OnPickedUp += HandlePickedUp;
            if (cleaning != null)
            {
                cleaning.OnStarted += HandleCleaningStarted;
                cleaning.OnCleaned += HandleCleaned;
            }
            if (fingerCut != null)
            {
                fingerCut.OnCut += HandleCut;
                fingerCut.OnBloodTouched += HandleBloodTouched;
            }
            if (awakening != null)
            {
                awakening.OnPortalOpened += HandlePortalOpened;
                awakening.OnPulledIn += HandlePulledIn;
            }
            if (portal != null) portal.OnEntered += HandleEntered;
        }

        void OnDisable()
        {
            if (pickup != null) pickup.OnPickedUp -= HandlePickedUp;
            if (cleaning != null)
            {
                cleaning.OnStarted -= HandleCleaningStarted;
                cleaning.OnCleaned -= HandleCleaned;
            }
            if (fingerCut != null)
            {
                fingerCut.OnCut -= HandleCut;
                fingerCut.OnBloodTouched -= HandleBloodTouched;
            }
            if (awakening != null)
            {
                awakening.OnPortalOpened -= HandlePortalOpened;
                awakening.OnPulledIn -= HandlePulledIn;
            }
            if (portal != null) portal.OnEntered -= HandleEntered;
        }

        IEnumerator Start()
        {
            if (severalBoxes && chest != null && pickup != null)
            {
                boxes = gameObject.AddComponent<AtticBoxes>();
                var cloth = FindAnyObjectByType<CleaningCloth>();
                boxes.Setup(chest, pickup.transform, cloth != null ? cloth.transform : null);
            }
            SetBeat(Beat.ChestClosed);
            if (!placeInFrontOfPlayer) yield break;

            yield return new WaitForSeconds(placeDelay);
            // Give MRUK a moment to load the room, so the set can sit on the real floor.
            MRUK mruk = MRUK.Instance;
            for (float t = 0f; mruk != null && mruk.isActiveAndEnabled && !mruk.IsInitialized && t < 3f; t += Time.deltaTime)
                yield return null;
            PlaceInFrontOfPlayer(mruk);
        }

        public void PlaceInFrontOfPlayer() => PlaceInFrontOfPlayer(MRUK.Instance);

        void PlaceInFrontOfPlayer(MRUK mruk)
        {
            Transform head = StoryFx.Head(input);
            if (head == null) return;

            bool desk = placement == Placement.Desk ||
                        (placement == Placement.Auto && (!XRSettings.isDeviceActive || (input != null && input.ActiveSource is WebcamHandSource)));
            Vector3 forward = StoryFx.Flat(head.forward);
            float floorY;
            float pullBack = 0f;
            string how;

            MRUKRoom room = mruk != null && mruk.isActiveAndEnabled && mruk.IsInitialized ? mruk.GetCurrentRoom() : null;
            if (desk)
            {
                floorY = head.position.y - deskFloorBelowEyes;
                if (chest != null) pullBack = chest.transform.localPosition.z - deskChestDistance;
                how = "desk mode (chest raised and within reach)";
            }
            else if (room != null && room.FloorAnchors.Count > 0)
            {
                floorY = room.FloorAnchors[0].transform.position.y;
                how = "on the room's floor";
            }
            else
            {
                // Floor-level tracking puts the floor at the tracking space; fall back to an average eye height.
                Transform space = head.parent;
                float aboveSpace = space != null ? head.position.y - space.position.y : 0f;
                floorY = aboveSpace > 0.8f && aboveSpace < 2.2f ? space.position.y : head.position.y - standingEyeHeight;
                how = "on the estimated floor";
            }

            Vector3 origin = new Vector3(head.position.x, floorY, head.position.z) - forward * pullBack;
            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(forward));
            Debug.Log($"[Attic] Set placed in front of the player, {how} (floor y = {floorY:F2}). H places it again.\n" +
                      $"Eyes at {transform.InverseTransformPoint(head.position):F2} in the set, looking {transform.InverseTransformDirection(head.forward):F2}; " +
                      $"camera {(Camera.main != null ? Camera.main.name : "none")} at {(Camera.main != null ? transform.InverseTransformPoint(Camera.main.transform.position).ToString("F2") : "-")}; " +
                      $"straight ahead: {WhatIsAhead(head)}");
        }

        // Debug: the nearest visible thing along the view, to tell "looking at a wall" from "the set is somewhere else".
        static string WhatIsAhead(Transform head)
        {
            var ray = new Ray(head.position, head.forward);
            Renderer best = null;
            float bestDistance = float.MaxValue;
            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                if (r.bounds.Contains(head.position)) continue; // the room around us, not what we look at
                if (r.bounds.IntersectRay(ray, out float d) && d < bestDistance)
                {
                    best = r;
                    bestDistance = d;
                }
            }
            return best != null ? $"{best.name} ({bestDistance:F2} m)" : "nothing";
        }

        void Update()
        {
            if (Current == Beat.ChestClosed)
            {
                ChestOpen found = boxes != null ? boxes.LanternBox : chest; // (with several boxes, only the lantern's counts)
                if (found != null && found.IsOpen)
                {
                    chest = found;
                    HandleChestOpened();
                }
            }

            if (!keyboardFallback) return;
            if (!introSearched)
            {
                // Once: a scene without the intro must not search the whole scene every frame.
                intro = FindAnyObjectByType<AtticIntro>();
                introSearched = true;
            }
            if (intro != null && intro.IsPlaying) return; // E / N skip the intro walk itself
            // E does the step you're on (open the chest, take the lantern, clean it, touch the edge, enter the rift);
            // N does it too (and skips Ember's lines).
            if (GameInput.Pressed(GameAction.Interact) || GameInput.Pressed(GameAction.Skip)) DoCurrentStep();
            // H re-places a movable set; the house version stands where it was built, so H does nothing there.
            if (placeInFrontOfPlayer && GameInput.Pressed(GameAction.ReplaceSet)) PlaceInFrontOfPlayer();
        }

        AtticIntro intro;
        bool introSearched;

        /// <summary>Debug: performs the step the player is currently expected to do.</summary>
        public void DoCurrentStep()
        {
            switch (Current)
            {
                case Beat.ChestClosed:
                    if (boxes != null) boxes.OpenNearest();
                    else if (chest != null) chest.OpenChest();
                    break;
                case Beat.LanternFound: if (pickup != null) pickup.PickUp(HandSide.Right); break;
                case Beat.LanternHeld:
                case Beat.Cleaning:
                    // The other hand takes the cloth and rubs the lantern clean (N again finishes it at once).
                    // (A cloth already in the player's hand can't be played out: finish the cleaning instead, or N would do nothing.)
                    var cloth = FindAnyObjectByType<CleaningCloth>();
                    bool played = cloth != null && !cloth.IsAutoCleaning && Current == Beat.LanternHeld && cloth.AutoClean();
                    if (!played && cleaning != null) cleaning.CompleteNow();
                    break;
                case Beat.Cleaned: if (fingerCut != null) fingerCut.Cut(HandSide.Right); break;
                case Beat.FingerCut:
                case Beat.Awakening: if (awakening != null) awakening.Skip(); break;
                case Beat.PortalOpen: if (portal != null) portal.Enter(); break;
                case Beat.Done:
                    if (RiftGate.Current != null) RiftGate.Current.Enter();
                    else if (portal != null && portal.IsOpen) portal.Enter();
                    break;
            }
        }

        // ---------------------------------------------------------------- story

        void HandleChestOpened()
        {
            SetBeat(Beat.LanternFound);
            if (pickup != null) pickup.Reveal();
        }

        void HandlePickedUp()
        {
            if (Current == Beat.ChestClosed) HandleChestOpened(); // picked up via debug before the chest check ran
            SetBeat(Beat.LanternHeld);
            if (cleaning != null) cleaning.Begin();
        }

        void HandleCleaningStarted() => SetBeat(Beat.Cleaning);

        void HandleCleaned()
        {
            SetBeat(Beat.Cleaned);
            if (fingerCut != null) fingerCut.Begin();
        }

        void HandleCut() => SetBeat(Beat.FingerCut);

        void HandleBloodTouched()
        {
            SetBeat(Beat.Awakening);
            if (awakening != null) awakening.TriggerAwakening();
        }

        void HandlePortalOpened() => SetBeat(Beat.PortalOpen);

        void HandleEntered() => SetBeat(Beat.Done);

        void HandlePulledIn() => SetBeat(Beat.Done);

        // Kept from the first version, for UnityEvents that call it.
        public void OpenChestSequence()
        {
            if (chest != null) chest.OpenChest();
        }

        public void BloodTouchLantern() => HandleBloodTouched();

        void SetBeat(Beat beat)
        {
            if (beat == Current && beat != Beat.ChestClosed) return;
            Current = beat;
            Debug.Log($"[Attic] ▶ {beat}{Hint(beat)}");
            onBeat?.Invoke(beat.ToString());
            OnBeatChanged?.Invoke(beat);
        }

        static string Hint(Beat beat)
        {
            switch (beat)
            {
                case Beat.ChestClosed: return ": take the cloth from the chest's table, wipe a box clean with it, then lift its lid (E or N wipes, then opens, the nearest). The lantern is in one of them";
                case Beat.LanternFound: return ": close a fist around the lantern's top ring, or point your left index finger at it (N)";
                case Beat.LanternHeld: return ": with your other hand take the old cloth from the chest (a fist on it, or point your left index finger at it) and rub the lantern (N)";
                case Beat.Cleaned: return ": touch the glinting edge with a fingertip (N)";
                case Beat.PortalOpen: return ": walk up to the portal (Enter or N)";
                case Beat.Done: return ": the rift pulls the player through (the Ascension happens in the AscensionCave scene)";
                default: return "";
            }
        }
    }
}
