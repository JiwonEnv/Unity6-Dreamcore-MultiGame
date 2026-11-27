using System.Collections;
using System.IO;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// -dmuishot=FOLDER (dev builds and the editor only): walks through every out-game screen, saves a screenshot of
    /// each (title, menu, create and join room, settings tabs, credits, a pop-up, then hosts and shows loading and the
    /// pause menu) and quits. For checking the layouts and the Korean font without clicking through by hand.
    /// </summary>
    public class DMUIShots : MonoBehaviour
    {
        string folder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfAsked()
        {
            if (DMArgs.TryGet("dmuishot=", out string path))
            {
                var shots = new GameObject("DM UI Shots").AddComponent<DMUIShots>();
                shots.folder = path;
                DontDestroyOnLoad(shots.gameObject);
            }
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            yield return new WaitUntil(() => DMOutGameUI.Instance != null);
            DMOutGameUI ui = DMOutGameUI.Instance;
            yield return Shot(ui, DMOutGameScreen.Title, "01_title");
            yield return Shot(ui, DMOutGameScreen.Menu, "02_menu");
            yield return Shot(ui, DMOutGameScreen.CreateRoom, "03_create_room");
            yield return Shot(ui, DMOutGameScreen.JoinRoom, "04_join_room");
            ui.SettingsView.Open();
            foreach (string tab in new[] { "graphics", "audio", "controls", "voice", "accessibility", "language" })
            {
                ui.SettingsView.ShowTab(tab);
                yield return Shot(ui, DMOutGameScreen.Settings, $"05_settings_{tab}");
            }
            ui.SettingsView.Close();
            yield return Shot(ui, DMOutGameScreen.Credits, "06_credits");
            ui.Show(DMOutGameScreen.Menu);
            ui.ShowPopup("종료", "게임을 끌까요?", "끄기", null, "취소", null);
            yield return Capture("07_popup");
            ui.ClosePopup(false);

            ui.Show(DMOutGameScreen.CreateRoom);
            ui.CreateRoom();
            yield return new WaitUntil(() => DMOutGameUI.Instance != null && DMOutGameUI.Instance.LoadingVisible && DMSession.IsListening);
            yield return Capture("08_loading");
            yield return new WaitUntil(() => DMGameState.Instance != null && DMGameState.Instance.RoundState == DMRoundState.Playing);
            yield return new WaitForSeconds(1f);
            DMOutGameUI.Instance.OpenPause();
            yield return Capture("09_pause");
            Application.Quit(0);
        }

        IEnumerator Shot(DMOutGameUI ui, DMOutGameScreen screen, string name)
        {
            ui.Show(screen);
            yield return Capture(name);
        }

        IEnumerator Capture(string name)
        {
            // Two frames: one to lay out, one to draw.
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));
            yield return new WaitForSeconds(0.3f);
        }
    }
}
