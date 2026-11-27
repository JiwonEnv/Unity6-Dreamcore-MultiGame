using Unity.Netcode;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// Prototype HUD drawn with IMGUI: score, stability timer, crosshair, price tags, monster cues, hiding prompts.
    /// It also drives the dream collapse value (global shader float _DreamCollapse) the oil pastel shader will read:
    /// full color until stability falls to half, then fading toward a bare sketch. Ported from the UE5 ADMHUD.
    /// </summary>
    public class DMHud : MonoBehaviour
    {
        static readonly Color Ink = new Color(0.12f, 0.1f, 0.1f);
        static readonly Color Paper = new Color(1f, 0.97f, 0.9f);
        static readonly Color Warning = new Color(1f, 0.45f, 0.35f);
        static readonly Color Good = new Color(0.55f, 1f, 0.6f);
        static readonly int CollapseId = Shader.PropertyToID("_DreamCollapse");

        static Font koreanFont;
        static string message;
        static float messageUntil;

        [Tooltip("Price tags are drawn on items closer than this (m).")]
        [SerializeField] float tagDistance = 7f;
        [Tooltip("The bell horse's jingle is \"heard\" (shown) within this distance (m) while it moves.")]
        [SerializeField] float jingleDistance = 25f;
        [Tooltip("Dream stability left when the picture starts losing its color.")]
        [SerializeField, Range(0f, 1f)] float collapseStartsAt = 0.5f;
        [Tooltip("How far the picture fades toward a bare sketch when stability reaches zero (1 = sketch only).")]
        [SerializeField, Range(0f, 1f)] float collapseAtWake = 0.75f;

        GUIStyle style;
        float collapse;

        public static DMHud Instance { get; private set; }

        /// <summary>Current value given to the oil pastel shader.</summary>
        public float Collapse => collapse;

        /// <summary>Dynamic OS font with Hangul (the built-in GUI font has none on some machines).</summary>
        public static Font KoreanFont
        {
            get
            {
                if (koreanFont == null)
                {
                    koreanFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 16);
                }
                return koreanFont;
            }
        }

        /// <summary>A short line under the crosshair for a few seconds (server messages to this player).</summary>
        public static void ShowMessage(string text)
        {
            message = text;
            messageUntil = Time.time + 3f;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            Shader.SetGlobalFloat(CollapseId, 0f);
        }

        void Update()
        {
            UpdateDreamCollapse(Time.deltaTime);
        }

        void UpdateDreamCollapse(float deltaTime)
        {
            DMGameState state = DMGameState.Instance;
            float target = 0f;
            if (state != null)
            {
                switch (state.RoundState)
                {
                    case DMRoundState.Playing:
                        if (state.RoundDuration > 0f)
                        {
                            // Full color until stability drops to Collapse Starts At, then fade toward the sketch.
                            target = Mathf.Lerp(0f, collapseAtWake, Mathf.InverseLerp(collapseStartsAt, 0f, state.Stability));
                        }
                        break;
                    case DMRoundState.Failed:
                        target = 1f; // the dream woke up: only the sketch is left
                        break;
                }
            }
            // Same easing as UE FInterpTo at speed 1.5.
            collapse = Mathf.Lerp(collapse, target, Mathf.Clamp01(deltaTime * 1.5f));
            // The player may tone the fade down (accessibility); Collapse itself keeps the designed value.
            Shader.SetGlobalFloat(CollapseId, collapse * DMSettings.Current.collapseIntensity);
        }

        /// <summary>Tests: jump straight to the current target instead of easing.</summary>
        public void SnapCollapse()
        {
            UpdateDreamCollapse(1f);
        }

        void OnGUI()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || Event.current.type != EventType.Repaint || DMOutGameUI.CoversScreen)
            {
                return;
            }
            style ??= new GUIStyle(GUI.skin.label) { font = KoreanFont, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = false, wordWrap = false, clipping = TextClipping.Overflow, padding = new RectOffset() };
            Camera view = DMViewCamera.Instance != null ? DMViewCamera.Instance.Camera : Camera.main;
            if (view == null)
            {
                return;
            }
            if (DMOutGameUI.BlocksGameInput)
            {
                // A menu is open on top: keep only the score and timer (IMGUI would draw over the menu).
                DrawRoundInfo();
                return;
            }
            DrawPriceTags(view);
            DrawMonsterCues(view);
            DrawCrosshairAndTarget();
            DrawDownedMessage();
            DrawHidingPrompt();
            DrawRoundInfo();
            if (Time.time < messageUntil)
            {
                Text(message, new Vector2(Screen.width * 0.5f, Screen.height * 0.7f), Warning, 16);
            }
        }

        void Text(string text, Vector2 center, Color color, int size)
        {
            style.fontSize = size;
            var content = new GUIContent(text);
            Vector2 extent = style.CalcSize(content);
            var rect = new Rect(center.x - extent.x * 0.5f, center.y - extent.y * 0.5f, extent.x, extent.y);
            style.normal.textColor = new Color(Ink.r, Ink.g, Ink.b, color.a);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), content, style);
            style.normal.textColor = color;
            GUI.Label(rect, content, style);
        }

        /// <summary>World point to GUI coordinates; false behind the camera.</summary>
        static bool Project(Camera view, Vector3 world, out Vector2 gui)
        {
            Vector3 screen = view.WorldToScreenPoint(world);
            gui = new Vector2(screen.x, Screen.height - screen.y);
            return screen.z > 0f;
        }

        void DrawRoundInfo()
        {
            DMGameState state = DMGameState.Instance;
            if (state == null)
            {
                return;
            }
            int seconds = Mathf.CeilToInt(state.RemainingSeconds);
            float centerX = Screen.width * 0.5f;
            Text($"모은 금액 {state.CollectedValue:N0} / 목표 {state.TargetValue:N0}", new Vector2(centerX, 40f), state.IsTargetReached ? Good : Paper, 22);
            Text($"꿈 안정도 {Mathf.CeilToInt(state.Stability * 100f)}%  ·  깨기까지 {seconds / 60}:{seconds % 60:00}", new Vector2(centerX, 72f), seconds <= 60 ? Warning : Paper, 18);
            if (state.BrokenValue > 0)
            {
                Text($"깨뜨린 금액 {state.BrokenValue:N0}", new Vector2(centerX, 100f), Warning, 14);
            }

            float y = Screen.height * 0.4f;
            switch (state.RoundState)
            {
                case DMRoundState.Preparing:
                    Text("꿈을 준비하는 중...", new Vector2(centerX, y), Paper, 28);
                    break;
                case DMRoundState.Aborted:
                    Text("꿈을 만들지 못했습니다", new Vector2(centerX, y), Warning, 36);
                    Text("라운드를 시작하지 않습니다. 로그를 확인하세요", new Vector2(centerX, y + 44f), Paper, 16);
                    break;
                case DMRoundState.Success:
                case DMRoundState.Failed:
                    bool won = state.RoundState == DMRoundState.Success;
                    Text(won ? "수거 성공!" : "꿈이 깨버렸다...", new Vector2(centerX, y), won ? Good : Warning, 48);
                    Text("잠시 후 다시 시작합니다", new Vector2(centerX, y + 50f), Paper, 18);
                    break;
            }
        }

        void DrawPriceTags(Camera view)
        {
            Vector3 eye = view.transform.position;
            foreach (DMValuable valuable in DMValuable.All)
            {
                Vector3 tag = valuable.transform.position + Vector3.up * (valuable.Size.magnitude * 0.5f + 0.2f);
                if ((tag - eye).sqrMagnitude > tagDistance * tagDistance || !Project(view, tag, out Vector2 at))
                {
                    continue;
                }
                if (valuable is DMCottonBall)
                {
                    Text(valuable.DisplayName, at, Good, 14);
                    continue;
                }
                Text(valuable.CurrentValue.ToString("N0"), at, valuable.Cracks > 0 ? Warning : Paper, 14);
            }
        }

        void DrawCrosshairAndTarget()
        {
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            DMDoll doll = DMDoll.Local;
            if (doll == null)
            {
                return;
            }
            Text("·", center, Paper, 20);
            DMGrab grab = doll.Grab;
            DMValuable valuable = grab.HeldValuable;
            if (valuable == null)
            {
                valuable = grab.TraceForValuable();
            }
            if (valuable == null)
            {
                return;
            }
            if (valuable is DMCottonBall)
            {
                Text($"{valuable.DisplayName}  ·  탈출 지점에 놓으면 부활", center + new Vector2(0f, 40f), Good, 16);
                return;
            }
            Text($"{valuable.DisplayName}  ·  {valuable.CurrentValue:N0}  ·  금 {valuable.Cracks}/{valuable.MaxCracks}", center + new Vector2(0f, 40f), Paper, 16);
            // Tell players up front that one of them cannot lift it alone.
            int needed = Mathf.CeilToInt(valuable.MassKg / grab.MaxLiftKg);
            if (needed > 1)
            {
                Text($"무거움: {needed}명 필요 (지금 {valuable.NumHolders}명)", center + new Vector2(0f, 64f), Warning, 14);
            }
        }

        void DrawMonsterCues(Camera view)
        {
            Vector3 listener = view.transform.position;
            float jingle = 0f;
            bool charging = false;
            foreach (DMMonster monster in DMMonster.All)
            {
                DMMonsterState state = monster.State;
                if (monster is DMButler butler)
                {
                    // "?" grows while it is noticing someone, "!" once it has.
                    bool chasing = state == DMMonsterState.Chase;
                    float suspicion = butler.Suspicion;
                    if ((!chasing && suspicion < 0.05f) || !Project(view, monster.transform.position + Vector3.up * (monster.Height + 0.5f), out Vector2 at))
                    {
                        continue;
                    }
                    Text(chasing ? "!" : "?", at, chasing ? Warning : Color.Lerp(Paper, Warning, suspicion), chasing ? 44 : Mathf.RoundToInt(22f + 18f * suspicion));
                }
                else if (monster is DMBellHorse && state != DMMonsterState.Idle)
                {
                    float distance = Vector3.Distance(listener, monster.transform.position);
                    jingle = Mathf.Max(jingle, 1f - distance / jingleDistance);
                    charging |= state == DMMonsterState.Charge && distance < jingleDistance;
                }
            }
            if (jingle > 0f)
            {
                Color color = charging ? Warning : Paper;
                color.a = Mathf.Clamp01(0.25f + jingle);
                Text(charging ? "딸랑딸랑딸랑! 종소리가 달려온다" : "딸랑… 딸랑…", new Vector2(Screen.width * 0.5f, Screen.height * 0.82f), color, Mathf.RoundToInt(14f + 10f * jingle));
            }
        }

        void DrawDownedMessage()
        {
            if (DMDoll.Local != null || DMViewCamera.Instance == null || DMViewCamera.Instance.FollowedBall == null)
            {
                return;
            }
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.3f);
            Text("솜이 터져버렸다!", center, Warning, 36);
            Text("동료가 솜뭉치를 탈출 지점에 놓아주면 다시 일어납니다", center + new Vector2(0f, 44f), Paper, 16);
        }

        void DrawHidingPrompt()
        {
            DMDoll doll = DMDoll.Local;
            if (doll == null)
            {
                return;
            }
            Vector2 position = new Vector2(Screen.width * 0.5f, Screen.height * 0.62f);
            if (doll.IsHiding)
            {
                Text("서랍장 안에 숨어 있다  ·  E: 나오기", position, Paper, 18);
                return;
            }
            DMHidingSpot spot = DMHidingSpot.FindReachable(doll);
            if (spot == null)
            {
                return;
            }
            if (doll.Grab.HeldValuable != null)
            {
                Text("물건을 놓아야 숨을 수 있다", position, Warning, 16);
            }
            else if (!spot.HasRoom)
            {
                Text("서랍장이 꽉 찼다", position, Warning, 16);
            }
            else
            {
                Text($"E: 서랍장에 숨기 ({spot.NumOccupants}/{spot.Capacity})", position, Paper, 18);
            }
        }
    }
}
