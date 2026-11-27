using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// Start screen for the prototype: host a game (this machine is the server and also plays) or join one by IP.
    /// Uses Netcode for GameObjects with the Unity Transport on port 7777.
    /// Test hooks: -dmhost starts hosting at once, -dmclient=IP joins at once.
    /// </summary>
    public class DMConnectMenu : MonoBehaviour
    {
        const ushort Port = 7777;

        [SerializeField] string address = "127.0.0.1";

        GUIStyle title;
        GUIStyle body;
        string status = string.Empty;

        /// <summary>The menu is showing (no game yet), so a click is for the menu and not the game.</summary>
        public static bool IsOpen { get; private set; } = true;

        void Start()
        {
            // Two copies on one PC keep running while the other has focus.
            Application.runInBackground = true;
            if (DMArgs.Has("dmhost"))
            {
                StartHost();
            }
            else if (DMArgs.TryGet("dmclient=", out string ip))
            {
                address = ip;
                StartClient();
            }
            else if (DMArgs.Has("dmclient"))
            {
                StartClient();
            }
        }

        static NetworkManager Net => NetworkManager.Singleton;

        public void StartHost()
        {
            Net.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", Port, "0.0.0.0");
            status = Net.StartHost() ? string.Empty : "호스트를 시작하지 못했습니다";
        }

        public void StartClient()
        {
            Net.GetComponent<UnityTransport>().SetConnectionData(address.Trim(), Port);
            status = Net.StartClient() ? $"{address} 에 접속하는 중..." : "접속을 시작하지 못했습니다";
        }

        void Update()
        {
            IsOpen = Net == null || !Net.IsListening;
        }

        void OnGUI()
        {
            if (Net == null)
            {
                return;
            }
            title ??= new GUIStyle(GUI.skin.label) { font = DMHud.KoreanFont, fontSize = 32, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            body ??= new GUIStyle(GUI.skin.label) { font = DMHud.KoreanFont, fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };

            if (Net.IsListening)
            {
                if (Net.IsClient && !Net.IsConnectedClient && !Net.IsServer)
                {
                    GUI.Label(new Rect(0, Screen.height * 0.5f - 20, Screen.width, 40), status, body);
                }
                return;
            }

            float width = 360f;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height * 0.3f;
            GUI.Label(new Rect(0, y, Screen.width, 50), "꿈 수거반", title);
            y += 70;
            if (GUI.Button(new Rect(x, y, width, 40), "호스트로 시작 (내가 서버)"))
            {
                StartHost();
            }
            y += 60;
            GUI.Label(new Rect(x, y, 80, 30), "주소", body);
            address = GUI.TextField(new Rect(x + 80, y, width - 80, 30), address);
            y += 40;
            if (GUI.Button(new Rect(x, y, width, 40), "참가"))
            {
                StartClient();
            }
            y += 60;
            GUI.Label(new Rect(0, y, Screen.width, 30), "같은 PC에서 둘 다 해 보려면 빌드를 두 번 실행해 하나는 호스트, 하나는 참가", body);
            if (!string.IsNullOrEmpty(status))
            {
                GUI.Label(new Rect(0, y + 30, Screen.width, 30), status, body);
            }
        }
    }
}
