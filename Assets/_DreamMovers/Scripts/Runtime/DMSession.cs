using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DreamMovers
{
    /// <summary>
    /// Getting into a room and out of it: the menu asks to host or join, the game scene loads, the network starts, and
    /// leaving (or being dropped) shuts it down and goes back to the menu with the reason.
    /// The host checks every player who knocks: same game version, room not full, and (if turned off) no joining
    /// while a round runs. Connection today is by IP address on port 7777; the room's "friends only" choice is kept
    /// for when the connection method (Unity Relay join code or Steam invites) is decided.
    /// </summary>
    public static class DMSession
    {
        public const string MenuScene = "MainMenu";
        public const string GameScene = "ProcGen";
        public const ushort Port = 7777;
        public const int MaxRoomSize = 4;
        /// <summary>A client that has not connected after this long gives up (the transport would try for a minute).</summary>
        public const float ConnectTimeoutSeconds = 12f;

        static readonly Dictionary<ulong, string> Names = new Dictionary<ulong, string>();
        static NetworkManager hooked;
        static AsyncOperation sceneLoad;
        static float connectStartedAt;
        static bool leaving;

        /// <summary>What the menu asked for and the game scene has not started yet.</summary>
        public static DMSessionRole Pending { get; private set; }
        public static DMSessionRole Role { get; private set; }
        public static string Address { get; private set; } = "127.0.0.1";
        public static int MaxPlayers { get; private set; } = MaxRoomSize;
        public static bool FriendsOnly { get; private set; }
        /// <summary>The room accepts players while a round runs (they start at the entrance). Off = only between rounds.</summary>
        public static bool AllowJoinDuringRound { get; set; } = true;
        /// <summary>This client got into the room at least once (decides what a drop means).</summary>
        public static bool WasConnected { get; private set; }
        /// <summary>A message for the menu to show once (why we left the room).</summary>
        public static string MenuMessage { get; set; }

        public static bool IsLoadingScene => sceneLoad != null && !sceneLoad.isDone;

        /// <summary>Static state survives entering play mode again when domain reload is off.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Names.Clear();
            hooked = null;
            sceneLoad = null;
            leaving = false;
            Pending = DMSessionRole.None;
            Role = DMSessionRole.None;
            WasConnected = false;
            MenuMessage = null;
            AllowJoinDuringRound = true;
            MaxPlayers = MaxRoomSize;
            FriendsOnly = false;
        }

        static NetworkManager Net => NetworkManager.Singleton;

        public static bool IsListening => Net != null && Net.IsListening;

        /// <summary>A client still knocking on the host's door.</summary>
        public static bool IsConnecting => IsListening && Net.IsClient && !Net.IsServer && !Net.IsConnectedClient;

        public static float ConnectingSeconds => IsConnecting ? Time.unscaledTime - connectStartedAt : 0f;

        /// <summary>Asked for a room or in one: the menus stay closed.</summary>
        public static bool InRoom => Pending != DMSessionRole.None || IsListening || IsLoadingScene;

        public static void Host(int maxPlayers, bool friendsOnly)
        {
            MaxPlayers = Mathf.Clamp(maxPlayers, 1, MaxRoomSize);
            FriendsOnly = friendsOnly;
            Pending = DMSessionRole.Host;
            EnterGameScene();
        }

        public static void Join(string address)
        {
            Address = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            DMSettings settings = DMSettings.Current;
            settings.lastAddress = Address;
            settings.Save();
            Pending = DMSessionRole.Client;
            EnterGameScene();
        }

        static void EnterGameScene()
        {
            leaving = false;
            if (SceneManager.GetActiveScene().name == GameScene && Net != null)
            {
                StartPending();
                return;
            }
            sceneLoad = SceneManager.LoadSceneAsync(GameScene, LoadSceneMode.Single);
        }

        /// <summary>
        /// The game scene is up: start what the menu asked for. Dev and test runs can ask from the command line
        /// instead (-dmhost, -dmclient[=IP]). Returns false (with <see cref="MenuMessage"/>) when the network would not start.
        /// </summary>
        public static bool StartPending()
        {
            if (Pending == DMSessionRole.None)
            {
                return false;
            }
            NetworkManager net = Net;
            if (net == null)
            {
                Fail("이 씬에는 네트워크가 없습니다");
                return false;
            }
            if (net.IsListening)
            {
                Pending = DMSessionRole.None;
                return true;
            }

            DMSessionRole role = Pending;
            Pending = DMSessionRole.None;
            Hook(net);
            net.NetworkConfig.ConnectionApproval = true;
            net.ConnectionApprovalCallback = Approve;
            string nickname = DMSettings.Current.EnsureNickname();
            net.NetworkConfig.ConnectionData = Payload(Application.version, nickname);
            Names.Clear();
            WasConnected = false;
            var transport = net.GetComponent<UnityTransport>();
            bool started;
            if (role == DMSessionRole.Host)
            {
                transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
                started = net.StartHost();
            }
            else
            {
                transport.SetConnectionData(Address, Port);
                transport.ConnectTimeoutMS = 1000;
                transport.MaxConnectAttempts = Mathf.CeilToInt(ConnectTimeoutSeconds);
                connectStartedAt = Time.unscaledTime;
                started = net.StartClient();
            }
            if (!started)
            {
                Fail(role == DMSessionRole.Host ? $"방을 만들지 못했습니다. 포트 {Port}을 다른 프로그램이 쓰고 있는지 확인해 주세요" : "접속을 시작하지 못했습니다. 주소를 확인해 주세요");
                return false;
            }
            Role = role;
            return true;
        }

        /// <summary>Dev and test hooks: -dmhost hosts at once, -dmclient=IP joins at once.</summary>
        public static bool AskFromCommandLine()
        {
            if (DMArgs.Has("dmhost"))
            {
                MaxPlayers = MaxRoomSize;
                Pending = DMSessionRole.Host;
                return true;
            }
            if (DMArgs.TryGet("dmclient=", out string ip))
            {
                Address = ip;
                Pending = DMSessionRole.Client;
                return true;
            }
            if (DMArgs.Has("dmclient"))
            {
                Pending = DMSessionRole.Client;
                return true;
            }
            return false;
        }

        /// <summary>Go where <see cref="AskFromCommandLine"/> asked: start at once in the game scene, or load it first.</summary>
        public static void EnterAskedRoom()
        {
            if (Pending != DMSessionRole.None)
            {
                EnterGameScene();
            }
        }

        /// <summary>Leave the room (or stop knocking) and go back to the menu. The host closes the room for everyone.</summary>
        public static void Leave(string message = null)
        {
            if (leaving)
            {
                return;
            }
            leaving = true;
            Pending = DMSessionRole.None;
            if (!string.IsNullOrEmpty(message))
            {
                MenuMessage = message;
            }
            NetworkManager net = Net;
            if (net != null)
            {
                Unhook(net);
                if (net.IsListening)
                {
                    if (net.IsServer)
                    {
                        foreach (ulong clientId in net.ConnectedClientsIds.Where(id => id != NetworkManager.ServerClientId).ToList())
                        {
                            net.DisconnectClient(clientId, "방장이 방을 닫았습니다");
                        }
                    }
                    net.Shutdown();
                }
                // A fresh one comes with the game scene next time.
                UnityEngine.Object.Destroy(net.gameObject);
            }
            Role = DMSessionRole.None;
            Names.Clear();
            sceneLoad = SceneManager.LoadSceneAsync(MenuScene, LoadSceneMode.Single);
        }

        /// <summary>Called every frame by the out-game UI: a client that knocks for too long gives up.</summary>
        public static void Tick()
        {
            if (sceneLoad != null && sceneLoad.isDone)
            {
                sceneLoad = null;
                leaving = false;
            }
            if (IsConnecting && ConnectingSeconds > ConnectTimeoutSeconds + 2f)
            {
                Leave($"{Address}에 접속하지 못했습니다. 주소와 방장 상태를 확인해 주세요");
            }
        }

        static void Fail(string message)
        {
            Pending = DMSessionRole.None;
            Leave(message);
        }

        // ---- Network events ----

        static void Hook(NetworkManager net)
        {
            if (hooked == net)
            {
                return;
            }
            if (hooked != null)
            {
                Unhook(hooked);
            }
            hooked = net;
            net.OnClientConnectedCallback += OnClientConnected;
            net.OnClientDisconnectCallback += OnClientDisconnected;
            net.OnTransportFailure += OnTransportFailure;
        }

        static void Unhook(NetworkManager net)
        {
            if (hooked != net)
            {
                return;
            }
            net.OnClientConnectedCallback -= OnClientConnected;
            net.OnClientDisconnectCallback -= OnClientDisconnected;
            net.OnTransportFailure -= OnTransportFailure;
            hooked = null;
        }

        static void OnClientConnected(ulong clientId)
        {
            if (Net != null && !Net.IsServer && clientId == Net.LocalClientId)
            {
                WasConnected = true;
            }
        }

        static void OnClientDisconnected(ulong clientId)
        {
            NetworkManager net = Net;
            if (net == null)
            {
                return;
            }
            if (net.IsServer)
            {
                Names.Remove(clientId);
                return;
            }
            // A client only hears about itself: refused, dropped, or the host left.
            string reason = net.DisconnectReason;
            // Netcode's own reasons ("[Disconnect Event]...") are for the log; the host's refusals are for the player.
            if (!string.IsNullOrEmpty(reason) && reason.StartsWith("["))
            {
                Debug.Log($"[DreamMovers] disconnected: {reason}");
                reason = null;
            }
            if (string.IsNullOrEmpty(reason))
            {
                reason = WasConnected ? "방장과 연결이 끊겼습니다" : $"{Address}에 접속하지 못했습니다. 주소와 방장 상태를 확인해 주세요";
            }
            Leave(reason);
        }

        static void OnTransportFailure()
        {
            Leave(Net != null && Net.IsServer ? "네트워크 오류로 방이 닫혔습니다" : "네트워크 오류로 연결이 끊겼습니다");
        }

        // ---- Who may come in ----

        public struct JoinRequest
        {
            public string Version;
            public string Nickname;
        }

        public static byte[] Payload(string version, string nickname) => Encoding.UTF8.GetBytes($"{version}\n{DMSettings.CleanNickname(nickname)}");

        public static JoinRequest ReadPayload(byte[] payload)
        {
            if (payload == null || payload.Length == 0 || payload.Length > 256)
            {
                return default;
            }
            string[] parts = Encoding.UTF8.GetString(payload).Split('\n');
            return new JoinRequest
            {
                Version = parts.Length > 0 ? parts[0] : null,
                Nickname = parts.Length > 1 ? DMSettings.CleanNickname(parts[1]) : string.Empty
            };
        }

        /// <summary>The host's rule for a knocking player. Empty reason when let in.</summary>
        public static bool Evaluate(JoinRequest request, string hostVersion, int playersInRoom, int maxPlayers, bool roundRunning, bool allowDuringRound, out string reason)
        {
            if (string.IsNullOrEmpty(request.Version) || request.Version != hostVersion)
            {
                reason = $"게임 버전이 다릅니다 (방: {hostVersion}, 나: {(string.IsNullOrEmpty(request.Version) ? "알 수 없음" : request.Version)})";
                return false;
            }
            if (playersInRoom >= maxPlayers)
            {
                reason = $"방이 가득 찼습니다 (최대 {maxPlayers}명)";
                return false;
            }
            if (roundRunning && !allowDuringRound)
            {
                reason = "판이 진행 중이라 지금은 들어갈 수 없습니다. 판이 끝나면 다시 참가해 주세요";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        static void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            NetworkManager net = Net;
            JoinRequest join = ReadPayload(request.Payload);
            bool isHost = net != null && request.ClientNetworkId == NetworkManager.ServerClientId;
            int inRoom = net != null ? net.ConnectedClientsIds.Count : 0;
            bool roundRunning = DMGameState.Instance != null && DMGameState.Instance.RoundState == DMRoundState.Playing;
            string reason = string.Empty;
            bool approved = isHost || Evaluate(join, Application.version, inRoom, MaxPlayers, roundRunning, AllowJoinDuringRound, out reason);
            response.Approved = approved;
            response.Reason = approved ? string.Empty : reason;
            // Dolls are spawned by the game mode, not as automatic player objects.
            response.CreatePlayerObject = false;
            response.Pending = false;
            if (approved)
            {
                Names[request.ClientNetworkId] = string.IsNullOrEmpty(join.Nickname) ? DefaultName(request.ClientNetworkId) : join.Nickname;
            }
        }

        static string DefaultName(ulong clientId) => $"플레이어 {clientId + 1}";

        /// <summary>Server: the nickname a player sent when joining.</summary>
        public static string NameFor(ulong clientId) => Names.TryGetValue(clientId, out string name) ? name : DefaultName(clientId);

        /// <summary>This PC's IPv4 addresses on the local network, for the room screen ("friends join with this").</summary>
        public static List<string> LocalAddresses()
        {
            var addresses = new List<string>();
            try
            {
                foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                    {
                        addresses.Add(address.ToString());
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DreamMovers] local address lookup failed: {e.Message}");
            }
            return addresses;
        }
    }
}
