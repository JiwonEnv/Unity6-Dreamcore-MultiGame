using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProcLevelGen.Tests
{
    /// <summary>
    /// Port of the UE5 -plgselftest run (seed 535117761, as in TestEvidence): generate in play mode, wait for the
    /// runtime navmesh like the game does (ready twice in a row), then run the generator's self-test. Screenshots go
    /// to Logs/PLGSelfTest under the project.
    /// </summary>
    public class PLGSelfTest
    {
        GameObject generatorObject;
        GameObject light;

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(generatorObject);
            Object.Destroy(light);
            GameObject probe = GameObject.Find("PLG SelfTest Player");
            if (probe != null)
            {
                Object.Destroy(probe);
            }
        }

        // URP 17.3 logs this from its own probe volume shader the first time it renders in some editor sessions
        // (also in a fresh project made from Unity's URP template). It is not ours, so it alone is tolerated.
        const string KnownUrpError = "Host type is not matching any asset type";

        [UnityTest]
        public IEnumerator Seed535117761PassesTheSelfTest()
        {
            var errors = new System.Collections.Generic.List<string>();
            void OnLog(string message, string stack, LogType type)
            {
                if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !message.Contains(KnownUrpError))
                {
                    errors.Add(message);
                }
            }
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += OnLog;
            try
            {
                yield return RunSelfTest();
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }
            Assert.IsEmpty(errors, "errors logged during the self-test");
        }

        IEnumerator RunSelfTest()
        {
            light = new GameObject("Sun", typeof(Light));
            light.GetComponent<Light>().type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            generatorObject = new GameObject("LevelGenerator");
            generatorObject.SetActive(false);
            var generator = generatorObject.AddComponent<PLGGenerator>();
            generatorObject.SetActive(true);
            // Start() picks a random seed; build the UE5 test seed instead, as -plgseed would.
            yield return null;
            generator.GenerateWithSeed(535117761, true);
            Assert.IsTrue(generator.IsGenerated);
            Assert.AreEqual(0xe94bc661u, generator.LayoutHash, "same layout as the UE5 self-test run");

            float waited = 0f;
            int readyPolls = 0;
            string why = string.Empty;
            while (readyPolls < 2 && waited < 30f)
            {
                yield return new WaitForSeconds(0.25f);
                waited += 0.25f;
                readyPolls = generator.IsNavigationReady(out why) ? readyPolls + 1 : 0;
            }
            Assert.AreEqual(2, readyPolls, $"navigation not ready after {waited:0.0}s: {why}");

            string shotDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/PLGSelfTest"));
            yield return generator.RunSelfTest(shotDir, false);
            Assert.AreEqual(0, generator.SelfTestFailures, "see the [PLGSelfTest] lines in the log");
            Assert.IsTrue(File.Exists(Path.Combine(shotDir, "PLGSelfTest_Top.png")));
        }
    }
}
