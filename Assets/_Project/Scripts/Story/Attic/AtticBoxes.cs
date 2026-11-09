using System.Collections;
using System.Collections.Generic;
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
        [SerializeField] Vector2Int foundOn = new Vector2Int(3, 4);

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
            lantern.position = chest.transform.position + Vector3.down * 40f;
            target = Mathf.Clamp(Random.Range(foundOn.x, foundOn.y + 1), 1, boxes.Count);
            Debug.Log($"[Attic] {boxes.Count} boxes to open; the lantern is in whichever is opened number {target}");
        }

        /// <summary>Opens the closed box nearest the player (keyboard / debug).</summary>
        public void OpenNearest()
        {
            ChestOpen box = Nearest();
            if (box == null) return;
            var dust = box.GetComponent<BoxDust>();
            if (dust != null && !dust.IsClean) dust.CleanNow(); // (the first press wipes it, the next opens it)
            else box.OpenChest();
        }

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
