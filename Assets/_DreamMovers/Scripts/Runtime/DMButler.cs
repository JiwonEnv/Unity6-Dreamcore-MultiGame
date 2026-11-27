using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// Candlestick Butler: walks a patrol loop, and its candle is a real spotlight that is also its sight.
    /// A doll that stays in the light (with line of sight) for Spot Seconds is spotted: the butler rings its hand bell,
    /// which the bell horse hears from far away, and follows slower than a walking doll. Hiding behind furniture or a
    /// carried antique breaks line of sight. Ported from the UE5 ADMButler (meters, m/s).
    /// </summary>
    public class DMButler : DMMonster
    {
        static readonly Color Velvet = new Color(0.42f, 0.04f, 0.06f);
        static readonly Color Brass = new Color(0.85f, 0.62f, 0.28f);
        static readonly Color Wax = new Color(0.95f, 0.92f, 0.82f);
        static readonly Color Shadow = new Color(0.07f, 0.06f, 0.06f);
        static readonly Color CalmLight = new Color(1f, 0.72f, 0.42f);
        static readonly Color AlarmLight = new Color(1f, 0.3f, 0.15f);

        [SerializeField] float sightRange = 10f;
        [Tooltip("Half angle of the light cone, in degrees.")]
        [SerializeField] float sightHalfAngle = 32f;
        [Tooltip("Seconds in the light before being spotted (faster up close).")]
        [SerializeField] float spotSeconds = 0.6f;
        [SerializeField] float loseSightSeconds = 3f;
        [SerializeField] float patrolSpeed = 1.4f;
        [Tooltip("Slower than a walking doll (3.3): being seen is a chase you can win.")]
        [SerializeField] float chaseSpeed = 2.7f;
        [SerializeField] float bellRadius = 40f;
        [SerializeField] float bellInterval = 5f;
        [SerializeField] float pausePerPoint = 1.5f;
        [SerializeField] float searchSeconds = 3f;
        [Tooltip("A search whose goal cannot be reached in this long ends anyway, back to patrol.")]
        [SerializeField] float maxSearchSeconds = 12f;
        [Tooltip("After giving up on a doll it could see but not reach (under furniture, behind a barrier), the butler " +
            "leaves a doll that stays at that spot alone for this long. Moving away from the spot ends it at once.")]
        [SerializeField] float unreachableIgnoreSeconds = 10f;
        [Tooltip("World patrol loop; set by the game mode on generated maps.")]
        [SerializeField] List<Vector3> patrolPoints = new List<Vector3>();

        readonly NetworkVariable<byte> suspicion = new NetworkVariable<byte>();
        readonly List<Vector3> points = new List<Vector3>();

        Light candlelight;
        int patrolIndex;
        float pauseUntil;
        float spotProgress;
        DMDoll target;
        Vector3 lastKnownLocation;
        float lastSeenTime;
        float lastBellTime = -100f;
        float lastRepathTime;
        /// <summary>When the butler started standing still in a chase without being able to catch (0 = moving or catching).</summary>
        float chaseStallSince;
        Vector3 unreachableSpot;
        float ignoreUnreachableUntil;
        float lastTraceTime;
        bool trace;

        public override string DisplayName => "촛대 집사";
        // About 190 cm: a head taller than the dolls' whole body.
        public override float Radius => 0.3f;
        public override float Height => 1.9f;
        /// <summary>0..1, how close it is to spotting someone. Drives the "?" over its head.</summary>
        public float Suspicion => suspicion.Value / 255f;
        public Light Candlelight => candlelight;
        public float SightRange => sightRange;

        protected override void Awake()
        {
            base.Awake();
            trace = DMArgs.Has("dmbutlertrace");
        }

        /// <summary>Before Spawn (server): the patrol loop in world space.</summary>
        public void SetPatrolPoints(List<Vector3> worldPoints) => patrolPoints = new List<Vector3>(worldPoints);

        protected override void BuildBody()
        {
            Vector3 thin = new Vector3(0.07f, 0.07f, 0.95f);
            AddPart("LegL", DMShape.Cylinder, new Vector3(0f, -12f, -47f), thin, Shadow);
            AddPart("LegR", DMShape.Cylinder, new Vector3(0f, 12f, -47f), thin, Shadow);
            AddPart("Coat", DMShape.Cylinder, new Vector3(0f, 0f, 25f), new Vector3(0.42f, 0.36f, 0.7f), Velvet, 0.85f);
            AddPart("WaxL", DMShape.Cylinder, new Vector3(10f, -20f, 5f), new Vector3(0.12f, 0.12f, 0.35f), Wax, 0.4f);
            AddPart("WaxR", DMShape.Cylinder, new Vector3(10f, 20f, 5f), new Vector3(0.12f, 0.12f, 0.35f), Wax, 0.4f);
            AddPart("ArmL", DMShape.Cylinder, new Vector3(12f, -30f, 25f), new Vector3(0.05f, 0.05f, 0.6f), Shadow, 0.6f, 0f, new Vector3(0f, 0f, -20f));
            AddPart("ArmR", DMShape.Cylinder, new Vector3(12f, 30f, 25f), new Vector3(0.05f, 0.05f, 0.6f), Shadow, 0.6f, 0f, new Vector3(0f, 0f, 20f));
            AddPart("BellL", DMShape.Cone, new Vector3(18f, -40f, -8f), Vector3.one * 0.14f, Brass, 0.2f, 1f);
            AddPart("BellR", DMShape.Cone, new Vector3(18f, 40f, -8f), Vector3.one * 0.14f, Brass, 0.2f, 1f);
            AddPart("Dish", DMShape.Cylinder, new Vector3(0f, 0f, 64f), new Vector3(0.7f, 0.7f, 0.03f), Brass, 0.2f, 1f);
            AddPart("Cup", DMShape.Cylinder, new Vector3(0f, 0f, 78f), new Vector3(0.18f, 0.18f, 0.25f), Brass, 0.2f, 1f);
            AddPart("Candle", DMShape.Cylinder, new Vector3(0f, 0f, 100f), new Vector3(0.1f, 0.1f, 0.25f), Wax, 0.5f);
            AddPart("Flame", DMShape.Sphere, new Vector3(0f, 0f, 118f), new Vector3(0.07f, 0.07f, 0.13f), new Color(1f, 0.65f, 0.2f), 0.9f);

            // The light is the sight: keep them in sync with the tuning values.
            var lightObject = new GameObject("Candlelight");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.95f + 1.12f, 0.12f);
            lightObject.transform.localRotation = Quaternion.Euler(14f, 0f, 0f);
            candlelight = lightObject.AddComponent<Light>();
            candlelight.type = LightType.Spot;
            candlelight.range = sightRange;
            candlelight.spotAngle = sightHalfAngle * 2f;
            candlelight.innerSpotAngle = sightHalfAngle * 1.2f;
            candlelight.color = CalmLight;
            candlelight.intensity = 3f;
            candlelight.shadows = LightShadows.Soft;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                if (patrolPoints.Count == 0)
                {
                    patrolPoints.Add(transform.position);
                }
                ResumePatrol();
            }
        }

        protected override void OnMonsterStateChanged()
        {
            bool alarmed = State == DMMonsterState.Chase;
            candlelight.color = alarmed ? AlarmLight : CalmLight;
            candlelight.intensity = alarmed ? 6f : 3f;
        }

        /// <summary>In the candlelight cone, within Sight Range, and not blocked (walls, furniture, carried antiques).</summary>
        public bool CanSee(DMDoll doll) => CanSee(doll, out _, out _);

        public bool CanSee(DMDoll doll, out float distance, out string why)
        {
            distance = 0f;
            if (doll == null || doll.IsDowned || doll.IsHiding)
            {
                why = "no doll, down or in a wardrobe";
                return false; // a closed wardrobe blocks the candlelight
            }
            Vector3 eye = candlelight.transform.position;
            Vector3 forward = candlelight.transform.forward;
            float maxCos = Mathf.Cos(sightHalfAngle * Mathf.Deg2Rad);
            // The eyes, and any part of the body that is not under something (a doll half under a desk shows the other half).
            doll.GetExposedPoints(points);
            string last = string.Empty;
            foreach (Vector3 point in points)
            {
                Vector3 toTarget = point - eye;
                float d = toTarget.magnitude;
                if (d > sightRange)
                {
                    last = $"out of range {d:0.0}";
                    continue;
                }
                float cos = Vector3.Dot(toTarget / Mathf.Max(d, 0.01f), forward);
                if (cos < maxCos)
                {
                    last = $"outside the cone ({Mathf.Acos(cos) * Mathf.Rad2Deg:0} deg off the light axis)";
                    continue;
                }
                if (DMPhysics.Linecast(eye, point, transform, out RaycastHit hit) && !hit.collider.transform.IsChildOf(doll.transform))
                {
                    last = $"blocked by {hit.collider.name} at {hit.point}";
                    continue; // behind a wall, furniture or a carried antique
                }
                distance = d;
                why = $"visible: candle {eye} -> {point} ({points.Count - 1} of 8 points exposed)";
                return true;
            }
            why = $"{last} ({points.Count - 1} of 8 body points exposed)";
            return false;
        }

        DMDoll FindVisibleDoll(out float distance)
        {
            DMDoll best = null;
            distance = float.MaxValue;
            bool ignoring = Time.time < ignoreUnreachableUntil;
            foreach (DMDoll doll in DMDoll.All)
            {
                // Still at the spot it just gave up on: it already knows it cannot get there.
                Vector3 flat = doll.transform.position - unreachableSpot;
                flat.y = 0f;
                if (ignoring && flat.magnitude < 1f)
                {
                    continue;
                }
                if (CanSee(doll, out float d, out _) && d < distance)
                {
                    best = doll;
                    distance = d;
                }
            }
            return best;
        }

        void RingBell()
        {
            lastBellTime = Time.time;
            DMNoise.Report(transform.position, bellRadius, gameObject);
        }

        /// <summary>Walk to the last known spot (or the nearest walkable point to it) and look around.</summary>
        void BeginSearch(bool unreachable)
        {
            if (unreachable)
            {
                unreachableSpot = lastKnownLocation;
                ignoreUnreachableUntil = Time.time + unreachableIgnoreSeconds;
            }
            Debug.Log($"[DreamMovers] {name}: {(unreachable ? "cannot reach the doll" : "lost sight of the doll")}, searching around {lastKnownLocation}");
            chaseStallSince = 0f;
            SetState(DMMonsterState.Search);
            spotProgress = 0f;
            // The last place it saw the doll, not where the doll is now.
            MoveTo(ReachablePoint(lastKnownLocation), chaseSpeed);
        }

        void ResumePatrol()
        {
            spotProgress = 0f;
            target = null;
            SetState(DMMonsterState.Patrol);
            if (patrolPoints.Count == 0)
            {
                return;
            }
            // Continue from the nearest point rather than walking back across the house.
            float best = float.MaxValue;
            for (int i = 0; i < patrolPoints.Count; ++i)
            {
                float d = (patrolPoints[i] - transform.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    patrolIndex = i;
                }
            }
            pauseUntil = 0f;
            MoveTo(patrolPoints[patrolIndex], patrolSpeed);
        }

        protected override void ServerThink(float deltaTime)
        {
            float now = Time.time;
            if (TryCatch() != null)
            {
                ResumePatrol();
                return;
            }

            switch (State)
            {
                case DMMonsterState.Patrol:
                case DMMonsterState.Search:
                {
                    DMDoll seen = FindVisibleDoll(out float distance);
                    if (seen != null)
                    {
                        // Up close it notices about twice as fast.
                        spotProgress += deltaTime * (2f - distance / sightRange);
                        if (spotProgress >= spotSeconds)
                        {
                            target = seen;
                            lastKnownLocation = seen.transform.position;
                            lastSeenTime = now;
                            SetState(DMMonsterState.Chase);
                            chaseStallSince = 0f;
                            RingBell();
                            MoveTo(ReachablePoint(seen.transform.position), chaseSpeed, true);
                            break;
                        }
                    }
                    else
                    {
                        spotProgress = Mathf.Max(0f, spotProgress - deltaTime * 0.5f);
                    }

                    if (State == DMMonsterState.Search)
                    {
                        // Done looking around, or the goal turned out to be out of reach: never wait on it forever.
                        if ((IsMoveDone() && StateAge > searchSeconds) || StateAge > maxSearchSeconds)
                        {
                            ResumePatrol();
                        }
                    }
                    else if (patrolPoints.Count > 1 && IsMoveDone())
                    {
                        if (pauseUntil == 0f)
                        {
                            pauseUntil = now + pausePerPoint;
                        }
                        else if (now >= pauseUntil)
                        {
                            pauseUntil = 0f;
                            patrolIndex = (patrolIndex + 1) % patrolPoints.Count;
                            MoveTo(patrolPoints[patrolIndex], patrolSpeed);
                        }
                    }
                    break;
                }

                case DMMonsterState.Chase:
                {
                    DMDoll chased = target;
                    if (chased == null || !chased.IsSpawned || chased.IsDowned)
                    {
                        ResumePatrol();
                        break;
                    }
                    bool sees = CanSee(chased);
                    if (sees)
                    {
                        lastSeenTime = now;
                        lastKnownLocation = chased.transform.position;
                    }
                    if (now - lastSeenTime > loseSightSeconds)
                    {
                        // Lost them: check where they were last seen, then give up.
                        BeginSearch(false);
                        break;
                    }
                    // Standing still without being able to catch them: there is no way to them (under a desk, behind
                    // something it cannot walk through). Waiting would last forever, so search instead.
                    Vector3 flatVelocity = Velocity;
                    flatVelocity.y = 0f;
                    bool still = flatVelocity.magnitude < 0.2f;
                    bool stalled = still && !CanCatch(chased);
                    if (still)
                    {
                        // Standing still in a chase it faces the doll (or where it last saw it), candle first.
                        Vector3 toward = (sees ? chased.transform.position : lastKnownLocation) - transform.position;
                        toward.y = 0f;
                        if (toward.sqrMagnitude > 0.0001f)
                        {
                            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toward), 270f * deltaTime);
                        }
                    }
                    chaseStallSince = stalled ? (chaseStallSince == 0f ? now : chaseStallSince) : 0f;
                    if (stalled && now - chaseStallSince > loseSightSeconds)
                    {
                        BeginSearch(true);
                        break;
                    }
                    if (now - lastRepathTime > 0.4f)
                    {
                        lastRepathTime = now;
                        // Toward where it can actually stand: next to them if they are in view, else the last place it saw them.
                        MoveTo(ReachablePoint(sees ? chased.transform.position : lastKnownLocation), chaseSpeed, true);
                    }
                    if (now - lastBellTime > bellInterval)
                    {
                        RingBell();
                    }
                    break;
                }

                default:
                    ResumePatrol();
                    break;
            }

            suspicion.Value = State == DMMonsterState.Chase ? (byte)255 : (byte)(Mathf.Clamp01(spotProgress / spotSeconds) * 255f);
            Trace(now);
        }

        /// <summary>-dmbutlertrace: one line every 0.25 s with state, sight, timers and the move.</summary>
        void Trace(float now)
        {
            if (!trace || now - lastTraceTime < 0.25f)
            {
                return;
            }
            lastTraceTime = now;
            // The chased doll, or else any one, so the sight result is about the doll that matters.
            DMDoll doll = target != null ? target : DMDoll.All.Count > 0 ? DMDoll.All[0] : null;
            bool sees = CanSee(doll, out _, out string why);
            Debug.Log($"[ButlerTrace] t={now:0.00} state={State} age={StateAge:0.00} suspicion={suspicion.Value} spot={spotProgress:0.00} sees={(sees ? 1 : 0)} ({why}) " +
                $"since-seen={(lastSeenTime > 0f ? now - lastSeenTime : -1f):0.00} last-known={lastKnownLocation} doll={(doll != null ? doll.transform.position.ToString() : "none")} " +
                $"{DescribeMove()} pos={transform.position} speed={Velocity.magnitude:0.00}");
        }
    }
}
