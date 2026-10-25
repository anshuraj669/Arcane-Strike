# ARCANE STRIKE — Rift Link Build Guide (Remote Multiplayer)

> **Rift Link:** two players in two different homes. A portal opens on each player's wall, and through it you see your partner's room, their glowing hands and their enemies. Shades crawl from one home into the other. You can throw a Tethered enemy through the portal to help your friend, or duel them across the Veil.

| Item | Value |
|---|---|
| Players | 2, remote (different homes, internet) |
| Engine | Unity 6 LTS + Meta XR All-in-One SDK (same as the GDD) |
| Networking | **Photon Fusion 2 — Shared Mode** (free tier: 20 concurrent users) |
| Voice (optional) | Photon Voice 2 |
| Dev machine | Linux (see §2.3 for what does and doesn't work on Linux) |
| Must stay true | Solo game is 100% complete without Rift Link. Judges will likely test with one headset. |

---

## 1. The Core Idea: "Link Space"

Every networking problem in Rift Link comes down to one question: **the two players live in two rooms of different shapes. How do they share one battlefield?**

The answer: treat the two portals as **the same doorway** seen from two sides.

```
          PLAYER A's ROOM                 PLAYER B's ROOM
     (link space  z > 0)               (link space  z < 0)

   ┌──────────────────────┐         ┌──────────────────────┐
   │                      │         │                      │
   │    🧑 A (seated)      │         │       🧑 B (seated)   │
   │                      │         │                      │
   │         ↓ looks      │         │      ↓ looks         │
   └────────[PORTAL]──────┘         └─────[PORTAL]─────────┘
             z = 0                         z = 0

   In LINK SPACE they are glued together at the portal:

   ┌──────────────────────┐
   │   A's room   (z > 0) │
   │        🧑 A          │
   ├────────[PORTAL]──────┤  ← origin (0,0,0) at the floor, under the portal center
   │        🧑 B          │
   │   B's room   (z < 0) │
   └──────────────────────┘
```

- **Link space origin** = the floor point directly under the portal center. +Y is up. +Z points into Player A's room.
- **Player A** maps link space → their world with their portal pose.
- **Player B** does the same, but **rotated 180° around Y**, so B's half (z < 0) lands in front of B's wall.
- **Everything networked** (players, enemies, projectiles, room geometry) is sent in **link space**.

Why this is the key trick:
- An enemy walking from z = +2 to z = −2 **naturally walks through the portal** from A's room into B's room. No teleport code is needed.
- Each client only needs one matrix (`linkToWorld`) to place everything.
- Floors line up (both at y = 0), so nothing floats or sinks.

---

## 2. Setup

### 2.1 What you need
| Need | Notes |
|---|---|
| Unity 6 LTS + **Android Build Support** (OpenJDK, SDK, NDK) | Install through Unity Hub (Linux supported) |
| **Meta XR All-in-One SDK** | Unity Package Manager → `com.meta.xr.sdk.all` |
| **Photon account** + **Fusion 2 SDK** | dashboard.photonengine.com → create a *Fusion 2* app → copy the App ID. Import the Fusion 2 `.unitypackage`. |
| **2 × Quest 3 / 3S** | For real end-to-end tests. Until then, one Quest + the desktop debug client (§2.3) is enough. |
| `adb` | `sudo apt install adb` — Quest Developer Mode on via the Meta Horizon phone app |

### 2.2 Project settings checklist
1. **Meta → Tools → Project Setup Tool → Fix All** (Android target, IL2CPP, ARM64, graphics, etc.).
2. `OVRManager` (on the camera rig):
   - Hand Tracking Support: **Controllers and Hands** (hands work alone, controllers optional)
   - Passthrough Support: **Required**
   - Scene Support: **Required**
3. Player Settings → Android → **Internet Access: Require**. Without this, Photon cannot connect on the Quest.
4. **Fusion → Realtime Settings**:
   - Paste your App ID.
   - Set **Fixed Region** (e.g. `asia`, `in`, `eu`, `us`). ⚠️ Both players **must be in the same region**, or they will never find each other's session.
5. **MRUK** (`MRUK` component): Data Source = **Device With Prefab Fallback**. On the Quest it uses your real room; in the editor it loads one of MRUK's sample rooms.
6. Add a layer named **`BeyondPortal`** (Project Settings → Tags and Layers).

### 2.3 Developing on Linux — what works
| Tool | Linux? | What to do instead |
|---|---|---|
| Unity Editor + Android build | ✅ | — |
| `adb install -r build.apk` | ✅ | This is your deploy loop |
| Meta Quest Developer Hub | ❌ | Use `adb` + the Developer Dashboard website |
| Quest Link / Meta XR Simulator | ❌ | Use the **desktop debug client** below |

**Desktop debug client (test multiplayer with one headset):**
- Run the game in the Unity Editor (it runs as a flat 2D window on Linux).
- MRUK loads a sample room through the prefab fallback.
- Drive a fake head and hands with mouse/keyboard (see `DebugRig` in §4.10).
- The editor joins as Player B, and the Quest joins as Player A.

```bash
# Deploy loop
adb devices                         # Quest must show as "device"
adb install -r Builds/ArcaneStrike.apk
adb logcat -s Unity                 # live Unity logs from the headset
```

---

## 3. Architecture

### 3.1 New systems (added next to the GDD architecture)
```
RiftLinkBootstrap      start / leave a Fusion session (join code or quick match)
 └── NetworkRunner (prefab)
      └── PlayerSpawner           spawns one SparkAvatar per player
LinkSpace              link ↔ world conversion, portal placement, side A/B
LocalRig               references to the local head / hands
SparkAvatar            (networked, one per player) head + hands sync, side resolution, room sharing
RemoteRoom             renders the partner's room as glowing boxes behind the portal
LinkTransformSync      (networked) syncs any object in link space + authority hand-off
NetworkEnemy           (networked) health, damage RPC, cross-portal hand-off
NetworkSpellCaster     (networked) spell visuals for the partner, damage by caster
RiftLinkGame           (networked, spawned by master client) waves, score, Twin Seal
Portal rendering       stencil mask + URP "Render Objects" for the BeyondPortal layer
```

### 3.2 Who owns what (state authority)
| Object | Authority | Why |
|---|---|---|
| Your SparkAvatar | You | Only you know where your head and hands are |
| An enemy | **The player whose room it is in** | Only that client knows its room's real furniture, floor and walls for AI/pathing |
| Enemy crossing the portal | Ownership **transfers** to the other player | `RequestStateAuthority()` when it is past the portal by 15 cm |
| Your projectiles | You (local only, not networked objects) | Partner gets a cosmetic copy via RPC; only your copy deals damage |
| Wave / score / boss state | Shared-mode **master client** | One referee for shared decisions |

### 3.3 Data flow for one Firebolt through the portal
```
A flicks fingers → GestureRecognizer → SpellSystem → NetworkSpellCaster.CastFirebolt()
   ├─ A spawns a real bolt (deals damage)
   └─ RPC_Firebolt(linkOrigin, linkDir) → B spawns a cosmetic bolt (no damage)

A's bolt flies through the portal and hits a Shade that lives in B's room
   └─ A calls shade.RPC_TakeDamage(20, A) → runs on B (the Shade's authority)
        └─ Health ≤ 0 → B despawns it → both clients play death VFX in Despawned()
             └─ A gets the Motes (LastHitBy == A)
```

---

## 4. Scripts

Put them in `Assets/_Project/Scripts/Link/` (and `Net/`, `Enemies/`, `Spells/` as marked). API names were written against Fusion 2 and recent MRUK versions. If a name differs in your version, the comment says what it should do.

### 4.1 `Net/RiftLinkBootstrap.cs`
```csharp
using System.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RiftLinkBootstrap : MonoBehaviour
{
    [SerializeField] NetworkRunner runnerPrefab;   // prefab: NetworkRunner + NetworkSceneManagerDefault + PlayerSpawner

    NetworkRunner runner;

    public static string NewJoinCode() => Random.Range(1000, 10000).ToString();

    // code == null → quick match (joins any open Rift Link session or creates one)
    public async Task<bool> Join(string code)
    {
        while (LinkSpace.Instance == null || !LinkSpace.Instance.IsReady)
            await Task.Yield();   // portal must be placed before we send anything

        runner = Instantiate(runnerPrefab);
        var result = await runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = code == null ? null : "rift-" + code,
            PlayerCount = 2,
            Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
            SceneManager = runner.GetComponent<NetworkSceneManagerDefault>(),
        });

        if (!result.Ok)
            Debug.LogError($"Rift Link failed: {result.ShutdownReason}");
        return result.Ok;
    }

    public async void Leave()
    {
        if (runner != null)
            await runner.Shutdown();
    }
}
```

### 4.2 `Net/PlayerSpawner.cs`
```csharp
using Fusion;
using UnityEngine;

// Lives on the NetworkRunner prefab, so Fusion registers it automatically.
public class PlayerSpawner : SimulationBehaviour, IPlayerJoined
{
    [SerializeField] NetworkObject sparkAvatarPrefab;

    public void PlayerJoined(PlayerRef player)
    {
        if (player == Runner.LocalPlayer)
            Runner.Spawn(sparkAvatarPrefab, Vector3.zero, Quaternion.identity, player);
    }
}
```

### 4.3 `Link/LinkSpace.cs` — the heart of Rift Link
```csharp
using Meta.XR.MRUtilityKit;
using UnityEngine;

public class LinkSpace : MonoBehaviour
{
    public static LinkSpace Instance { get; private set; }

    [SerializeField] Transform portalVisual;   // rift VFX + PortalMask quad, scaled to 1×1 in its prefab
    [SerializeField] float portalWidth = 1.6f;
    [SerializeField] float portalHeight = 1.9f;
    [SerializeField] float portalBottom = 0.25f;

    public bool IsReady { get; private set; }
    public bool IsSideA { get; private set; } = true;
    public MRUKAnchor PortalWall { get; private set; }

    Pose portalPose;
    Matrix4x4 linkToWorld = Matrix4x4.identity;
    Matrix4x4 worldToLink = Matrix4x4.identity;

    void Awake() => Instance = this;

    void Start() => MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);

    void OnSceneLoaded()
    {
        var room = MRUK.Instance.GetCurrentRoom();
        PortalWall = room.GetKeyWall(out Vector2 wallSize);   // largest wall
        var floor = room.FloorAnchor.transform.position;

        // Portal +Z must point INTO the room. Check against the floor center instead of trusting anchor axes.
        Vector3 forward = Vector3.ProjectOnPlane(PortalWall.transform.forward, Vector3.up).normalized;
        if (Vector3.Dot(forward, floor - PortalWall.transform.position) < 0f)
            forward = -forward;

        Vector3 origin = PortalWall.transform.position;
        origin.y = floor.y;
        portalPose = new Pose(origin, Quaternion.LookRotation(forward, Vector3.up));

        portalWidth = Mathf.Min(portalWidth, wallSize.x - 0.2f);
        portalVisual.SetPositionAndRotation(
            origin + Vector3.up * (portalBottom + portalHeight * 0.5f) + forward * 0.01f,
            portalPose.rotation);
        portalVisual.localScale = new Vector3(portalWidth, portalHeight, 1f);

        Rebuild();
        IsReady = true;
    }

    public void SetSide(bool sideA)
    {
        IsSideA = sideA;
        Rebuild();
    }

    void Rebuild()
    {
        // Side B sees link space rotated 180°, so its half (z < 0) lands in front of its own wall.
        var flip = IsSideA ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
        linkToWorld = Matrix4x4.TRS(portalPose.position, portalPose.rotation * flip, Vector3.one);
        worldToLink = linkToWorld.inverse;
    }

    public Vector3 ToWorld(Vector3 linkPos) => linkToWorld.MultiplyPoint3x4(linkPos);
    public Vector3 ToLink(Vector3 worldPos) => worldToLink.MultiplyPoint3x4(worldPos);
    public Quaternion ToWorld(Quaternion linkRot) => linkToWorld.rotation * linkRot;
    public Quaternion ToLink(Quaternion worldRot) => Quaternion.Inverse(linkToWorld.rotation) * worldRot;
    public Vector3 DirToWorld(Vector3 linkDir) => linkToWorld.MultiplyVector(linkDir);
    public Vector3 DirToLink(Vector3 worldDir) => worldToLink.MultiplyVector(worldDir);

    // How far a link-space point is inside MY room (negative = in the partner's room).
    public float DepthOnMySide(Vector3 linkPos) => IsSideA ? linkPos.z : -linkPos.z;
    public bool IsOnMySide(Vector3 linkPos) => DepthOnMySide(linkPos) > 0f;

    // True if a world point on the wall plane is inside the portal opening (projectiles pass, others hit the wall).
    public bool IsInsidePortal(Vector3 worldPos)
    {
        Vector3 local = Quaternion.Inverse(portalPose.rotation) * (worldPos - portalPose.position);
        return Mathf.Abs(local.x) < portalWidth * 0.5f
            && local.y > portalBottom && local.y < portalBottom + portalHeight;
    }
}
```

### 4.4 `Link/LocalRig.cs`
```csharp
using UnityEngine;

// Scene object: drag in the OVRCameraRig anchors and the OVRHand components.
public class LocalRig : MonoBehaviour
{
    public static LocalRig Instance { get; private set; }

    public Transform head;        // OVRCameraRig/TrackingSpace/CenterEyeAnchor
    public Transform leftWrist;   // LeftHandAnchor
    public Transform rightWrist;  // RightHandAnchor
    public OVRHand leftHand;
    public OVRHand rightHand;

    void Awake() => Instance = this;
}
```

### 4.5 `Link/SparkAvatar.cs` — partner's head + hands, side resolution, room sharing
The partner appears as a **floating lantern-mask head and two glowing Spark hands**. It fits the story, needs no Meta Avatars approval process, and costs about 100 bytes per tick.

```csharp
using System.Collections;
using Fusion;
using Meta.XR.MRUtilityKit;
using UnityEngine;

public class SparkAvatar : NetworkBehaviour
{
    public static SparkAvatar Local { get; private set; }

    [Networked] public NetworkBool IsSideA { get; set; }
    [Networked] Vector3 HeadPos { get; set; }
    [Networked] Quaternion HeadRot { get; set; }
    [Networked] Vector3 LeftPos { get; set; }
    [Networked] Quaternion LeftRot { get; set; }
    [Networked] Vector3 RightPos { get; set; }
    [Networked] Quaternion RightRot { get; set; }
    [Networked] float LeftPinch { get; set; }
    [Networked] float RightPinch { get; set; }
    [Networked] NetworkBool LeftTracked { get; set; }
    [Networked] NetworkBool RightTracked { get; set; }

    [Header("Partner visuals (all on the BeyondPortal layer)")]
    [SerializeField] GameObject visualsRoot;
    [SerializeField] Transform head, leftHand, rightHand;
    [SerializeField] SparkHandVisual leftVisual, rightVisual;   // your glow-hand script: SetPinch(float), SetVisible(bool)
    [SerializeField] float smoothing = 20f;

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            Local = this;
            IsSideA = true;                       // alone → side A until we meet a partner
            LinkSpace.Instance.SetSide(true);
            visualsRoot.SetActive(false);         // you see your own real hands through passthrough

            foreach (var other in FindObjectsByType<SparkAvatar>(FindObjectsSortMode.None))
                if (other != this) StartCoroutine(MeetPartner(other));
        }
        else if (Local != null)
        {
            Local.StartCoroutine(Local.MeetPartner(this));
        }
    }

    IEnumerator MeetPartner(SparkAvatar partner)
    {
        yield return new WaitForSeconds(1f);   // let the partner's first state arrive

        // Both on the same side → the player who joined later (higher PlayerId) switches.
        if (partner.IsSideA == IsSideA &&
            Object.StateAuthority.PlayerId > partner.Object.StateAuthority.PlayerId)
        {
            IsSideA = !partner.IsSideA;
            LinkSpace.Instance.SetSide(IsSideA);
        }

        SendMyRoom();   // after side is final, because link coordinates depend on it
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        var rig = LocalRig.Instance;
        var link = LinkSpace.Instance;

        HeadPos = link.ToLink(rig.head.position);
        HeadRot = link.ToLink(rig.head.rotation);
        LeftPos = link.ToLink(rig.leftWrist.position);
        LeftRot = link.ToLink(rig.leftWrist.rotation);
        RightPos = link.ToLink(rig.rightWrist.position);
        RightRot = link.ToLink(rig.rightWrist.rotation);

        LeftTracked = rig.leftHand.IsTracked;
        RightTracked = rig.rightHand.IsTracked;
        LeftPinch = rig.leftHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
        RightPinch = rig.rightHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
    }

    public override void Render()
    {
        if (HasStateAuthority) return;

        var link = LinkSpace.Instance;
        float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);

        Follow(head, link.ToWorld(HeadPos), link.ToWorld(HeadRot), t);
        Follow(leftHand, link.ToWorld(LeftPos), link.ToWorld(LeftRot), t);
        Follow(rightHand, link.ToWorld(RightPos), link.ToWorld(RightRot), t);

        leftVisual.SetVisible(LeftTracked);
        rightVisual.SetVisible(RightTracked);
        leftVisual.SetPinch(LeftPinch);
        rightVisual.SetPinch(RightPinch);
    }

    static void Follow(Transform target, Vector3 pos, Quaternion rot, float t)
    {
        target.SetPositionAndRotation(
            Vector3.Lerp(target.position, pos, t),
            Quaternion.Slerp(target.rotation, rot, t));
    }

    // ---------- Room sharing: boxes only, never camera images ----------

    void SendMyRoom()
    {
        var link = LinkSpace.Instance;
        RPC_ClearRoom();

        foreach (var anchor in MRUK.Instance.GetCurrentRoom().Anchors)
        {
            if (anchor == link.PortalWall) continue;   // would cover the portal opening on the partner's side

            var a = anchor.transform;
            if (anchor.VolumeBounds.HasValue)          // furniture: couch, table, bed…
            {
                var b = anchor.VolumeBounds.Value;
                RPC_AddBox(link.ToLink(a.TransformPoint(b.center)), link.ToLink(a.rotation), b.size);
            }
            else if (anchor.PlaneRect.HasValue)        // walls, floor, ceiling, doors, windows
            {
                var r = anchor.PlaneRect.Value;
                RPC_AddBox(link.ToLink(a.TransformPoint(r.center)), link.ToLink(a.rotation),
                           new Vector3(r.width, r.height, 0.02f));
            }
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
    void RPC_ClearRoom() => RemoteRoom.Instance.Clear();

    [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
    void RPC_AddBox(Vector3 linkCenter, Quaternion linkRot, Vector3 size) =>
        RemoteRoom.Instance.AddBox(linkCenter, linkRot, size);

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Local == this) Local = null;
        else if (RemoteRoom.Instance != null) RemoteRoom.Instance.Clear();   // partner left
    }
}
```

### 4.6 `Link/RemoteRoom.cs`
```csharp
using System.Collections.Generic;
using UnityEngine;

// Draws the partner's room as glowing "Hollow-tinted" blocks behind your portal.
public class RemoteRoom : MonoBehaviour
{
    public static RemoteRoom Instance { get; private set; }

    [SerializeField] GameObject boxPrefab;   // BeyondPortal layer, emissive edge material, NO collider

    readonly List<GameObject> boxes = new();

    void Awake() => Instance = this;

    public void Clear()
    {
        foreach (var box in boxes) Destroy(box);
        boxes.Clear();
    }

    public void AddBox(Vector3 linkCenter, Quaternion linkRot, Vector3 size)
    {
        var link = LinkSpace.Instance;
        var box = Instantiate(boxPrefab, link.ToWorld(linkCenter), link.ToWorld(linkRot), transform);
        box.transform.localScale = size;
        boxes.Add(box);
    }
}
```

### 4.7 `Link/LinkTransformSync.cs` — for enemies and any shared object
```csharp
using Fusion;
using UnityEngine;

// Requires NetworkObject with "Allow State Authority Override" ticked (needed for portal hand-off).
public class LinkTransformSync : NetworkBehaviour, IStateAuthorityChanged
{
    [Networked] Vector3 LinkPos { get; set; }
    [Networked] Quaternion LinkRot { get; set; }
    [Networked] Vector3 LinkVel { get; set; }

    [SerializeField] float smoothing = 15f;

    Rigidbody body;
    Renderer[] renderers;
    int homeLayer, beyondLayer, currentLayer = -1;

    public override void Spawned()
    {
        body = GetComponent<Rigidbody>();
        renderers = GetComponentsInChildren<Renderer>(true);
        homeLayer = gameObject.layer;
        beyondLayer = LayerMask.NameToLayer("BeyondPortal");

        if (!HasStateAuthority)
        {
            var link = LinkSpace.Instance;
            transform.SetPositionAndRotation(link.ToWorld(LinkPos), link.ToWorld(LinkRot));
        }
        UpdatePhysicsOwner();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        var link = LinkSpace.Instance;
        LinkPos = link.ToLink(transform.position);
        LinkRot = link.ToLink(transform.rotation);
        if (body) LinkVel = link.DirToLink(body.linearVelocity);
    }

    public override void Render()
    {
        var link = LinkSpace.Instance;

        if (!HasStateAuthority)
        {
            float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, link.ToWorld(LinkPos), t),
                Quaternion.Slerp(transform.rotation, link.ToWorld(LinkRot), t));
        }

        // Behind my wall → only visible through the portal (stencil).
        SetLayer(link.IsOnMySide(link.ToLink(transform.position)) ? homeLayer : beyondLayer);
    }

    public void StateAuthorityChanged()
    {
        UpdatePhysicsOwner();
        if (HasStateAuthority && body)
            body.linearVelocity = LinkSpace.Instance.DirToWorld(LinkVel);   // a thrown Shade keeps flying
    }

    void UpdatePhysicsOwner()
    {
        if (body) body.isKinematic = !HasStateAuthority;
    }

    void SetLayer(int layer)
    {
        if (layer == currentLayer) return;
        currentLayer = layer;
        foreach (var r in renderers) r.gameObject.layer = layer;
    }
}
```

### 4.8 `Enemies/NetworkEnemy.cs`
```csharp
using Fusion;
using UnityEngine;

[RequireComponent(typeof(LinkTransformSync))]
public class NetworkEnemy : NetworkBehaviour
{
    [Networked] public int Health { get; set; }
    [Networked] public PlayerRef LastHitBy { get; set; }

    [SerializeField] int maxHealth = 20;
    [SerializeField] int moteReward = 5;
    [SerializeField] GameObject deathVfx;

    const float HandoffDepth = 0.15f;   // hysteresis so ownership doesn't flip back and forth at the portal
    bool authorityRequested;

    public override void Spawned()
    {
        if (HasStateAuthority) Health = maxHealth;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        // Your existing EnemyAI runs here, using YOUR room (MRUK) for pathing.
        // To attack the partner: path to the portal center, then keep walking — ownership transfers automatically.
    }

    public override void Render()
    {
        if (HasStateAuthority) { authorityRequested = false; return; }

        var link = LinkSpace.Instance;
        if (!authorityRequested && link.DepthOnMySide(link.ToLink(transform.position)) > HandoffDepth)
        {
            Object.RequestStateAuthority();   // it walked into my room → I run its AI now
            authorityRequested = true;
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeDamage(int amount, PlayerRef attacker)
    {
        if (Health <= 0) return;
        Health -= amount;
        LastHitBy = attacker;
        if (Health <= 0) Runner.Despawn(Object);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        Instantiate(deathVfx, transform.position, Quaternion.identity);
        if (hasState && LastHitBy == runner.LocalPlayer)
            MoteWallet.Add(moteReward);   // your existing Motes system
    }
}
```

Spawning enemies: **the room's owner spawns them** from its own rifts with `Runner.Spawn(enemyPrefab, riftPos, riftRot)`. In shared mode, the spawner automatically gets state authority.

### 4.9 `Spells/NetworkSpellCaster.cs` (on the SparkAvatar prefab)
```csharp
using Fusion;
using UnityEngine;

public class NetworkSpellCaster : NetworkBehaviour
{
    [SerializeField] Firebolt fireboltPrefab;

    // Called by your SpellSystem when the local player casts (only on the local avatar).
    public void CastFirebolt(Vector3 worldOrigin, Vector3 worldDir)
    {
        SpawnBolt(worldOrigin, worldDir, Object.StateAuthority);   // real bolt: deals damage

        var link = LinkSpace.Instance;
        RPC_Firebolt(link.ToLink(worldOrigin), link.DirToLink(worldDir));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
    void RPC_Firebolt(Vector3 linkOrigin, Vector3 linkDir)
    {
        var link = LinkSpace.Instance;
        SpawnBolt(link.ToWorld(linkOrigin), link.DirToWorld(linkDir), PlayerRef.None);   // cosmetic copy
    }

    void SpawnBolt(Vector3 origin, Vector3 dir, PlayerRef damageOwner)
    {
        var bolt = Instantiate(fireboltPrefab, origin, Quaternion.LookRotation(dir));
        bolt.Init(dir, damageOwner);
    }
}

// In Firebolt.OnTriggerEnter:
//   if (owner != PlayerRef.None && other.TryGetComponent(out NetworkEnemy enemy))
//       enemy.RPC_TakeDamage(20, owner);
//   if (other is the real wall && !LinkSpace.Instance.IsInsidePortal(hitPoint)) → explode on the wall
//   (otherwise keep flying through the portal)
```

Volt Burst, Tether and Lumen follow the same pattern: the caster applies the effect, and the partner gets an RPC to play the visuals.

### 4.10 `Link/DebugRig.cs` — desktop client for testing with one headset
```csharp
using UnityEngine;

// Editor / desktop only: moves fake head + hands so the Linux editor can act as Player B.
public class DebugRig : MonoBehaviour
{
#if UNITY_EDITOR || DESKTOP_DEBUG
    [SerializeField] float speed = 1.5f;

    void Update()
    {
        var rig = LocalRig.Instance;
        var move = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
        rig.rightWrist.position += rig.head.rotation * move * speed * Time.deltaTime;

        if (Input.GetMouseButton(1))
            rig.head.Rotate(-Input.GetAxis("Mouse Y") * 3f, Input.GetAxis("Mouse X") * 3f, 0f);

        if (Input.GetKeyDown(KeyCode.Space))
            SparkAvatar.Local?.GetComponent<NetworkSpellCaster>()
                .CastFirebolt(rig.rightWrist.position, rig.head.forward);
    }
#endif
}
```
`OVRHand.IsTracked` is false in the editor, so `SparkAvatar` sends hidden hands. Add an editor override that reports hands as tracked when testing.

---

## 5. Portal Rendering (seeing the partner's room through your wall)

Goal: things in the partner's half (layer `BeyondPortal`) are drawn **only inside the portal opening**, and never anywhere else on your wall.

### 5.1 `Shaders/PortalMask.shader`
```hlsl
Shader "ArcaneStrike/PortalMask"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-10" }
        Pass
        {
            ColorMask 0
            ZWrite Off
            Cull Off
            Stencil { Ref 1 Comp Always Pass Replace }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);   // Quest single-pass stereo
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
```
Put a **Quad** with this material inside `portalVisual` (scale 1×1, so `LinkSpace` sizes it).

### 5.2 URP renderer setup
1. Open your **URP Renderer** asset. Under *Filtering*, **remove `BeyondPortal`** from both the Opaque and Transparent layer masks.
2. Add Renderer Feature → **Render Objects** named "Beyond Portal Opaque":
   - Event: *AfterRenderingOpaques*
   - Queue: Opaque, Layer Mask: `BeyondPortal`
   - Overrides → Stencil: **Value 1, Compare Function Equal, Pass Keep**
3. Duplicate it as "Beyond Portal Transparent" (Event *AfterRenderingTransparents*, Queue Transparent) so particles and spells work too.
4. Add a **backdrop** behind the portal: a large inside-out box (Cull Front) with a dark violet Hollow gradient, on the `BeyondPortal` layer, placed in the partner's half. Without it, you would see your real wall through the portal wherever there is no remote geometry.

### 5.3 ⚠️ Depth API gotcha
Your real wall is **closer** than anything in the partner's room. Depth API occlusion would therefore hide everything behind the portal.
→ Objects on `BeyondPortal` must use a material **without** environment-depth occlusion. Swap the material variant in `LinkTransformSync.SetLayer` or keep two materials per enemy.

---

## 6. Rift Link Game Modes

### 6.1 Co-op: "Twin Wards" (main mode, about 8 minutes)
- Each player has rifts and Ward Stones in their own room.
- The Link Portal sits between the rooms. **Shades always push toward the weaker home**, so players must help each other.

| Co-op mechanic | Gesture | What happens |
|---|---|---|
| **Fire Through** | Firebolt aimed at the portal | Hits Shades in your partner's room |
| **Throw Through** | Tether a Shade, throw it into the portal | It flies into your partner's room and becomes their problem (or their bowling ball) |
| **Pass the Light** | Push an open palm toward the portal (costs 20 Motes) | A mote orb flies through and heals your partner's weakest Ward |
| **Twin Seal** (boss finisher) | **Both** players cast Seal within 1 second | Seals the Gatekeeper, whose body spans both rooms. The master client checks the timing. |

**Boss: "The Twin Gatekeeper"**: a colossal Shade standing *inside the portal*, with one hand in each room. Each player destroys the hand in their own room, then both perform Twin Seal.

### 6.2 PvP: "Duel Across the Veil" (3 minutes, best of 3)
- Firebolt your partner's Spark avatar through the portal. Aegis blocks, and a perfect-timed Aegis reflects the shot.
- Hits land on a head/torso hitbox around the partner's synced head pose.
- Only the **target's** client decides hits on itself (`RPC_Hit` → the target's authority), which keeps cheating and lag arguments simple.

