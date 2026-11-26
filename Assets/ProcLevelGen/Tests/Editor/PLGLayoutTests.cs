using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProcLevelGen.Tests
{
    /// <summary>
    /// Port of the UE5 editor check (Tools/ue/test_proc_level.py) plus a comparison with the UE5 results: for seeds
    /// 1..200 the Unity port must place the same modules (same layout hash) as the UE5 plugin did.
    /// </summary>
    public class PLGLayoutTests
    {
        const int RoomCount = 4;

        static PLGLayoutSettings SampleSettings() => new PLGLayoutSettings
        {
            Modules = PLG.MakeSampleModules(),
            NumThemes = PLG.MakeSampleThemes().Count,
            RoomCount = RoomCount
        };

        struct Expected
        {
            public int Modules;
            public int Attempt;
            public uint Hash;
        }

        static Dictionary<int, Expected> LoadUE5Results()
        {
            string path = Path.Combine(Application.dataPath, "ProcLevelGen/Tests/Editor/UE5SeedHashes.txt");
            var results = new Dictionary<int, Expected>();
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.StartsWith("#") || line.Trim().Length == 0)
                {
                    continue;
                }
                string[] parts = line.Split(' ');
                results[int.Parse(parts[0])] = new Expected
                {
                    Modules = int.Parse(parts[1]),
                    Attempt = int.Parse(parts[2]),
                    Hash = uint.Parse(parts[3], System.Globalization.NumberStyles.HexNumber)
                };
            }
            return results;
        }

        [Test]
        public void RandomStreamMatchesUE5()
        {
            // FRandomStream(1): seed = 1 * 196314165 + 907633515 = 1103947680; 1103947680 >> 9 = 2156147.
            var rng = new PLGRandomStream(1);
            Assert.AreEqual(2156147f / 8388608f, rng.GetFraction());
            Assert.AreEqual(unchecked(1103947680u * 196314165u + 907633515u), rng.GetUnsignedInt());
        }

        [Test]
        public void SampleKitFollowsTheSpec()
        {
            CollectionAssert.IsEmpty(PLG.ValidateModules(PLG.MakeSampleModules(), 50f));
        }

        [Test]
        public void SameLayoutsAsUE5ForSeeds1To200()
        {
            Dictionary<int, Expected> ue5 = LoadUE5Results();
            PLGLayoutSettings settings = SampleSettings();
            var mismatches = new List<string>();
            foreach (KeyValuePair<int, Expected> pair in ue5)
            {
                var layout = new PLGLayout();
                Assert.IsTrue(PLG.GenerateLayout(settings, pair.Key, layout, out string report), report);
                int count = layout.Modules.Count(m => !m.Removed);
                uint hash = PLG.HashLayout(layout);
                string expectedReport = $"seed {pair.Key}: {pair.Value.Modules} modules (attempt {pair.Value.Attempt})";
                if (report != expectedReport || hash != pair.Value.Hash)
                {
                    mismatches.Add($"{report}, hash {hash:x8} / UE5: {expectedReport}, hash {pair.Value.Hash:x8}");
                }
            }
            Assert.IsEmpty(mismatches, $"{mismatches.Count} of {ue5.Count} seeds differ from UE5:\n" + string.Join("\n", mismatches.Take(20)));
        }

        [Test]
        public void Seeds1To200PassEveryCheck()
        {
            PLGLayoutSettings settings = SampleSettings();
            var failures = new List<string>();
            var sizes = new List<int>();
            for (int seed = 1; seed <= 200; ++seed)
            {
                var layout = new PLGLayout();
                if (!PLG.GenerateLayout(settings, seed, layout, out string report))
                {
                    failures.Add(report);
                    continue;
                }
                string problem = Check(settings, layout);
                if (problem == null)
                {
                    // Building the same seed again gives exactly the same layout.
                    var again = new PLGLayout();
                    PLG.GenerateLayout(settings, seed, again, out _);
                    if (Signature(again) != Signature(layout))
                    {
                        problem = "the same seed built a different layout the second time";
                    }
                }
                if (problem != null)
                {
                    failures.Add($"seed {seed}: {problem}");
                }
                sizes.Add(layout.Modules.Count(m => !m.Removed));
            }
            Debug.Log($"[PLGTest] modules per map: min {sizes.Min()}, avg {sizes.Average():0.0}, max {sizes.Max()}");
            Assert.IsEmpty(failures, string.Join("\n", failures));
            // UE5: min 7, avg 8.6, max 16.
            Assert.AreEqual(7, sizes.Min());
            Assert.AreEqual(16, sizes.Max());
            Assert.AreEqual(8.6, System.Math.Round(sizes.Average(), 1));
        }

        /// <summary>The independent checks of test_proc_level.py, on top of PLG.ValidateLayout.</summary>
        static string Check(PLGLayoutSettings settings, PLGLayout layout)
        {
            List<string> problems = PLG.ValidateLayout(settings.Modules, layout, settings.RoomCount);
            if (problems.Count > 0)
            {
                return string.Join("; ", problems);
            }
            List<PLGPlacedModule> mods = layout.Modules;
            var live = Enumerable.Range(0, mods.Count).Where(i => !mods[i].Removed).ToList();

            // Reachability by a plain flood fill, separate from DoorDistances.
            var seen = new HashSet<int> { live[0] };
            var stack = new Stack<int>(seen);
            while (stack.Count > 0)
            {
                foreach (int o in mods[stack.Pop()].Links)
                {
                    if (o >= 0 && seen.Add(o))
                    {
                        stack.Push(o);
                    }
                }
            }
            if (!seen.SetEquals(live))
            {
                return $"{live.Count - seen.Count} module(s) unreachable from the start";
            }

            var themes = live.Where(i => settings.Modules[mods[i].Def].Role == PLGRole.Room).Select(i => mods[i].Theme).ToList();
            List<int> first = themes.Take(settings.NumThemes).ToList();
            if (first.Distinct().Count() != first.Count)
            {
                return $"repeated theme among the first rooms: {string.Join(",", themes)}";
            }
            return null;
        }

        static string Signature(PLGLayout layout)
        {
            return string.Join("|", layout.Modules.Select(m =>
                $"{m.Def},{Mathf.RoundToInt(m.Location.x)},{Mathf.RoundToInt(m.Location.y)},{m.Quarter},{m.Theme},{string.Join(".", m.Links)},{m.Removed},{m.Depth}"));
        }

        [Test]
        public void GeneratorBuildsGrayboxInEditMode()
        {
            var go = new GameObject("PLG Test Generator");
            try
            {
                var generator = go.AddComponent<PLGGenerator>();
                generator.GenerateWithSeed(1, false);
                Assert.IsTrue(generator.IsGenerated);
                Assert.AreEqual(0x738bcd7fu, generator.LayoutHash);
                Assert.Greater(generator.BoxCount, 0);
                Assert.AreEqual(generator.BoxCount, go.GetComponentsInChildren<BoxCollider>().Length);
                // Spawn slots resolve in world space inside their module's footprint.
                List<PLGSpawnSlot> items = generator.GetSpawnSlots("Item");
                Assert.Greater(items.Count, 0);
                foreach (PLGSpawnSlot slot in items)
                {
                    Vector3 plan = PLGSpace.ToPlan(slot.Pose.position);
                    PLGPlacedModule placed = generator.Layout.Modules[slot.Module];
                    Assert.IsTrue(PLG.Footprint(generator.Modules[placed.Def], placed).IsInside(new Vector2(plan.x, plan.y)), $"slot of module {slot.Module}");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TestKitsFailWithoutBuilding()
        {
            LogAssert.ignoreFailingMessages = true;
            foreach (string kit in new[] { "empty", "impossible" })
            {
                var go = new GameObject("PLG Test Generator " + kit);
                try
                {
                    var generator = go.AddComponent<PLGGenerator>();
                    generator.ApplyTestKit(kit);
                    bool? reported = null;
                    generator.Generated += (_, ok) => reported = ok;
                    generator.GenerateWithSeed(535117761, true);
                    Assert.IsFalse(generator.IsGenerated, kit);
                    Assert.AreEqual(false, reported, kit);
                    Assert.AreEqual(0, generator.BoxCount, kit);
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }
        }
    }
}
