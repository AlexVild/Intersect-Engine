﻿using MessagePack;

namespace Intersect.Network.Packets.Client
{
    [MessagePackObject]
    public class RequestRecipesPacket : IntersectPacket
    {
        public RequestRecipesPacket()
        {
        }
    }
}
