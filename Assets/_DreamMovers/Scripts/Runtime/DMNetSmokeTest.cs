using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// Two-process network check (dev builds and the editor only), started with -dmnettest on both a host (-dmhost) and
    /// a client (-dmclient). The host waits for the client's doll, freezes the monsters, puts both dolls next to a
    /// heavy antique and grabs it; the client's own script aims and grabs too. Two holders must lift what one cannot.
    /// Both sides log "[DMNetTest] PASS/FAIL" lines and their layout hash, then quit (exit code 1 on any failure).
    /// </summary>
    public class DMNetSmokeTest : MonoBehaviour
    {
        int failures;
        DMValuable heavy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfAsked()
        {
            if (DMArgs.Has("dmnettest"))
            {
                // The run starts in the menu scene and moves to the game scene: keep the checker alive across it.
                DontDestroyOnLoad(new GameObject("DM Net Smoke Test").AddComponent<DMNetSmokeTest>().gameObject);
            }
            if (DMArgs.TryGet("dmshot=", out string path))
            {
                var shot = new GameObject("DM Shot").AddComponent<DMNetSmokeTest>();
                DontDestroyOnLoad(shot.gameObject);
                shot.StartCoroutine(TakeShot(path));
            }
        }

        /// <summary>-dmshot=PATH: once the round runs, look at the nearest antique, save a screenshot and quit.</summary>
        static IEnumerator TakeShot(string path)
        {
            yield return WaitFor(() => DMGameState.Instance != null && DMGameState.Instance.RoundState == DMRoundState.Playing && DMDoll.Local != null, 60f);
            DMDoll doll = DMDoll.Local;
            DMValuable item = DMValuable.All.OrderBy(v => (v.transform.position - doll.transform.position).sqrMagnitude).FirstOrDefault();
            if (doll != null && item != null)
            {
                foreach (DMMonster monster in DMMonster.All)
                {
                    monster.ThinkPaused = true;
                }
                Vector3 toward = item.transform.position - doll.transform.position;
                toward.y = 0f;
                doll.OwnerTeleport(item.transform.position - toward.normalized * 1.6f + Vector3.up * 0.05f, Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg);
                doll.OwnerSetPitch(20f);
            }
            yield return new WaitForSeconds(2f);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(1f);
            Application.Quit(0);
        }

        void Check(bool passed, string name, string detail = "")
        {
            failures += passed ? 0 : 1;
            Debug.Log($"[DMNetTest] {(passed ? "PASS" : "FAIL")} {name}{(string.IsNullOrEmpty(detail) ? "" : ": " + detail)}");
        }

        IEnumerator Start()
        {
            if (!DMArgs.Has("dmnettest"))
            {
                yield break;
            }
            yield return new WaitUntil(() => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
            yield return NetworkManager.Singleton.IsServer ? RunHost() : RunClient();
            Debug.Log($"[DMNetTest] {(failures == 0 ? "ALL PASSED" : "FAILED")}, {failures} failure(s)");
            yield return new WaitForSeconds(1f);
            Application.Quit(failures == 0 ? 0 : 1);
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float timeout)
        {
            float until = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
        }

        static DMDoll RemoteDoll => DMDoll.All.FirstOrDefault(d => !d.IsOwner);

        IEnumerator RunHost()
        {
            NetworkManager net = NetworkManager.Singleton;
            yield return WaitFor(() => DMGameState.Instance != null && DMGameState.Instance.RoundState == DMRoundState.Playing
                && RemoteDoll != null && DMDoll.Local != null, 90f);
            DMGameState state = DMGameState.Instance;
            Check(state != null && state.RoundState == DMRoundState.Playing, "host_round_playing");
            Check(RemoteDoll != null, "host_sees_client_doll", $"{net.ConnectedClientsIds.Count} clients connected");
            if (RemoteDoll == null || DMDoll.Local == null)
            {
                yield break;
            }
            Debug.Log($"[DMNetTest] host layout seed {DMGameMode.Instance.Generator.Seed} hash {DMGameMode.Instance.Generator.LayoutHash:x8}");
            Check(state.TargetValue == Mathf.RoundToInt(DMGameMode.Instance.MapValue * 0.4f), "target_40_percent_for_two", $"target {state.TargetValue}, map {DMGameMode.Instance.MapValue}");
            foreach (DMMonster monster in DMMonster.All)
            {
                monster.ThinkPaused = true;
            }

            // The heaviest one-piece job: needs two holders (more than 20 kg).
            heavy = DMValuable.All.Where(v => !(v is DMCottonBall) && v.MassKg > 20f).OrderBy(v => v.MassKg).First();
            Transform origin = DMGameMode.Instance.Generator.transform;
            Vector3 itemAt = origin.TransformPoint(new Vector3(0f, heavy.Size.y * 0.5f + 0.02f, 2f));
            heavy.Body.linearVelocity = Vector3.zero;
            heavy.Body.angularVelocity = Vector3.zero;
            heavy.transform.SetPositionAndRotation(itemAt, origin.rotation);
            heavy.Body.position = itemAt;
            Physics.SyncTransforms();

            // Host doll on the left, client doll on the right, both two meters back and facing it.
            DMDoll.Local.OwnerTeleport(origin.TransformPoint(new Vector3(-0.8f, 0.05f, 0f)), origin.eulerAngles.y);
            RemoteDoll.ServerTeleport(origin.TransformPoint(new Vector3(0.8f, 0.05f, 0f)), origin.eulerAngles.y);
            yield return new WaitForSeconds(2f);
            Vector3 clientAt = RemoteDoll.transform.position;
            Check(Vector3.Distance(clientAt, origin.TransformPoint(new Vector3(0.8f, 0.05f, 0f))) < 0.3f, "client_movement_syncs_to_host", $"client doll at {clientAt}");

            // Host alone first.
            float startY = heavy.transform.position.y;
            Vector3 point = heavy.GetComponentInChildren<Collider>().ClosestPoint(DMDoll.Local.EyePosition);
            Check(DMDoll.Local.Grab.ServerGrab(heavy, point), "host_grabs_heavy");
            DMDoll.Local.OwnerSetPitch(-25f);
            // The client grabs on its own once it sees the item next to it; wait for that.
            yield return WaitFor(() => heavy.NumHolders >= 2, 15f);
            Check(heavy.NumHolders == 2, "two_holders", $"{heavy.NumHolders} holding {heavy.DisplayName} ({heavy.MassKg} kg)");
            yield return new WaitForSeconds(2.5f);
            float rise = heavy.transform.position.y - startY;
            Check(rise > 0.3f, "two_dolls_lift_heavy", $"rose {rise:0.00} m");

            // Let go, tell the client we are done, and give it time to log.
            DMDoll.Local.Grab.ServerDropHeld();
            yield return new WaitForSeconds(3f);
            net.Shutdown();
        }

        IEnumerator RunClient()
        {
            NetworkManager net = NetworkManager.Singleton;
            yield return WaitFor(() => net.IsConnectedClient && DMGameState.Instance != null && DMGameState.Instance.RoundState == DMRoundState.Playing
                && DMDoll.Local != null && DMLevelSync.Ready, 90f);
            Check(net.IsConnectedClient, "client_connected");
            Check(DMDoll.Local != null, "client_has_own_doll");
            var generator = FindAnyObjectByType<ProcLevelGen.PLGGenerator>();
            Debug.Log($"[DMNetTest] client layout seed {generator.Seed} hash {generator.LayoutHash:x8}");
            Check(generator.IsGenerated && generator.Seed == DMGameState.Instance.LevelSeed, "client_built_server_seed", $"seed {generator.Seed}");
            int antiques = DMValuable.All.Count(v => !(v is DMCottonBall));
            Check(antiques == 15, "client_sees_antiques", $"{antiques}");
            Check(DMHidingSpot.All.Count > 0 && DMMonster.All.Count == 2, "client_sees_wardrobes_and_monsters", $"{DMHidingSpot.All.Count} wardrobes, {DMMonster.All.Count} monsters");
            if (DMDoll.Local == null)
            {
                yield break;
            }

            // Wait for the host to bring a heavy antique next to us, then aim, grab, and look up.
            DMDoll doll = DMDoll.Local;
            yield return WaitFor(() => DMValuable.All.Any(v => v.MassKg > 20f && Vector3.Distance(v.transform.position, doll.transform.position) < 3f), 60f);
            heavy = DMValuable.All.FirstOrDefault(v => v.MassKg > 20f && Vector3.Distance(v.transform.position, doll.transform.position) < 3f);
            Check(heavy != null, "client_sees_heavy_brought_over");
            if (heavy == null)
            {
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            Vector3 to = heavy.transform.position - doll.EyePosition;
            doll.OwnerTeleport(doll.transform.position, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
            doll.OwnerSetPitch(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
            yield return new WaitForSeconds(0.3f);
            doll.ScriptedInput = new DMDollInput { GrabPressed = true };
            yield return null;
            doll.ScriptedInput = new DMDollInput();
            yield return WaitFor(() => doll.Grab.HeldValuable == heavy, 5f);
            Check(doll.Grab.HeldValuable == heavy, "client_grab_accepted_by_server");
            doll.OwnerSetPitch(-25f);
            yield return WaitFor(() => heavy.NumHolders >= 2, 10f);
            Check(heavy.NumHolders == 2, "client_sees_two_holders");
            float startY = heavy.transform.position.y;
            yield return new WaitForSeconds(2f);
            Check(heavy.transform.position.y - startY > 0.2f || heavy.transform.position.y > 0.8f, "client_sees_item_lifted", $"item at y {heavy.transform.position.y:0.00}");
            // The host ends the test by shutting down.
            yield return WaitFor(() => !net.IsListening || !net.IsConnectedClient, 30f);
        }
    }
}
