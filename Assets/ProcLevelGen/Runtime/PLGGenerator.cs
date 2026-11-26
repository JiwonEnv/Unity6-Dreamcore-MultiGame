using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace ProcLevelGen
{
    [Serializable]
    public class PLGGeneratedEvent : UnityEvent<PLGGenerator, bool> { }

    /// <summary>
    /// Drop one in a scene: on Start it assembles the module kit from a seed and builds graybox floors and walls.
    /// The server picks the seed (see <see cref="SeedChosen"/>) and sends only that; every machine builds the same
    /// layout locally with <see cref="ApplyServerSeed"/>. In the editor, Preview and Preview New Seed build the same
    /// thing without playing (not saved with the scene). Ported from the UE5 APLGGenerator.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshSurface))]
    public class PLGGenerator : MonoBehaviour
    {
        const string GeneratedRootName = "PLG Generated";

        [Header("Generation")]
        [Tooltip("0 or New Seed Each Run: the server picks one when play starts.")]
        [SerializeField] int seed = 1;
        [SerializeField] bool newSeedEachRun = true;
        [Tooltip("General rooms placed before the end room.")]
        [SerializeField, Range(0, 32)] int roomCount = 4;
        [SerializeField, Range(0f, 1f)] float corridorChance = 0.45f;
        [Tooltip("Server: further seeds tried when a seed gives no valid layout (each seed already retries internally).")]
        [SerializeField, Range(0, 16)] int maxSeedRetries = 3;
        [Tooltip("Modules stay within this distance (cm) of the generator, so one nav area can cover the house.")]
        [SerializeField, Min(1000f)] float maxHalfExtent = 5000f;
        [Tooltip("Log every placed module after generation.")]
        [SerializeField] bool logModules;

        [Header("Kit")]
        [SerializeField] List<PLGModuleDef> modules = PLG.MakeSampleModules();
        [SerializeField] List<PLGTheme> themes = PLG.MakeSampleThemes();

        [Header("Kit spec (cm)")]
        [Tooltip("Grid the kit is checked against.")]
        [SerializeField] float gridSize = 50f;
        [SerializeField] float wallHeight = 400f;
        [Tooltip("Full wall thickness; each module builds half of it inside its footprint.")]
        [SerializeField] float wallThickness = 20f;
        [SerializeField] float doorHeight = 250f;
        [Tooltip("Floor slab below Z = 0.")]
        [SerializeField] float floorThickness = 20f;

        [Header("Graybox")]
        [Tooltip("Base material, tinted per color (URP Lit _BaseColor). Falls back to URP Lit when empty.")]
        [SerializeField] Material grayboxMaterial;
        [SerializeField] Color fixedFloorColor = new Color(0.55f, 0.42f, 0.32f);
        [SerializeField] Color corridorFloorColor = new Color(0.4f, 0.36f, 0.33f);
        [SerializeField] Color defaultWallColor = new Color(0.9f, 0.85f, 0.75f);
        [SerializeField] Color propColor = new Color(0.35f, 0.22f, 0.15f);
        [Tooltip("Module name (and theme) shown in the scene view over each module. Never in the game.")]
        [SerializeField] bool showLabels = true;

        [Header("Events")]
        [Tooltip("Fired on every machine after generation, successful or not. Content spawning hooks in here.")]
        [SerializeField] PLGGeneratedEvent onGenerated = new PLGGeneratedEvent();

        [NonSerialized] PLGLayout layout = new PLGLayout();
        [NonSerialized] bool generationSucceeded;
        Transform generatedRoot;
        readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        int boxCount;
        NavMeshSurface surface;
        AsyncOperation navBuild;
        int selfTestFailures;

        /// <summary>Fired on every machine after generation, successful or not (code listeners).</summary>
        public event Action<PLGGenerator, bool> Generated;

        /// <summary>Server: the seed it settled on (after fallbacks); the network layer replicates this.</summary>
        public event Action<int> SeedChosen;

        /// <summary>
        /// True on the server (and offline). The network layer sets it to false on clients before Start; clients then
        /// build with <see cref="ApplyServerSeed"/>.
        /// </summary>
        public bool IsAuthority { get; set; } = true;

        public PLGLayout Layout => layout;
        /// <summary>The last generation produced a layout that passed PLG.ValidateLayout and was built.</summary>
        public bool IsGenerated => generationSucceeded;
        public uint LayoutHash => PLG.HashLayout(layout);
        public IReadOnlyList<PLGModuleDef> Modules => modules;
        public IReadOnlyList<PLGTheme> Themes => themes;
        public int Seed => seed;
        public int RoomCount => roomCount;
        /// <summary>Graybox boxes currently built.</summary>
        public int BoxCount => boxCount;
        public PLGGeneratedEvent OnGenerated => onGenerated;

        void Reset()
        {
            ConfigureSurface(GetComponent<NavMeshSurface>());
        }

        void Awake()
        {
            surface = GetComponent<NavMeshSurface>();
            ConfigureSurface(surface);
        }

        /// <summary>Navmesh from the generated colliders only (labels and markers never cut holes).</summary>
        static void ConfigureSurface(NavMeshSurface target)
        {
            if (target == null)
            {
                return;
            }
            target.collectObjects = CollectObjects.Children;
            target.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        }

        void Start()
        {
            if (IsAuthority)
            {
                if (newSeedEachRun || seed == 0)
                {
                    seed = UnityEngine.Random.Range(1, int.MaxValue);
                }
                bool selfTest = false;
                if (Debug.isDebugBuild || Application.isEditor)
                {
                    // Test hooks: -plgseed=N fixes the seed, -plgtestkit=empty|impossible makes generation fail on purpose.
                    if (TryGetArg("plgseed=", out string forcedSeed) && int.TryParse(forcedSeed, out int parsed) && parsed != 0)
                    {
                        seed = parsed;
                    }
                    if (TryGetArg("plgtestkit=", out string testKit))
                    {
                        ApplyTestKit(testKit);
                    }
                    selfTest = HasArg("plgselftest");
                }
                Generate(true);
                if (selfTest)
                {
                    StartCoroutine(RunSelfTest(TryGetArg("plgshotdir=", out string dir) ? dir : Application.persistentDataPath, true));
                }
            }
            else if (!newSeedEachRun)
            {
                // The saved seed is the server's seed too, so the server may never send one.
                Generate(false);
            }
        }

        /// <summary>Clients: build exactly the seed the server settled on.</summary>
        public void ApplyServerSeed(int serverSeed)
        {
            seed = serverSeed;
            Generate(false);
        }

        /// <summary>Dev only: "empty" removes every module, "impossible" leaves too little room for the start and hub.</summary>
        public void ApplyTestKit(string testKit)
        {
            if (testKit == "empty")
            {
                modules = new List<PLGModuleDef>();
            }
            else if (testKit == "impossible")
            {
                // The start and the hub alone need more room than this.
                maxHalfExtent = 1000f;
            }
            Debug.LogWarning($"{name}: test kit '{testKit}'");
        }

        /// <summary>Builds the layout for the current seed (editor preview, or tests).</summary>
        [ContextMenu("Preview")]
        public void Preview()
        {
            if (seed == 0)
            {
                seed = 1;
            }
            Generate(false);
        }

        [ContextMenu("Preview New Seed")]
        public void PreviewNewSeed()
        {
            seed = UnityEngine.Random.Range(1, int.MaxValue);
            Generate(false);
        }

        [ContextMenu("Clear Preview")]
        public void ClearPreview()
        {
            layout = new PLGLayout();
            generationSucceeded = false;
            BuildGraybox();
        }

        /// <summary>Sets the seed and builds it, as the server would (fallback seeds allowed). For tests and tools.</summary>
        public void GenerateWithSeed(int newSeed, bool allowReseed)
        {
            seed = newSeed;
            Generate(allowReseed);
        }

        /// <summary>World pose of a placed module's pivot (floor top, footprint center).</summary>
        public Pose GetModulePose(int placedIndex)
        {
            if (placedIndex < 0 || placedIndex >= layout.Modules.Count)
            {
                return new Pose(transform.position, transform.rotation);
            }
            PLGPlacedModule placed = layout.Modules[placedIndex];
            return new Pose(transform.TransformPoint(PLGSpace.ToUnity(placed.Location)), transform.rotation * PLGSpace.Yaw(90f * placed.Quarter));
        }

        /// <summary>Every spawn point with this tag in the built layout, in world space.</summary>
        public List<PLGSpawnSlot> GetSpawnSlots(string tag)
        {
            var slots = new List<PLGSpawnSlot>();
            for (int i = 0; i < layout.Modules.Count; ++i)
            {
                PLGPlacedModule placed = layout.Modules[i];
                if (placed.Removed || placed.Def < 0 || placed.Def >= modules.Count)
                {
                    continue;
                }
                PLGModuleDef def = modules[placed.Def];
                Vector3 moduleLocal = PLGSpace.ToUnity(placed.Location);
                Quaternion moduleRotation = PLGSpace.Yaw(90f * placed.Quarter);
                foreach (PLGSpawnPoint point in def.SpawnPoints)
                {
                    if (point.Tag != tag)
                    {
                        continue;
                    }
                    Vector3 local = moduleLocal + moduleRotation * PLGSpace.ToUnity(point.Location);
                    slots.Add(new PLGSpawnSlot
                    {
                        Pose = new Pose(transform.TransformPoint(local), transform.rotation * moduleRotation * PLGSpace.Yaw(point.Yaw)),
                        Module = i,
                        Role = def.Role,
                        Theme = placed.Theme
                    });
                }
            }
            return slots;
        }

        /// <summary>Random stream for content choices, derived from the seed (the server uses it; clients get the results).</summary>
        public PLGRandomStream MakeContentStream()
        {
            return new PLGRandomStream(unchecked((int)PLGHash.CombineFast(unchecked((uint)seed), 0x5EEDC0DEu)));
        }

        /// <summary>Builds the layout for seed. With allowReseed (server), tries up to maxSeedRetries more seeds and keeps the one that worked.</summary>
        void Generate(bool allowReseed)
        {
            foreach (string problem in PLG.ValidateModules(modules, gridSize))
            {
                Debug.LogWarning($"{name}: kit: {problem}");
            }

            var settings = new PLGLayoutSettings
            {
                Modules = modules,
                NumThemes = themes.Count,
                RoomCount = roomCount,
                CorridorChance = corridorChance,
                MaxHalfExtent = maxHalfExtent
            };

            generationSucceeded = false;
            int firstSeed = seed;
            int tries = 1 + (allowReseed ? maxSeedRetries : 0);
            for (int attempt = 0; attempt < tries && !generationSucceeded; ++attempt)
            {
                int trySeed = attempt == 0
                    ? firstSeed
                    : Math.Max(1, unchecked((int)(PLGHash.CombineFast(unchecked((uint)firstSeed), (uint)(0xA11CE + attempt)) & 0x7FFFFFFF)));
                if (!PLG.GenerateLayout(settings, trySeed, layout, out string report))
                {
                    Debug.LogError($"{name}: {report}");
                    continue;
                }
                // Check the result on its own before trusting it, also when a fallback seed made it.
                List<string> problems = PLG.ValidateLayout(modules, layout, roomCount);
                if (problems.Count > 0)
                {
                    Debug.LogError($"{name}: seed {trySeed} gave an invalid layout: {string.Join("; ", problems)}");
                    continue;
                }
                seed = trySeed;
                generationSucceeded = true;
                Debug.Log($"{name}: {report}, layout hash {LayoutHash:x8}" + (attempt > 0 ? $" (fallback {attempt} after seed {firstSeed} failed)" : ""));
                if (logModules)
                {
                    for (int i = 0; i < layout.Modules.Count; ++i)
                    {
                        PLGPlacedModule placed = layout.Modules[i];
                        if (!placed.Removed)
                        {
                            Debug.Log($"  [{i}] {modules[placed.Def].Name} at ({placed.Location.x:0}, {placed.Location.y:0}) yaw {placed.Quarter * 90} depth {placed.Depth} theme {placed.Theme}");
                        }
                    }
                }
            }
            if (!generationSucceeded)
            {
                layout = new PLGLayout();
                Debug.LogError($"{name}: generation failed after {tries} seed(s), starting from seed {firstSeed}; nothing was built");
            }

            BuildGraybox();
            if (generationSucceeded && Application.isPlaying)
            {
                // The whole house just appeared: rebuild the navmesh over all of it.
                StartNavBuild();
            }
            if (generationSucceeded && IsAuthority && allowReseed)
            {
                SeedChosen?.Invoke(seed);
            }
            Generated?.Invoke(this, generationSucceeded);
            onGenerated?.Invoke(this, generationSucceeded);
        }

        void StartNavBuild()
        {
            if (surface == null)
            {
                surface = GetComponent<NavMeshSurface>();
            }
            if (surface.navMeshData == null)
            {
                surface.navMeshData = new NavMeshData(surface.agentTypeID)
                {
                    name = name,
                    position = transform.position,
                    rotation = transform.rotation
                };
            }
            surface.AddData();
            navBuild = surface.UpdateNavMesh(surface.navMeshData);
        }

        /// <summary>
        /// Server: the navmesh covers the built layout. Nav data exists, no build is pending, and every module has navmesh
        /// near its pivot (within a quarter of its size, inside its own footprint). Says what is missing otherwise.
        /// </summary>
        public bool IsNavigationReady(out string why)
        {
            if (surface == null || surface.navMeshData == null)
            {
                why = "no navigation data";
                return false;
            }
            if (navBuild != null && !navBuild.isDone)
            {
                why = "navmesh build in progress";
                return false;
            }
            for (int i = 0; i < layout.Modules.Count; ++i)
            {
                if (!layout.Modules[i].Removed && !ProjectIntoModule(i, out _))
                {
                    why = $"module {i} has no navmesh near its pivot yet";
                    return false;
                }
            }
            why = string.Empty;
            return true;
        }

        /// <summary>Navmesh point near a module's pivot and inside its footprint (see IsNavigationReady).</summary>
        bool ProjectIntoModule(int placedIndex, out Vector3 point)
        {
            // Within a quarter of the module's size of its pivot, so furniture on the pivot does not matter,
            // and inside its own footprint, so a neighbor's navmesh does not count.
            PLGPlacedModule placed = layout.Modules[placedIndex];
            PLGModuleDef def = modules[placed.Def];
            float reach = Mathf.Min(def.Size.x, def.Size.y) * 0.25f * PLGSpace.CmToM;
            point = default;
            if (!NavMesh.SamplePosition(GetModulePose(placedIndex).position, out NavMeshHit hit, reach, NavMesh.AllAreas))
            {
                return false;
            }
            Vector3 plan = PLGSpace.ToPlan(transform.InverseTransformPoint(hit.position));
            if (!PLG.Footprint(def, placed).IsInside(new Vector2(plan.x, plan.y)))
            {
                return false;
            }
            point = hit.position;
            return true;
        }

        /// <summary>
        /// Checks the built layout and reports each result: generation succeeded, the layout is valid, graybox exists,
        /// the player (tag Player) stands on generated floor, and navigation reaches every module from the start.
        /// A module with no navmesh near its pivot counts as not reached. Returns the number of failures.
        /// </summary>
        public int RunGenerationChecks(Action<bool, string> report)
        {
            int failures = 0;
            void Check(bool passed, string what)
            {
                failures += passed ? 0 : 1;
                report(passed, what);
            }

            int live = layout.Modules.Count(m => !m.Removed);
            Check(generationSucceeded && live > 0, $"generation succeeded: seed {seed}, {live} modules, layout hash {LayoutHash:x8}");
            if (!generationSucceeded)
            {
                return failures;
            }
            List<string> problems = PLG.ValidateLayout(modules, layout, roomCount);
            Check(problems.Count == 0, problems.Count == 0 ? "layout passes ValidateLayout" : $"layout invalid: {string.Join("; ", problems)}");
            Check(boxCount > 0, $"{boxCount} graybox boxes built");

            // The player spawned in the start module and stands on generated floor, not falling through.
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                Vector3 from = player.transform.position;
                bool floor = false;
                foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 3f).OrderBy(h => h.distance))
                {
                    if (hit.collider.transform.IsChildOf(player.transform))
                    {
                        continue;
                    }
                    floor = hit.collider.transform.IsChildOf(transform);
                    break;
                }
                float vy = player.TryGetComponent(out Rigidbody body) ? body.linearVelocity.y
                    : player.TryGetComponent(out CharacterController controller) ? controller.velocity.y : 0f;
                Check(floor && Mathf.Abs(vy) < 0.1f, $"player stands on the generated floor (y {from.y:0.00}, vy {vy:0.00})");
            }
            else
            {
                Check(false, "player exists (tag Player)");
            }

            // Navigation from the start module reaches every module. A module with no navmesh near its pivot is a failure, not skipped.
            if (surface == null || surface.navMeshData == null)
            {
                Check(false, "navigation data exists");
                return failures;
            }
            if (!ProjectIntoModule(0, out Vector3 startOnNav))
            {
                Check(false, "start module has navmesh near its pivot");
                return failures;
            }
            int reached = 0;
            string missed = string.Empty;
            var path = new NavMeshPath();
            for (int i = 1; i < layout.Modules.Count; ++i)
            {
                if (layout.Modules[i].Removed)
                {
                    continue;
                }
                string moduleName = $"{modules[layout.Modules[i].Def].Name}[{i}]";
                if (!ProjectIntoModule(i, out Vector3 goalOnNav))
                {
                    missed += $" {moduleName}(no navmesh near its pivot)";
                    continue;
                }
                NavMesh.CalculatePath(startOnNav, goalOnNav, NavMesh.AllAreas, path);
                if (path.status == NavMeshPathStatus.PathComplete)
                {
                    ++reached;
                }
                else
                {
                    missed += $" {moduleName}({(path.status == NavMeshPathStatus.PathPartial ? "partial path" : "no path")})";
                }
            }
            Check(missed.Length == 0, $"navigation reaches {reached}/{live - 1} modules from the start" + (missed.Length == 0 ? "" : ", missed:" + missed));
            return failures;
        }

        Material GetMaterial(Color color)
        {
            if (materials.TryGetValue(color, out Material existing) && existing != null)
            {
                return existing;
            }
            Material source = grayboxMaterial != null ? grayboxMaterial : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var material = new Material(source) { name = $"PLG Graybox {ColorUtility.ToHtmlStringRGB(color)}", hideFlags = HideFlags.DontSave };
            material.SetColor("_BaseColor", color);
            materials[color] = material;
            return material;
        }

        void BuildGraybox()
        {
            Transform old = generatedRoot != null ? generatedRoot : transform.Find(GeneratedRootName);
            if (old != null)
            {
                if (Application.isPlaying)
                {
                    // Inactive right away, so the navmesh rebuild in this frame does not collect it.
                    old.gameObject.SetActive(false);
                    Destroy(old.gameObject);
                }
                else
                {
                    DestroyImmediate(old.gameObject);
                }
            }
            foreach (Material material in materials.Values)
            {
                if (material != null)
                {
                    if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
                }
            }
            materials.Clear();
            boxCount = 0;

            var root = new GameObject(GeneratedRootName);
            // Like the UE preview: never saved with the scene, rebuilt from the seed.
            root.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            root.transform.SetParent(transform, false);
            generatedRoot = root.transform;

            for (int i = 0; i < layout.Modules.Count; ++i)
            {
                PLGPlacedModule placed = layout.Modules[i];
                if (placed.Removed || placed.Def < 0 || placed.Def >= modules.Count)
                {
                    continue;
                }
                AddModuleGeometry(i, placed);
            }
        }

        void AddModuleGeometry(int index, PLGPlacedModule placed)
        {
            PLGModuleDef def = modules[placed.Def];
            var moduleRoot = new GameObject($"{index} {def.Name}");
            moduleRoot.hideFlags = generatedRoot.gameObject.hideFlags;
            moduleRoot.transform.SetParent(generatedRoot, false);
            moduleRoot.transform.localPosition = PLGSpace.ToUnity(placed.Location);
            moduleRoot.transform.localRotation = PLGSpace.Yaw(90f * placed.Quarter);

            Color floorColor = fixedFloorColor;
            Color wallColor = defaultWallColor;
            if (def.Role == PLGRole.Corridor)
            {
                floorColor = corridorFloorColor;
            }
            else if (def.Role == PLGRole.Room && placed.Theme >= 0 && placed.Theme < themes.Count)
            {
                floorColor = themes[placed.Theme].FloorColor;
                wallColor = themes[placed.Theme].WallColor;
            }
            Material floor = GetMaterial(floorColor);
            Material walls = GetMaterial(wallColor);

            // Boxes are given in module plan space (pivot = floor top, footprint center), centered, in cm.
            void AddBox(Material material, Vector3 center, Vector3 size, string boxName)
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = boxName;
                box.hideFlags = moduleRoot.hideFlags;
                box.transform.SetParent(moduleRoot.transform, false);
                box.transform.localPosition = PLGSpace.ToUnity(center);
                box.transform.localScale = PLGSpace.SizeToUnity(size);
                box.GetComponent<MeshRenderer>().sharedMaterial = material;
                ++boxCount;
            }

            AddBox(floor, new Vector3(0f, 0f, -floorThickness * 0.5f), new Vector3(def.Size.x, def.Size.y, floorThickness), "Floor");
            if (def.Props.Count > 0)
            {
                Material props = GetMaterial(propColor);
                foreach (PLGPropBox prop in def.Props)
                {
                    AddBox(props, prop.Center, prop.Size, "Prop");
                }
            }

            // Half the wall thickness, inside the footprint: the neighbor builds the other half.
            float half = wallThickness * 0.5f;
            for (int edgeIndex = 0; edgeIndex < 4; ++edgeIndex)
            {
                var edge = (PLGEdge)edgeIndex;
                bool xEdge = edge == PLGEdge.PosX || edge == PLGEdge.NegX;
                float sign = (edge == PLGEdge.PosX || edge == PLGEdge.PosY) ? 1f : -1f;
                float length = xEdge ? def.Size.y : def.Size.x;
                float across = sign * ((xEdge ? def.Size.x : def.Size.y) * 0.5f - half * 0.5f);
                // Walls on the Y edges stop where the X edge walls already fill the corners.
                float trim = xEdge ? 0f : half;

                var gaps = new List<Vector2>();
                for (int s = 0; s < def.Sockets.Count; ++s)
                {
                    PLGSocket socket = def.Sockets[s];
                    if (socket.Edge == edge && s < placed.Links.Count && placed.Links[s] != PLG.IndexNone)
                    {
                        gaps.Add(new Vector2(socket.Offset - socket.Width * 0.5f, socket.Offset + socket.Width * 0.5f));
                    }
                }
                gaps.Sort((a, b) => a.x.CompareTo(b.x));

                void Segment(float from, float to, float bottom, float top, string segmentName)
                {
                    if (to - from < 0.5f || top - bottom < 0.5f)
                    {
                        return;
                    }
                    float along = (from + to) * 0.5f;
                    float z = (bottom + top) * 0.5f;
                    Vector3 center = xEdge ? new Vector3(across, along, z) : new Vector3(along, across, z);
                    Vector3 size = xEdge ? new Vector3(half, to - from, top - bottom) : new Vector3(to - from, half, top - bottom);
                    AddBox(walls, center, size, segmentName);
                }

                float cursor = -length * 0.5f + trim;
                foreach (Vector2 gap in gaps)
                {
                    Segment(cursor, gap.x, 0f, wallHeight, $"Wall {edge}");
                    Segment(gap.x, gap.y, doorHeight, wallHeight, $"Lintel {edge}");
                    cursor = gap.y;
                }
                Segment(cursor, length * 0.5f - trim, 0f, wallHeight, $"Wall {edge}");
            }
        }

        void OnDestroy()
        {
            foreach (Material material in materials.Values)
            {
                if (material != null)
                {
                    if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
                }
            }
            materials.Clear();
        }

        /// <summary>Plan bounds of everything built (invalid when nothing is).</summary>
        public PLGBox2 LayoutBounds()
        {
            var bounds = new PLGBox2();
            foreach (PLGPlacedModule placed in layout.Modules)
            {
                if (!placed.Removed)
                {
                    bounds = bounds.Union(PLG.Footprint(modules[placed.Def], placed));
                }
            }
            return bounds;
        }

        // ---- Self-test (-plgselftest), dev builds and the editor only ----

        /// <summary>
        /// After the runtime navmesh settles, checks the layout, that the player stands on the generated floor and that
        /// navigation reaches every module from the start, saves a player and a top-down shot (with the navmesh drawn)
        /// to shotDir, then quits when asked. Results are [PLGSelfTest] PASS/FAIL lines in the log.
        /// With no object tagged Player, a capsule probe stands in for the player at the start module.
        /// </summary>
        public IEnumerator RunSelfTest(string shotDir, bool quitWhenDone)
        {
            selfTestFailures = 0;
            if (GameObject.FindWithTag("Player") == null)
            {
                SpawnProbePlayer();
            }
            yield return new WaitForSeconds(8f);
            RunGenerationChecks(SelfTestCheck);
            Directory.CreateDirectory(shotDir);
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                // Eye height, looking along the start module's only door (+X in plan).
                Vector3 eye = player.transform.position + Vector3.up * 0.6f;
                SaveShot(eye, transform.rotation * PLGSpace.Yaw(0f), 90f, Path.Combine(shotDir, "PLGSelfTest_Player.png"), null);
            }
            yield return new WaitForSeconds(1f);
            TakeTopDownShot(Path.Combine(shotDir, "PLGSelfTest_Top.png"));
            yield return new WaitForSeconds(1f);
            Debug.Log($"[PLGSelfTest] {(selfTestFailures == 0 ? "ALL PASSED" : "FAILED")}, {selfTestFailures} failure(s)");
            if (quitWhenDone)
            {
                Application.Quit(selfTestFailures == 0 ? 0 : 1);
            }
        }

        public int SelfTestFailures => selfTestFailures;

        void SelfTestCheck(bool passed, string what)
        {
            selfTestFailures += passed ? 0 : 1;
            Debug.Log($"[PLGSelfTest] {(passed ? "PASS" : "FAIL")} {what}");
        }

        void SpawnProbePlayer()
        {
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            probe.name = "PLG SelfTest Player";
            probe.tag = "Player";
            // Same spot as the UE map's first PlayerStart: inside the start module, 150 cm toward its door.
            probe.transform.position = transform.TransformPoint(PLGSpace.ToUnity(new Vector3(150f, 0f, 100f)));
            var body = probe.AddComponent<Rigidbody>();
            body.constraints = RigidbodyConstraints.FreezeRotation;
        }

        void TakeTopDownShot(string path)
        {
            PLGBox2 bounds = LayoutBounds();
            if (!bounds.IsValid)
            {
                return;
            }
            // Looking straight down, plan +X is up on screen and +Y is right. 90 degree horizontal FOV on 16:9.
            Vector2 center = bounds.Center;
            float height = Mathf.Max(bounds.Size.y, bounds.Size.x * 16f / 9f) * 0.55f + 600f;
            Vector3 position = transform.TransformPoint(PLGSpace.ToUnity(new Vector3(center.x, center.y, height)));
            Quaternion down = transform.rotation * Quaternion.Euler(90f, 0f, 0f);
            GameObject overlay = BuildNavMeshOverlay();
            SaveShot(position, down, 90f, path, overlay);
            if (overlay != null)
            {
                Destroy(overlay);
            }
        }

        /// <summary>The navmesh as a flat green mesh a little above the floor (UE "show Navigation").</summary>
        GameObject BuildNavMeshOverlay()
        {
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            if (tri.vertices.Length == 0)
            {
                return null;
            }
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(tri.vertices.Select(v => v + Vector3.up * 0.05f).ToArray());
            mesh.SetTriangles(tri.indices, 0);
            var overlay = new GameObject("PLG NavMesh Overlay");
            overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", new Color(0.2f, 0.9f, 0.3f));
            overlay.AddComponent<MeshRenderer>().sharedMaterial = material;
            return overlay;
        }

        /// <summary>Renders one frame from a temporary camera into a 1280x720 PNG (works in batch mode too).</summary>
        static void SaveShot(Vector3 position, Quaternion rotation, float horizontalFov, string path, GameObject keepAlive)
        {
            const int width = 1280;
            const int height = 720;
            var cameraObject = new GameObject("PLG Shot Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.aspect = (float)width / height;
            camera.fieldOfView = Camera.HorizontalToVerticalFieldOfView(horizontalFov, camera.aspect);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500f;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            else
            {
                camera.targetTexture = target;
                camera.Render();
            }
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, image.EncodeToPNG());
            Debug.Log($"[PLGSelfTest] saved {path}");
            camera.targetTexture = null;
            Destroy(image);
            Destroy(target);
            Destroy(cameraObject);
        }

        static bool TryGetArg(string prefix, out string value)
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (!arg.StartsWith("-"))
                {
                    continue;
                }
                string trimmed = arg.TrimStart('-');
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    value = trimmed.Substring(prefix.Length);
                    return true;
                }
            }
            value = null;
            return false;
        }

        /// <summary>Only flags count (leading '-'), so a value like "-testFilter PLGSelfTest" never turns a hook on.</summary>
        static bool HasArg(string name)
        {
            return Environment.GetCommandLineArgs().Any(a => a.StartsWith("-") && string.Equals(a.TrimStart('-'), name, StringComparison.OrdinalIgnoreCase));
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            // Labels are for the scene view only; players never see them.
            if (!showLabels)
            {
                return;
            }
            for (int i = 0; i < layout.Modules.Count; ++i)
            {
                PLGPlacedModule placed = layout.Modules[i];
                if (placed.Removed || placed.Def < 0 || placed.Def >= modules.Count)
                {
                    continue;
                }
                string text = modules[placed.Def].Name;
                if (placed.Theme >= 0 && placed.Theme < themes.Count)
                {
                    text += "\n" + themes[placed.Theme].Name;
                }
                UnityEditor.Handles.Label(GetModulePose(i).position + Vector3.up * 0.1f, text);
            }
        }
#endif
    }
}
