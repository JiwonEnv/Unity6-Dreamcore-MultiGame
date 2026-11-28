using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// Bell Horse: blind, hunts by sound. It stands at its post until a noise reaches it (a bumped antique, sprinting,
    /// the butler's bell, wardrobe doors), then charges to where the sound came from, faster than a sprinting doll.
    /// There it searches for a few seconds and walks back. A doll that keeps still is safe even right beside it.
    /// Its own bells jingle while it runs, which is the players' warning (shown on the HUD until there is audio).
    /// Ported from the UE5 ADMBellHorse (meters, m/s).
    /// </summary>
    public class DMBellHorse : DMMonster
    {
        static readonly Color Patina = new Color(0.22f, 0.42f, 0.36f);
        static readonly Color Bronze = new Color(0.45f, 0.3f, 0.16f);
        static readonly Color BellBrass = new Color(0.85f, 0.62f, 0.26f);

        [SerializeField] float chargeSpeed = 6f;
        [SerializeField] float returnSpeed = 1.7f;
        [SerializeField] float searchSeconds = 3f;
        [Tooltip("Noises closer than this (m) to the horse are ignored, so it does not chase its own clatter.")]
        [SerializeField] float ignoreNoiseWithin = 2.5f;

        Vector3 post;
        Vector3 noiseLocation;

        public override string DisplayName => "종 달린 말";
        public override float Radius => 0.5f;
        public override float Height => 1.6f;
        public Vector3 Post => post;

        protected override void BuildBody()
        {
            // Facing +Z. Thin legs, a long body hung with bells, drooping neck, eyeless head.
            AddPart("Body", DMShape.Cube, new Vector3(0f, 0f, 30f), new Vector3(1.7f, 0.55f, 0.55f), Patina, 0.45f, 1f);
            AddPart("Neck", DMShape.Cylinder, new Vector3(95f, 0f, 30f), new Vector3(0.25f, 0.25f, 0.8f), Patina, 0.45f, 1f, new Vector3(-50f, 0f, 0f));
            AddPart("Head", DMShape.Cube, new Vector3(130f, 0f, -5f), new Vector3(0.5f, 0.25f, 0.3f), Bronze, 0.4f, 1f, new Vector3(-35f, 0f, 0f));
            for (int i = 0; i < 4; ++i)
            {
                Vector3 at = new Vector3(i < 2 ? 65f : -65f, i % 2 == 1 ? 20f : -20f, -35f);
                AddPart($"Leg{i}", DMShape.Cylinder, at, new Vector3(0.07f, 0.07f, 0.9f), Bronze, 0.5f, 1f);
            }
            // Bells of different sizes along the belly and back: polished, so they glint.
            float[] bellX = { -70f, -35f, 0f, 35f, 70f, -50f, 20f, 55f };
            for (int i = 0; i < bellX.Length; ++i)
            {
                bool belly = i < 5;
                float bellSize = 0.16f + 0.05f * (i % 3);
                AddPart($"Bell{i}", DMShape.Cone, new Vector3(bellX[i], belly ? 0f : (i % 2 == 1 ? 30f : -30f), belly ? -8f : 50f),
                    Vector3.one * bellSize, BellBrass, 0.2f, 1f);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                post = transform.position;
                DMNoise.Heard += OnNoise;
            }
        }

        public override void OnNetworkDespawn()
        {
            DMNoise.Heard -= OnNoise;
            base.OnNetworkDespawn();
        }

        void OnNoise(Vector3 location, float radius, GameObject source)
        {
            float distance = Vector3.Distance(location, transform.position);
            if (ThinkPaused || source == gameObject || distance > radius || distance < ignoreNoiseWithin || !agent.isOnNavMesh)
            {
                return;
            }
            // Sounds come from wherever things hit (next to a wall, under furniture): head for the nearest walkable spot,
            // or the move fails and the horse never comes.
            noiseLocation = ReachablePoint(location);
            SetState(DMMonsterState.Charge);
            MoveTo(noiseLocation, chargeSpeed);
        }

        /// <summary>Tests: send it home and stop it, so a noisy check does not leave it in the way.</summary>
        public void ServerPark()
        {
            ServerPlace(post, transform.eulerAngles.y);
            StopMoving();
            SetState(DMMonsterState.Idle);
        }

        protected override void ServerThink(float deltaTime)
        {
            switch (State)
            {
                case DMMonsterState.Charge:
                    TryCatch();
                    if (IsMoveDone())
                    {
                        StopMoving();
                        SetState(DMMonsterState.Search);
                    }
                    break;

                case DMMonsterState.Search:
                    // Standing where the sound was; anyone who bumps into it now is caught.
                    TryCatch();
                    transform.Rotate(0f, 60f * deltaTime, 0f, Space.World);
                    if (StateAge > searchSeconds)
                    {
                        SetState(DMMonsterState.Return);
                        MoveTo(post, returnSpeed);
                    }
                    break;

                case DMMonsterState.Return:
                    TryCatch();
                    if (IsMoveDone())
                    {
                        SetState(DMMonsterState.Idle);
                    }
                    break;
            }
        }
    }
}