### 6.3 `RiftLinkGame` (shared state, spawned by the master client)
- Spawned once with `if (Runner.IsSharedModeMasterClient) Runner.Spawn(gamePrefab)`.
- `[Networked]` properties: `Wave`, `Phase`, `SharedScore`, `TwinSealWindowStart`.
- `RPC_SealCast(PlayerRef who)` → `RpcTargets.StateAuthority`. If both players cast within 1 s → `RPC_TwinSealSuccess()` to All.
- Tick "Allow State Authority Override" on it too. If the master leaves, Fusion picks a new master client, which should take authority of the game object.

---

## 7. Rift Link Script (story hook + Ember lines)

**First time Rift Link opens:**

*[MR] The largest real wall cracks — but this rift is gold, not violet. Through it: another room. Another person's silhouette, hands glowing.*

**EMBER:** Wait. Wait wait wait. That's not a Hollow rift. That's... a *Link*. The Lanternbearers used these to fight side by side across cities.

**EMBER:** There's another Spark! Look at their hands! Wave. Go on, wave. It's polite.

**[GESTURE: wave → partner sees your glowing hand wave through the portal]**

**EMBER:** Rules of the Link: what hurts their home can crawl into yours. What you throw through, they catch. And if you both seal at once... well. Let's find out.

**Mid-battle lines (randomized):**
- "Your partner's Ward is cracking! Send them light — palm to the portal!"
- "Something's crawling toward the Link. Yours now. Sorry. Not sorry."
- "Nice throw! I think it landed on their couch."
- "Twin Seal — together, on three! One... oh, just do it!"

