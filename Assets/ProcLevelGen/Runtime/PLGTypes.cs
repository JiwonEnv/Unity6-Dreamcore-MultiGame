using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProcLevelGen
{
    // Module spec (Docs/ModuleSpec.md), ported from the UE5 ProcLevelGen plugin (tag ue5-final):
    // - Kit and layout data stay in the spec's plan space: centimeters, X and Y on the floor plan, Z up, yaw turning
    //   +X toward +Y. PLGSpace converts to Unity (meters, Y up) only when something is built in the scene.
    // - Pivot: center of the footprint, on the top of the floor (Z = 0). Modules only turn in 90 degree steps around it.
    // - Footprint sizes are multiples of 100 cm, so the pivot always lands on the 50 cm grid.
    // - Each module owns half of the wall (10 of 20 cm) inside its footprint; two modules side by side make one 20 cm wall.
    // - A door socket is a point on the footprint edge facing out. Two sockets connect when they meet, face each other
    //   and have the same width. Sockets left unconnected stay solid wall.

    public enum PLGRole
    {
        /// <summary>Placed first, at the generator's origin.</summary>
        Start,
        /// <summary>Placed right on a Start socket.</summary>
        Hub,
        /// <summary>Placed last, as deep in the layout as it fits.</summary>
        End,
        /// <summary>General room: reused frames, each gets a theme.</summary>
        Room,
        /// <summary>Connects rooms; dead-end corridors are removed.</summary>
        Corridor
    }

    /// <summary>Footprint edge in the module's own space; the value is also the number of 90 degree turns of its outward normal from +X.</summary>
    public enum PLGEdge
    {
        PosX,
        PosY,
        NegX,
        NegY
    }

    [Serializable]
    public struct PLGSocket
    {
        public PLGEdge Edge;

        [Tooltip("cm from the edge's center along the edge (toward +Y on X edges, +X on Y edges). Multiple of the grid.")]
        public float Offset;

        [Tooltip("Door width in cm: 150 small door, 300 double door. Only equal widths connect.")]
        public float Width;

        public PLGSocket(PLGEdge edge, float width, float offset = 0f)
        {
            Edge = edge;
            Width = width;
            Offset = offset;
        }
    }

    /// <summary>A place in a module where the game may put content; what goes there is up to the game, by tag.</summary>
    [Serializable]
    public struct PLGSpawnPoint
    {
        public string Tag;

        [Tooltip("Module plan space (cm), Z = 0 is the floor top.")]
        public Vector3 Location;

        [Tooltip("Facing in module plan space, degrees.")]
        public float Yaw;
    }

    /// <summary>Static furniture built with the graybox on every machine, so it needs no replication (desks, shelves).</summary>
    [Serializable]
    public struct PLGPropBox
    {
        [Tooltip("Module plan space (cm).")]
        public Vector3 Center;

        public Vector3 Size;
    }

    [Serializable]
    public class PLGModuleDef
    {
        public string Name;
        public PLGRole Role = PLGRole.Room;

        [Tooltip("Footprint in cm (X, Y), walls included. Multiples of 100.")]
        public Vector2 Size = new Vector2(800f, 800f);

        public List<PLGSocket> Sockets = new List<PLGSocket>();

        [Tooltip("Relative chance against other modules with the same role.")]
        [Min(0f)]
        public float Weight = 1f;

        public List<PLGSpawnPoint> SpawnPoints = new List<PLGSpawnPoint>();
        public List<PLGPropBox> Props = new List<PLGPropBox>();
    }

    /// <summary>Look of a general room. Graybox only tints; real themes will swap wall and floor materials and contents.</summary>
    [Serializable]
    public class PLGTheme
    {
        public string Name;
        public Color FloorColor = new Color(0.5f, 0.4f, 0.3f);
        public Color WallColor = new Color(0.9f, 0.85f, 0.75f);
    }

    [Serializable]
    public class PLGPlacedModule
    {
        /// <summary>Index into the generator's module list.</summary>
        public int Def = PLG.IndexNone;

        /// <summary>Pivot in the generator's plan space (cm).</summary>
        public Vector2 Location;

        /// <summary>Yaw in 90 degree steps (0..3).</summary>
        public int Quarter;

        /// <summary>Shortest distance from the start module, counted in doors (loops included). IndexNone when removed.</summary>
        public int Depth;

        /// <summary>Index into the theme list, rooms only.</summary>
        public int Theme = PLG.IndexNone;

        /// <summary>Per socket of the def: the placed module behind that door, or IndexNone when it stays wall.</summary>
        public List<int> Links = new List<int>();

        /// <summary>Pruned (a dead-end corridor); skipped when building.</summary>
        public bool Removed;

        public PLGPlacedModule Clone()
        {
            var copy = (PLGPlacedModule)MemberwiseClone();
            copy.Links = new List<int>(Links);
            return copy;
        }
    }

    /// <summary>A spawn point resolved into the world for one placed module.</summary>
    public struct PLGSpawnSlot
    {
        /// <summary>World position and rotation (Unity space).</summary>
        public Pose Pose;

        /// <summary>Index into the layout's modules.</summary>
        public int Module;

        public PLGRole Role;
        public int Theme;
    }

    [Serializable]
    public class PLGLayout
    {
        public int Seed;
        public List<PLGPlacedModule> Modules = new List<PLGPlacedModule>();
    }

    /// <summary>An open door during generation: (placed module, socket).</summary>
    public readonly struct PLGDoor : IEquatable<PLGDoor>
    {
        public readonly int Module;
        public readonly int Socket;

        public PLGDoor(int module, int socket)
        {
            Module = module;
            Socket = socket;
        }

        public bool Equals(PLGDoor other) => Module == other.Module && Socket == other.Socket;
        public override bool Equals(object obj) => obj is PLGDoor other && Equals(other);
        public override int GetHashCode() => (Module * 397) ^ Socket;
    }

    /// <summary>Axis-aligned box on the floor plan, with UE FBox2D semantics (Intersect counts touching edges).</summary>
    public struct PLGBox2
    {
        public Vector2 Min;
        public Vector2 Max;
        public bool IsValid;

        public PLGBox2(Vector2 min, Vector2 max)
        {
            Min = min;
            Max = max;
            IsValid = true;
        }

        public PLGBox2 ExpandBy(float w) => new PLGBox2(Min - new Vector2(w, w), Max + new Vector2(w, w));

        public bool Intersect(PLGBox2 other)
        {
            if (Min.x > other.Max.x || other.Min.x > Max.x)
            {
                return false;
            }
            if (Min.y > other.Max.y || other.Min.y > Max.y)
            {
                return false;
            }
            return true;
        }

        /// <summary>Strictly inside (UE FBox2D::IsInside).</summary>
        public bool IsInside(Vector2 p) => p.x > Min.x && p.x < Max.x && p.y > Min.y && p.y < Max.y;

        public Vector2 Center => (Min + Max) * 0.5f;
        public Vector2 Size => Max - Min;

        /// <summary>Grows to include another box (UE FBox2D operator+=).</summary>
        public PLGBox2 Union(PLGBox2 other)
        {
            if (!IsValid)
            {
                return other;
            }
            if (!other.IsValid)
            {
                return this;
            }
            return new PLGBox2(Vector2.Min(Min, other.Min), Vector2.Max(Max, other.Max));
        }
    }
}
