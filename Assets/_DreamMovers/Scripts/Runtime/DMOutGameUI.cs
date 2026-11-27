using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace DreamMovers
{
    /// <summary>
    /// Every screen outside play: title, main menu, room create and join, settings, credits, the pause menu, the
    /// loading screen and pop-ups. One per scene (the menu scene and the game scene each have one); what to show
    /// follows <see cref="DMSession"/>. The look is in the UI_*.uxml and UI_OutGame.uss files; this only finds
    /// elements by name, so the layout can change in UI Builder as long as the names stay.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class DMOutGameUI : MonoBehaviour
    {
        static readonly string[] Tips =
        {
            "무거운 골동품은 둘이 같이 잡아야 들립니다",
            "달리면 종 달린 말이 듣습니다",
            "촛대 집사의 촛불 빛 밖, 그림자로 다니세요",
            "골동품을 가져갈수록 꿈이 빨리 깹니다",
            "잡힌 친구는 솜뭉치가 됩니다. 오르골 침대로 데려가면 살아납니다",
        };

        static bool titleShown;
        static bool commandLineUsed;

        [Header("Screens (Art/UI/OutGame)")]
        [SerializeField] VisualTreeAsset titleScreen;
        [SerializeField] VisualTreeAsset mainMenuScreen;
        [SerializeField] VisualTreeAsset createRoomScreen;
        [SerializeField] VisualTreeAsset joinRoomScreen;
        [SerializeField] VisualTreeAsset settingsScreen;
        [SerializeField] VisualTreeAsset creditsScreen;
        [SerializeField] VisualTreeAsset pauseScreen;
        [SerializeField] VisualTreeAsset loadingScreen;
        [SerializeField] VisualTreeAsset popupScreen;

        readonly Dictionary<DMOutGameScreen, VisualElement> screens = new Dictionary<DMOutGameScreen, VisualElement>();
        VisualElement loading;
        VisualElement popup;
        Label loadingText;
        Label loadingDetail;
        Button loadingCancel;
        Label popupTitle;
        Label popupMessage;
        Button popupOk;
        Button popupCancel;
        Action onPopupOk;
        Action onPopupCancel;
        TextField nicknameField;
        TextField addressField;
        DropdownField maxPlayersField;
        DropdownField visibilityField;
        Label myAddress;
        Label pauseKeys;
        Label pausePlayers;
        DMSettingsScreen settingsView;
        DMOutGameScreen current = DMOutGameScreen.None;
        DMOutGameScreen settingsReturn = DMOutGameScreen.Menu;
        bool built;
        int tip;

        public static DMOutGameUI Instance { get; private set; }

        /// <summary>A screen, pop-up or the loading screen is up: the doll should not take the mouse.</summary>
        public static bool BlocksGameInput => Instance != null && Instance.Blocking;

        /// <summary>The loading screen covers everything (the HUD stays hidden).</summary>
        public static bool CoversScreen => Instance != null && Instance.LoadingVisible;

        public DMOutGameScreen Current => current;
        public bool PopupOpen { get; private set; }
        public bool LoadingVisible { get; private set; }
        public string PopupText => popupMessage != null ? popupMessage.text : string.Empty;
        public bool Blocking => current != DMOutGameScreen.None || PopupOpen || LoadingVisible;
        public DMSettingsScreen SettingsView => settingsView;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            titleShown = false;
            commandLineUsed = false;
            Instance = null;
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            settingsView?.Dispose();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void Start()
        {
            Build();
            if (DMSession.Pending != DMSessionRole.None)
            {
                // The menu asked for a room and this is the game scene.
                DMSession.StartPending();
            }
            else if (!commandLineUsed && !DMSession.IsListening)
            {
                commandLineUsed = true;
                if (DMSession.AskFromCommandLine())
                {
                    titleShown = true;
                    DMSession.EnterAskedRoom();
                }
            }

            if (DMSession.InRoom)
            {
                Show(DMOutGameScreen.None);
            }
            else
            {
                Show(titleShown ? DMOutGameScreen.Menu : DMOutGameScreen.Title);
            }
            if (!string.IsNullOrEmpty(DMSession.MenuMessage) && !DMSession.InRoom)
            {
                ShowPopup("알림", DMSession.MenuMessage, "확인", null);
                DMSession.MenuMessage = null;
            }
            tip = UnityEngine.Random.Range(0, Tips.Length);
        }

        // ---- Building ----

        void Build()
        {
            if (built)
            {
                return;
            }
            built = true;
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            root.style.unityFontDefinition = FontDefinition.FromFont(DMHud.KoreanFont);

            VisualElement title = Add(root, DMOutGameScreen.Title, titleScreen);
            VisualElement menu = Add(root, DMOutGameScreen.Menu, mainMenuScreen);
            VisualElement create = Add(root, DMOutGameScreen.CreateRoom, createRoomScreen);
            VisualElement join = Add(root, DMOutGameScreen.JoinRoom, joinRoomScreen);
            VisualElement credits = Add(root, DMOutGameScreen.Credits, creditsScreen);
            VisualElement pause = Add(root, DMOutGameScreen.Pause, pauseScreen);
            VisualElement settings = Add(root, DMOutGameScreen.Settings, settingsScreen);
            loading = Holder(root, loadingScreen, "loading");
            popup = Holder(root, popupScreen, "popup");

            string version = $"v{Application.version}";
            SetText(title, "version", version);
            SetText(menu, "version", version);

            // Main menu
            nicknameField = menu?.Q<TextField>("nickname");
            if (nicknameField != null)
            {
                nicknameField.maxLength = DMSettings.MaxNicknameLength;
                nicknameField.RegisterCallback<FocusOutEvent>(_ => SaveNickname());
            }
            OnClick(menu, "create-room", () => Show(DMOutGameScreen.CreateRoom));
            OnClick(menu, "join-room", () => Show(DMOutGameScreen.JoinRoom));
            OnClick(menu, "settings", () => OpenSettings(DMOutGameScreen.Menu));
            OnClick(menu, "credits", () => Show(DMOutGameScreen.Credits));
            OnClick(menu, "quit", AskQuit);

            // Create room
            maxPlayersField = create?.Q<DropdownField>("max-players");
            if (maxPlayersField != null)
            {
                maxPlayersField.choices = Enumerable.Range(1, DMSession.MaxRoomSize).Select(n => n == 1 ? "1명 (혼자)" : $"{n}명").ToList();
                maxPlayersField.index = DMSession.MaxRoomSize - 1;
            }
            visibilityField = create?.Q<DropdownField>("visibility");
            if (visibilityField != null)
            {
                visibilityField.choices = new List<string> { "공개", "친구만" };
                visibilityField.index = 0;
            }
            myAddress = create?.Q<Label>("my-address");
            OnClick(create, "back", () => Show(DMOutGameScreen.Menu));
            OnClick(create, "create", CreateRoom);

            // Join room
            addressField = join?.Q<TextField>("address");
            OnClick(join, "back", () => Show(DMOutGameScreen.Menu));
            OnClick(join, "join", JoinRoom);

            OnClick(credits, "back", () => Show(DMOutGameScreen.Menu));

            // Pause
            pauseKeys = pause?.Q<Label>("keys");
            pausePlayers = pause?.Q<Label>("players");
            OnClick(pause, "resume", Resume);
            OnClick(pause, "settings", () => OpenSettings(DMOutGameScreen.Pause));
            OnClick(pause, "show-keys", ToggleKeyList);
            OnClick(pause, "leave", AskLeave);

            // Settings
            if (settings != null)
            {
                settingsView = new DMSettingsScreen(settings);
                OnClick(settings, "close", CloseSettings);
            }

            // Loading and pop-up
            loadingText = loading?.Q<Label>("loading-text");
            loadingDetail = loading?.Q<Label>("loading-detail");
            loadingCancel = loading?.Q<Button>("cancel");
            if (loadingCancel != null)
            {
                loadingCancel.clicked += () => DMSession.Leave();
            }
            popupTitle = popup?.Q<Label>("popup-title");
            popupMessage = popup?.Q<Label>("popup-message");
            popupOk = popup?.Q<Button>("popup-ok");
            popupCancel = popup?.Q<Button>("popup-cancel");
            if (popupOk != null)
            {
                popupOk.clicked += () => ClosePopup(true);
            }
            if (popupCancel != null)
            {
                popupCancel.clicked += () => ClosePopup(false);
            }
            SetVisible(loading, false);
            SetVisible(popup, false);
        }

        VisualElement Add(VisualElement root, DMOutGameScreen screen, VisualTreeAsset asset)
        {
            VisualElement holder = Holder(root, asset, screen.ToString());
            if (holder != null)
            {
                screens[screen] = holder;
                SetVisible(holder, false);
            }
            return holder;
        }

        static VisualElement Holder(VisualElement root, VisualTreeAsset asset, string name)
        {
            if (asset == null)
            {
                Debug.LogWarning($"[DreamMovers] out-game screen '{name}' has no layout assigned");
                return null;
            }
            var holder = new VisualElement { name = $"{name}-holder", pickingMode = PickingMode.Ignore };
            holder.style.position = Position.Absolute;
            holder.style.left = 0;
            holder.style.top = 0;
            holder.style.right = 0;
            holder.style.bottom = 0;
            asset.CloneTree(holder);
            root.Add(holder);
            return holder;
        }

        static void OnClick(VisualElement screen, string name, Action action)
        {
            Button button = screen?.Q<Button>(name);
            if (button == null)
            {
                if (screen != null)
                {
                    Debug.LogWarning($"[DreamMovers] button '{name}' is missing from {screen.name}");
                }
                return;
            }
            button.clicked += action;
        }

        static void SetText(VisualElement screen, string name, string text)
        {
            Label label = screen?.Q<Label>(name);
            if (label != null)
            {
                label.text = text;
            }
        }

        static void SetVisible(VisualElement element, bool visible)
        {
            if (element != null)
            {
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ---- Screens ----

        public void Show(DMOutGameScreen screen)
        {
            current = screen;
            foreach (KeyValuePair<DMOutGameScreen, VisualElement> entry in screens)
            {
                SetVisible(entry.Value, entry.Key == screen);
            }
            switch (screen)
            {
                case DMOutGameScreen.Title:
                    titleShown = true;
                    break;
                case DMOutGameScreen.Menu:
                    if (nicknameField != null)
                    {
                        nicknameField.SetValueWithoutNotify(DMSettings.Current.EnsureNickname());
                    }
                    break;
                case DMOutGameScreen.CreateRoom:
                    if (myAddress != null)
                    {
                        List<string> addresses = DMSession.LocalAddresses();
                        string mine = addresses.Count > 0 ? string.Join(", ", addresses) : "찾지 못함";
                        myAddress.text = $"내 주소: {mine} (포트 {DMSession.Port})\n같은 PC에서 참가: 127.0.0.1";
                    }
                    break;
                case DMOutGameScreen.JoinRoom:
                    addressField?.SetValueWithoutNotify(DMSettings.Current.lastAddress);
                    break;
                case DMOutGameScreen.Pause:
                    if (pauseKeys != null)
                    {
                        pauseKeys.text = DMControls.Summary();
                        SetVisible(pauseKeys, false);
                    }
                    RefreshPlayers();
                    break;
            }
        }

        public void ShowPopup(string title, string message, string okText, Action onOk, string cancelText = null, Action onCancel = null)
        {
            if (popup == null)
            {
                Debug.Log($"[DreamMovers] {title}: {message}");
                onOk?.Invoke();
                return;
            }
            popupTitle.text = title;
            popupMessage.text = message;
            popupOk.text = okText;
            SetVisible(popupCancel, cancelText != null);
            if (cancelText != null)
            {
                popupCancel.text = cancelText;
            }
            onPopupOk = onOk;
            onPopupCancel = onCancel;
            PopupOpen = true;
            SetVisible(popup, true);
        }

        public void ClosePopup(bool ok)
        {
            if (!PopupOpen)
            {
                return;
            }
            PopupOpen = false;
            SetVisible(popup, false);
            Action action = ok ? onPopupOk : onPopupCancel;
            onPopupOk = null;
            onPopupCancel = null;
            action?.Invoke();
        }

        void SaveNickname()
        {
            if (nicknameField == null)
            {
                return;
            }
            DMSettings settings = DMSettings.Current;
            string name = DMSettings.CleanNickname(nicknameField.value);
            if (string.IsNullOrEmpty(name))
            {
                // Empty is not allowed: keep the old one.
                nicknameField.SetValueWithoutNotify(settings.EnsureNickname());
                return;
            }
            if (name != settings.nickname)
            {
                settings.nickname = name;
                settings.Save();
            }
            nicknameField.SetValueWithoutNotify(name);
        }

        public void CreateRoom()
        {
            SaveNickname();
            int players = maxPlayersField != null && maxPlayersField.index >= 0 ? maxPlayersField.index + 1 : DMSession.MaxRoomSize;
            bool friendsOnly = visibilityField != null && visibilityField.index == 1;
            Show(DMOutGameScreen.None);
            DMSession.Host(players, friendsOnly);
        }

        public void JoinRoom()
        {
            SaveNickname();
            string address = addressField != null ? addressField.value : DMSettings.Current.lastAddress;
            Show(DMOutGameScreen.None);
            DMSession.Join(address);
        }

        void OpenSettings(DMOutGameScreen returnTo)
        {
            settingsReturn = returnTo;
            settingsView?.Open();
            Show(DMOutGameScreen.Settings);
        }

        public void CloseSettings()
        {
            settingsView?.Close();
            Show(DMSession.InRoom ? (settingsReturn == DMOutGameScreen.Pause ? DMOutGameScreen.Pause : DMOutGameScreen.None) : DMOutGameScreen.Menu);
        }

        public void OpenPause()
        {
            Show(DMOutGameScreen.Pause);
        }

        public void Resume()
        {
            Show(DMOutGameScreen.None);
            if (DMDoll.Local != null && Application.isFocused)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void ToggleKeyList()
        {
            if (pauseKeys != null)
            {
                SetVisible(pauseKeys, pauseKeys.resolvedStyle.display == DisplayStyle.None);
            }
        }

        void RefreshPlayers()
        {
            if (pausePlayers == null)
            {
                return;
            }
            DMGameState state = DMGameState.Instance;
            if (state == null)
            {
                pausePlayers.text = string.Empty;
                return;
            }
            var lines = new StringBuilder();
            NetworkManager net = NetworkManager.Singleton;
            foreach (DMPlayerEntry entry in state.Players)
            {
                lines.Append(entry.Name.ToString());
                if (entry.ClientId == NetworkManager.ServerClientId)
                {
                    lines.Append(" (방장)");
                }
                if (net != null && entry.ClientId == net.LocalClientId)
                {
                    lines.Append(" (나)");
                }
                lines.Append('\n');
            }
            pausePlayers.text = lines.ToString().TrimEnd();
        }

        void AskLeave()
        {
            NetworkManager net = NetworkManager.Singleton;
            bool host = net != null && net.IsServer;
            string message = host ? "방을 닫을까요? 다른 사람도 모두 메뉴로 돌아갑니다." : "방을 나갈까요?";
            ShowPopup("방 나가기", message, "나가기", () => DMSession.Leave(), "취소", null);
        }

        void AskQuit()
        {
            ShowPopup("종료", "게임을 끌까요?", "끄기", Quit, "취소", null);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---- Every frame ----

        void Update()
        {
            DMSession.Tick();
            bool inRoom = DMSession.InRoom;

            // Menus close once a room is asked for; the pause menu only exists in a room.
            if (inRoom && current != DMOutGameScreen.None && current != DMOutGameScreen.Pause && current != DMOutGameScreen.Settings)
            {
                Show(DMOutGameScreen.None);
            }
            else if (!inRoom && (current == DMOutGameScreen.None || current == DMOutGameScreen.Pause))
            {
                Show(titleShown ? DMOutGameScreen.Menu : DMOutGameScreen.Title);
            }

            UpdateLoading();
            if (current == DMOutGameScreen.Pause)
            {
                RefreshPlayers();
            }
            HandleKeys();

            if (Blocking && Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        void UpdateLoading()
        {
            string text = null;
            string detail = null;
            bool canCancel = false;
            DMGameState state = DMGameState.Instance;
            if (DMSession.IsLoadingScene)
            {
                text = "불러오는 중…";
            }
            else if (DMSession.IsConnecting)
            {
                text = "접속하는 중…";
                detail = $"{DMSession.Address} · {Mathf.FloorToInt(DMSession.ConnectingSeconds)}초";
                canCancel = true;
            }
            else if (DMSession.IsListening && (state == null || state.RoundState == DMRoundState.Preparing))
            {
                text = "꿈을 짓는 중…";
                detail = Tips[tip % Tips.Length];
                canCancel = true;
            }
            bool show = text != null;
            if (show && !LoadingVisible)
            {
                tip = (tip + 1) % Tips.Length;
            }
            LoadingVisible = show && loading != null;
            SetVisible(loading, LoadingVisible);
            if (LoadingVisible)
            {
                loadingText.text = text;
                loadingDetail.text = detail ?? string.Empty;
                SetVisible(loadingCancel, canCancel);
            }
        }

        void HandleKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (current == DMOutGameScreen.Title && !PopupOpen)
            {
                bool any = (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
                    || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
                if (any)
                {
                    Show(DMOutGameScreen.Menu);
                }
                return;
            }
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame || (settingsView != null && settingsView.EatsEscape))
            {
                return;
            }
            if (PopupOpen)
            {
                ClosePopup(false);
                return;
            }
            switch (current)
            {
                case DMOutGameScreen.Settings:
                    CloseSettings();
                    break;
                case DMOutGameScreen.CreateRoom:
                case DMOutGameScreen.JoinRoom:
                case DMOutGameScreen.Credits:
                    Show(DMOutGameScreen.Menu);
                    break;
                case DMOutGameScreen.Pause:
                    Resume();
                    break;
                case DMOutGameScreen.None:
                    if (DMSession.IsListening && !LoadingVisible)
                    {
                        OpenPause();
                    }
                    break;
            }
        }
    }
}
