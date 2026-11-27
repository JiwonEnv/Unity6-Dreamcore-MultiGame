using Unity.Netcode;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// The "thread" grab: the owner picks a target and the server pulls it with a capped spring force.
    /// Forces from several holders simply add up, so an item heavier than Max Lift Kg drags along the floor for one
    /// player and lifts once a second player grabs it. The server aims from the doll's synced position and pitch, so
    /// only grab, release and hold-distance changes go over the network. Ported from the UE5 UDMGrabComponent.
    /// </summary>
    [RequireComponent(typeof(DMDoll))]
    public class DMGrab : NetworkBehaviour
    {
        [Header("Grab (m)")]
        [SerializeField] float grabRange = 4f;
        [SerializeField] float minHoldDistance = 0.9f;
        [SerializeField] float maxHoldDistance = 4f;
        [Tooltip("Mouse wheel step for pulling the item closer or pushing it away.")]
        [SerializeField] float holdDistanceStep = 0.25f;
        [Tooltip("How far a client's grab point may be from the item's surface; the item may have moved a little.")]
        [SerializeField] float grabPointTolerance = 0.3f;
        [Tooltip("Strength: the heaviest item one player can hold up against gravity.")]
        [SerializeField] float maxLiftKg = 20f;
        [Tooltip("Spring frequency (rad/s): higher snaps to the aim point faster but jitters more over the network.")]
        [SerializeField] float springFrequency = 9f;
        [SerializeField, Min(0f)] float springDampingRatio = 0.9f;
        [Tooltip("The thread snaps if the item ends up this far past the hold point (stuck behind a wall).")]
        [SerializeField] float breakDistance = 2.5f;
        [SerializeField] Color threadColor = new Color(1f, 0.35f, 0.55f);

        readonly NetworkVariable<NetworkObjectReference> held = new NetworkVariable<NetworkObjectReference>();
        /// <summary>Grab point in the item's local space, synced so every machine draws the thread to the same spot.</summary>
        readonly NetworkVariable<Vector3> localGrabOffset = new NetworkVariable<Vector3>();

        DMDoll doll;
        float holdDistance = 2f;
        LineRenderer thread;
        DMValuable serverHeld;
        bool trace;
        float lastTrace;

        public float MaxLiftKg => maxLiftKg;
        public float GrabRange => grabRange;

        /// <summary>What this doll holds, on every machine.</summary>
        public DMValuable HeldValuable
        {
            get
            {
                if (IsServer)
                {
                    return serverHeld != null && serverHeld.IsSpawned ? serverHeld : null;
                }
                return held.Value.TryGet(out NetworkObject item) ? item.GetComponent<DMValuable>() : null;
            }
        }

        void Awake()
        {
            doll = GetComponent<DMDoll>();
            // Purely cosmetic and rebuilt on every machine from the synced grab state.
            var threadObject = new GameObject("Grab Thread");
            threadObject.transform.SetParent(transform, false);
            thread = threadObject.AddComponent<LineRenderer>();
            thread.useWorldSpace = true;
            thread.positionCount = 2;
            thread.widthMultiplier = 0.015f;
            thread.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // The shared shape material (URP Lit, always in the build) tinted like every placeholder part.
            thread.sharedMaterial = DMShapes.SharedMaterial;
            DMShapes.Tint(thread, threadColor, 0.9f);
            thread.enabled = false;
            trace = DMArgs.Has("dmgrabtrace");
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                ServerDropHeld();
            }
        }

        Vector3 ViewLocation => doll.EyePosition;
        Vector3 AimDirection => doll.ViewRotation * Vector3.forward;

        /// <summary>From the lower right of the view, like a hand casting the thread.</summary>
        Vector3 ThreadStart
        {
            get
            {
                Quaternion aim = doll.ViewRotation;
                return ViewLocation + aim * new Vector3(0.18f, -0.18f, 0.3f);
            }
        }

        /// <summary>What the doll is aiming at within grab range (HUD and the owner's grab).</summary>
        public DMValuable TraceForValuable(out Vector3 hitPoint)
        {
            hitPoint = default;
            Vector3 start = ViewLocation;
            if (!DMPhysics.Linecast(start, start + AimDirection * grabRange, transform, out RaycastHit hit))
            {
                return null;
            }
            DMValuable valuable = hit.collider.GetComponentInParent<DMValuable>();
            if (valuable != null)
            {
                hitPoint = hit.point;
            }
            return valuable;
        }

        public DMValuable TraceForValuable() => TraceForValuable(out _);

        // ---- Owner input ----

        public void TryGrab()
        {
            DMValuable target = TraceForValuable(out Vector3 point);
            if (target != null)
            {
                holdDistance = Mathf.Clamp(Vector3.Distance(ViewLocation, point), minHoldDistance, maxHoldDistance);
                GrabRpc(target.NetworkObject, point);
            }
        }

        public void Release() => ReleaseRpc();

        public void AdjustHoldDistance(float direction)
        {
            if (HeldValuable == null || Mathf.Approximately(direction, 0f))
            {
                return;
            }
            holdDistance = Mathf.Clamp(holdDistance + Mathf.Sign(direction) * holdDistanceStep, minHoldDistance, maxHoldDistance);
            SetHoldDistanceRpc(holdDistance);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void GrabRpc(NetworkObjectReference target, Vector3 worldGrabPoint)
        {
            if (target.TryGet(out NetworkObject item))
            {
                ServerGrab(item.GetComponent<DMValuable>(), worldGrabPoint);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ReleaseRpc() => ServerDropHeld();

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SetHoldDistanceRpc(float distance) => holdDistance = Mathf.Clamp(distance, minHoldDistance, maxHoldDistance);

        // ---- Server ----

        /// <summary>
        /// Server: rejected unless the point is on the target, within reach, in plain view, and the holder is up and
        /// not hiding. Public so tests can send crafted requests. Returns whether the grab was accepted.
        /// </summary>
        public bool ServerGrab(DMValuable target, Vector3 worldGrabPoint)
        {
            if (!IsServer || target == null || !target.IsSpawned || target.IsExtracted)
            {
                return false;
            }
            if (doll.IsHiding || doll.IsDowned)
            {
                Debug.Log($"[DreamMovers] {name}: grab rejected, the doll is hiding or down");
                return false;
            }
            // The client traced against a slightly older world; allow some slack but no grabbing across the map.
            Vector3 view = ViewLocation;
            float distance = Vector3.Distance(view, worldGrabPoint);
            if (distance > grabRange * 1.5f)
            {
                Debug.Log($"[DreamMovers] {name}: grab rejected, point {distance:0.00} m away");
                return false;
            }
            // The point has to be on the item, so the item itself is within reach too.
            float toSurface = float.MaxValue;
            foreach (Collider part in target.GetComponentsInChildren<Collider>())
            {
                toSurface = Mathf.Min(toSurface, Vector3.Distance(part.ClosestPoint(worldGrabPoint), worldGrabPoint));
            }
            if (toSurface > grabPointTolerance)
            {
                Debug.Log($"[DreamMovers] {name}: grab rejected, point {toSurface:0.00} m from {target.DisplayName}");
                return false;
            }
            // And nothing solid (a wall, another item) between the eyes and that point.
            Vector3 past = worldGrabPoint + (worldGrabPoint - view).normalized * 0.05f;
            if (DMPhysics.Linecast(view, past, transform, out RaycastHit hit) && !hit.collider.transform.IsChildOf(target.transform))
            {
                Debug.Log($"[DreamMovers] {name}: grab rejected, {hit.collider.name} is in the way");
                return false;
            }

            ServerDropHeld();
            serverHeld = target;
            held.Value = target.NetworkObject;
            localGrabOffset.Value = target.transform.InverseTransformPoint(worldGrabPoint);
            holdDistance = Mathf.Clamp(distance, minHoldDistance, maxHoldDistance);
            target.AddHolder();
            return true;
        }

        /// <summary>Server: tests set the hold distance directly.</summary>
        public void ServerSetHoldDistance(float distance) => holdDistance = Mathf.Clamp(distance, minHoldDistance, maxHoldDistance);

        public void ServerDropHeld()
        {
            if (!IsServer)
            {
                return;
            }
            if (serverHeld != null && serverHeld.IsSpawned)
            {
                serverHeld.RemoveHolder();
            }
            serverHeld = null;
            if (IsSpawned)
            {
                held.Value = default;
            }
        }

        void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || serverHeld == null)
            {
                return;
            }
            // The item may have been banked or shattered by someone else.
            if (!serverHeld.IsSpawned || serverHeld.IsExtracted)
            {
                serverHeld = null;
                held.Value = default;
                return;
            }
            ApplyForce();
        }

        void ApplyForce()
        {
            Rigidbody body = serverHeld.Body;
            Vector3 grabPoint = serverHeld.transform.TransformPoint(localGrabOffset.Value);
            Vector3 target = ViewLocation + AimDirection * holdDistance;
            Vector3 offset = target - grabPoint;
            if (offset.magnitude > breakDistance)
            {
                ServerDropHeld();
                return;
            }

            // Mass-scaled damped spring, so light and heavy items feel equally responsive until the cap kicks in.
            // Gravity compensation is shared between holders; otherwise two holders would fling light items upward.
            float mass = serverHeld.MassKg;
            int holders = Mathf.Max(1, serverHeld.NumHolders);
            Vector3 pointVelocity = body.GetPointVelocity(grabPoint);
            Vector3 accel = offset * (springFrequency * springFrequency)
                - pointVelocity * (2f * springDampingRatio * springFrequency)
                - Physics.gravity / holders;

            // The cap is what makes heavy items a two-person job.
            Vector3 force = Vector3.ClampMagnitude(accel * mass, maxLiftKg * -Physics.gravity.y);
            body.AddForceAtPosition(force, grabPoint, ForceMode.Force);
            if (trace && Time.time - lastTrace > 0.5f)
            {
                lastTrace = Time.time;
                Debug.Log($"[GrabTrace] {name} holds {serverHeld.DisplayName} ({mass} kg, {holders} holders): offset {offset} force {force} " +
                    $"kinematic {body.isKinematic} sleeping {body.IsSleeping()} velocity {body.linearVelocity} at {body.position}");
            }
        }

        void LateUpdate()
        {
            DMValuable item = HeldValuable;
            if (item == null)
            {
                thread.enabled = false;
                return;
            }
            thread.SetPosition(0, ThreadStart);
            thread.SetPosition(1, item.transform.TransformPoint(localGrabOffset.Value));
            thread.enabled = true;
        }
    }
}
