using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace DreamMovers
{
    /// <summary>
    /// Base for the living antiques. Brains run on the server only (a small state machine in ServerThink, moving with a
    /// NavMeshAgent over the runtime navmesh); clients see the synced transform and state. Touching a doll downs it.
    /// Pivot at the feet. Ported from the UE5 ADMMonster.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public abstract class DMMonster : NetworkBehaviour
    {
        public static readonly List<DMMonster> All = new List<DMMonster>();

        [Tooltip("Extra reach (m) beyond the two capsules touching.")]
        [SerializeField] protected float catchRadius = 0.4f;

        readonly NetworkVariable<DMMonsterState> monsterState = new NetworkVariable<DMMonsterState>(DMMonsterState.Idle);
        readonly List<Vector3> exposed = new List<Vector3>();

        protected NavMeshAgent agent;
        float stateStartTime;
        Vector3 lastMoveGoal;
        bool lastMoveAccepted;

        public DMMonsterState State => monsterState.Value;
        public abstract string DisplayName { get; }
        /// <summary>Capsule radius and height (m); the collider and the agent use them.</summary>
        public abstract float Radius { get; }
        public abstract float Height { get; }
        public Vector3 Velocity => agent != null && agent.enabled ? agent.velocity : Vector3.zero;
        public float StateAge => Time.time - stateStartTime;

        /// <summary>Builds the placeholder body from parts. Called from Awake on every machine.</summary>
        protected abstract void BuildBody();

        protected virtual void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.radius = Radius;
            agent.height = Height;
            agent.angularSpeed = 300f;
            agent.acceleration = 30f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            agent.enabled = false;

            var capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = Radius;
            capsule.height = Height;
            capsule.center = new Vector3(0f, Height * 0.5f, 0f);
            // Kinematic: it shoves antiques out of the way but nothing pushes it.
            var body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;

            var parts = new GameObject("Body").transform;
            parts.SetParent(transform, false);
            BuildBody();
        }

        /// <summary>
        /// A placeholder part from UE5 numbers: location (cm) from the capsule center (X forward, Y right, Z up),
        /// scale of the 100 cm engine shape, rotation (pitch, yaw, roll degrees).
        /// </summary>
        protected MeshRenderer AddPart(string partName, DMShape shape, Vector3 ueLocation, Vector3 ueScale, Color color,
            float roughness = 0.6f, float metallic = 0f, Vector3 uePitchYawRoll = default)
        {
            float halfHeightCm = Height * 50f;
            Vector3 position = new Vector3(ueLocation.y, ueLocation.z + halfHeightCm, ueLocation.x) * 0.01f;
            Vector3 size = new Vector3(ueScale.y, ueScale.z, ueScale.x);
            // UE pitch tips the top forward when negative; Unity's X rotation tips it forward when positive.
            Vector3 euler = new Vector3(-uePitchYawRoll.x, uePitchYawRoll.y, -uePitchYawRoll.z);
            return DMShapes.AddPart(transform.Find("Body"), partName, shape, position, size, color, roughness, metallic, euler);
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            monsterState.OnValueChanged += OnStateChanged;
            stateStartTime = Time.time;
            if (IsServer)
            {
                agent.enabled = true;
                agent.Warp(transform.position);
            }
            OnMonsterStateChanged();
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            monsterState.OnValueChanged -= OnStateChanged;
        }

        void OnStateChanged(DMMonsterState previous, DMMonsterState current)
        {
            if (!IsServer)
            {
                OnMonsterStateChanged();
            }
        }

        void Update()
        {
            if (IsSpawned && IsServer && !ThinkPaused && agent.enabled && agent.isOnNavMesh)
            {
                ServerThink(Time.deltaTime);
            }
        }

        /// <summary>Server only, every frame.</summary>
        protected virtual void ServerThink(float deltaTime) { }

        /// <summary>Server and clients, whenever the state changes.</summary>
        protected virtual void OnMonsterStateChanged() { }

        protected void SetState(DMMonsterState state)
        {
            if (monsterState.Value == state)
            {
                return;
            }
            Debug.Log($"[DreamMovers] {name}: {monsterState.Value} -> {state}");
            monsterState.Value = state;
            stateStartTime = Time.time;
            OnMonsterStateChanged();
        }

        /// <summary>rightUpTo (chasing): all the way to the goal, not stopping short of it.</summary>
        protected void MoveTo(Vector3 goal, float speed, bool rightUpTo = false)
        {
            agent.speed = speed;
            agent.stoppingDistance = rightUpTo ? 0.1f : 0.4f;
            lastMoveGoal = goal;
            lastMoveAccepted = agent.SetDestination(goal);
        }

        protected void StopMoving()
        {
            if (agent.isOnNavMesh)
            {
                agent.ResetPath();
            }
        }

        /// <summary>True once the current move finished or failed.</summary>
        protected bool IsMoveDone()
        {
            if (!lastMoveAccepted || agent.pathPending)
            {
                return !lastMoveAccepted;
            }
            return !agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid || agent.remainingDistance <= agent.stoppingDistance + 0.05f;
        }

        /// <summary>For logs: move status and goal.</summary>
        public string DescribeMove()
        {
            return $"move={(agent.pathPending ? "Pending" : agent.hasPath ? "Moving" : "Idle")} accepted={lastMoveAccepted} goal={lastMoveGoal} " +
                $"to-goal={Vector3.Distance(transform.position, lastMoveGoal):0.00} status={agent.pathStatus}";
        }

        /// <summary>Nearest walkable point to location, or location itself if none is close.</summary>
        protected static Vector3 ReachablePoint(Vector3 location)
        {
            return NavMesh.SamplePosition(location, out NavMeshHit hit, 2.5f, NavMesh.AllAreas) ? hit.position : location;
        }

        /// <summary>
        /// Close enough (Catch Radius beyond both capsules, similar height) and nothing static in between: a line from
        /// the top of the monster to the doll must not cross a wall, a tabletop or a wardrobe. Carried antiques and
        /// other characters do not shield anyone.
        /// </summary>
        public bool CanCatch(DMDoll doll)
        {
            if (doll == null || doll.IsDowned || doll.IsHiding)
            {
                return false;
            }
            Vector3 delta = doll.transform.position - transform.position;
            float reach = Radius + DMDoll.Radius + catchRadius;
            if (new Vector2(delta.x, delta.z).sqrMagnitude > reach * reach || Mathf.Abs(delta.y) >= 1.5f)
            {
                return false;
            }
            // Reaching down from above: a wall or the top of a desk the doll crawled under is in the way.
            Vector3 from = transform.position + Vector3.up * (Height * 0.95f);
            doll.GetExposedPoints(exposed);
            // The middle of the body, or any part sticking out from under cover.
            exposed[0] = doll.Center;
            foreach (Vector3 point in exposed)
            {
                if (!DMPhysics.StaticLinecast(from, point, transform, doll.transform))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Downs the first doll Can Catch allows.</summary>
        protected DMDoll TryCatch()
        {
            foreach (DMDoll doll in DMDoll.All.ToArray())
            {
                if (CanCatch(doll))
                {
                    doll.Down();
                    return doll;
                }
            }
            return null;
        }

        /// <summary>Tests: move the monster without walking (server).</summary>
        public void ServerPlace(Vector3 position, float yaw)
        {
            // Off the navmesh (outside the house) the agent refuses; the transform still moves for geometry checks.
            if (!agent.Warp(position))
            {
                transform.position = position;
            }
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Physics.SyncTransforms();
        }

        /// <summary>Tests: freeze or wake the brain (server).</summary>
        public bool ThinkPaused { get; set; }
    }
}
