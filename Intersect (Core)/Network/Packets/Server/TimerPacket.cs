﻿using Intersect.GameObjects.Timers;
using MessagePack;
using System;

namespace Intersect.Network.Packets.Server
{
    [MessagePackObject]
    public class TimerPacket : AbstractTimedPacket
    {
        [Key(3)]
        public Guid DescriptorId;

        [Key(4)]
        public long Timestamp;

        [Key(5)]
        public long StartTime;

        [Key(6)]
        public TimerType Type;

        [Key(7)]
        public string DisplayName;

        [Key(8)]
        public bool ContinueAfterExpiration;

        // EF
        public TimerPacket()
        {
        }

        public TimerPacket(Guid descriptorId, long timestamp, long startTime, TimerType type, string displayName, bool continueAfterExpiration)
        {
            DescriptorId = descriptorId;
            Timestamp = timestamp;
            StartTime = startTime;
            Type = type;
            DisplayName = displayName;
            ContinueAfterExpiration = continueAfterExpiration;
        }
    }
}
