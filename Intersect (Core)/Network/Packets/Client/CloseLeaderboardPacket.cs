﻿using MessagePack;

namespace Intersect.Network.Packets.Client
{
    [MessagePackObject]
    public class CloseLeaderboardPacket : IntersectPacket
    {
        // EF
        public CloseLeaderboardPacket() { }
    }
}
