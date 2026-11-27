using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProcLevelGen;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DreamMovers.Tests
{
    /// <summary>
    /// Play mode port of the UE5 -dmproctest run on the generated map (seed 535117761, the UE5 test seed): host a game
    /// in the ProcGen scene and drive the host's own doll through readiness, content, the wardrobe, catching, crouching,
    /// grabbing, delivery, stability, collapse, the solo catch and the restart. Each check logs "[DMTest] PASS/FAIL".
    /// </summary>
    public class DMGameplayTests
    {
        const int Seed = 535117761;
        const string ScenePath = "Assets/_DreamMovers/Scenes/ProcGen.unity";
        // URP 17.3 logs this from its own probe volume shader in some editor sessions; not ours (see PLGSelfTest).
        const string KnownUrpError = "Host type is not matching any asset type";

        readonly List<string> failures = new List<string>();
        readonly List<string> errors = new List<string>();
        int noisesFromDoll;
        int noisesFromWardrobe;

        static NetworkManager Net => NetworkManager.Singleton;
        static DMGameState State => DMGameState.Instance;
        static DMGameMode Mode => DMGameMode.Instance;
        static PLGGenerator Generator => Mode.Generator;
        static DMDoll Doll => DMDoll.Local;
        static DMButler Butler => DMMonster.All.OfType<DMButler>().FirstOrDefault();
        static DMBellHorse Horse => DMMonster.All.OfType<DMBellHorse>().FirstOrDefault();

        void Check(bool passed, string name, string detail = "")
        {
            Debug.Log($"[DMTest] {(passed ? "PASS" : "FAIL")} {name}{(string.IsNullOrEmpty(detail) ? "" : ": " + detail)}");
            if (!passed)
            {
                failures.Add($"{name}: {detail}");
            }
        }

        void OnLog(string message, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !message.Contains(KnownUrpError))
            {
                errors.Add(message);
            }
        }

        void OnNoise(Vector3 location, float radius, GameObject source)
        {
            if (Doll != null && source == Doll.gameObject)
            {
                ++noisesFromDoll;
            }
            if (source != null && source.GetComponent<DMHidingSpot>() != null)
            {
                ++noisesFromWardrobe;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Application.logMessageReceived -= OnLog;
            DMNoise.Heard -= OnNoise;
            if (Net != null)
            {
                Net.Shutdown();
                UnityEngine.Object.Destroy(Net.gameObject);
            }
            yield return null;
            // Unload the map (and its navmesh), so later tests in the same run start from an empty world.
            Scene empty = SceneManager.CreateScene("DMTest Empty");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; --i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded)
                {
                    yield return SceneManager.UnloadSceneAsync(scene);
                }
            }
        }

        /// <summary>Plan point of the start module (cm, X toward its door) to world.</summary>
        static Vector3 Plan(float x, float y, float z = 0f) => Generator.transform.TransformPoint(PLGSpace.ToUnity(new Vector3(x, y, z)));

        /// <summary>Plan +X (the start module's door) is world yaw 0.</summary>
        static float PlanYaw(float degrees) => Generator.transform.eulerAngles.y + degrees;

        static IEnumerator Wait(float seconds)
        {
            yield return new WaitForSeconds(seconds);
        }

        static void PlaceItem(DMValuable item, Vector3 position)
        {
            item.Body.linearVelocity = Vector3.zero;
            item.Body.angularVelocity = Vector3.zero;
            item.transform.SetPositionAndRotation(position, Quaternion.identity);
            item.Body.position = position;
            item.Body.rotation = Quaternion.identity;
            Physics.SyncTransforms();
        }

        static void Aim(DMDoll doll, Vector3 point)
        {
            Vector3 to = point - doll.EyePosition;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            doll.OwnerTeleport(doll.transform.position, yaw);
            doll.OwnerSetPitch(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
        }

        static void SetMonstersPaused(bool paused)
        {
            foreach (DMMonster monster in DMMonster.All)
            {
                monster.ThinkPaused = paused;
            }
            if (paused && Horse != null)
            {
                Horse.ServerPark();
                Horse.ThinkPaused = true;
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator HostPlaysOneRoundOnTheTestSeed()
        {
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += OnLog;
            DMNoise.Heard += OnNoise;

            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            // As -plgseed would: the UE5 test seed, every run.
            FieldInfo seedField = typeof(PLGGenerator).GetField("seed", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo eachRun = typeof(PLGGenerator).GetField("newSeedEachRun", BindingFlags.Instance | BindingFlags.NonPublic);
            seedField.SetValue(Generator, Seed);
            eachRun.SetValue(Generator, false);

            Assert.IsTrue(Net.StartHost(), "host started");

            // ---- Readiness ----
            bool sawPreparing = false;
            bool monstersBeforeReady = false;
            float waited = 0f;
            while ((State == null || State.RoundState != DMRoundState.Playing) && waited < 40f)
            {
                if (State != null && State.RoundState == DMRoundState.Preparing)
                {
                    sawPreparing = true;
                    monstersBeforeReady |= DMMonster.All.Count > 0;
                }
                if (State != null && State.RoundState == DMRoundState.Aborted)
                {
                    break;
                }
                yield return null;
                waited += Time.deltaTime;
            }
            Check(State != null && State.RoundState == DMRoundState.Playing, "round_starts_when_ready", $"state {State?.RoundState} after {waited:0.0} s {State?.AbortReason}");
            if (State == null || State.RoundState != DMRoundState.Playing)
            {
                Assert.Fail(string.Join("\n", failures));
            }
            Check(sawPreparing && !monstersBeforeReady, "no_monsters_while_preparing");
            Check(Generator.Seed == Seed && Generator.LayoutHash == 0xe94bc661u, "same_layout_as_ue5", $"seed {Generator.Seed} hash {Generator.LayoutHash:x8}");

            // ---- Content ----
            Generator.RunGenerationChecks((passed, what) => Check(passed, "generation", what));
            int antiques = DMValuable.All.Count(v => !(v is DMCottonBall));
            Check(antiques == Mode.ExpectedAntiques && antiques == 15, "antique_count", $"{antiques} in the map, {Mode.ExpectedAntiques} expected");
            Check(Mode.BlockedAntiqueSpawns == 0, "antique_spots_free_at_spawn", $"{Mode.BlockedAntiqueSpawns} blocked");
            int onFloor = DMValuable.All.Count(v => Physics.Raycast(v.transform.position, Vector3.down, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && v.transform.position.y > -0.5f);
            Check(onFloor == antiques, "antiques_on_floor", $"{onFloor} of {antiques}");
            int hideSlots = Generator.GetSpawnSlots("HideSmall").Count + Generator.GetSpawnSlots("HideLarge").Count;
            Check(DMHidingSpot.All.Count == hideSlots && hideSlots > 0, "wardrobes", $"{DMHidingSpot.All.Count} wardrobes for {hideSlots} hiding spots");
            Check(Butler != null && Horse != null, "monsters_spawned");
            int wantTarget = Mathf.RoundToInt(Mode.MapValue * 0.3f);
            Check(State.TargetValue == wantTarget && wantTarget > 0, "solo_target_30_percent", $"target {State.TargetValue}, map {Mode.MapValue}");
            Check(Doll != null && DMLevelSync.Ready, "host_has_a_doll");

            yield return Wait(0.5f);
            DMDoll doll = Doll;
            SetMonstersPaused(true);

            // ---- Wardrobe ----
            DMHidingSpot wardrobe = DMHidingSpot.All.OrderBy(s => (s.transform.position - doll.transform.position).sqrMagnitude).First();
            Vector3 front = wardrobe.transform.TransformPoint(new Vector3(0f, 0.05f, wardrobe.Size.z * 0.5f + 0.5f));
            doll.OwnerTeleport(front, wardrobe.transform.eulerAngles.y + 180f);
            yield return Wait(0.2f);
            noisesFromWardrobe = 0;
            doll.ServerInteract();
            yield return Wait(0.3f);
            Check(doll.IsHiding && doll.HidingSpot == wardrobe && wardrobe.NumOccupants == 1, "wardrobe_enter", $"hiding {doll.IsHiding}, occupants {wardrobe.NumOccupants}");
            Check(noisesFromWardrobe > 0, "wardrobe_enter_makes_noise");
            Vector3 inFront = wardrobe.transform.TransformPoint(new Vector3(0f, 0f, wardrobe.Size.z * 0.5f + 1.5f));
            Butler.ServerPlace(inFront, wardrobe.transform.eulerAngles.y + 180f);
            Check(!Butler.CanSee(doll) && !Butler.CanCatch(doll), "hidden_doll_unseen_and_uncatchable");
            DMValuable anyItem = DMValuable.All.First();
            Check(!doll.Grab.ServerGrab(anyItem, anyItem.transform.position), "server_rejects_grab_while_hidden");
            doll.ServerInteract();
            yield return Wait(0.3f);
            Check(!doll.IsHiding && wardrobe.NumOccupants == 0, "wardrobe_leave", $"hiding {doll.IsHiding}, at {doll.transform.position}");

            // ---- Catching geometry (start module: 10 x 10 m, walls at plan x/y = +-500) ----
            doll.OwnerTeleport(Plan(-430f, 0f, 5f), PlanYaw(180f));
            Butler.ServerPlace(Plan(-560f, 0f, 0f), PlanYaw(0f));
            Check(!Butler.CanCatch(doll), "no_catch_through_wall", "doll inside, butler just outside the wall");
            Butler.ServerPlace(Plan(-350f, 0f, 0f), PlanYaw(180f));
            Check(Butler.CanCatch(doll), "catch_in_the_open", "0.8 m apart, nothing between");
            Butler.ServerPlace(Plan(300f, 300f, 0f), PlanYaw(0f));
            yield return Wait(0.2f);

            // ---- Crouch is quiet, sprint is loud ----
            doll.OwnerTeleport(Plan(-400f, -300f, 5f), PlanYaw(0f));
            yield return Wait(0.3f);
            noisesFromDoll = 0;
            Vector3 crouchFrom = doll.transform.position;
            doll.ScriptedInput = new DMDollInput { Move = Vector2.up, Crouch = true };
            yield return Wait(1.5f);
            CharacterController controller = doll.GetComponent<CharacterController>();
            Check(doll.IsCrouched && noisesFromDoll == 0 && doll.ServerSpeed > 0.5f && doll.ServerSpeed < 1.7f, "crouch_is_slow_and_silent",
                $"crouched {doll.IsCrouched}, speed {doll.ServerSpeed:0.00} m/s, noises {noisesFromDoll}, from {crouchFrom} to {doll.transform.position}, " +
                $"controller {controller.enabled} grounded {controller.isGrounded} height {controller.height}");
            doll.ScriptedInput = new DMDollInput();
            yield return Wait(0.3f);
            doll.OwnerTeleport(Plan(-400f, -300f, 5f), PlanYaw(0f));
            yield return Wait(0.2f);
            noisesFromDoll = 0;
            doll.ScriptedInput = new DMDollInput { Move = Vector2.up, Sprint = true };
            yield return Wait(1.2f);
            Check(noisesFromDoll > 0, "sprint_is_loud", $"footstep noises {noisesFromDoll} at {doll.ServerSpeed:0.00} m/s");
            doll.ScriptedInput = new DMDollInput();
            yield return Wait(0.3f);
            SetMonstersPaused(true);

            // ---- Grab and lift a light antique ----
            DMValuable lightItem = DMValuable.All.Where(v => !(v is DMCottonBall) && v.MassKg <= 1f).OrderByDescending(v => v.CurrentValue).First();
            doll.OwnerTeleport(Plan(0f, 200f, 5f), PlanYaw(0f));
            PlaceItem(lightItem, Plan(200f, 200f, lightItem.Size.y * 50f + 2f));
            yield return Wait(1.2f);
            Aim(doll, lightItem.transform.position);
            yield return null;
            doll.ScriptedInput = new DMDollInput { GrabPressed = true };
            yield return null;
            doll.ScriptedInput = new DMDollInput();
            yield return Wait(0.3f);
            Check(doll.Grab.HeldValuable == lightItem, "grab_light", $"{lightItem.DisplayName}, aiming at {doll.Grab.TraceForValuable()?.DisplayName ?? "nothing"}");
            float startY = lightItem.transform.position.y;
            doll.OwnerSetPitch(0f);
            yield return Wait(1f);
            Check(lightItem.transform.position.y - startY > 0.2f, "lift_light", $"rose {lightItem.transform.position.y - startY:0.00} m");

            // ---- Carry to the extraction zone and let go ----
            Vector3 zone = DMExtractionZone.Instance.transform.position;
            // Walking there is not the point here: doll and item jump next to the zone together, the thread stays taut.
            doll.OwnerTeleport(Plan(-80f, 0f, 5f), PlanYaw(180f));
            doll.OwnerSetPitch(15f);
            doll.Grab.ServerSetHoldDistance(1.7f);
            PlaceItem(lightItem, doll.EyePosition + doll.ViewRotation * Vector3.forward * 1.7f);
            yield return Wait(1.5f);
            bool overZone = DMExtractionZone.Instance.Contains(lightItem.transform.position);
            Check(doll.Grab.HeldValuable == lightItem && overZone, "carry_light_to_zone", $"held {doll.Grab.HeldValuable == lightItem}, item {lightItem.transform.position}, zone {zone}");
            int value = lightItem.CurrentValue;
            int collectedBefore = State.CollectedValue;
            float remainingBefore = State.RemainingSeconds;
            float timeBefore = Time.time;
            doll.ScriptedInput = new DMDollInput { GrabReleased = true };
            yield return null;
            doll.ScriptedInput = new DMDollInput();
            yield return Wait(1f);
            float drain = remainingBefore - State.RemainingSeconds - (Time.time - timeBefore);
            Check(State.CollectedValue - collectedBefore == value && value > 0, "deliver_adds_value", $"+{State.CollectedValue - collectedBefore}, item worth {value}");
            Check(Mathf.Abs(drain - value * 0.02f) < 0.35f, "deliver_drains_20s_per_1000", $"stability -{drain:0.00} s beyond time, expected {value * 0.02f:0.00} s");

            // ---- A heavy antique needs two ----
            DMValuable heavy = DMValuable.All.Where(v => !(v is DMCottonBall) && v.MassKg >= 25f).OrderBy(v => v.MassKg).First();
            doll.OwnerTeleport(Plan(-100f, -250f, 5f), PlanYaw(0f));
            PlaceItem(heavy, Plan(150f, -250f, heavy.Size.y * 50f + 2f));
            yield return Wait(1.5f);
            Aim(doll, heavy.transform.position);
            yield return null;
            float heavyY = heavy.transform.position.y;
            doll.ScriptedInput = new DMDollInput { GrabPressed = true };
            yield return null;
            doll.ScriptedInput = new DMDollInput();
            yield return Wait(0.2f);
            bool heldHeavy = doll.Grab.HeldValuable == heavy;
            doll.OwnerSetPitch(-20f);
            yield return Wait(1.5f);
            float rise = heavy.transform.position.y - heavyY;
            Check(heldHeavy && rise < 0.25f, "solo_cannot_lift_heavy", $"{heavy.DisplayName} {heavy.MassKg} kg, held {heldHeavy}, rose {rise:0.00} m");
            doll.Grab.ServerDropHeld();
            yield return Wait(0.3f);

            // ---- Server grab validation ----
            DMValuable spare = DMValuable.All.Where(v => !(v is DMCottonBall) && v != heavy && v.MassKg <= 1f).OrderBy(v => v.MassKg).First();
            doll.OwnerTeleport(Plan(-400f, 0f, 5f), PlanYaw(180f));
            PlaceItem(spare, Plan(-560f, 0f, 60f));
            Vector3 behindWall = spare.GetComponentInChildren<Collider>().ClosestPoint(doll.EyePosition);
            Check(!doll.Grab.ServerGrab(spare, behindWall), "server_rejects_grab_through_wall", $"item {Vector3.Distance(doll.EyePosition, spare.transform.position):0.00} m away behind the wall");
            PlaceItem(spare, Plan(-300f, 0f, spare.Size.y * 50f + 1f));
            Vector3 onItem = spare.GetComponentInChildren<Collider>().ClosestPoint(doll.EyePosition);
            Check(!doll.Grab.ServerGrab(spare, onItem + Vector3.up * 1.5f), "server_rejects_point_off_the_item");
            Check(doll.Grab.ServerGrab(spare, onItem), "server_accepts_valid_grab", $"item {Vector3.Distance(doll.EyePosition, onItem):0.00} m away in plain view");
            doll.Grab.ServerDropHeld();

            // ---- Collapse follows stability ----
            Check(State.Stability > 0.5f && DMHud.Instance.Collapse < 0.02f, "full_color_above_half_stability", $"stability {State.Stability:0.00}, collapse {DMHud.Instance.Collapse:0.00}");
            State.DebugDrainStability(State.RemainingSeconds - State.RoundDuration * 0.25f);
            yield return null;
            DMHud.Instance.SnapCollapse();
            float wantCollapse = Mathf.Lerp(0f, 0.75f, Mathf.InverseLerp(0.5f, 0f, State.Stability));
            Check(Mathf.Abs(DMHud.Instance.Collapse - wantCollapse) < 0.05f, "collapse_follows_stability", $"stability {State.Stability:0.00}, collapse {DMHud.Instance.Collapse:0.00}, want {wantCollapse:0.00}");

            // ---- The butler catches a doll in the open; solo, that ends the round ----
            doll.OwnerTeleport(Plan(0f, 0f, 5f), PlanYaw(0f));
            Butler.ServerPlace(Plan(80f, 0f, 0f), PlanYaw(180f));
            Butler.ThinkPaused = false;
            waited = 0f;
            while (DMDoll.Local != null && waited < 5f)
            {
                yield return null;
                waited += Time.deltaTime;
            }
            Check(DMDoll.Local == null && DMCottonBall.ForClient(Net.LocalClientId) != null, "butler_catches_unshielded_doll", $"after {waited:0.0} s");
            waited = 0f;
            while (State.RoundState == DMRoundState.Playing && waited < 3f)
            {
                yield return null;
                waited += Time.deltaTime;
            }
            Check(State.RoundState == DMRoundState.Failed, "solo_down_fails_at_once", $"state {State.RoundState} {waited:0.0} s after the catch");

            // ---- Restart: a new round on a fresh map, the doll back at the start ----
            waited = 0f;
            bool sawRestart = false;
            while (waited < 40f)
            {
                sawRestart |= State.RoundState == DMRoundState.Preparing;
                if (sawRestart && State.RoundState == DMRoundState.Playing)
                {
                    break;
                }
                yield return null;
                waited += Time.deltaTime;
            }
            Check(sawRestart && State.RoundState == DMRoundState.Playing && DMDoll.Local != null && DMCottonBall.Balls.Count == 0, "restart_after_failure",
                $"state {State.RoundState}, doll {DMDoll.Local != null}, balls {DMCottonBall.Balls.Count}");
            Check(State.CollectedValue == 0 && DMValuable.All.Count(v => !(v is DMCottonBall)) == Mode.ExpectedAntiques, "restart_resets_content",
                $"collected {State.CollectedValue}, antiques {DMValuable.All.Count}");

            Check(errors.Count == 0, "no_errors_logged", string.Join(" | ", errors.Take(5)));
            Assert.IsEmpty(failures, "see the [DMTest] lines in the log");
        }
    }
}
