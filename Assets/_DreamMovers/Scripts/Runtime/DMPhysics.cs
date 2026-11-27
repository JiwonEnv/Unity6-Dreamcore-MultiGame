using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// Traces shared by the grab, the monsters and the wardrobes. Unity has no object channels like UE, so
    /// "static" (UE WorldStatic) means a collider with no rigidbody that is not a character: walls, floors,
    /// furniture boxes and wardrobes. Carried antiques, dolls and monsters are not static.
    /// </summary>
    public static class DMPhysics
    {
        static readonly RaycastHit[] Hits = new RaycastHit[32];

        public static bool IsStatic(Collider collider)
        {
            return collider != null && !collider.isTrigger && collider.attachedRigidbody == null && !(collider is CharacterController)
                && collider.GetComponentInParent<DMMonster>() == null;
        }

        /// <summary>Nearest hit from a to b, skipping colliders under ignore (and triggers). Everything solid counts.</summary>
        public static bool Linecast(Vector3 a, Vector3 b, Transform ignore, out RaycastHit hit)
        {
            return Cast(a, b, ignore, null, false, out hit);
        }

        /// <summary>Like Linecast, but also skips colliders under alsoIgnore.</summary>
        public static bool Linecast(Vector3 a, Vector3 b, Transform ignore, Transform alsoIgnore, out RaycastHit hit)
        {
            return Cast(a, b, ignore, alsoIgnore, false, out hit);
        }

        /// <summary>Only static geometry (see IsStatic) blocks.</summary>
        public static bool StaticLinecast(Vector3 a, Vector3 b, Transform ignore = null, Transform alsoIgnore = null)
        {
            return Cast(a, b, ignore, alsoIgnore, true, out _);
        }

        static bool Cast(Vector3 a, Vector3 b, Transform ignore, Transform alsoIgnore, bool staticOnly, out RaycastHit best)
        {
            best = default;
            Vector3 delta = b - a;
            float length = delta.magnitude;
            if (length < 1e-4f)
            {
                return false;
            }
            int count = Physics.RaycastNonAlloc(a, delta / length, Hits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < count; ++i)
            {
                RaycastHit hit = Hits[i];
                Transform t = hit.collider.transform;
                if ((ignore != null && t.IsChildOf(ignore)) || (alsoIgnore != null && t.IsChildOf(alsoIgnore)))
                {
                    continue;
                }
                if (staticOnly && !IsStatic(hit.collider))
                {
                    continue;
                }
                if (!found || hit.distance < best.distance)
                {
                    best = hit;
                    found = true;
                }
            }
            return found;
        }
    }
}
