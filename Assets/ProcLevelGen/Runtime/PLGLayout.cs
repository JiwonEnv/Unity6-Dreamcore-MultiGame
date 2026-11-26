using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProcLevelGen
{
    public class PLGLayoutSettings
    {
        public List<PLGModuleDef> Modules = new List<PLGModuleDef>();
        public int NumThemes;
        /// <summary>General rooms to place before the end room.</summary>
        public int RoomCount = 4;
        /// <summary>Chance to try a corridor before a room at each open door.</summary>
        public float CorridorChance = 0.45f;
        /// <summary>Every footprint must stay within this distance (cm) of the origin, so one nav area covers the house.</summary>
        public float MaxHalfExtent = 5000f;
        /// <summary>Growth steps per attempt.</summary>
        public int MaxSteps = 300;
        /// <summary>Fresh tries (derived from the seed) before giving up.</summary>
        public int MaxAttempts = 16;
    }

    /// <summary>
    /// Seeded room-module assembly, ported line by line from the UE5 plugin (PLGLayout.cpp).
    /// Pure functions: the same settings and seed give the same layout on every machine, and the same layout as UE5.
    /// </summary>
    public static class PLG
    {
        public const int IndexNone = -1;
        const int NumRoles = 5;
        const float MatchTolerance = 1f;
        const float KindaSmallNumber = 1e-4f;

        /// <summary>Rotates a plan vector by quarter x 90 degrees (positive yaw, +X toward +Y).</summary>
        public static Vector2 Rotate(Vector2 v, int quarter)
        {
            switch (((quarter % 4) + 4) % 4)
            {
                case 1: return new Vector2(-v.y, v.x);
                case 2: return new Vector2(-v.x, -v.y);
                case 3: return new Vector2(v.y, -v.x);
                default: return v;
            }
        }

        /// <summary>Socket position in the module's own plan space (on the footprint edge).</summary>
        public static Vector2 SocketLocalPoint(PLGModuleDef def, PLGSocket socket)
        {
            Vector2 half = def.Size * 0.5f;
            switch (socket.Edge)
            {
                case PLGEdge.PosX: return new Vector2(half.x, socket.Offset);
                case PLGEdge.NegX: return new Vector2(-half.x, socket.Offset);
                case PLGEdge.PosY: return new Vector2(socket.Offset, half.y);
                default: return new Vector2(socket.Offset, -half.y);
            }
        }

        public static Vector2 SocketWorldPoint(PLGModuleDef def, PLGPlacedModule placed, PLGSocket socket)
        {
            return placed.Location + Rotate(SocketLocalPoint(def, socket), placed.Quarter);
        }

        /// <summary>Outward direction of the socket in generator plan space, as 90 degree turns from +X.</summary>
        public static int SocketWorldQuarter(PLGPlacedModule placed, PLGSocket socket)
        {
            return ((int)socket.Edge + placed.Quarter) % 4;
        }

        public static PLGBox2 Footprint(PLGModuleDef def, PLGPlacedModule placed)
        {
            Vector2 half = (placed.Quarter % 2 == 0 ? def.Size : new Vector2(def.Size.y, def.Size.x)) * 0.5f;
            return new PLGBox2(placed.Location - half, placed.Location + half);
        }

        /// <summary>Shortest distance from the start module (index 0) to every module, counted in doors; IndexNone if unreachable or removed.</summary>
        public static int[] DoorDistances(PLGLayout layout)
        {
            var distance = Enumerable.Repeat(IndexNone, layout.Modules.Count).ToArray();
            if (layout.Modules.Count == 0 || layout.Modules[0].Removed)
            {
                return distance;
            }
            var queue = new List<int> { 0 };
            distance[0] = 0;
            for (int head = 0; head < queue.Count; ++head)
            {
                int current = queue[head];
                foreach (int next in layout.Modules[current].Links)
                {
                    if (next >= 0 && next < layout.Modules.Count && !layout.Modules[next].Removed && distance[next] == IndexNone)
                    {
                        distance[next] = distance[current] + 1;
                        queue.Add(next);
                    }
                }
            }
            return distance;
        }

        /// <summary>
        /// Independent check of a finished layout, one line per problem (empty when valid): one start, hub and end,
        /// RoomCount rooms, no overlapping footprints, every door link two-way and meeting at one point, everything
        /// reachable from the start, no dead-end corridors, and Depth equal to the door distance.
        /// </summary>
        public static List<string> ValidateLayout(IReadOnlyList<PLGModuleDef> modules, PLGLayout layout, int roomCount)
        {
            var problems = new List<string>();
            var live = new List<int>();
            for (int i = 0; i < layout.Modules.Count; ++i)
            {
                PLGPlacedModule placed = layout.Modules[i];
                if (placed.Def < 0 || placed.Def >= modules.Count)
                {
                    problems.Add($"module {i} has no definition");
                    return problems;
                }
                if (!placed.Removed)
                {
                    live.Add(i);
                }
            }
            if (live.Count == 0 || live[0] != 0)
            {
                problems.Add("no start module at index 0");
                return problems;
            }

            var counts = new int[NumRoles];
            foreach (int i in live)
            {
                ++counts[(int)modules[layout.Modules[i].Def].Role];
            }
            var expected = new[] { (PLGRole.Start, 1), (PLGRole.Hub, 1), (PLGRole.End, 1), (PLGRole.Room, roomCount) };
            foreach (var (role, count) in expected)
            {
                if (counts[(int)role] != count)
                {
                    problems.Add($"{counts[(int)role]} module(s) with role {(int)role}, expected {count}");
                }
            }

            for (int a = 0; a < live.Count; ++a)
            {
                PLGBox2 boxA = Footprint(modules[layout.Modules[live[a]].Def], layout.Modules[live[a]]).ExpandBy(-MatchTolerance);
                for (int b = a + 1; b < live.Count; ++b)
                {
                    if (boxA.Intersect(Footprint(modules[layout.Modules[live[b]].Def], layout.Modules[live[b]]).ExpandBy(-MatchTolerance)))
                    {
                        problems.Add($"modules {live[a]} and {live[b]} overlap");
                    }
                }
            }

            foreach (int i in live)
            {
                PLGPlacedModule placed = layout.Modules[i];
                PLGModuleDef def = modules[placed.Def];
                if (placed.Links.Count != def.Sockets.Count)
                {
                    problems.Add($"module {i} has {placed.Links.Count} links for {def.Sockets.Count} sockets");
                    continue;
                }
                int numLinks = 0;
                for (int s = 0; s < placed.Links.Count; ++s)
                {
                    int other = placed.Links[s];
                    if (other == IndexNone)
                    {
                        continue;
                    }
                    ++numLinks;
                    if (other < 0 || other >= layout.Modules.Count || layout.Modules[other].Removed)
                    {
                        problems.Add($"door {i}:{s} leads to a missing module {other}");
                        continue;
                    }
                    PLGPlacedModule otherPlaced = layout.Modules[other];
                    PLGModuleDef otherDef = modules[otherPlaced.Def];
                    Vector2 point = SocketWorldPoint(def, placed, def.Sockets[s]);
                    bool meets = false;
                    for (int b = 0; b < otherPlaced.Links.Count && b < otherDef.Sockets.Count; ++b)
                    {
                        meets |= otherPlaced.Links[b] == i && Vector2.Distance(SocketWorldPoint(otherDef, otherPlaced, otherDef.Sockets[b]), point) < MatchTolerance;
                    }
                    if (!meets)
                    {
                        problems.Add($"door {i}:{s} -> {other} does not meet a door back");
                    }
                }
                if (def.Role == PLGRole.Corridor && numLinks < 2)
                {
                    problems.Add($"dead-end corridor {i}");
                }
            }

            int[] distance = DoorDistances(layout);
            foreach (int i in live)
            {
                if (distance[i] == IndexNone)
                {
                    problems.Add($"module {i} is unreachable from the start");
                }
                else if (layout.Modules[i].Depth != distance[i])
                {
                    problems.Add($"module {i} depth {layout.Modules[i].Depth}, door distance {distance[i]}");
                }
            }
            return problems;
        }

        /// <summary>Hash of what the layout builds (modules, placement, themes, links); equal layouts give equal hashes, also against UE5.</summary>
        public static uint HashLayout(PLGLayout layout)
        {
            uint hash = (uint)layout.Modules.Count;
            foreach (PLGPlacedModule placed in layout.Modules)
            {
                hash = PLGHash.CombineFast(hash, placed.Removed ? 1u : 0u);
                if (placed.Removed)
                {
                    continue;
                }
                hash = PLGHash.CombineFast(hash, unchecked((uint)placed.Def));
                hash = PLGHash.CombineFast(hash, PLGHash.TypeHash64(RoundToInt64(placed.Location.x)));
                hash = PLGHash.CombineFast(hash, PLGHash.TypeHash64(RoundToInt64(placed.Location.y)));
                hash = PLGHash.CombineFast(hash, unchecked((uint)placed.Quarter));
                hash = PLGHash.CombineFast(hash, unchecked((uint)placed.Theme));
                foreach (int link in placed.Links)
                {
                    hash = PLGHash.CombineFast(hash, unchecked((uint)link));
                }
            }
            return hash;
        }

        /// <summary>UE5 FMath::RoundToInt(double): floor(x + 0.5) as a 64-bit integer (Location is FVector2D, doubles).</summary>
        static long RoundToInt64(double x) => (long)Math.Floor(x + 0.5);

        /// <summary>Grid and socket rule violations, one line each (empty when the catalog follows the spec).</summary>
        public static List<string> ValidateModules(IReadOnlyList<PLGModuleDef> modules, float grid)
        {
            var problems = new List<string>();
            bool OnGrid(float value, float step) => Mathf.Abs(value % step) <= 0.01f;
            var roles = new bool[NumRoles];
            foreach (PLGModuleDef def in modules)
            {
                roles[(int)def.Role] = true;
                // Twice the grid keeps the centered pivot on a grid point.
                if (!OnGrid(def.Size.x, grid * 2f) || !OnGrid(def.Size.y, grid * 2f))
                {
                    problems.Add($"{def.Name}: size {def.Size.x:0} x {def.Size.y:0} is not a multiple of {grid * 2f:0} cm");
                }
                if (def.Sockets.Count == 0)
                {
                    problems.Add($"{def.Name}: no door sockets");
                }
                foreach (PLGSocket socket in def.Sockets)
                {
                    bool xEdge = socket.Edge == PLGEdge.PosX || socket.Edge == PLGEdge.NegX;
                    float edgeHalf = (xEdge ? def.Size.y : def.Size.x) * 0.5f;
                    if (!OnGrid(socket.Offset, grid))
                    {
                        problems.Add($"{def.Name}: socket offset {socket.Offset:0} is off the {grid:0} cm grid");
                    }
                    // Leave a wall stub of at least one grid cell beside every door.
                    if (Mathf.Abs(socket.Offset) + socket.Width * 0.5f > edgeHalf - grid + 0.01f)
                    {
                        problems.Add($"{def.Name}: a {socket.Width:0} cm door at offset {socket.Offset:0} is too close to the corner");
                    }
                }
            }
            foreach (PLGModuleDef def in modules)
            {
                Vector2 half = def.Size * 0.5f;
                foreach (PLGSpawnPoint point in def.SpawnPoints)
                {
                    if (Mathf.Abs(point.Location.x) > half.x || Mathf.Abs(point.Location.y) > half.y)
                    {
                        problems.Add($"{def.Name}: spawn point {point.Tag} at ({point.Location.x:0}, {point.Location.y:0}) is outside the footprint");
                    }
                }
                foreach (PLGPropBox prop in def.Props)
                {
                    if (Mathf.Abs(prop.Center.x) + prop.Size.x * 0.5f > half.x || Mathf.Abs(prop.Center.y) + prop.Size.y * 0.5f > half.y)
                    {
                        problems.Add($"{def.Name}: a prop at ({prop.Center.x:0}, {prop.Center.y:0}) sticks out of the footprint");
                    }
                }
            }
            for (int role = 0; role < NumRoles; ++role)
            {
                if (!roles[role])
                {
                    problems.Add($"no module with role {(PLGRole)role}");
                }
            }
            return problems;
        }

        /// <summary>
        /// Start at the origin, the hub on one of its doors, then rooms and corridors on random open doors until
        /// RoomCount rooms are in, then the end room on the open door farthest from the start (shortest distance in doors,
        /// loops included) where it fits. Dead-end corridors are removed and rooms get themes. Retries with derived seeds;
        /// returns false (and why in report) if no attempt succeeds.
        /// </summary>
        public static bool GenerateLayout(PLGLayoutSettings settings, int seed, PLGLayout outLayout, out string report)
        {
            string failure = string.Empty;
            int attempts = Math.Max(1, settings.MaxAttempts);
            for (int attempt = 0; attempt < attempts; ++attempt)
            {
                int attemptSeed = attempt == 0 ? seed : unchecked((int)PLGHash.CombineFast(unchecked((uint)seed), (uint)attempt));
                var builder = new Builder(settings, attemptSeed, outLayout);
                if (builder.Run(out failure))
                {
                    outLayout.Seed = seed;
                    int count = outLayout.Modules.Count(m => !m.Removed);
                    report = $"seed {seed}: {count} modules (attempt {attempt + 1})";
                    return true;
                }
            }
            outLayout.Modules.Clear();
            report = $"seed {seed}: failed after {attempts} attempts, last: {failure}";
            return false;
        }

        static void Shuffle<T>(List<T> list, PLGRandomStream rng)
        {
            for (int i = list.Count - 1; i > 0; --i)
            {
                int j = rng.RandRange(0, i);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>Weighted order: heavier modules tend to come first, everything still gets a turn.</summary>
        static List<int> OrderByWeight(List<int> candidates, IReadOnlyList<PLGModuleDef> defs, PLGRandomStream rng)
        {
            // Efraimidis-Spirakis: key = u^(1/w), sorted descending.
            var keyed = new List<(float Key, int Index)>();
            foreach (int index in candidates)
            {
                float weight = Mathf.Max(defs[index].Weight, KindaSmallNumber);
                keyed.Add((MathF.Pow(Mathf.Max(rng.FRand(), KindaSmallNumber), 1f / weight), index));
            }
            keyed.Sort((a, b) => b.Key.CompareTo(a.Key));
            return keyed.Select(k => k.Index).ToList();
        }

        /// <summary>One generation attempt.</summary>
        sealed class Builder
        {
            readonly PLGLayoutSettings settings;
            readonly List<PLGModuleDef> defs;
            readonly PLGRandomStream rng;
            readonly PLGLayout layout;
            readonly List<int>[] byRole = new List<int>[NumRoles];
            /// <summary>Doors not connected yet.</summary>
            readonly List<PLGDoor> open = new List<PLGDoor>();

            public Builder(PLGLayoutSettings settings, int seed, PLGLayout layout)
            {
                this.settings = settings;
                defs = settings.Modules;
                rng = new PLGRandomStream(seed);
                this.layout = layout;
                for (int r = 0; r < NumRoles; ++r)
                {
                    byRole[r] = new List<int>();
                }
                for (int i = 0; i < defs.Count; ++i)
                {
                    byRole[(int)defs[i].Role].Add(i);
                }
            }

            public bool Run(out string failure)
            {
                failure = string.Empty;
                layout.Modules.Clear();
                open.Clear();

                if (!PlaceStart())
                {
                    failure = "no Start module";
                    return false;
                }
                var startDoors = new List<PLGDoor>(open);
                if (!PlaceRoleAtAny(PLGRole.Hub, startDoors))
                {
                    failure = "the hub does not fit on any start door";
                    return false;
                }

                int roomsLeft = settings.RoomCount;
                for (int step = 0; roomsLeft > 0 && step < settings.MaxSteps && open.Count > 0; ++step)
                {
                    PLGDoor at = open[rng.RandHelper(open.Count)];
                    bool corridorFirst = rng.FRand() < settings.CorridorChance;
                    PLGRole first = corridorFirst ? PLGRole.Corridor : PLGRole.Room;
                    PLGRole second = corridorFirst ? PLGRole.Room : PLGRole.Corridor;
                    if (PlaceRoleAt(first, at))
                    {
                        roomsLeft -= first == PLGRole.Room ? 1 : 0;
                    }
                    else if (PlaceRoleAt(second, at))
                    {
                        roomsLeft -= second == PLGRole.Room ? 1 : 0;
                    }
                    else
                    {
                        open.RemoveAll(d => d.Equals(at)); // nothing fits behind this door: it stays wall
                    }
                }
                if (roomsLeft > 0)
                {
                    failure = $"{roomsLeft} room(s) did not fit";
                    return false;
                }

                // The end room goes behind the open door farthest from the start. Loops can make a module closer than
                // the branch it was placed on, so distances are measured over the doors as they are now.
                int[] distance = DoorDistances(layout);
                var deepest = new List<PLGDoor>(open);
                Shuffle(deepest, rng);
                deepest = deepest.OrderByDescending(d => distance[d.Module]).ToList(); // stable, like StableSort
                if (!PlaceRoleAtAny(PLGRole.End, deepest))
                {
                    failure = "the end room does not fit on any open door";
                    return false;
                }

                PruneDeadEndCorridors();
                AssignThemes();
                int[] final = DoorDistances(layout);
                for (int i = 0; i < layout.Modules.Count; ++i)
                {
                    layout.Modules[i].Depth = final[i];
                }
                return true;
            }

            bool PlaceStart()
            {
                List<int> starts = OrderByWeight(byRole[(int)PLGRole.Start], defs, rng);
                if (starts.Count == 0)
                {
                    return false;
                }
                var start = new PLGPlacedModule { Def = starts[0] };
                start.Links.AddRange(Enumerable.Repeat(IndexNone, defs[start.Def].Sockets.Count));
                layout.Modules.Add(start);
                for (int s = 0; s < defs[start.Def].Sockets.Count; ++s)
                {
                    open.Add(new PLGDoor(0, s));
                }
                return true;
            }

            bool PlaceRoleAtAny(PLGRole role, List<PLGDoor> doors)
            {
                foreach (PLGDoor at in doors)
                {
                    if (open.Contains(at) && PlaceRoleAt(role, at))
                    {
                        return true;
                    }
                }
                return false;
            }

            bool PlaceRoleAt(PLGRole role, PLGDoor at)
            {
                foreach (int defIndex in OrderByWeight(byRole[(int)role], defs, rng))
                {
                    if (TryPlace(defIndex, at))
                    {
                        return true;
                    }
                }
                return false;
            }

            /// <summary>Puts defIndex behind the open door at, trying each of its matching sockets; false if every way overlaps.</summary>
            bool TryPlace(int defIndex, PLGDoor at)
            {
                PLGPlacedModule from = layout.Modules[at.Module];
                PLGSocket doorA = defs[from.Def].Sockets[at.Socket];
                Vector2 pointA = SocketWorldPoint(defs[from.Def], from, doorA);
                int quarterA = SocketWorldQuarter(from, doorA);

                PLGModuleDef def = defs[defIndex];
                var candidates = new List<int>();
                for (int s = 0; s < def.Sockets.Count; ++s)
                {
                    if (NearlyEqual(def.Sockets[s].Width, doorA.Width))
                    {
                        candidates.Add(s);
                    }
                }
                Shuffle(candidates, rng);

                foreach (int socketB in candidates)
                {
                    var placed = new PLGPlacedModule { Def = defIndex };
                    // Socket B must face back at socket A.
                    placed.Quarter = ((quarterA + 2 - (int)def.Sockets[socketB].Edge) % 4 + 4) % 4;
                    placed.Location = pointA - Rotate(SocketLocalPoint(def, def.Sockets[socketB]), placed.Quarter);
                    if (!Fits(Footprint(def, placed)))
                    {
                        continue;
                    }
                    placed.Depth = from.Depth + 1;
                    placed.Links.AddRange(Enumerable.Repeat(IndexNone, def.Sockets.Count));
                    layout.Modules.Add(placed);
                    int newIndex = layout.Modules.Count - 1;
                    Link(at.Module, at.Socket, newIndex, socketB);
                    open.RemoveAll(d => d.Equals(at));

                    // Its other doors either meet a door that is already open (closing a loop) or become open.
                    for (int s = 0; s < def.Sockets.Count; ++s)
                    {
                        if (s == socketB)
                        {
                            continue;
                        }
                        Vector2 point = SocketWorldPoint(def, placed, def.Sockets[s]);
                        int facing = (SocketWorldQuarter(placed, def.Sockets[s]) + 2) % 4;
                        int match = open.FindIndex(other =>
                        {
                            PLGPlacedModule otherModule = layout.Modules[other.Module];
                            PLGSocket otherSocket = defs[otherModule.Def].Sockets[other.Socket];
                            return NearlyEqual(otherSocket.Width, def.Sockets[s].Width)
                                && SocketWorldQuarter(otherModule, otherSocket) == facing
                                && Vector2.Distance(SocketWorldPoint(defs[otherModule.Def], otherModule, otherSocket), point) < MatchTolerance;
                        });
                        if (match != IndexNone)
                        {
                            Link(open[match].Module, open[match].Socket, newIndex, s);
                            open.RemoveAt(match);
                        }
                        else
                        {
                            open.Add(new PLGDoor(newIndex, s));
                        }
                    }
                    return true;
                }
                return false;
            }

            static bool NearlyEqual(float a, float b) => Mathf.Abs(a - b) <= 1e-8f;

            bool Fits(PLGBox2 box)
            {
                float limit = settings.MaxHalfExtent;
                if (box.Min.x < -limit || box.Min.y < -limit || box.Max.x > limit || box.Max.y > limit)
                {
                    return false;
                }
                // Shrunk by a centimeter: modules may share an edge, never area.
                PLGBox2 inner = box.ExpandBy(-MatchTolerance);
                foreach (PLGPlacedModule placed in layout.Modules)
                {
                    if (!placed.Removed && inner.Intersect(Footprint(defs[placed.Def], placed).ExpandBy(-MatchTolerance)))
                    {
                        return false;
                    }
                }
                return true;
            }

            void Link(int moduleA, int socketA, int moduleB, int socketB)
            {
                layout.Modules[moduleA].Links[socketA] = moduleB;
                layout.Modules[moduleB].Links[socketB] = moduleA;
            }

            /// <summary>A corridor that leads nowhere is just a closet: remove it, and the door to it becomes wall.</summary>
            void PruneDeadEndCorridors()
            {
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    for (int i = 0; i < layout.Modules.Count; ++i)
                    {
                        PLGPlacedModule module = layout.Modules[i];
                        if (module.Removed || defs[module.Def].Role != PLGRole.Corridor)
                        {
                            continue;
                        }
                        int numLinks = module.Links.Count(o => o != IndexNone);
                        if (numLinks > 1)
                        {
                            continue;
                        }
                        for (int s = 0; s < module.Links.Count; ++s)
                        {
                            int other = module.Links[s];
                            if (other != IndexNone)
                            {
                                List<int> back = layout.Modules[other].Links;
                                for (int b = 0; b < back.Count; ++b)
                                {
                                    if (back[b] == i)
                                    {
                                        back[b] = IndexNone;
                                    }
                                }
                                module.Links[s] = IndexNone;
                            }
                        }
                        module.Removed = true;
                        changed = true;
                    }
                }
            }

            /// <summary>Rooms walk through a shuffled theme list, so the first rooms never repeat a theme.</summary>
            void AssignThemes()
            {
                if (settings.NumThemes <= 0)
                {
                    return;
                }
                var order = Enumerable.Range(0, settings.NumThemes).ToList();
                Shuffle(order, rng);
                int next = 0;
                foreach (PLGPlacedModule module in layout.Modules)
                {
                    if (!module.Removed && defs[module.Def].Role == PLGRole.Room)
                    {
                        module.Theme = order[next++ % order.Count];
                    }
                }
            }
        }

        /// <summary>
        /// The 3 + 2 + 3 graybox kit: start, hub, end; two general room frames; straight, corner and T corridors.
        /// Spawn point tags: Item (floor spot for a carried object), HideSmall / HideLarge (against a wall, facing into
        /// the room, for a one or two person hiding spot), Guard (a post for a waiting monster).
        /// General rooms carry a desk prop to crawl under.
        /// </summary>
        public static List<PLGModuleDef> MakeSampleModules()
        {
            const float small = 150f;
            const float @double = 300f;
            PLGSocket Door(PLGEdge edge, float width, float offset = 0f) => new PLGSocket(edge, width, offset);
            PLGSpawnPoint Spot(string tag, float x, float y, float yaw = 0f) =>
                new PLGSpawnPoint { Tag = tag, Location = new Vector3(x, y, 0f), Yaw = yaw };

            // Desk on four legs with 80 cm under the top: a crouched 60 cm doll fits, a standing one does not.
            // A tablecloth hangs on all four sides down to 65 cm, so a doll fully under the top is covered from the low
            // angles the candlelight reaches it at from a few meters away; one that sticks out past the edge is not.
            List<PLGPropBox> Desk(float x, float y, float sizeX, float sizeY)
            {
                const float clearance = 80f;
                const float top = 5f;
                const float leg = 8f;
                const float clothBottom = 65f;
                const float cloth = 3f;
                var boxes = new List<PLGPropBox>
                {
                    new PLGPropBox { Center = new Vector3(x, y, clearance + top * 0.5f), Size = new Vector3(sizeX, sizeY, top) }
                };
                float clothZ = (clothBottom + clearance) * 0.5f;
                float clothHeight = clearance - clothBottom;
                foreach (float side in new[] { -1f, 1f })
                {
                    boxes.Add(new PLGPropBox { Center = new Vector3(x, y + side * (sizeY - cloth) * 0.5f, clothZ), Size = new Vector3(sizeX, cloth, clothHeight) });
                    boxes.Add(new PLGPropBox { Center = new Vector3(x + side * (sizeX - cloth) * 0.5f, y, clothZ), Size = new Vector3(cloth, sizeY - 2f * cloth, clothHeight) });
                }
                var corners = new[] { new Vector2(-1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(1f, 1f) };
                foreach (Vector2 corner in corners)
                {
                    boxes.Add(new PLGPropBox
                    {
                        Center = new Vector3(x + corner.x * (sizeX - leg) * 0.5f, y + corner.y * (sizeY - leg) * 0.5f, clearance * 0.5f),
                        Size = new Vector3(leg, leg, clearance)
                    });
                }
                return boxes;
            }

            PLGModuleDef Module(string name, PLGRole role, float x, float y, List<PLGSocket> sockets, float weight = 1f) =>
                new PLGModuleDef { Name = name, Role = role, Size = new Vector2(x, y), Sockets = sockets, Weight = weight };

            var output = new List<PLGModuleDef>();
            // Fixed rooms
            output.Add(Module("Start", PLGRole.Start, 1000f, 1000f, new List<PLGSocket> { Door(PLGEdge.PosX, @double) }));
            PLGModuleDef hub = Module("Hub", PLGRole.Hub, 1600f, 1600f, new List<PLGSocket>
                { Door(PLGEdge.NegX, @double), Door(PLGEdge.PosX, @double), Door(PLGEdge.PosY, small), Door(PLGEdge.NegY, small) });
            hub.SpawnPoints = new List<PLGSpawnPoint>
                { Spot("Guard", 0f, 0f), Spot("Item", -450f, 450f), Spot("Item", 450f, -450f), Spot("HideLarge", 755f, -500f, 180f) };
            output.Add(hub);

            PLGModuleDef end = Module("End", PLGRole.End, 800f, 800f, new List<PLGSocket> { Door(PLGEdge.NegX, small) });
            end.SpawnPoints = new List<PLGSpawnPoint>
                { Spot("Item", 200f, 200f), Spot("Item", 200f, -200f), Spot("Item", 250f, 0f), Spot("HideSmall", 0f, -355f, 90f) };
            output.Add(end);

            // General room frames, reused and themed. Doors stay clear of furniture and hiding spots.
            PLGModuleDef roomSmall = Module("RoomSmall", PLGRole.Room, 800f, 800f, new List<PLGSocket>
                { Door(PLGEdge.NegX, small), Door(PLGEdge.PosX, small), Door(PLGEdge.PosY, small) });
            roomSmall.SpawnPoints = new List<PLGSpawnPoint>
                { Spot("Item", -250f, 200f), Spot("Item", 250f, 200f), Spot("Item", 0f, -100f), Spot("Item", 220f, -150f), Spot("HideSmall", 200f, -355f, 90f) };
            roomSmall.Props = Desk(-200f, -320f, 150f, 80f);
            output.Add(roomSmall);

            PLGModuleDef roomLarge = Module("RoomLarge", PLGRole.Room, 1200f, 1000f, new List<PLGSocket>
                { Door(PLGEdge.NegX, @double), Door(PLGEdge.PosX, small), Door(PLGEdge.PosY, small, 300f), Door(PLGEdge.NegY, small, -300f) });
            roomLarge.SpawnPoints = new List<PLGSpawnPoint>
            {
                Spot("Item", -350f, -150f), Spot("Item", 0f, 250f), Spot("Item", 350f, 150f), Spot("Item", 50f, -200f), Spot("Item", -100f, 50f),
                Spot("HideLarge", -300f, 455f, -90f)
            };
            roomLarge.Props = Desk(300f, -410f, 150f, 80f);
            output.Add(roomLarge);

            // Corridors, 300 cm wide
            output.Add(Module("CorridorStraight", PLGRole.Corridor, 800f, 300f, new List<PLGSocket> { Door(PLGEdge.NegX, small), Door(PLGEdge.PosX, small) }, 2f));
            output.Add(Module("CorridorCorner", PLGRole.Corridor, 300f, 300f, new List<PLGSocket> { Door(PLGEdge.NegX, small), Door(PLGEdge.PosY, small) }));
            output.Add(Module("CorridorT", PLGRole.Corridor, 300f, 300f, new List<PLGSocket>
                { Door(PLGEdge.NegX, small), Door(PLGEdge.PosX, small), Door(PLGEdge.PosY, small) }));
            return output;
        }

        public static List<PLGTheme> MakeSampleThemes()
        {
            PLGTheme Theme(string name, Color floor, Color wall) => new PLGTheme { Name = name, FloorColor = floor, WallColor = wall };
            return new List<PLGTheme>
            {
                Theme("Theme1", new Color(0.75f, 0.55f, 0.6f), new Color(0.98f, 0.88f, 0.9f)),
                Theme("Theme2", new Color(0.45f, 0.6f, 0.75f), new Color(0.85f, 0.93f, 1f)),
                Theme("Theme3", new Color(0.45f, 0.32f, 0.2f), new Color(0.8f, 0.72f, 0.55f)),
                Theme("Theme4", new Color(0.2f, 0.18f, 0.25f), new Color(0.45f, 0.4f, 0.5f)),
            };
        }
    }
}
