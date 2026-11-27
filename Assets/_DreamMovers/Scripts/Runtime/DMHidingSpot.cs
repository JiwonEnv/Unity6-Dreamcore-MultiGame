using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// A wardrobe a doll can climb into. Inside, the butler's candlelight cannot find you and nothing can catch you,
    /// but the doors creak on the way in and out, and the bell horse hears that. Empty hands only.
    /// The root sits on the floor; the front (the side you step out of) faces +Z. Ported from the UE5 ADMHidingSpot.
    /// </summary>
    public class DMHidingSpot : NetworkBehaviour
    {
        public static readonly List<DMHidingSpot> All = new List<DMHidingSpot>();

        [Tooltip("Outer size in meters (width, height, depth).")]
        [SerializeField] Vector3 size = new Vector3(1.3f, 2f, 0.7f);
        [Tooltip("One doll for a narrow wardrobe, two for a wide one.")]
        [SerializeField, Range(1, 2)] int capacity = 2;
        [Tooltip("The doors creak this far (m) when someone gets in or out.")]
        [SerializeField] float doorNoiseRadius = 9f;
        [Tooltip("How close (m, from the front face) a doll must stand to get in.")]
        [SerializeField] float reachDistance = 1.2f;

        readonly NetworkVariable<Vector3> syncedSize = new NetworkVariable<Vector3>();
        readonly NetworkVariable<int> syncedCapacity = new NetworkVariable<int>();
        /// <summary>For the HUD prompt.</summary>
        readonly NetworkVariable<int> numOccupants = new NetworkVariable<int>();

        readonly List<DMDoll> occupants = new List<DMDoll>();
        Transform box;

        public Vector3 Size => size;
        public int Capacity => capacity;
        public int NumOccupants => numOccupants.Value;
        public bool HasRoom => numOccupants.Value < capacity;

        void Awake()
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(transform, false);
            body.GetComponent<MeshRenderer>().sharedMaterial = DMShapes.SharedMaterial;
            DMShapes.Tint(body.GetComponent<MeshRenderer>(), new Color(0.38f, 0.24f, 0.16f), 0.7f);
            box = body.transform;
            ApplyShape();
        }

        /// <summary>Server, before Spawn.</summary>
        public void Configure(Vector3 outerSize, int dolls)
        {
            size = outerSize;
            capacity = Mathf.Clamp(dolls, 1, 2);
            syncedSize.Value = size;
            syncedCapacity.Value = capacity;
            ApplyShape();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsServer)
            {
                syncedSize.Value = size;
                syncedCapacity.Value = capacity;
            }
            else
            {
                size = syncedSize.Value;
                capacity = syncedCapacity.Value;
            }
            ApplyShape();
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
        }

        void ApplyShape()
        {
            if (box == null)
            {
                return;
            }
            box.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            box.localScale = size;
        }

        /// <summary>Closest wardrobe the doll can reach: in front of the doors, on the same floor, nothing in between. Null if none.</summary>
        public static DMHidingSpot FindReachable(DMDoll doll)
        {
            if (doll == null)
            {
                return null;
            }
            DMHidingSpot best = null;
            float bestDistance = float.MaxValue;
            foreach (DMHidingSpot spot in All)
            {
                // Distance to the front face, measured in the wardrobe's own space.
                Vector3 local = spot.transform.InverseTransformPoint(doll.transform.position + Vector3.up * 0.05f);
                float front = local.z - spot.size.z * 0.5f;
                bool inFront = front > -0.1f && front < spot.reachDistance && Mathf.Abs(local.x) < spot.size.x * 0.5f + 0.3f;
                // Standing (or crouching) on the wardrobe's own floor, not on something above or below it.
                bool sameFloor = local.y > -0.2f && local.y < spot.size.y;
                if (!inFront || !sameFloor || front >= bestDistance)
                {
                    continue;
                }
                // Nothing between the doll and the doors.
                Vector3 from = doll.Center;
                Vector3 doors = spot.transform.TransformPoint(new Vector3(Mathf.Clamp(local.x, -spot.size.x * 0.5f, spot.size.x * 0.5f),
                    spot.transform.InverseTransformPoint(from).y, spot.size.z * 0.5f + 0.01f));
                if (DMPhysics.Linecast(from, doors, doll.transform, out RaycastHit hit) && !hit.collider.transform.IsChildOf(spot.transform))
                {
                    continue;
                }
                best = spot;
                bestDistance = front;
            }
            return best;
        }

        void PurgeOccupants()
        {
            occupants.RemoveAll(d => d == null || !d.IsSpawned || d.IsDowned);
            numOccupants.Value = occupants.Count;
        }

        float SideOffset(int slot) => capacity > 1 ? (slot == 0 ? -0.25f : 0.25f) * size.x : 0f;

        Vector3 SlotLocation(int slot) => transform.TransformPoint(new Vector3(SideOffset(slot), 0.05f, 0f));

        Vector3 ExitLocation(int slot, float extra = 0f) => transform.TransformPoint(new Vector3(SideOffset(slot), 0.05f, size.z * 0.5f + 0.5f + extra));

        /// <summary>Server: puts the doll inside if there is room.</summary>
        public bool TryEnter(DMDoll doll)
        {
            if (!IsServer || doll == null)
            {
                return false;
            }
            PurgeOccupants();
            if (!HasRoom || occupants.Contains(doll))
            {
                return false;
            }
            // Take the free side so two dolls do not end up on top of each other.
            int slot = 0;
            if (capacity > 1 && occupants.Count == 1)
            {
                slot = transform.InverseTransformPoint(occupants[0].transform.position).x < 0f ? 1 : 0;
            }
            occupants.Add(doll);
            numOccupants.Value = occupants.Count;
            DMNoise.Report(transform.position, doorNoiseRadius, gameObject);
            // Facing out through the doors, the way you would peek through the gap.
            doll.EnterHiding(this, SlotLocation(slot), transform.eulerAngles.y);
            return true;
        }

        /// <summary>Room for a standing doll and floor under it.</summary>
        bool IsExitClear(DMDoll doll, Vector3 feet)
        {
            Vector3 bottom = feet + Vector3.up * (DMDoll.Radius + 0.05f);
            Vector3 top = feet + Vector3.up * (DMDoll.StandingHeight - DMDoll.Radius);
            foreach (Collider hit in Physics.OverlapCapsule(bottom, top, DMDoll.Radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(doll.transform))
                {
                    return false;
                }
            }
            return Physics.Raycast(feet + Vector3.up * 0.1f, Vector3.down, 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Server: lets the doll out in front of the doors. Tries each exit spot and moves the doll only to one that is
        /// free and has floor under it; if every one is blocked the doll stays inside and this returns false.
        /// </summary>
        public bool Leave(DMDoll doll)
        {
            if (!IsServer || doll == null)
            {
                return false;
            }
            int index = occupants.IndexOf(doll);
            if (index < 0)
            {
                return false;
            }
            int slot = capacity > 1 && transform.InverseTransformPoint(doll.transform.position).x > 0f ? 1 : 0;
            // Own side first, then the other side, then straight out and a step further.
            var exits = new List<Vector3> { ExitLocation(slot) };
            if (capacity > 1)
            {
                exits.Add(ExitLocation(1 - slot));
            }
            exits.Add(transform.TransformPoint(new Vector3(0f, 0.05f, size.z * 0.5f + 0.5f)));
            exits.Add(transform.TransformPoint(new Vector3(0f, 0.05f, size.z * 0.5f + 1f)));
            Vector3? exit = null;
            foreach (Vector3 candidate in exits)
            {
                if (IsExitClear(doll, candidate))
                {
                    exit = candidate;
                    break;
                }
            }
            if (exit == null)
            {
                // Still inside, still hidden, still taking up the place.
                Debug.Log($"[DreamMovers] {doll.name}: every exit of {name} is blocked; staying inside");
                return false;
            }
            occupants.RemoveAt(index);
            numOccupants.Value = occupants.Count;
            DMNoise.Report(transform.position, doorNoiseRadius, gameObject);
            doll.LeaveHiding(exit.Value);
            return true;
        }

        /// <summary>Server: the doll is gone (caught, left the game); frees its place without moving it.</summary>
        public void Release(DMDoll doll)
        {
            if (IsServer && occupants.Remove(doll) && IsSpawned)
            {
                numOccupants.Value = occupants.Count;
            }
        }
    }
}
