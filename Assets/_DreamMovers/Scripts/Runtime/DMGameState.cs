using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// Round score and dream stability, synced to every player for the HUD, plus the level seed every machine builds.
    /// Stability is the time left until the dream wakes: it runs down on its own and every banked antique drains more.
    /// Spawned by the server's <see cref="DMGameMode"/>. Ported from the UE5 ADMGameState.
    /// </summary>
    public class DMGameState : NetworkBehaviour
    {
        public static DMGameState Instance { get; private set; }

        [Tooltip("Seconds of stability lost per 1,000 value banked: taking memories out makes the dream fade faster.")]
        [SerializeField, Min(0f)] float drainSecondsPer1000 = 20f;

        readonly NetworkVariable<int> collectedValue = new NetworkVariable<int>();
        /// <summary>Base value of everything that shattered, shown in the round summary.</summary>
        readonly NetworkVariable<int> brokenValue = new NetworkVariable<int>();
        readonly NetworkVariable<int> targetValue = new NetworkVariable<int>();
        /// <summary>Server time when the dream wakes up. Banking pulls it earlier.</summary>
        readonly NetworkVariable<double> roundEndTime = new NetworkVariable<double>();
        readonly NetworkVariable<float> roundDuration = new NetworkVariable<float>();
        readonly NetworkVariable<DMRoundState> roundState = new NetworkVariable<DMRoundState>(DMRoundState.Preparing);
        /// <summary>The seed the server's generator settled on; clients build the same layout from it (0 = none yet).</summary>
        readonly NetworkVariable<int> levelSeed = new NetworkVariable<int>();
        /// <summary>Bumped on every restart, so a client rebuilds even when the seed stays the same (-plgseed).</summary>
        readonly NetworkVariable<int> levelBuild = new NetworkVariable<int>();
        readonly NetworkVariable<FixedString128Bytes> abortReason = new NetworkVariable<FixedString128Bytes>();

        public int CollectedValue => collectedValue.Value;
        public int BrokenValue => brokenValue.Value;
        public int TargetValue => targetValue.Value;
        public DMRoundState RoundState => roundState.Value;
        public bool IsTargetReached => targetValue.Value > 0 && collectedValue.Value >= targetValue.Value;
        public float RoundDuration => roundDuration.Value;
        public int LevelSeed => levelSeed.Value;
        /// <summary>Changes whenever the server builds a new map; clients rebuild when it differs from what they built.</summary>
        public int LevelBuild => levelBuild.Value;
        public string AbortReason => abortReason.Value.ToString();

        public float RemainingSeconds => roundState.Value == DMRoundState.Playing ? Mathf.Max(0f, (float)(roundEndTime.Value - DMNet.ServerTime)) : 0f;

        /// <summary>1 at the start of the round, 0 when the dream wakes.</summary>
        public float Stability => roundDuration.Value > 0f ? RemainingSeconds / roundDuration.Value : 0f;

        public override void OnNetworkSpawn()
        {
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ---- Server ----

        /// <summary>Server: a new map is being prepared (restart): scores reset, timer stopped.</summary>
        public void Prepare(int seed)
        {
            collectedValue.Value = 0;
            brokenValue.Value = 0;
            targetValue.Value = 0;
            roundDuration.Value = 0f;
            abortReason.Value = default;
            roundState.Value = DMRoundState.Preparing;
            levelSeed.Value = seed;
            levelBuild.Value++;
        }

        public void StartRound(int target, float durationSeconds)
        {
            collectedValue.Value = 0;
            brokenValue.Value = 0;
            targetValue.Value = target;
            roundEndTime.Value = DMNet.ServerTime + durationSeconds;
            roundDuration.Value = durationSeconds;
            roundState.Value = DMRoundState.Playing;
        }

        public void SetTargetValue(int target)
        {
            if (roundState.Value == DMRoundState.Playing || roundState.Value == DMRoundState.Preparing)
            {
                targetValue.Value = target;
            }
        }

        /// <summary>Banks value and drains Drain Seconds Per 1000 of stability for every 1,000 of it.</summary>
        public void AddCollected(int value)
        {
            if (roundState.Value == DMRoundState.Playing)
            {
                collectedValue.Value += value;
                roundEndTime.Value -= value * drainSecondsPer1000 / 1000f;
            }
        }

        public void AddBroken(int value)
        {
            if (roundState.Value == DMRoundState.Playing)
            {
                brokenValue.Value += value;
            }
        }

        public void EndRound(DMRoundState result)
        {
            if (roundState.Value == DMRoundState.Playing)
            {
                roundState.Value = result;
            }
        }

        /// <summary>Server: only from Preparing.</summary>
        public void AbortRound(string reason)
        {
            if (roundState.Value == DMRoundState.Preparing)
            {
                abortReason.Value = new FixedString128Bytes(reason.Length > 120 ? reason.Substring(0, 120) : reason);
                roundState.Value = DMRoundState.Aborted;
            }
        }

        /// <summary>Tests only: take stability away without banking anything.</summary>
        public void DebugDrainStability(float seconds) => roundEndTime.Value -= seconds;
    }
}
