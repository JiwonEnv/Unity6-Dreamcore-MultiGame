using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// A fragile antique the players carry to the extraction zone. Physics and value live on the server; clients only
    /// see the synced result (NetworkTransform + NetworkRigidbody make the body kinematic there). Every hard impact
    /// cracks it and costs value, and the last crack breaks it. Ported from the UE5 ADMValuable.
    /// The look is a child "Shape" sized from the spec, so the root stays unscaled for the network transform.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class DMValuable : NetworkBehaviour
    {
        // Settling into place after spawning produces small hits that should not count.
        const float HitGraceSeconds = 1f;
        // One collision usually reports several contacts over a few frames.
        const float CrackCooldownSeconds = 0.25f;
        const float NoiseCooldownSeconds = 0.3f;
        const float LoudBumpSpeed = 8f;
        // Angular damping while held keeps an off-center grab from spinning the item like a propeller.
        const float HeldAngularDamping = 8f;
        const float FreeAngularDamping = 0.1f;

        /// <summary>Every spawned antique on this machine (cotton balls too).</summary>
        public static readonly List<DMValuable> All = new List<DMValuable>();

        [Header("Valuable (items placed by hand; spawned ones take the spec)")]
        [SerializeField] string displayName = "골동품";
        [SerializeField, Min(0)] int baseValue = 1000;
        [Tooltip("Heavier than one player's grab strength (DMGrab.MaxLiftKg) means it needs two.")]
        [SerializeField, Min(0.1f)] protected float massKg = 2f;
        [Tooltip("Impact speed (m/s) along the contact normal that starts cracking it. Lower = more fragile.")]
        [SerializeField, Min(0.5f)] protected float crackSpeed = 3.5f;
        [Tooltip("Fraction of the base value lost by a crack right at Crack Speed; harder hits lose more.")]
        [SerializeField, Range(0f, 1f)] float lossPerCrack = 0.15f;
        [SerializeField, Min(1)] int maxCracks = 3;
        [SerializeField] protected DMShape shape = DMShape.Cube;
        [SerializeField] protected Vector3 size = new Vector3(0.3f, 0.3f, 0.3f);
        [SerializeField] protected Color tint = new Color(0.9f, 0.75f, 0.45f);
        [SerializeField, Range(0f, 1f)] protected float roughness = 0.6f;
        [SerializeField, Range(0f, 1f)] protected float metallic;

        [Header("Noise")]
        [Tooltip("Bumps slower than this (m/s) are silent.")]
        [SerializeField] protected float quietSpeed = 1.5f;
        [Tooltip("How far a bump carries (m): from a soft knock at Quiet Speed up to a clatter at 8 m/s. Cracks are louder.")]
        [SerializeField] Vector2 bumpNoiseRadius = new Vector2(3f, 15f);
        [SerializeField] float breakNoiseRadius = 25f;

        readonly NetworkVariable<int> currentValue = new NetworkVariable<int>();
        readonly NetworkVariable<int> cracks = new NetworkVariable<int>();
        readonly NetworkVariable<int> numHolders = new NetworkVariable<int>();
        /// <summary>Unset for items placed by hand, which carry their look in the fields above.</summary>
        readonly NetworkVariable<DMValuableSpec> spec = new NetworkVariable<DMValuableSpec>();

        Rigidbody body;
        MeshRenderer shapeRenderer;
        Transform shapeTransform;
        bool extracted;
        float hitsEnabledTime;
        float lastCrackTime = -10f;
        float lastNoiseTime = -10f;

        public Rigidbody Body => body;
        public virtual string DisplayName => displayName;
        public int CurrentValue => currentValue.Value;
        public int Cracks => cracks.Value;
        public int MaxCracks => maxCracks;
        public int NumHolders => numHolders.Value;
        public float MassKg => massKg;
        public bool IsExtracted => extracted;
        /// <summary>Full size of the shape in meters (for price tags and spawn checks).</summary>
        public Vector3 Size => size;

        protected virtual void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.angularDamping = FreeAngularDamping;
        }

        /// <summary>Server, before Spawn: turns a default item into this antique.</summary>
        public void InitFromSpec(DMValuableSpec value)
        {
            spec.Value = value;
            ApplySpec();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            spec.OnValueChanged += OnSpecChanged;
            cracks.OnValueChanged += OnCracksChanged;
            ApplySpec();
            if (IsServer)
            {
                currentValue.Value = baseValue;
                hitsEnabledTime = Time.time + HitGraceSeconds;
            }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            spec.OnValueChanged -= OnSpecChanged;
            cracks.OnValueChanged -= OnCracksChanged;
        }

        void OnSpecChanged(DMValuableSpec previous, DMValuableSpec current) => ApplySpec();

        void OnCracksChanged(int previous, int current) => ApplyLook();

        void ApplySpec()
        {
            DMValuableSpec s = spec.Value;
            if (s.IsSet)
            {
                displayName = s.Name.ToString();
                baseValue = s.Value;
                massKg = s.MassKg;
                crackSpeed = s.CrackSpeed;
                shape = s.Shape;
                size = s.Size;
                tint = s.Tint;
                roughness = s.Roughness;
                metallic = s.Metallic;
            }
            BuildShape();
            ApplyLook();
        }

        /// <summary>The child that holds the mesh and the collider, rebuilt when the shape changes.</summary>
        void BuildShape()
        {
            if (shapeTransform == null)
            {
                var child = new GameObject("Shape");
                child.transform.SetParent(transform, false);
                shapeTransform = child.transform;
                child.AddComponent<MeshFilter>();
                shapeRenderer = child.AddComponent<MeshRenderer>();
                shapeRenderer.sharedMaterial = DMShapes.SharedMaterial;
            }
            shapeTransform.localScale = DMShapes.ScaleFor(shape, size);
            Mesh mesh = DMShapes.GetMesh(shape);
            shapeTransform.GetComponent<MeshFilter>().sharedMesh = mesh;
            foreach (Collider old in shapeTransform.GetComponents<Collider>())
            {
                Destroy(old);
            }
            if (shape == DMShape.Cube)
            {
                shapeTransform.gameObject.AddComponent<BoxCollider>();
            }
            else
            {
                var hull = shapeTransform.gameObject.AddComponent<MeshCollider>();
                hull.sharedMesh = mesh;
                hull.convex = true;
            }
            body.mass = massKg;
        }

        void ApplyLook()
        {
            if (shapeRenderer == null)
            {
                return;
            }
            // Each crack darkens it, so damage reads at a glance before the HUD number does. A cracked surface loses its shine.
            float damage = maxCracks > 0 ? (float)cracks.Value / maxCracks : 0f;
            Color color = Color.Lerp(tint, new Color(0.08f, 0.06f, 0.06f), damage * 0.8f);
            DMShapes.Tint(shapeRenderer, color, Mathf.Lerp(roughness, 0.8f, damage), metallic);
        }

        /// <summary>Server: called by grabs as players start and stop holding this.</summary>
        public void AddHolder()
        {
            numHolders.Value++;
            body.angularDamping = HeldAngularDamping;
            body.WakeUp();
        }

        public void RemoveHolder()
        {
            numHolders.Value = Mathf.Max(0, numHolders.Value - 1);
            if (numHolders.Value == 0)
            {
                body.angularDamping = FreeAngularDamping;
            }
        }

        /// <summary>Server: bank the current value and remove the item.</summary>
        public virtual void Extract()
        {
            if (!IsServer || extracted)
            {
                return;
            }
            extracted = true;
            if (DMGameState.Instance != null)
            {
                DMGameState.Instance.AddCollected(currentValue.Value);
            }
            NetworkObject.Despawn(true);
        }

        /// <summary>For subclasses that leave through the extraction zone without banking value.</summary>
        protected void MarkExtracted() => extracted = true;

        void OnCollisionEnter(Collision collision)
        {
            if (!IsSpawned || !IsServer || extracted)
            {
                return;
            }
            float now = Time.time;
            if (now < hitsEnabledTime || now - lastCrackTime < CrackCooldownSeconds || collision.contactCount == 0)
            {
                return;
            }
            ContactPoint contact = collision.GetContact(0);
            // Relative velocity before the hit, along the contact normal.
            float impactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            bool crack = impactSpeed >= crackSpeed;
            if (impactSpeed >= quietSpeed && (crack || now - lastNoiseTime >= NoiseCooldownSeconds))
            {
                lastNoiseTime = now;
                float t = Mathf.InverseLerp(quietSpeed, LoudBumpSpeed, impactSpeed);
                float radius = Mathf.Lerp(bumpNoiseRadius.x, bumpNoiseRadius.y, t);
                DMNoise.Report(contact.point, crack ? radius * 1.5f : radius, gameObject);
            }
            if (crack)
            {
                lastCrackTime = now;
                Crack(impactSpeed);
            }
        }

        void Crack(float impactSpeed)
        {
            cracks.Value++;
            if (cracks.Value >= maxCracks)
            {
                DMNoise.Report(transform.position, breakNoiseRadius, gameObject);
                currentValue.Value = 0;
                extracted = true;
                if (DMGameState.Instance != null)
                {
                    DMGameState.Instance.AddBroken(baseValue);
                }
                NetworkObject.Despawn(true);
                return;
            }
            // A hit twice as hard as Crack Speed costs twice as much.
            float loss = lossPerCrack * (impactSpeed / crackSpeed);
            currentValue.Value = Mathf.Max(0, currentValue.Value - Mathf.RoundToInt(baseValue * loss));
            ApplyLook();
        }
    }
}
