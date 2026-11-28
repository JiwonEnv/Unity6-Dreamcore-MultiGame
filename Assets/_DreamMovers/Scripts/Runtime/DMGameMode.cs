using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ProcLevelGen;
using Unity.Netcode;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// One dream run, server only: the target is a share of everything on the map that grows with the player count,
    /// the timer is the dream's stability. Banking the target wins, running out of stability (or everyone caught)
    /// fails; the round restarts on its own after a short summary, on a new map.
    /// On a generated map it fills the layout (wardrobes, antiques), waits for the navmesh, then spawns the monsters and
    /// starts the timer. Ported from the UE5 ADMGameMode; a restart rebuilds in place instead of reloading the level.
    /// </summary>
    public class DMGameMode : MonoBehaviour
    {
        /// <summary>The butler starts in the chapel (GDD 6.3), or the deepest room if this layout has none.</summary>
        const string ButlerTheme = "Chapel";

        public static DMGameMode Instance { get; private set; }

        [Header("Prefabs (made by Dream Movers > Build Gameplay Prefabs)")]
        [SerializeField] NetworkObject gameStatePrefab;
        [SerializeField] NetworkObject dollPrefab;
        [SerializeField] NetworkObject valuablePrefab;
        [SerializeField] NetworkObject cottonBallPrefab;
        [SerializeField] NetworkObject hidingSpotPrefab;
        [SerializeField] NetworkObject butlerPrefab;
        [SerializeField] NetworkObject bellHorsePrefab;

        [Header("Scene")]
        [Tooltip("Empty when the scene has no generated map (a hand-built level).")]
        [SerializeField] PLGGenerator generator;
        [SerializeField] Transform[] playerStarts;

        [Header("Round")]
        [SerializeField, Min(10f)] float roundSeconds = 720f;
        [Tooltip("Target = map value x (Base + Per Player x players): 30% solo, 60% for four.")]
        [SerializeField, Range(0f, 1f)] float baseTargetShare = 0.2f;
        [SerializeField, Range(0f, 1f)] float targetSharePerPlayer = 0.1f;
        [SerializeField, Min(1f)] float summarySeconds = 8f;
        [Tooltip("A generated map whose navmesh is not ready after this long aborts the round instead of starting it.")]
        [SerializeField, Min(1f)] float navReadyTimeoutSeconds = 30f;

        int mapValue;
        int expectedAntiques;
        int blockedAntiqueSpawns;
        int wardrobes;
        bool started;
        Coroutine prepare;
        Coroutine check;

        public int MapValue => mapValue;
        /// <summary>Generated maps: antiques that should be in the map, and how many spawn spots were already blocked.</summary>
        public int ExpectedAntiques => expectedAntiques;
        public int BlockedAntiqueSpawns => blockedAntiqueSpawns;
        public int Wardrobes => wardrobes;
        public PLGGenerator Generator => generator;
        /// <summary>Tests: the solo doll may be caught on purpose without the round failing at once.</summary>
        public bool IgnoreAllDown { get; set; }

        void Awake()
        {
            Instance = this;
            if (DMArgs.TryGet("dmnavtimeout=", out string timeout) && float.TryParse(timeout, out float seconds))
            {
                navReadyTimeoutSeconds = seconds;
            }
        }

        void OnEnable()
        {
            StartCoroutine(HookNetwork());
        }

        IEnumerator HookNetwork()
        {
            while (NetworkManager.Singleton == null)
            {
                yield return null;
            }
            NetworkManager manager = NetworkManager.Singleton;
            manager.OnServerStarted += OnServerStarted;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            if (manager.IsServer && !started)
            {
                OnServerStarted();
            }
        }

        void OnDestroy()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null)
            {
                manager.OnServerStarted -= OnServerStarted;
                manager.OnClientConnectedCallback -= OnClientConnected;
                manager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            if (generator != null)
            {
                generator.Generated -= OnGenerated;
            }
            if (Instance == this)
            {
                Instance = null;
            }
        }

        static NetworkManager Net => NetworkManager.Singleton;
        static DMGameState State => DMGameState.Instance;

        void OnServerStarted()
        {
            if (started)
            {
                return;
            }
            started = true;
            Instantiate(gameStatePrefab).Spawn();

            if (generator == null)
            {
                // A hand-built map is ready as it is.
                SpawnMissingDolls();
                mapValue = DMValuable.All.Sum(v => v.CurrentValue);
                BeginRound();
                return;
            }
            generator.IsAuthority = true;
            generator.Generated += OnGenerated;
            // Start runs now: it picks the seed (or -plgseed) and builds; OnGenerated takes it from there.
            generator.enabled = true;
        }

        void OnGenerated(PLGGenerator built, bool succeeded)
        {
            if (!Net.IsServer)
            {
                return;
            }
            built.GetComponent<DMLevelSync>()?.MarkServerBuilt(succeeded);
            State.Prepare(succeeded ? built.Seed : 0);
            if (!succeeded)
            {
                AbortRound($"level generation failed (seed {built.Seed})");
                return;
            }
            // Antiques settle and the target is known while the navmesh builds; the timer and the monsters wait for it.
            PopulateGeneratedLevel();
            SpawnMissingDolls();
            mapValue = DMValuable.All.Sum(v => v.CurrentValue);
            UpdateTarget();
            if (prepare != null)
            {
                StopCoroutine(prepare);
            }
            prepare = StartCoroutine(WaitForNavigation());
        }

        IEnumerator WaitForNavigation()
        {
            float since = Time.time;
            // The navmesh has to look ready on two polls in a row, so a build that is about to start is not mistaken for done.
            int readyPolls = 0;
            while (true)
            {
                yield return new WaitForSeconds(0.25f);
                if (generator == null)
                {
                    AbortRound("the level generator disappeared");
                    yield break;
                }
                readyPolls = generator.IsNavigationReady(out string why) ? readyPolls + 1 : 0;
                float waited = Time.time - since;
                if (readyPolls >= 2)
                {
                    Debug.Log($"[DreamMovers] generated level ready after {waited:0.00} s: starting the round");
                    SpawnGeneratedMonsters();
                    BeginRound();
                    yield break;
                }
                if (waited > navReadyTimeoutSeconds)
                {
                    AbortRound($"navigation not ready after {waited:0.0} s ({why})");
                    yield break;
                }
            }
        }

        void BeginRound()
        {
            State.StartRound(0, roundSeconds);
            UpdateTarget();
            if (check != null)
            {
                StopCoroutine(check);
            }
            check = StartCoroutine(CheckRound());
        }

        void AbortRound(string reason)
        {
            Debug.LogError($"[DreamMovers] round aborted: {reason}");
            State.AbortRound(reason);
        }

        // ---- Content ----

        static void Shuffle<T>(List<T> list, PLGRandomStream rng)
        {
            for (int i = list.Count - 1; i > 0; --i)
            {
                int j = rng.RandRange(0, i);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        void PopulateGeneratedLevel()
        {
            PLGRandomStream rng = generator.MakeContentStream();

            // Wardrobes on every hiding spot: narrow ones for one doll, wide ones for two. Size is (width, height, depth) in m.
            wardrobes = 0;
            (string Tag, Vector3 Size, int Capacity)[] kinds =
            {
                ("HideSmall", new Vector3(0.7f, 1.8f, 0.7f), 1),
                ("HideLarge", new Vector3(1.3f, 2.0f, 0.7f), 2),
            };
            foreach ((string tag, Vector3 size, int capacity) in kinds)
            {
                foreach (PLGSpawnSlot slot in generator.GetSpawnSlots(tag))
                {
                    NetworkObject spot = Instantiate(hidingSpotPrefab, slot.Pose.position, slot.Pose.rotation);
                    spot.GetComponent<DMHidingSpot>().Configure(size, capacity);
                    spot.Spawn(true);
                    ++wardrobes;
                }
            }
            Physics.SyncTransforms();

            // Antiques: the chapter's set, each once, on shuffled floor spots.
            List<PLGSpawnSlot> itemSlots = generator.GetSpawnSlots("Item");
            List<DMValuableSpec> specs = DMValuableCatalog.MakeChapterOne();
            Shuffle(itemSlots, rng);
            Shuffle(specs, rng);
            // Every antique of the set should get a spot; fewer spots than antiques is a kit problem the tests report.
            expectedAntiques = specs.Count;
            blockedAntiqueSpawns = 0;
            int count = Mathf.Min(itemSlots.Count, specs.Count);
            for (int i = 0; i < count; ++i)
            {
                DMValuableSpec spec = specs[i];
                Quaternion rotation = Quaternion.Euler(0f, rng.FRand() * 360f, 0f) * itemSlots[i].Pose.rotation;
                Vector3 position = itemSlots[i].Pose.position + Vector3.up * (spec.Size.y * 0.5f + 0.01f);
                // Something already there (a wall, a wardrobe, another antique) would push the item off its spot.
                if (Physics.CheckBox(position, spec.Size * 0.5f - Vector3.one * 0.01f, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    ++blockedAntiqueSpawns;
                    Debug.LogWarning($"[DreamMovers] antique spot {i} ({position}) is already blocked");
                }
                NetworkObject item = Instantiate(valuablePrefab, position, rotation);
                item.GetComponent<DMValuable>().InitFromSpec(spec);
                item.Spawn(true);
                Physics.SyncTransforms();
            }
            Debug.Log($"[DreamMovers] generated level (seed {generator.Seed}): {count} of {expectedAntiques} antiques on {itemSlots.Count} spots " +
                $"({blockedAntiqueSpawns} spots blocked), {wardrobes} wardrobes");
        }

        void SpawnGeneratedMonsters()
        {
            // Bell horse waits on the hub's post.
            List<PLGSpawnSlot> posts = generator.GetSpawnSlots("Guard");
            if (posts.Count > 0)
            {
                Instantiate(bellHorsePrefab, posts[0].Pose.position, posts[0].Pose.rotation).Spawn(true);
            }

            // Butler: starts in the chapel and walks a loop through every general room and the hub.
            PLGLayout layout = generator.Layout;
            IReadOnlyList<PLGModuleDef> defs = generator.Modules;
            IReadOnlyList<PLGTheme> themes = generator.Themes;
            bool IsChapel(int module) => module >= 0 && layout.Modules[module].Theme >= 0 && layout.Modules[module].Theme < themes.Count
                && themes[layout.Modules[module].Theme].Name == ButlerTheme;

            var route = new List<int>();
            int butlerRoom = PLG.IndexNone;
            for (int i = 0; i < layout.Modules.Count; ++i)
            {
                PLGPlacedModule placed = layout.Modules[i];
                if (placed.Removed)
                {
                    continue;
                }
                PLGRole role = defs[placed.Def].Role;
                if (role == PLGRole.Room || role == PLGRole.Hub)
                {
                    route.Add(i);
                }
                if (role == PLGRole.Room)
                {
                    bool deeper = butlerRoom == PLG.IndexNone || placed.Depth > layout.Modules[butlerRoom].Depth;
                    if (IsChapel(i) || (!IsChapel(butlerRoom) && deeper))
                    {
                        butlerRoom = i;
                    }
                }
            }
            if (butlerRoom != PLG.IndexNone)
            {
                // Start the loop at the butler's own room.
                int first = route.IndexOf(butlerRoom);
                var patrol = new List<Vector3>();
                for (int k = 0; k < route.Count; ++k)
                {
                    patrol.Add(generator.GetModulePose(route[(first + k) % route.Count]).position);
                }
                NetworkObject butler = Instantiate(butlerPrefab, patrol[0], Quaternion.identity);
                butler.GetComponent<DMButler>().SetPatrolPoints(patrol);
                butler.Spawn(true);
            }
            Debug.Log($"[DreamMovers] generated level monsters: horse {(posts.Count > 0 ? "yes" : "no")}, butler patrols {(butlerRoom != PLG.IndexNone ? route.Count : 0)} rooms");
        }

        // ---- Players ----

        void OnClientConnected(ulong clientId)
        {
            if (!Net.IsServer)
            {
                return;
            }
            if (generator == null || generator.IsGenerated)
            {
                SpawnDoll(clientId, StartPosition(Net.ConnectedClientsIds.Count - 1), StartRotation());
            }
            UpdateTarget();
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (Net != null && Net.IsServer)
            {
                UpdateTarget(clientId);
            }
        }

        Vector3 StartPosition(int index)
        {
            if (playerStarts == null || playerStarts.Length == 0)
            {
                return Vector3.up;
            }
            Transform start = playerStarts[Mathf.Abs(index) % playerStarts.Length];
            return start.position;
        }

        Quaternion StartRotation()
        {
            return playerStarts != null && playerStarts.Length > 0 ? playerStarts[0].rotation : Quaternion.identity;
        }

        /// <summary>Every connected player without a doll or a cotton ball gets a doll at a start.</summary>
        void SpawnMissingDolls()
        {
            int index = 0;
            foreach (ulong clientId in Net.ConnectedClientsIds)
            {
                if (DMDoll.ForClient(clientId) == null && DMCottonBall.ForClient(clientId) == null)
                {
                    SpawnDoll(clientId, StartPosition(index), StartRotation());
                }
                ++index;
            }
        }

        void SpawnDoll(ulong clientId, Vector3 position, Quaternion rotation)
        {
            if (DMDoll.ForClient(clientId) != null)
            {
                return;
            }
            NetworkObject doll = Instantiate(dollPrefab, position, rotation);
            doll.SpawnAsPlayerObject(clientId, true);
        }

        /// <summary>Server: a caught doll bursts into a cotton ball teammates can carry to the extraction zone.</summary>
        public void SpawnCottonBall(ulong clientId, Vector3 position, float yaw)
        {
            NetworkObject ball = Instantiate(cottonBallPrefab, position, Quaternion.Euler(0f, yaw, 0f));
            ball.GetComponent<DMCottonBall>().SetDownedPlayer(clientId, yaw);
            ball.Spawn(true);
        }

        /// <summary>A cotton ball reached the extraction zone: give its player a fresh doll there.</summary>
        public void RevivePlayer(ulong clientId, Vector3 location)
        {
            if (!Net.ConnectedClientsIds.Contains(clientId) || DMDoll.ForClient(clientId) != null)
            {
                return;
            }
            SpawnDoll(clientId, location + Vector3.up * 1f, StartRotation());
        }

        // ---- Round ----

        void UpdateTarget(ulong? leaving = null)
        {
            if (State == null || mapValue <= 0)
            {
                return;
            }
            int players = Net.ConnectedClientsIds.Count(id => id != leaving);
            float share = Mathf.Min(1f, baseTargetShare + targetSharePerPlayer * Mathf.Max(1, players));
            State.SetTargetValue(Mathf.RoundToInt(mapValue * share));
        }

        IEnumerator CheckRound()
        {
            while (true)
            {
                yield return new WaitForSeconds(0.5f);
                if (State == null || State.RoundState != DMRoundState.Playing)
                {
                    continue;
                }
                if (State.IsTargetReached)
                {
                    State.EndRound(DMRoundState.Success);
                }
                else if (State.RemainingSeconds <= 0f || AreAllPlayersDown())
                {
                    // The dream woke up (time and banking drained it) before enough was banked, or nobody is left to carry anything.
                    State.EndRound(DMRoundState.Failed);
                }
                else
                {
                    continue;
                }
                yield return new WaitForSeconds(summarySeconds);
                RestartRound();
                yield break;
            }
        }

        bool AreAllPlayersDown()
        {
            // Only once someone has actually been caught, so players still joining do not count as down.
            if (IgnoreAllDown || DMCottonBall.Balls.Count == 0)
            {
                return false;
            }
            return Net.ConnectedClientsIds.All(id => DMDoll.ForClient(id) == null);
        }

        /// <summary>Clears every spawned thing and builds a new map (the same seed when -plgseed fixes it).</summary>
        public void RestartRound()
        {
            if (prepare != null)
            {
                StopCoroutine(prepare);
            }
            foreach (NetworkObject spawned in Net.SpawnManager.SpawnedObjectsList.ToList())
            {
                if (spawned != null && spawned.IsSpawned && spawned.GetComponent<DMGameState>() == null)
                {
                    spawned.Despawn(true);
                }
            }
            StartCoroutine(RebuildNextFrame());
        }

        IEnumerator RebuildNextFrame()
        {
            // Despawned objects are destroyed at the end of the frame; until then their colliders would block the new spots.
            yield return null;
            if (generator == null)
            {
                // Nothing to rebuild by hand: players start again where they started.
                SpawnMissingDolls();
                BeginRound();
                yield break;
            }
            int seed = DMArgs.TryGet("plgseed=", out string forced) && int.TryParse(forced, out int parsed) && parsed != 0
                ? parsed
                : Random.Range(1, int.MaxValue);
            generator.GenerateWithSeed(seed, true);
        }
    }
}
