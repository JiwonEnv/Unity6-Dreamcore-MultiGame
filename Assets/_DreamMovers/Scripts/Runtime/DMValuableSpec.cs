using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// Everything that makes one antique spawned at runtime (a generated map). Set on the server before spawning and
    /// sent to clients with the spawn. Meters and kilograms; speeds in m/s.
    /// </summary>
    [Serializable]
    public struct DMValuableSpec : INetworkSerializable, IEquatable<DMValuableSpec>
    {
        public FixedString64Bytes Name;
        public DMShape Shape;
        /// <summary>Full size in meters.</summary>
        public Vector3 Size;
        public float MassKg;
        /// <summary>Impact speed (m/s) along the contact normal that starts cracking it. Lower = more fragile.</summary>
        public float CrackSpeed;
        public int Value;
        public Color Tint;
        /// <summary>Glass and polished brass are glossy (low roughness): the oil pastel shader turns those into star glints.</summary>
        public float Roughness;
        public float Metallic;

        public bool IsSet => Size != Vector3.zero;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref Shape);
            serializer.SerializeValue(ref Size);
            serializer.SerializeValue(ref MassKg);
            serializer.SerializeValue(ref CrackSpeed);
            serializer.SerializeValue(ref Value);
            serializer.SerializeValue(ref Tint);
            serializer.SerializeValue(ref Roughness);
            serializer.SerializeValue(ref Metallic);
        }

        public bool Equals(DMValuableSpec other)
        {
            return Name.Equals(other.Name) && Shape == other.Shape && Size == other.Size && MassKg == other.MassKg && CrackSpeed == other.CrackSpeed
                && Value == other.Value && Tint == other.Tint && Roughness == other.Roughness && Metallic == other.Metallic;
        }
    }
}
