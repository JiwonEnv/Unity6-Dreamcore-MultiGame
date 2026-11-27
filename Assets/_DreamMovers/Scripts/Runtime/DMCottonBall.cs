using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// What is left of a caught doll. Carried like any item (light, unbreakable, worth nothing); dropping it in the
    /// extraction zone revives its player there instead of banking value. The downed player's camera follows it while
    /// they wait (<see cref="DMViewCamera"/>). Ported from the UE5 ADMCottonBall.
    /// </summary>
    public class DMCottonBall : DMValuable
    {
        public static readonly List<DMCottonBall> Balls = new List<DMCottonBall>();

        readonly NetworkVariable<ulong> downedClient = new NetworkVariable<ulong>(ulong.MaxValue);
        /// <summary>The way the doll was facing when it was caught; the waiting camera starts looking that way.</summary>
        readonly NetworkVariable<float> facingYaw = new NetworkVariable<float>();

        public ulong DownedClient => downedClient.Value;
        public float FacingYaw => facingYaw.Value;

        public override string DisplayName
        {
            get
            {
                if (downedClient.Value == ulong.MaxValue)
                {
                    return "솜뭉치";
                }
                return $"인형 {downedClient.Value + 1}의 솜뭉치";
            }
        }

        protected override void Awake()
        {
            shape = DMShape.Sphere;
            size = Vector3.one * 0.45f;
            massKg = 3f;
            crackSpeed = 1000f; // never cracks
            quietSpeed = 1000f; // and lands softly
            tint = new Color(0.96f, 0.94f, 0.9f);
            roughness = 0.95f;
            base.Awake();
        }

        public static DMCottonBall ForClient(ulong clientId)
        {
            foreach (DMCottonBall ball in Balls)
            {
                if (ball.downedClient.Value == clientId)
                {
                    return ball;
                }
            }
            return null;
        }

        /// <summary>Server, before Spawn.</summary>
        public void SetDownedPlayer(ulong clientId, float yaw)
        {
            downedClient.Value = clientId;
            facingYaw.Value = yaw;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Balls.Add(this);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            Balls.Remove(this);
        }

        public override void Extract()
        {
            if (!IsServer || IsExtracted)
            {
                return;
            }
            if (DMGameMode.Instance != null)
            {
                DMGameMode.Instance.RevivePlayer(downedClient.Value, transform.position);
            }
            // Not banked: a revived friend is not loot.
            MarkExtracted();
            NetworkObject.Despawn(true);
        }
    }
}
