﻿using Intersect.Client.General.Enhancement;
using Intersect.Client.General.UpgradeStation;
using Intersect.GameObjects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Intersect.Client.Entities
{
    public partial class Player : Entity
    {
        public EnhancementInterface Enhancement { get; set; } = new EnhancementInterface();
        
        public UpgradeStationInterface UpgradeStation { get; set; } = new UpgradeStationInterface();

        public List<Guid> KnownEnhancements { get; set; } = new List<Guid>();

        public void SetKnownEnhancements(Guid[] knownEnhancements)
        {
            knownEnhancements.OrderBy(id => EnhancementDescriptor.GetName(id));
            KnownEnhancements.Clear();
            KnownEnhancements.AddRange(knownEnhancements);
        }
    }
}
