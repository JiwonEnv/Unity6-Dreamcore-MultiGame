using System;
using Unity.Netcode;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// Server-side noise events: bumped antiques, sprinting dolls, wardrobe doors, the butler's bell.
    /// The bell horse is blind and hunts only by these. Set <see cref="ShowNoise"/> to draw them in the scene view.
    /// Ported from the UE5 UDMNoiseSubsystem; distances are in meters.
    /// </summary>
    public static class DMNoise
    {
        /// <summary>Location, radius (m), and what made it.</summary>
        public static event Action<Vector3, float, GameObject> Heard;

        /// <summary>Draws each noise as a sphere for a moment (scene view, server only). Turned on with -dmshownoise.</summary>
        public static bool ShowNoise = DMArgs.Has("dmshownoise");

        /// <summary>Anything within radius (m) of location can hear it. Ignored on clients.</summary>
        public static void Report(Vector3 location, float radius, GameObject source)
        {
            if (!DMNet.IsServer)
            {
                return;
            }
            if (ShowNoise)
            {
                DrawSphere(location, radius);
            }
            Heard?.Invoke(location, radius, source);
        }

        static void DrawSphere(Vector3 center, float radius)
        {
            const int segments = 24;
            Color color = new Color(1f, 0.55f, 0f);
            for (int axis = 0; axis < 3; ++axis)
            {
                Vector3 previous = Vector3.zero;
                for (int i = 0; i <= segments; ++i)
                {
                    float angle = i * Mathf.PI * 2f / segments;
                    Vector2 circle = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    Vector3 point = center + (axis == 0 ? new Vector3(circle.x, circle.y, 0f) : axis == 1 ? new Vector3(circle.x, 0f, circle.y) : new Vector3(0f, circle.x, circle.y));
                    if (i > 0)
                    {
                        Debug.DrawLine(previous, point, color, 0.6f);
                    }
                    previous = point;
                }
            }
        }
    }
}
