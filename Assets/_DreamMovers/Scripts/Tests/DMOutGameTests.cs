using System.Collections;
using System.IO;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DreamMovers.Tests
{
    /// <summary>
    /// Out-game checks: settings save and load, the host's join rules, and the whole menu flow (menu scene → create a
    /// room → game scene hosting → pause → leave → back in the menu), plus a failed join coming back with a reason.
    /// Settings go to a temporary file, never the player's own.
    /// </summary>
    public class DMOutGameTests
    {
        const string MenuScenePath = "Assets/_DreamMovers/Scenes/MainMenu.unity";
        // URP 17.3 logs this from its own probe volume shader in some editor sessions; not ours (see PLGSelfTest).
        const string KnownUrpError = "Host type is not matching any asset type";
        // Unity Transport logs this when a join gets no answer, which is what the failed-join step does on purpose.
        const string ExpectedJoinFailure = "Failed to connect to server";

        string settingsPath;

        [SetUp]
        public void SetUp()
        {
            settingsPath = Path.Combine(Application.temporaryCachePath, $"dm_settings_test_{System.Guid.NewGuid():N}.json");
            DMSettings.PathOverride = settingsPath;
            DMSettings.Reload();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
                Object.Destroy(NetworkManager.Singleton.gameObject);
            }
            yield return null;
            Scene empty = SceneManager.CreateScene($"DMOutGame Empty {System.Guid.NewGuid():N}");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; --i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded)
                {
                    yield return SceneManager.UnloadSceneAsync(scene);
                }
            }
            if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }
            DMSettings.PathOverride = null;
            DMSettings.Reload();
            DMSession.MenuMessage = null;
        }

        [Test]
        public void SettingsSaveAndLoad()
        {
            DMSettings settings = DMSettings.Current;
            settings.masterVolume = 0.25f;
            settings.mouseSensitivity = 2.5f;
            settings.invertY = true;
            settings.collapseIntensity = 0.4f;
            settings.language = "en";
            settings.nickname = "  솜뭉치가된곰인형입니다아아  ";
            settings.Save();

            DMSettings.Reload();
            DMSettings loaded = DMSettings.Current;
            Assert.AreEqual(0.25f, loaded.masterVolume, 1e-4f);
            Assert.AreEqual(2.5f, loaded.mouseSensitivity, 1e-4f);
            Assert.IsTrue(loaded.invertY);
            Assert.AreEqual(0.4f, loaded.collapseIntensity, 1e-4f);
            Assert.AreEqual("en", loaded.language);
            Assert.AreEqual(DMSettings.MaxNicknameLength, loaded.nickname.Length, "nickname trimmed and cut to 12");

            File.WriteAllText(settingsPath, "{ not json");
            DMSettings.Reload();
            Assert.AreEqual(1f, DMSettings.Current.masterVolume, "a broken file falls back to defaults");

            DMSettings.Current.masterVolume = 9f;
            DMSettings.Current.language = "xx";
            DMSettings.Current.Sanitize();
            Assert.AreEqual(1f, DMSettings.Current.masterVolume);
            Assert.AreEqual("ko", DMSettings.Current.language);

            DMSettings.Current.nickname = "곰";
            DMSettings.Current.mouseSensitivity = 2f;
            DMSettings.Current.ResetOptions();
            Assert.AreEqual(1f, DMSettings.Current.mouseSensitivity, "reset restores options");
            Assert.AreEqual("곰", DMSettings.Current.nickname, "reset keeps the profile");
        }

        [Test]
        public void KeyChangesAreSavedAndRestored()
        {
            DMControls.Map.RemoveAllBindingOverrides();
            DMControls.Jump.ApplyBindingOverride(0, "<Keyboard>/f");
            string saved = DMControls.SaveOverrides();
            DMControls.Map.RemoveAllBindingOverrides();
            Assert.AreEqual("Space", DMControls.Jump.GetBindingDisplayString(0));
            DMControls.LoadOverrides(saved);
            Assert.AreEqual("F", DMControls.Jump.GetBindingDisplayString(0));
            DMControls.LoadOverrides(string.Empty);
            Assert.AreEqual("Space", DMControls.Jump.GetBindingDisplayString(0));
            Assert.AreEqual(9, DMControls.Rebindables().Count, "WASD (4) + jump, sprint, grab, crouch, interact");
        }

        [Test]
        public void HostLetsInOnlyWhoFits()
        {
            DMSession.JoinRequest request = DMSession.ReadPayload(DMSession.Payload("0.1", "토끼"));
            Assert.AreEqual("0.1", request.Version);
            Assert.AreEqual("토끼", request.Nickname);

            Assert.IsTrue(DMSession.Evaluate(request, "0.1", 1, 4, false, true, out string reason));
            Assert.IsEmpty(reason);
            Assert.IsFalse(DMSession.Evaluate(request, "0.2", 1, 4, false, true, out reason));
            StringAssert.Contains("버전", reason);
            Assert.IsFalse(DMSession.Evaluate(request, "0.1", 4, 4, false, true, out reason));
            StringAssert.Contains("가득", reason);
            Assert.IsFalse(DMSession.Evaluate(request, "0.1", 1, 1, false, true, out reason), "a solo room takes nobody");
            Assert.IsTrue(DMSession.Evaluate(request, "0.1", 2, 4, true, true, out reason), "joining mid-round allowed by default");
            Assert.IsFalse(DMSession.Evaluate(request, "0.1", 2, 4, true, false, out reason));
            StringAssert.Contains("진행 중", reason);
            Assert.IsFalse(DMSession.Evaluate(DMSession.ReadPayload(null), "0.1", 1, 4, false, true, out reason), "no payload");
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float timeout)
        {
            float until = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator MenuToRoomAndBack()
        {
            LogAssert.ignoreFailingMessages = true;
            var errors = new System.Collections.Generic.List<string>();
            void OnLog(string message, string stack, LogType type)
            {
                if ((type == LogType.Error || type == LogType.Exception) && !message.Contains(KnownUrpError) && !message.Contains(ExpectedJoinFailure))
                {
                    errors.Add(message);
                }
            }
            Application.logMessageReceived += OnLog;
            try
            {
                yield return SceneManager.LoadSceneAsync(MenuScenePath, LoadSceneMode.Single);
                yield return null;
                DMOutGameUI ui = DMOutGameUI.Instance;
                Assert.IsNotNull(ui, "menu scene has the out-game UI");
                Assert.IsTrue(ui.Current == DMOutGameScreen.Title || ui.Current == DMOutGameScreen.Menu, $"starts on title or menu, was {ui.Current}");
                Assert.IsTrue(DMOutGameUI.BlocksGameInput);

                ui.Show(DMOutGameScreen.Menu);
                Assert.IsFalse(string.IsNullOrEmpty(DMSettings.Current.nickname), "a nickname is picked on first visit");
                ui.Show(DMOutGameScreen.Settings);
                ui.SettingsView.Open();
                ui.SettingsView.ShowTab("controls");
                ui.CloseSettings();
                Assert.AreEqual(DMOutGameScreen.Menu, ui.Current);
                Assert.IsTrue(File.Exists(settingsPath), "closing settings saves");

                // Create a room: the game scene loads and this machine hosts.
                ui.Show(DMOutGameScreen.CreateRoom);
                ui.CreateRoom();
                yield return WaitFor(() => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer, 30f);
                Assert.IsTrue(NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer, "hosting after create room");
                Assert.AreEqual(DMSession.GameScene, SceneManager.GetActiveScene().name);
                ui = DMOutGameUI.Instance;
                Assert.IsNotNull(ui, "game scene has the out-game UI");

                yield return WaitFor(() => DMGameState.Instance != null && DMGameState.Instance.RoundState == DMRoundState.Playing, 60f);
                Assert.AreEqual(DMRoundState.Playing, DMGameState.Instance.RoundState, "round starts");
                yield return null;
                Assert.IsFalse(ui.LoadingVisible, "loading screen gone once the round plays");
                Assert.AreEqual(DMOutGameScreen.None, ui.Current, "no menu over the game");
                Assert.AreEqual(1, DMGameState.Instance.Players.Count, "host is in the player list");
                Assert.AreEqual(DMSettings.Current.nickname, DMGameState.Instance.Players[0].Name.ToString(), "with its nickname");

                ui.OpenPause();
                Assert.IsTrue(DMOutGameUI.BlocksGameInput, "pause frees the mouse");
                ui.Resume();
                Assert.AreEqual(DMOutGameScreen.None, ui.Current);

                // Leave: back to the menu, network gone.
                DMSession.Leave();
                yield return WaitFor(() => SceneManager.GetActiveScene().name == DMSession.MenuScene && DMOutGameUI.Instance != null, 30f);
                yield return null;
                Assert.AreEqual(DMSession.MenuScene, SceneManager.GetActiveScene().name, "back in the menu");
                Assert.IsTrue(NetworkManager.Singleton == null, "network shut down and removed");
                Assert.AreEqual(DMOutGameScreen.Menu, DMOutGameUI.Instance.Current, "menu (title only once)");

                // Join nowhere: comes back to the menu with the reason.
                DMOutGameUI.Instance.Show(DMOutGameScreen.JoinRoom);
                DMSession.Join("127.0.0.1");
                yield return WaitFor(() => DMSession.IsConnecting, 20f);
                Assert.IsTrue(DMSession.IsConnecting || SceneManager.GetActiveScene().name == DMSession.MenuScene, "knocking");
                yield return WaitFor(() => SceneManager.GetActiveScene().name == DMSession.MenuScene && DMOutGameUI.Instance != null && DMOutGameUI.Instance.PopupOpen, DMSession.ConnectTimeoutSeconds + 15f);
                Assert.AreEqual(DMSession.MenuScene, SceneManager.GetActiveScene().name, "failed join returns to the menu");
                Assert.IsTrue(DMOutGameUI.Instance.PopupOpen, "with a pop-up");
                StringAssert.Contains("접속하지 못했습니다", DMOutGameUI.Instance.PopupText);
                Debug.Log($"[DMTest] PASS out-game flow; failed join said: {DMOutGameUI.Instance.PopupText}");

                Assert.IsEmpty(errors, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }
        }
    }
}
