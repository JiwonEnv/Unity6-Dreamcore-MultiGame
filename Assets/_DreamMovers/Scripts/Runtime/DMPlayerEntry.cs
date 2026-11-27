using System;
using Unity.Collections;
using Unity.Netcode;

namespace DreamMovers
{
    /// <summary>One player in the room, as everyone sees it (pause menu player list).</summary>
    public struct DMPlayerEntry : INetworkSerializable, IEquatable<DMPlayerEntry>
    {
        public ulong ClientId;
        public FixedString64Bytes Name;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Name);
        }

        public bool Equals(DMPlayerEntry other) => ClientId == other.ClientId && Name.Equals(other.Name);

        public override bool Equals(object obj) => obj is DMPlayerEntry other && Equals(other);

        public override int GetHashCode() => ClientId.GetHashCode();
    }
}
