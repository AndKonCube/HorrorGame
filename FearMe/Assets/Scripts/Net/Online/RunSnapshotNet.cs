#if FEARME_COOP_ONLINE
using System;
using FearMe.Core;
using Unity.Netcode;

namespace FearMe.Net.Online
{
    // The run snapshot as Netcode sends it. A wrapper, so the gameplay side
    // never has to know about Netcode's serialisation.
    public struct RunSnapshotNet : INetworkSerializable, IEquatable<RunSnapshotNet>
    {
        // False until the host has written a real snapshot, so a guest never
        // mistakes the all-zero default for "a key at spot 0".
        public bool Valid;
        public RunSnapshot Value;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Valid);
            serializer.SerializeValue(ref Value.keysHeld);
            serializer.SerializeValue(ref Value.keySpot);
            serializer.SerializeValue(ref Value.zonesUnlocked);
            serializer.SerializeValue(ref Value.pagesHeld);
            serializer.SerializeValue(ref Value.pageSpot0);
            serializer.SerializeValue(ref Value.pageSpot1);
            serializer.SerializeValue(ref Value.pageSpot2);
            serializer.SerializeValue(ref Value.banished);
            serializer.SerializeValue(ref Value.banishSerial);
            serializer.SerializeValue(ref Value.banishSeconds);
            serializer.SerializeValue(ref Value.demonLevel);
            serializer.SerializeValue(ref Value.boltsOpen);
        }

        public bool Equals(RunSnapshotNet other) => Valid == other.Valid && Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is RunSnapshotNet other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode() * 2 + (Valid ? 1 : 0);
    }
}
#endif
