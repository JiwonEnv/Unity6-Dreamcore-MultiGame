using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace DreamMovers
{
    /// <summary>
    /// First-person doll, one per player. Small capsule (the dolls are about 2.5 heads tall), a placeholder body that
    /// only other players see, and the thread grab (<see cref="DMGrab"/>). Hold Ctrl to crouch (60 cm, slow, silent),
    /// Shift to sprint (loud), E to climb into a wardrobe. Input comes from the shared, rebindable DMControls.
    /// Movement is owner-driven (the owner's NetworkTransform pushes position and yaw); the server reads the synced
    /// state for noise, sight and catching. Ported from the UE5 ADMCharacter. Pivot at the feet.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class DMDoll : NetworkBehaviour
    {
        public const float Radius = 0.28f;
        public const float StandingHeight = 1.1f;
        /// <summary>Low enough to hide under a 70 cm tabletop.</summary>
        public const float CrouchedHeight = 0.6f;
        const float StandingEye = 0.9f;
        const float CrouchedEye = 0.5f;

        static readonly Color[] PlayerColors =
        {
            new Color(0.85f, 0.2f, 0.2f),  // mouse: red
            new Color(0.45f, 0.3f, 0.2f),  // bear: brown
            new Color(0.35f, 0.75f, 0.3f), // frog: green
            new Color(0.9f, 0.6f, 0.8f),   // rabbit: pink
        };

        /// <summary>Every spawned doll on this machine.</summary>
        public static readonly List<DMDoll> All = new List<DMDoll>();

        /// <summary>This machine's own doll, if it has one.</summary>
        public static DMDoll Local { get; private set; }

        [Header("Movement (m, m/s)")]
        [SerializeField] float walkSpeed = 3.3f;
        [SerializeField] float sprintSpeed = 5.2f;
        [Tooltip("Crouched dolls make no footstep noise at this speed.")]
        [SerializeField] float crouchSpeed = 1.5f;
        [SerializeField] float jumpSpeed = 3.8f;
        [SerializeField, Range(0f, 1f)] float airControl = 0.35f;
        [Tooltip("Degrees per pixel of mouse movement.")]
        [SerializeField] float lookSensitivity = 0.1f;

        [Header("Noise (m)")]
        [Tooltip("Sprinting footsteps are heard this far; walking is silent.")]
        [SerializeField] float sprintNoiseRadius = 8f;
        [SerializeField] float landingNoiseRadius = 5f;

        // Owner-written view state the server aims the grab from.
        readonly NetworkVariable<float> pitch = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> crouched = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> grounded = new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<NetworkObjectReference> hidingSpot = new NetworkVariable<NetworkObjectReference>();

        CharacterController controller;
        NetworkTransform networkTransform;
        DMGrab grab;
        Transform body;
        MeshRenderer bodyRenderer;
        MeshRenderer headRenderer;
        Vector3 velocity;
        bool downed;

        // Owner input.
        /// <summary>This doll reads the shared controls (<see cref="DMControls"/>), so key changes apply at once.</summary>
        bool readsControls;
        /// <summary>Input reached the doll last frame; when a menu or alt-tab cuts it off, held buttons are let go once.</summary>
        bool inputWasLive;

        // Server bookkeeping.
        Vector3 lastServerPosition;
        float serverSpeed;
        float footstepTimer;

        /// <summary>Tests drive the doll through this instead of the devices when it is set (owner only).</summary>
        public DMDollInput? ScriptedInput { get; set; }

        public DMGrab Grab => grab;
        public float Pitch => pitch.Value;
        public float Yaw => transform.eulerAngles.y;
        public bool IsCrouched => crouched.Value;
        public bool IsDowned => downed;
        public bool IsHiding => HidingSpot != null;
        public float Height => crouched.Value ? CrouchedHeight : StandingHeight;
        /// <summary>Horizontal speed the server measured from the synced position (m/s).</summary>
        public float ServerSpeed => serverSpeed;

        public DMHidingSpot HidingSpot => hidingSpot.Value.TryGet(out NetworkObject spot) ? spot.GetComponent<DMHidingSpot>() : null;

        /// <summary>The eyes: the server aims the grab and monsters look from or at here.</summary>
        public Vector3 EyePosition => transform.position + Vector3.up * (crouched.Value ? CrouchedEye : StandingEye);

        public Vector3 Center => transform.position + Vector3.up * (Height * 0.5f);

        public Quaternion ViewRotation => Quaternion.Euler(pitch.Value, transform.eulerAngles.y, 0f);

        public static DMDoll ForClient(ulong clientId)
        {
            foreach (DMDoll doll in All)
            {
                if (doll.OwnerClientId == clientId && !doll.downed)
                {
                    return doll;
                }
            }
            return null;
        }

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            networkTransform = GetComponent<NetworkTransform>();
            grab = GetComponent<DMGrab>();
            controller.radius = Radius;
            controller.stepOffset = 0.3f;
            controller.skinWidth = 0.02f;
            // A slow crouch at a high frame rate moves less than the default 1 mm per frame, which the controller drops.
            controller.minMoveDistance = 0f;
            FitController(false);

            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            bodyRenderer = DMShapes.AddPart(body, "Torso", DMShape.Cylinder, Vector3.zero, Vector3.one, Color.white);
            headRenderer = DMShapes.AddPart(body, "Head", DMShape.Sphere, Vector3.zero, Vector3.one, Color.white);
            FitBody(false);
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            crouched.OnValueChanged += OnCrouchedChanged;
            hidingSpot.OnValueChanged += OnHidingChanged;
            grounded.OnValueChanged += OnGroundedChanged;
            Color color = PlayerColors[(int)(OwnerClientId % (ulong)PlayerColors.Length)];
            DMShapes.Tint(bodyRenderer, color);
            DMShapes.Tint(headRenderer, color);
            if (IsOwner)
            {
                Local = this;
                // Only other players see the body; its shadow stays.
                bodyRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                headRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                BuildInput();
            }
            lastServerPosition = transform.position;
            ApplyHiding();
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            crouched.OnValueChanged -= OnCrouchedChanged;
            hidingSpot.OnValueChanged -= OnHidingChanged;
            grounded.OnValueChanged -= OnGroundedChanged;
            if (Local == this)
            {
                Local = null;
            }
            if (readsControls)
            {
                DMControls.Map.Disable();
                readsControls = false;
            }
            if (IsServer && HidingSpot != null)
            {
                HidingSpot.Release(this);
            }
        }

        void BuildInput()
        {
            DMControls.Map.Enable();
            readsControls = true;
        }

        void FitController(bool isCrouched)
        {
            float height = isCrouched ? CrouchedHeight : StandingHeight;
            controller.height = height;
            controller.center = new Vector3(0f, height * 0.5f, 0f);
        }

        /// <summary>Fits the placeholder body to the standing or crouched capsule (UE offsets, from the feet).</summary>
        void FitBody(bool isCrouched)
        {
            Transform torso = bodyRenderer.transform;
            Transform head = headRenderer.transform;
            torso.localPosition = new Vector3(0f, isCrouched ? 0.2f : 0.4f, 0f);
            torso.localScale = DMShapes.ScaleFor(DMShape.Cylinder, new Vector3(0.5f, isCrouched ? 0.4f : 0.8f, 0.5f));
            head.localPosition = new Vector3(0f, isCrouched ? 0.45f : 1f, 0f);
            head.localScale = Vector3.one * (isCrouched ? 0.4f : 0.45f);
        }

        void OnCrouchedChanged(bool previous, bool current)
        {
            FitBody(current);
            if (!IsOwner)
            {
                FitController(current);
            }
        }

        void OnGroundedChanged(bool previous, bool current)
        {
            if (IsServer && current && !previous && !IsHiding)
            {
                DMNoise.Report(transform.position, landingNoiseRadius, gameObject);
            }
        }

        void OnHidingChanged(NetworkObjectReference previous, NetworkObjectReference current) => ApplyHiding();

        /// <summary>Hiding on whichever machine runs it: no collision, no movement, body hidden.</summary>
        void ApplyHiding()
        {
            bool hiding = IsHiding;
            controller.enabled = !hiding;
            body.gameObject.SetActive(!hiding);
            velocity = Vector3.zero;
        }

        // ---- Owner ----

        void Update()
        {
            if (!IsSpawned)
            {
                return;
            }
            if (IsOwner)
            {
                OwnerUpdate();
            }
            if (IsServer)
            {
                ServerUpdate();
            }
        }

        DMDollInput ReadInput()
        {
            if (ScriptedInput.HasValue)
            {
                return ScriptedInput.Value;
            }
            if (!readsControls || !DMControls.Map.enabled || !Application.isFocused || Cursor.lockState != CursorLockMode.Locked)
            {
                // The release of a held grab never arrives once input stops (pause menu, alt-tab): let go here.
                bool cutOff = inputWasLive;
                inputWasLive = false;
                return cutOff ? new DMDollInput { GrabReleased = true } : default;
            }
            inputWasLive = true;
            return new DMDollInput
            {
                Move = DMControls.Move.ReadValue<Vector2>(),
                Look = LookDelta(),
                Jump = DMControls.Jump.WasPressedThisFrame(),
                Sprint = DMControls.Sprint.IsPressed(),
                Crouch = DMControls.Crouch.IsPressed(),
                GrabPressed = DMControls.Grab.WasPressedThisFrame(),
                GrabReleased = DMControls.Grab.WasReleasedThisFrame(),
                HoldDistance = DMControls.HoldDistance.ReadValue<float>(),
                Interact = DMControls.Interact.WasPressedThisFrame()
            };
        }

        /// <summary>Mouse look scaled by the player's sensitivity, Y flipped if they asked.</summary>
        Vector2 LookDelta()
        {
            DMSettings settings = DMSettings.Current;
            Vector2 look = DMControls.Look.ReadValue<Vector2>() * (lookSensitivity * settings.mouseSensitivity);
            if (settings.invertY)
            {
                look.y = -look.y;
            }
            return look;
        }

        void OwnerUpdate()
        {
            if (Cursor.lockState != CursorLockMode.Locked && Application.isFocused && Mouse.current != null
                && Mouse.current.leftButton.wasPressedThisFrame && !DMOutGameUI.BlocksGameInput)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }
            // Esc opens the pause menu (which frees the mouse); without one (tests, old scenes) it just frees the mouse.
            if (DMOutGameUI.Instance == null && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            DMDollInput input = ReadInput();
            // Look works even while hiding (peeking through the gap).
            transform.Rotate(0f, input.Look.x, 0f, Space.World);
            pitch.Value = Mathf.Clamp(pitch.Value - input.Look.y, -85f, 85f);

            if (input.GrabPressed && !IsHiding)
            {
                grab.TryGrab();
            }
            if (input.GrabReleased)
            {
                grab.Release();
            }
            if (Mathf.Abs(input.HoldDistance) > 0.01f)
            {
                grab.AdjustHoldDistance(input.HoldDistance);
            }
            if (input.Interact)
            {
                InteractRpc();
            }

            // No floor yet (the client is still building the map) or inside a wardrobe: stay put.
            if (IsHiding || !DMLevelSync.Ready || !controller.enabled)
            {
                return;
            }

            // Crouch: hold to stay low; stays crouched on its own while something low is overhead.
            bool wantCrouch = input.Crouch;
            if (crouched.Value && !wantCrouch && !HasHeadroom())
            {
                wantCrouch = true;
            }
            if (wantCrouch != crouched.Value)
            {
                crouched.Value = wantCrouch;
                FitController(wantCrouch);
            }

            float speed = crouched.Value ? crouchSpeed : input.Sprint ? sprintSpeed : walkSpeed;
            Vector3 wish = transform.TransformDirection(new Vector3(input.Move.x, 0f, input.Move.y));
            wish = Vector3.ClampMagnitude(wish, 1f) * speed;
            bool onGround = controller.isGrounded;
            if (onGround)
            {
                velocity.x = wish.x;
                velocity.z = wish.z;
                velocity.y = -1f;
                if (input.Jump && !crouched.Value)
                {
                    velocity.y = jumpSpeed;
                }
            }
            else
            {
                // A little steering in the air.
                Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
                flat = Vector3.MoveTowards(flat, wish, speed * airControl * 4f * Time.deltaTime);
                velocity.x = flat.x;
                velocity.z = flat.z;
                velocity.y += Physics.gravity.y * Time.deltaTime;
            }
            controller.Move(velocity * Time.deltaTime);
            if (grounded.Value != controller.isGrounded)
            {
                grounded.Value = controller.isGrounded;
            }
        }

        bool HasHeadroom()
        {
            Vector3 bottom = transform.position + Vector3.up * (Radius + 0.05f);
            Vector3 top = transform.position + Vector3.up * (StandingHeight - Radius);
            foreach (Collider hit in Physics.OverlapCapsule(bottom, top, Radius * 0.95f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(transform) && hit.attachedRigidbody == null)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Owner: move without interpolation (wardrobes, tests).</summary>
        public void OwnerTeleport(Vector3 position, float yaw)
        {
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            networkTransform.Teleport(position, Quaternion.Euler(0f, yaw, 0f), transform.localScale);
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            Physics.SyncTransforms();
            controller.enabled = wasEnabled && !IsHiding;
            velocity = Vector3.zero;
        }

        /// <summary>Owner: set the view pitch directly (tests).</summary>
        public void OwnerSetPitch(float degrees) => pitch.Value = Mathf.Clamp(degrees, -85f, 85f);

        /// <summary>Owner: crouch or stand right away (tests).</summary>
        public void OwnerSetCrouched(bool value)
        {
            crouched.Value = value;
            FitController(value);
        }

        /// <summary>Server: move a doll whose owner may be remote (the owner moves it, the result syncs back).</summary>
        public void ServerTeleport(Vector3 position, float yaw) => TeleportOwnerRpc(position, yaw, false);

        [Rpc(SendTo.Owner)]
        void TeleportOwnerRpc(Vector3 position, float yaw, bool stand)
        {
            if (stand && crouched.Value)
            {
                crouched.Value = false;
                FitController(false);
            }
            OwnerTeleport(position, yaw);
        }

        [Rpc(SendTo.Owner)]
        void MessageOwnerRpc(string text)
        {
            DMHud.ShowMessage(text);
        }

        // ---- Server ----

        void ServerUpdate()
        {
            Vector3 position = transform.position;
            Vector3 delta = position - lastServerPosition;
            lastServerPosition = position;
            delta.y = 0f;
            float measured = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
            // Smoothed: owner updates arrive in steps, not every frame.
            serverSpeed = Mathf.Lerp(serverSpeed, measured, 1f - Mathf.Exp(-Time.deltaTime * 8f));
            bool sprinting = !crouched.Value && grounded.Value && !IsHiding && serverSpeed > walkSpeed + 0.3f;
            footstepTimer -= Time.deltaTime;
            if (sprinting && footstepTimer <= 0f)
            {
                footstepTimer = 0.35f;
                DMNoise.Report(position, sprintNoiseRadius, gameObject);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void InteractRpc()
        {
            ServerInteract();
        }

        /// <summary>Server: E pressed. Out of the wardrobe if inside, else into the closest reachable one (empty hands only).</summary>
        public void ServerInteract()
        {
            if (downed)
            {
                return;
            }
            DMHidingSpot spot = HidingSpot;
            if (spot != null)
            {
                if (!spot.Leave(this))
                {
                    MessageOwnerRpc("서랍장 문이 무언가에 막혀 있다");
                }
                return;
            }
            // Climbing in with an antique on the thread is not allowed: let go first.
            if (grab.HeldValuable != null)
            {
                return;
            }
            DMHidingSpot reachable = DMHidingSpot.FindReachable(this);
            if (reachable != null)
            {
                reachable.TryEnter(this);
            }
        }

        /// <summary>Server, called by the wardrobe: the doll is inside, out of sight and out of reach.</summary>
        public void EnterHiding(DMHidingSpot spot, Vector3 location, float facingYaw)
        {
            if (!IsServer || spot == null)
            {
                return;
            }
            grab.ServerDropHeld();
            hidingSpot.Value = spot.NetworkObject;
            TeleportOwnerRpc(location, facingYaw, true);
        }

        public void LeaveHiding(Vector3 exitLocation)
        {
            if (!IsServer)
            {
                return;
            }
            // The wardrobe already checked that this spot is free and has floor under it.
            hidingSpot.Value = default;
            TeleportOwnerRpc(exitLocation, transform.eulerAngles.y, false);
        }

        /// <summary>Server: caught by a monster. The doll bursts into a cotton ball teammates can carry to the extraction zone.</summary>
        public void Down()
        {
            if (!IsServer || downed)
            {
                return;
            }
            downed = true;
            Debug.Log($"[DreamMovers] {name} (client {OwnerClientId}) was caught");
            DMHidingSpot spot = HidingSpot;
            if (spot != null)
            {
                spot.Release(this);
            }
            grab.ServerDropHeld();
            if (DMGameMode.Instance != null)
            {
                DMGameMode.Instance.SpawnCottonBall(OwnerClientId, Center, transform.eulerAngles.y);
            }
            NetworkObject.Despawn(true);
        }

        /// <summary>
        /// Points a monster can look at or grab: the eyes, plus every point around the body (at its middle) that has
        /// nothing static within 60 cm above it. A doll fully under a desk gives only its eyes; one sticking out past
        /// the edge also gives the part that sticks out.
        /// </summary>
        public void GetExposedPoints(List<Vector3> points)
        {
            points.Clear();
            points.Add(EyePosition);
            Vector3 center = Center;
            // Three quarters of the radius: a doll whose side pokes out a few centimeters still counts as under cover.
            float radius = Radius * 0.75f;
            for (int step = 0; step < 8; ++step)
            {
                Vector3 point = center + Quaternion.Euler(0f, step * 45f, 0f) * Vector3.forward * radius;
                if (!DMPhysics.StaticLinecast(point, point + Vector3.up * 0.6f, transform))
                {
                    points.Add(point);
                }
            }
        }
    }
}
