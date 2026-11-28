using System.Collections.Generic;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// The music box bed. Items let go of inside the box are banked; items still on a thread are not, so players can
    /// carry through it without cashing in by accident. Banking runs on the server; every machine sees the pad.
    /// Ported from the UE5 ADMExtractionZone (3 x 3 x 2 m).
    /// </summary>
    public class DMExtractionZone : MonoBehaviour
    {
        public static DMExtractionZone Instance { get; private set; }

        [Tooltip("Zone size in meters, centered on this object.")]
        [SerializeField] Vector3 size = new Vector3(3f, 2f, 3f);
        [SerializeField, Min(0.05f)] float checkInterval = 0.2f;

        readonly Collider[] inside = new Collider[64];
        readonly HashSet<DMValuable> found = new HashSet<DMValuable>();
        float nextCheck;

        public Vector3 Size => size;

        void Awake()
        {
            Instance = this;
            // Floor pad so the zone is visible until it has real art.
            MeshRenderer pad = DMShapes.AddPart(transform, "Pad", DMShape.Cylinder, new Vector3(0f, -size.y * 0.5f + 0.02f, 0f),
                new Vector3(size.x, 0.04f, size.z), new Color(0.95f, 0.75f, 0.35f));
            pad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>The item's center is inside the zone.</summary>
        public bool Contains(Vector3 point)
        {
            Vector3 local = transform.InverseTransformPoint(point);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f;
        }

        void Update()
        {
            if (!DMNet.IsServer || Time.time < nextCheck)
            {
                return;
            }
            nextCheck = Time.time + checkInterval;
            BankItemsInside();
        }

        void BankItemsInside()
        {
            found.Clear();
            int count = Physics.OverlapBoxNonAlloc(transform.position, size * 0.5f, inside, transform.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; ++i)
            {
                DMValuable valuable = inside[i].GetComponentInParent<DMValuable>();
                if (valuable != null && valuable.IsSpawned)
                {
                    found.Add(valuable);
                }
            }
            foreach (DMValuable valuable in found)
            {
                if (valuable.NumHolders == 0)
                {
                    valuable.Extract();
                }
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.95f, 0.75f, 0.35f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