**Duel intro:**
**EMBER:** A friendly duel. *Friendly.* Nobody burn anybody's curtains.

---

## 8. Build Plan (fits inside the GDD timeline)

Rift Link is roughly **9–11 working days**. To make room, apply the GDD cut list now: **merge Ch 4 + 5 and drop Ending B**.

| Phase | Days | Goal | Done when |
|---|---|---|---|
| 0. Setup | 1 | Fusion imported, App ID, Fixed Region, layers, Internet permission | Quest + editor both log "connected" |
| 1. Hello Link | 1 | Bootstrap + PlayerSpawner + join code | 2 clients in the same session |
| 2. Link Space | 2 | `LinkSpace`, portal on key wall, side resolution | A debug cube at link (0,1,1) appears 1 m in front of the portal on **both** clients |
| 3. Portal render | 1–2 | Stencil mask, Render Objects, backdrop | A cube at link (0,1,−2) is visible **only through the portal** |
| 4. Spark Avatars + room | 1–2 | Head/hands sync, remote room boxes | You see your partner's hands moving inside their room's outline |
| 5. Shared enemies | 2 | `LinkTransformSync`, `NetworkEnemy`, hand-off, damage RPC | A Shade walks from B's room into A's room and A can kill it |
| 6. Co-op mechanics | 1–2 | Fire Through, Throw Through, Pass the Light, Twin Seal | One full Twin Wards round playable |
| 7. Two-headset test + video | 1 | Real test across two internet connections | Recorded Rift Link footage for the demo video |
| (stretch) PvP Duel | 1 | Duel Across the Veil | — |

