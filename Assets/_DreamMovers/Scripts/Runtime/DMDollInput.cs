using UnityEngine;

namespace DreamMovers
{
    /// <summary>One frame of doll input, from the devices or a test script.</summary>
    public struct DMDollInput
    {
        /// <summary>x = right, y = forward, -1..1.</summary>
        public Vector2 Move;
        /// <summary>Degrees this frame: x = yaw, y = pitch up.</summary>
        public Vector2 Look;
        public bool Jump;
        public bool Sprint;
        public bool Crouch;
        public bool GrabPressed;
        public bool GrabReleased;
        public float HoldDistance;
        public bool Interact;
    }
}
