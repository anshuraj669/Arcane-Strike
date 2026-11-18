using System.Collections;
using System.Collections.Generic;
using ArcaneStrike.HandInput;
using ArcaneStrike.Props;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The attic has several old boxes to open, not one: the original chest and copies of it set about the room (each
    /// its own size and angle), all at table height so nobody has to kneel: the chest on a round table, one on the
    /// dresser, one on a nightstand, one on another table (the tables are copies of the house's own furniture). Each
    /// is thick with dust and has to be wiped clean with the cloth before its lid will lift (<see cref="BoxDust"/>). Grandfather's lantern isn't in a fixed box. Whichever box the player opens third or
    /// fourth (chosen at random each play) is the one that holds it, so it's never the first and differs for every
    /// player. The boxes before it are empty, and after each one another box knocks, as if something inside stirs.
    /// Which box stands where is shuffled each play too, so the big old chest isn't always the one in front. The lantern
    /// waits out of sight until its box is opened. The cloth starts on the front table, in front of whichever box is there.
    /// AtticGameManager adds this when the scene starts and asks <see cref="LanternBox"/> which box to watch.
    /// </summary>
    public class AtticBoxes : MonoBehaviour
    {
        [System.Serializable]
        public class Place
        {
            [Tooltip("What it stands on: a piece of furniture in the scene, by name. Copied here when `copy` is ticked, " +
                     "used where it stands when not.")]
            public string stand;
            public bool copy;
            [Tooltip("Where the box goes, in the attic's own space (the chest's parent); its height comes from the stand.")]
            public Vector3 at;
            public float turn;
            public float size = 1f;
        }

        [Tooltip("Where the boxes go. The chest and its copies are dealt to them in a random order; the cloth lies on the first.")]
        [SerializeField] Place[] places =
        {
            new Place { stand = "Prop_round_wooden_table_02", copy = true, at = new Vector3(0f, 0f, 0.9f), turn = 0f, size = 1f },
            new Place { stand = "Prop_vintage_wooden_drawer_01", copy = false, at = new Vector3(1.66f, 0f, 0.74f), turn = 90f, size = 0.62f },
            new Place { stand = "Prop_ClassicNightstand_01", copy = true, at = new Vector3(-0.9f, 0f, -1.35f), turn = 33f, size = 0.6f },
            new Place { stand = "Prop_round_wooden_table_02", copy = true, at = new Vector3(-0.4f, 0f, 2.3f), turn = -10f, size = 0.85f },
        };
        [Tooltip("The lantern turns up in the box opened at this count: a random number from x to y (never more than the boxes).")]
        [SerializeField] Vector2Int foundOn = new Vector2Int(2, 3);
        [Tooltip("All the boxes side by side in one row where the chest stands (straight ahead on coming in), instead of " +
                 "about the room on the furniture (`places`): easy to see, cleaned and opened one after another.")]
        [SerializeField] bool inARow = true;
        [SerializeField] int rowCount = 3;
        [SerializeField] float rowSize = 0.8f;

        /// <summary>The box the lantern was found in (null until then).</summary>
        public ChestOpen LanternBox { get; private set; }
        /// <summary>How many boxes have been opened.</summary>
        public int Opened { get; private set; }

        readonly List<ChestOpen> boxes = new List<ChestOpen>();
        Transform lantern, cloth;
        Vector3 lanternPosition;
        Quaternion lanternRotation;
        int target;

        /// <summary>Copies `chest` round the room and takes the lantern (and the cloth) out of it until its box is found.</summary>
        public void Setup(ChestOpen chest, Transform lanternTransform, Transform clothTransform)
        {
            lantern = lanternTransform;
            cloth = clothTransform;
            Transform room = chest.transform.parent;

            // The lantern steps out while the chest is copied, so no copy gets a second lantern.
            lanternPosition = chest.transform.InverseTransformPoint(lantern.position);
            lanternRotation = Quaternion.Inverse(chest.transform.rotation) * lantern.rotation;
            lantern.SetParent(room, true);

            if (inARow)
            {
                SetupRow(chest, room);
                return;
            }

            // The chest and its copies, dealt to the places in a random order: the big old chest stands somewhere new
            // every time, not always in front of the player.
            for (int i = 0; i < places.Length; i++)
            {
                ChestOpen box = i == 0 ? chest : Instantiate(chest, room);
                if (i > 0)
                {
                    box.name = $"{chest.name}_{i + 1}";
                    box.Quiet(); // only the first chest knocks (the story's): the others wait their turn
                }
                boxes.Add(box);
            }
            for (int i = boxes.Count - 1; i > 0; i--)
            {
                int k = Random.Range(0, i + 1);
                (boxes[i], boxes[k]) = (boxes[k], boxes[i]);
            }
            for (int i = 0; i < places.Length; i++)
            {
                Place place = places[i];
                ChestOpen box = boxes[i];
                box.transform.localPosition = place.at;
                box.transform.localRotation = Quaternion.Euler(0f, place.turn, 0f);
                box.transform.localScale = Vector3.one * place.size;
                RaiseOnto(box.transform, place, room);
            }
            Finish();
        }

        void Finish()
        {
            foreach (ChestOpen box in boxes) box.gameObject.AddComponent<BoxDust>().Setup(box);

            // The cloth lies on the first table (straight ahead as the player comes in), in front of its box, ready to hand.
            if (cloth != null)
            {
                Transform first = boxes[0].transform;
                cloth.position = new Vector3(0f, first.position.y + 0.03f, 0f) +
                                 Vector3.ProjectOnPlane(first.position + first.rotation * new Vector3(0f, 0f, -0.31f), Vector3.up);
                var body = cloth.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.position = cloth.position;
                    body.linearVelocity = Vector3.zero;
                }
                var grab = cloth.GetComponent<Grabbable>();
                if (grab != null) grab.Rehome();
            }

            foreach (ChestOpen box in boxes)
            {
                box.KeyboardFallback = false; // E / N open the nearest box (AtticGameManager), not every box at once
                box.Opened += HandleOpened;
            }

            // Out of sight (far under the house) until it is found.
            lantern.position = boxes[0].transform.position + Vector3.down * 40f;
            target = Mathf.Clamp(Random.Range(foundOn.x, foundOn.y + 1), 1, boxes.Count);
            Debug.Log($"[Attic] {boxes.Count} boxes to open; the lantern is in whichever is opened number {target}");
        }

        // The row: the chest and copies of it side by side where the chest stands (straight ahead on coming in),
        // fronts to the player, made small enough to fit between the walls on either side: copies that don't fit
        // end up inside a wall, where nobody can see them. Which one holds the lantern is still random (see foundOn).
        void SetupRow(ChestOpen chest, Transform room)
        {
            Transform c = chest.transform;
            Bounds b = new Bounds(c.position, Vector3.one * 0.5f);
            bool any = false;
            foreach (Renderer r in chest.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || r.name == "Dust") continue; // (solid parts only: particles have huge bounds)
                if (!any) b = r.bounds;
                else b.Encapsulate(r.bounds);
                any = true;
            }
            Vector3 across = StoryFx.Flat(c.right);
            float width = Mathf.Clamp(Mathf.Abs(across.x) * b.size.x + Mathf.Abs(across.z) * b.size.z, 0.25f, 1.5f);

            // Room to either side, at half the box's height.
            Vector3 middle = new Vector3(b.center.x, b.center.y, b.center.z);
            float left = Clearance(middle, -across, c), right = Clearance(middle, across, c);
            float span = left + right - 0.12f; // (a hand's width off each wall)
            const float GapFraction = 0.3f;
            float perUnit = width * (rowCount + GapFraction * (rowCount - 1));
            float k = Mathf.Clamp(span / Mathf.Max(perUnit, 0.01f), 0.45f, rowSize);
            float boxWidth = width * k, step = boxWidth * (1f + GapFraction);
            float total = boxWidth * rowCount + boxWidth * GapFraction * (rowCount - 1);
            // Moved sideways only as far as it must be to clear both walls (centred if it can't clear them).
            float lo = total * 0.5f + 0.06f - left, hi = right - total * 0.5f - 0.06f;
            float shift = lo <= hi ? Mathf.Clamp(0f, lo, hi) : (right - left) * 0.5f;
            Debug.Log($"[Attic] Room for the row: {left:F2} m left, {right:F2} m right of the chest; boxes at {k:F2}× the chest, {step:F2} m apart.");

            Vector3 pivot = c.position + across * shift;
            Vector3 scale = c.localScale * k;
            Quaternion facing = c.rotation;
            var spots = new Vector3[rowCount];
            for (int i = 0; i < rowCount; i++)
            {
                spots[i] = pivot + across * ((i - (rowCount - 1) * 0.5f) * step);
                ChestOpen box = i == 0 ? chest : Instantiate(chest, room);
                if (i > 0)
                {
                    box.name = $"{chest.name}_{i + 1}";
                    box.Quiet();
                }
                box.transform.SetPositionAndRotation(spots[i], facing);
                box.transform.localScale = scale;
                boxes.Add(box);
            }
            middle = pivot;
            // The cloth goes in front of the box at the left end, and the boxes are opened left to right.
            boxes.Sort((x, y) => Vector3.Dot(x.transform.position - middle, across).CompareTo(Vector3.Dot(y.transform.position - middle, across)));
            Debug.Log($"[Attic] {rowCount} boxes in a row at {room.InverseTransformPoint(middle):F2} (attic space), out in the open.");
            Finish();
        }

        // How far (m, up to 3) from `from` along `direction` to the nearest wall or furniture (not the chest itself,
        // triggers or loose things).
        static float Clearance(Vector3 from, Vector3 direction, Transform chest)
        {
            float best = 3f;
            foreach (RaycastHit hit in Physics.RaycastAll(from, direction, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(chest) || hit.collider.attachedRigidbody != null) continue;
                if (hit.distance < best) best = hit.distance;
            }
            return best;
        }

        bool acting;

        /// <summary>Keyboard (E / N): the player goes to the closed box nearest them and opens it with their hands:
        /// walks up to it, turns to it, wipes the dust off the lid, takes the front edge in both hands and lifts.</summary>
        public void OpenNearest()
        {
            if (acting) return;
            ChestOpen box = Nearest();
            if (box == null) return;
            StartCoroutine(OpenByHand(box));
        }

        static IEnumerator Walk(PlayerLocomotion walker, Transform head, Vector3 to)
        {
            walker.WalkTo(to, 0.03f);
            for (float t = 0f; t < 6f && walker.WalkingTo; t += Time.deltaTime) yield return null;
            walker.CancelWalkTo();
        }

        // How far in front of the box's middle the player's eyes stand to open it: its half depth plus an arm's reach.
        static float StandOff(ChestOpen box)
        {
            var b = new Bounds(box.transform.position, Vector3.zero);
            foreach (MeshRenderer r in box.GetComponentsInChildren<MeshRenderer>())
                if (r.enabled && r.name != "Dust") b.Encapsulate(r.bounds);
            Vector3 f = StoryFx.Flat(box.transform.forward);
            float halfDepth = 0.5f * (Mathf.Abs(f.x) * b.size.x + Mathf.Abs(f.z) * b.size.z);
            return halfDepth + 0.42f;
        }

        IEnumerator OpenByHand(ChestOpen box)
        {
            acting = true;
            Transform head = Camera.main != null ? Camera.main.transform : null;
            var dust = box.GetComponent<BoxDust>();
            if (head == null || box.Lid == null)
            {
                if (dust != null && !dust.IsClean) dust.CleanNow();
                box.OpenChest();
                acting = false;
                yield break;
            }
            Debug.Log($"[Attic] Going to open {box.name} by hand.");

            // Up to it.
            var walker = FindAnyObjectByType<PlayerLocomotion>();
            // To a spot square in front of the box (not straight at its edge, which cuts across the next box in the
            // row and gets stuck on it); if something's still in the way, step back from the row first and come in again.
            Vector3 boxFront = StoryFx.Flat(-box.transform.forward);
            if (Vector3.Dot(boxFront, head.position - box.transform.position) < 0f) boxFront = -boxFront; // (the side the player is on)
            Vector3 stand = box.transform.position + boxFront * StandOff(box);
            if (walker != null && !walker.Paused && FlatDistance(head.position, stand) > 0.12f)
            {
                yield return Walk(walker, head, stand);
                if (FlatDistance(head.position, stand) > 0.2f)
                {
                    yield return Walk(walker, head, stand + boxFront * 0.45f);
                    yield return Walk(walker, head, stand);
                }
            }

            // Turn to face it (on a screen; in a headset the player turns their own head).
            if (!UnityEngine.XR.XRSettings.isDeviceActive)
            {
                Transform rig = head.root;
                for (float t = 0f; t < 0.6f; t += Time.deltaTime)
                {
                    Vector3 to = StoryFx.Flat(box.transform.position - head.position);
                    float angle = Vector3.SignedAngle(StoryFx.Flat(head.forward), to, Vector3.up);
                    rig.RotateAround(head.position, Vector3.up, angle * (1f - Mathf.Exp(-Time.deltaTime * 9f)));
                    yield return null;
                }
            }

            Vector3 front = LidFront(box, head, out float halfWidth, out Vector3 toPlayer);
            if (FlatDistance(head.position, front) > 1.2f)
            {
                // Couldn't get close (something in the way): open it the old way.
                if (dust != null && !dust.IsClean) dust.CleanNow();
                box.OpenChest();
                acting = false;
                yield break;
            }
            Vector3 across = Vector3.Cross(Vector3.up, toPlayer); // (to the player's left)
            Vector3 top = front - toPlayer * 0.12f;

            // Crouch down to it (on a screen; in a headset the player bends down themselves): standing, a low lid is
            // further from the shoulders than an arm reaches, and the hands would look pulled off the body.
            float crouched = 0f;
            if (!UnityEngine.XR.XRSettings.isDeviceActive)
            {
                float shoulderY = head.position.y - 0.25f;
                float want = Mathf.Clamp(shoulderY - (front.y + 0.48f), 0f, 0.6f);
                yield return Crouch(head.root, want, 0.45f);
                crouched = want;
            }

            // The dust: the right hand wipes it off the lid, flat, side to side.
            if (dust != null && !dust.IsClean)
            {
                Quaternion flatHand = Quaternion.LookRotation(Vector3.Slerp(-toPlayer, Vector3.down, 0.15f), Vector3.up);
                for (float t = 0f; t < 1.6f; t += Time.deltaTime)
                {
                    float sweep = t < 0.35f ? 0f : Mathf.Sin((t - 0.35f) * 10f);
                    ActedHands.Hold(HandSide.Right, top + Vector3.up * 0.03f - across * sweep * halfWidth * 0.55f, flatHand, 0.1f);
                    if (t > 1.1f && !dust.IsClean) dust.CleanNow();
                    yield return null;
                }
                if (!dust.IsClean) dust.CleanNow();
            }

            // Both hands to the front edge of the lid, fingers curling over it.
            Quaternion grip = Quaternion.LookRotation(Vector3.Slerp(-toPlayer, Vector3.down, 0.4f).normalized, Vector3.up);
            Vector3 left = front + across * halfWidth * 0.55f + toPlayer * 0.035f + Vector3.up * 0.025f;
            Vector3 right = front - across * halfWidth * 0.55f + toPlayer * 0.035f + Vector3.up * 0.025f;
            for (float t = 0f; t < 0.9f; t += Time.deltaTime)
            {
                float curl = Mathf.Lerp(0.2f, 0.62f, Mathf.InverseLerp(0.5f, 0.9f, t));
                ActedHands.Hold(HandSide.Left, left, grip, curl);
                ActedHands.Hold(HandSide.Right, right, grip, curl);
                yield return null;
            }

            // Lift: the hands rise with the lid's edge as it swings up, then let go.
            Vector3 leftOnLid = box.Lid.InverseTransformPoint(left), rightOnLid = box.Lid.InverseTransformPoint(right);
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                Vector3 up = Vector3.up * (0.04f * t / 0.3f);
                ActedHands.Hold(HandSide.Left, left + up, grip, 0.65f);
                ActedHands.Hold(HandSide.Right, right + up, grip, 0.65f);
                yield return null;
            }
            box.OpenChest();
            if (!box.IsOpen && dust != null) { dust.CleanNow(); box.OpenChest(); }
            for (float t = 0f; t < 0.75f; t += Time.deltaTime)
            {
                ActedHands.Hold(HandSide.Left, box.Lid.TransformPoint(leftOnLid) + Vector3.up * 0.04f, grip, 0.65f);
                ActedHands.Hold(HandSide.Right, box.Lid.TransformPoint(rightOnLid) + Vector3.up * 0.04f, grip, 0.65f);
                yield return null;
            }
            ActedHands.ReleaseBoth();
            if (crouched > 0f) yield return Crouch(head.root, -crouched, 0.5f);
            acting = false;
        }

        // Lowers (or, negative, raises) the player's view by `drop` metres over `seconds`, eased.
        static IEnumerator Crouch(Transform rig, float drop, float seconds)
        {
            if (Mathf.Abs(drop) < 0.01f) yield break;
            float done = 0f;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds)) * drop;
                rig.position -= Vector3.up * (k - done);
                done = k;
                yield return null;
            }
            rig.position -= Vector3.up * (drop - done);
        }

        // The middle of the lid's front edge (the side facing the player), on top; how wide the lid is (half); and the
        // way from the box to the player, across the floor.
        static Vector3 LidFront(ChestOpen box, Transform head, out float halfWidth, out Vector3 toPlayer)
        {
            var b = new Bounds(box.Lid.position, Vector3.zero);
            bool any = false;
            foreach (MeshRenderer r in box.Lid.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || r.name == "Dust") continue;
                if (!any) b = r.bounds;
                else b.Encapsulate(r.bounds);
                any = true;
            }
            toPlayer = StoryFx.Flat(head.position - b.center);
            if (toPlayer.sqrMagnitude < 1e-4f) toPlayer = -StoryFx.Flat(box.transform.forward);
            toPlayer.Normalize();
            Vector3 e = b.extents;
            float depth = Mathf.Abs(toPlayer.x) * e.x + Mathf.Abs(toPlayer.z) * e.z;
            halfWidth = Mathf.Clamp(Mathf.Abs(toPlayer.z) * e.x + Mathf.Abs(toPlayer.x) * e.z, 0.1f, 0.3f);
            Vector3 front = b.center + toPlayer * depth;
            front.y = b.max.y;
            return front;
        }

        static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // Stands the box on its furniture: a copy set down here on the attic floor, or the piece where it already is.
        static void RaiseOnto(Transform box, Place place, Transform room)
        {
            GameObject source = string.IsNullOrEmpty(place.stand) ? null : GameObject.Find(place.stand);
            if (source == null)
            {
                Debug.LogWarning($"[Attic] No '{place.stand}' to stand {box.name} on: it stays on the floor.");
                return;
            }
            GameObject stand = source;
            if (place.copy)
            {
                stand = Instantiate(source, room);
                stand.name = place.stand + "_Attic";
                stand.transform.SetPositionAndRotation(room.TransformPoint(place.at), room.rotation * Quaternion.Euler(0f, place.turn, 0f));
                float floor = room.TransformPoint(Vector3.zero).y;
                if (Bounds(stand, out Bounds b)) stand.transform.position += Vector3.up * (floor - b.min.y); // (feet on the attic floor)
            }
            if (Bounds(stand, out Bounds top)) box.position = new Vector3(box.position.x, top.max.y + 0.002f, box.position.z);
        }

        static bool Bounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) bounds = r.bounds;
                else bounds.Encapsulate(r.bounds);
                any = true;
            }
            return any;
        }

        ChestOpen Nearest()
        {
            Transform head = Camera.main != null ? Camera.main.transform : null;
            ChestOpen best = null;
            float bestDistance = float.MaxValue;
            foreach (ChestOpen box in boxes)
            {
                if (box == null || box.IsOpen) continue;
                float d = head != null ? (box.transform.position - head.position).sqrMagnitude : 0f;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = box;
                }
            }
            return best;
        }

        void HandleOpened(ChestOpen box)
        {
            Opened++;
            bool last = boxes.TrueForAll(b => b == null || b.IsOpen);
            if (LanternBox == null && (Opened >= target || last))
            {
                PutLanternIn(box);
                return;
            }
            Debug.Log($"[Attic] Box {Opened} opened: only dust and old rags. Try another one.");
            if (LanternBox == null) StartCoroutine(KnockElsewhere());
        }

        void PutLanternIn(ChestOpen box)
        {
            LanternBox = box;
            lantern.SetParent(box.transform, false);
            lantern.localPosition = lanternPosition;
            lantern.localRotation = lanternRotation;
            lantern.localScale = Vector3.one / Mathf.Max(box.transform.localScale.x, 0.01f); // (the lantern keeps its size)
            Debug.Log($"[Attic] Box {Opened} holds grandfather's lantern ({box.name})");
        }

        // A moment after an empty box, another one knocks: something in there wants out.
        IEnumerator KnockElsewhere()
        {
            yield return new WaitForSeconds(1.5f);
            var closed = boxes.FindAll(b => b != null && !b.IsOpen);
            if (closed.Count == 0 || LanternBox != null) yield break;
            ChestOpen next = closed[Random.Range(0, closed.Count)];
            next.Rattle(1.5f, 4f);
        }

        void OnDestroy()
        {
            foreach (ChestOpen box in boxes)
                if (box != null) box.Opened -= HandleOpened;
        }
    }
}