**Do Phase 2 first and verify it with debug cubes before writing anything else.** If link space is wrong, everything after it will be wrong in confusing ways.

---

## 9. Testing Checklist
- ☐ Debug cube test (Phase 2) passes for **both** sides A and B
- ☐ Left/right is correct: A raises their right hand, and B sees it on B's left (like facing someone through a door)
- ☐ Floors line up: partner's hands are at a sensible height in their room
- ☐ Enemy crosses the portal smoothly; no flicker or ownership ping-pong
- ☐ Thrown Shade keeps its velocity after crossing
- ☐ Partner leaves mid-match → remote room clears and the game continues solo
- ☐ Both players on different networks (home Wi-Fi + phone hotspot)
- ☐ 72 FPS held with both rooms' enemies visible
- ☐ Solo mode still works with no internet at all

## 10. Privacy & Rules
- **Never send passthrough camera images.** Only room box geometry (sizes, positions) and head/hand poses are sent.
- Say so in the app's privacy policy and the Devpost text ("Rift Link shares only an abstract outline of your room").
- Random 4-digit codes, sessions limited to 2 players.
- Before a store launch (not needed for the contest): add mute/block/report for partners, as social apps on the Meta Horizon Store are expected to have.

## 11. Troubleshooting
| Symptom | Likely cause | Fix |
|---|---|---|
| Quest never connects | Internet Access not set to *Require* | Player Settings → Android |
| Both connect but never meet | Different Photon regions | Set **Fixed Region** in Realtime Settings |
| Partner appears in front of the wall | Side resolution failed (both side A) | Check the `MeetPartner` log; ensure `IsSideA` is `[Networked]` |
| Partner is mirrored or upside-down | Wrong portal forward or a mirror instead of a 180° rotation | Use `Quaternion.Euler(0,180,0)`, never a negative scale |
| Remote room covers the portal | Key wall was sent | Skip `LinkSpace.PortalWall` in `SendMyRoom` |
| Nothing visible through the portal | Depth occlusion or layer still in the default renderer filter | §5.2 step 1 and §5.3 |
| Enemy jitters at the portal | Authority ping-pong | Increase `HandoffDepth` to 0.25 |
| `RequestStateAuthority` does nothing | "Allow State Authority Override" unticked | Tick it on the enemy's NetworkObject |

---

## 12. Devpost Pitch Line
> *"Arcane Strike's **Rift Link** turns two real homes into one battlefield. A portal opens on your wall into your friend's room. Shades crawl between your homes, you can throw enemies through to each other, and you seal the final rift together with your bare hands."*
