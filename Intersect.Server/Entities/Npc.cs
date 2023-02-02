﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Intersect.Enums;
using Intersect.GameObjects;
using Intersect.GameObjects.Events;
using Intersect.Logging;
using Intersect.Network.Packets.Server;
using Intersect.Server.Core;
using Intersect.Server.Database;
using Intersect.Server.Database.PlayerData.Players;
using Intersect.Server.Entities.Combat;
using Intersect.Server.Entities.Events;
using Intersect.Server.Entities.Pathfinding;
using Intersect.Server.Entities.PlayerData;
using Intersect.Server.General;
using Intersect.Server.Localization;
using Intersect.Server.Maps;
using Intersect.Server.Networking;
using Intersect.Server.Utilities;
using Intersect.Utilities;

namespace Intersect.Server.Entities
{

    public partial class Npc : AttackingEntity
    {

        //Spell casting
        public long CastFreq;

        /// <summary>
        /// Damage Map - Keep track of who is doing the most damage to this npc and focus accordingly
        /// </summary>
        public ConcurrentDictionary<Entity, long> DamageMap = new ConcurrentDictionary<Entity, long>();

        public ConcurrentDictionary<Guid, bool> LootMap = new ConcurrentDictionary<Guid, bool>();

        public Guid[] LootMapCache = Array.Empty<Guid>();

        /// <summary>
        /// Returns the entity that ranks the highest on this NPC's damage map.
        /// </summary>
        public Entity DamageMapHighest { 
            get {
                long damage = 0;
                Entity top = null;
                foreach (var pair in DamageMap)
                {
                    // Only include players on the current instance
                    if (pair.Value > damage && pair.Key.MapInstanceId == MapInstanceId)
                    {
                        top = pair.Key;
                        damage = pair.Value;
                    }
                }
                return top;
            } 
        }

        public bool Despawnable;

        //Moving
        public long LastRandomMove;

        //Pathfinding
        private Pathfinder mPathFinder;

        private Task mPathfindingTask;

        public byte Range;

        //Respawn/Despawn
        public long RespawnTime;

        public long FindTargetWaitTime;
        public int FindTargetDelay = 500;

        private int mTargetFailCounter = 0;
        private int mTargetFailMax = 10;

        private int mResetDistance = 0;
        private int mResetCounter = 0;
        private int mResetMax = 100;
        private bool mResetting = false;

        private int mLastTargetDir = -1;
        private long mLastOverrideAttack = 0L;

        private Dictionary<Guid, ThreatLevel> PlayerThreatLevels = new Dictionary<Guid, ThreatLevel>();
        private long LastThreatLevelReset = 0L;
        private long DirChangeTime = 0L;

        /// <summary>
        /// The map on which this NPC was "aggro'd" and started chasing a target.
        /// </summary>
        public MapController AggroCenterMap;

        /// <summary>
        /// The X value on which this NPC was "aggro'd" and started chasing a target.
        /// </summary>
        public int AggroCenterX;

        /// <summary>
        /// The Y value on which this NPC was "aggro'd" and started chasing a target.
        /// </summary>
        public int AggroCenterY;

        /// <summary>
        /// The Z value on which this NPC was "aggro'd" and started chasing a target.
        /// </summary>
        public int AggroCenterZ;

        public string PermadeathKey = string.Empty; // A key that we can use to reference if this entity is permadead in an instance

        public Npc(NpcBase myBase, bool despawnable = false) : base()
        {
            Name = myBase.Name;
            Sprite = myBase.Sprite;
            Color = myBase.Color;
            Level = myBase.Level;
            ImmuneTo = myBase.Immunities;
            Base = myBase;
            Despawnable = despawnable;

            for (var i = 0; i < (int) Stats.StatCount; i++)
            {
                BaseStats[i] = myBase.Stats[i];
                Stat[i] = new Stat((Stats) i, this);
            }

            var spellSlot = 0;
            for (var I = 0; I < Base.Spells.Count; I++)
            {
                var slot = new SpellSlot(spellSlot);
                slot.Set(new Spell(Base.Spells[I]));
                Spells.Add(slot);
                spellSlot++;
            }

            for (var i = 0; i < (int) Vitals.VitalCount; i++)
            {
                SetMaxVital(i, myBase.MaxVital[i]);
                SetVital(i, myBase.MaxVital[i]);
            }

            Range = (byte) myBase.SightRange;
            mPathFinder = new Pathfinder(this);
            if (myBase.DeathAnimation != null)
            {
                DeathAnimation = myBase.DeathAnimation.Id;
            }
        }

        public NpcBase Base { get; private set; }

        private bool IsStunnedOrSleeping => CachedStatuses.Any(PredicateStunnedOrSleeping);

        private bool IsUnableToCastSpells => CachedStatuses.Any(PredicateUnableToCastSpells);

        public override EntityTypes GetEntityType()
        {
            return EntityTypes.GlobalEntity;
        }

        public override void Die(bool generateLoot = true, Entity killer = null)
        {
            lock (EntityLock) {
                base.Die(generateLoot, killer);

                AggroCenterMap = null;
                AggroCenterX = 0;
                AggroCenterY = 0;
                AggroCenterZ = 0;

                if (MapController.TryGetInstanceFromMap(MapId, MapInstanceId, out var instance))
                {
                    instance.RemoveEntity(this);
                    if (Base.DeathTransformId != Guid.Empty)
                    {
                        instance.SpawnNpc((byte)X, (byte)Y, (byte)Dir, Base.DeathTransformId);
                    }
                }
                PacketSender.SendEntityDie(this);
                PacketSender.SendEntityLeave(this);

                // Do not process permadeaths on the overworld or if the entity was not killed by some other entity
                if (MapInstanceId != Guid.Empty && killer != null)
                {
                    if (!string.IsNullOrEmpty(PermadeathKey) && InstanceProcessor.TryGetInstanceController(MapInstanceId, out var instanceController))
                    {
                        instanceController.PermadeadNpcs.Add(PermadeathKey);
                    }
                }

                if (killer is Player playerKiller)
                {
                    long recordKilled = playerKiller.IncrementRecord(RecordType.NpcKilled, Base.Id);

                    // If we've just unlocked some bestiary item, send a KC update, which will force a bestiary update on the client
                    var bestiaryThresholds = Base.BestiaryUnlocks.Values.Where(val => val > 0).ToList();
                    if (bestiaryThresholds.Contains((int)recordKilled))
                    {
                        PacketSender.SendKillCount(playerKiller, Base.Id);
                    }
                    else if (Options.SendNpcRecordUpdates && recordKilled % Options.NpcRecordUpdateInterval == 0)
                    {
                        playerKiller.SendRecordUpdate(Strings.Records.enemykilled.ToString(recordKilled, Name));
                    }

                    ChallengeUpdateProcesser.UpdateChallengesOf(new BeastsKilledOverTime(playerKiller, Base.Id));
                }
            }
        }

        public bool TargetHasStealth(Entity target)
        {
            return target == null || target.CachedStatuses.Any(s => s.Type == StatusTypes.Stealth);
        }

        //Targeting
        public void AssignTarget(Entity en)
        {
            var oldTarget = Target;

            // Are we resetting? If so, do not allow for a new target.
            var pathTarget = mPathFinder?.GetTarget();
            if (AggroCenterMap != null && pathTarget != null &&
                pathTarget.TargetMapId == AggroCenterMap.Id && pathTarget.TargetX == AggroCenterX && pathTarget.TargetY == AggroCenterY)
            {
                if (en == null)
                {
                                    return;

                }
                else
                {
                    return;

                }
            }

            //Why are we doing all of this logic if we are assigning a target that we already have?
            if (en != null && en != Target)
            {
                // Can't assign a new target if taunted, unless we're resetting their target somehow.
                // Also make sure the taunter is in range.. If they're dead or gone, we go for someone else!
                if ((pathTarget != null && AggroCenterMap != null && (pathTarget.TargetMapId != AggroCenterMap.Id || pathTarget.TargetX != AggroCenterX || pathTarget.TargetY != AggroCenterY)))
                {
                    foreach (var status in CachedStatuses)
                    {
                        if (status.Type == StatusTypes.Taunt && en != status.Attacker && GetDistanceTo(status.Attacker) != 9999)
                        {
                            return;
                        }
                    }
                }

                if (en.GetType() == typeof(Projectile))
                {
                    if (((Projectile)en).Owner != this && !TargetHasStealth((Projectile)en))
                    {
                        Target = ((Projectile)en).Owner;
                    }
                }
                else
                {
                    if (en.GetType() == typeof(Npc))
                    {
                        if (((Npc)en).Base == Base)
                        {
                            if (Base.AttackAllies == false)
                            {
                                return;
                            }
                        }
                    }

                    if (en.GetType() == typeof(Player))
                    {
                        //TODO Make sure that the npc can target the player
                        if (this != en && !TargetHasStealth(en))
                        {
                            Target = en;
                        }
                    }
                    else
                    {
                        if (this != en && !TargetHasStealth(en))
                        {
                            Target = en;
                        }
                    }
                }

                // Are we configured to handle resetting NPCs after they chase a target for a specified amount of tiles?
                if (Options.Npc.AllowResetRadius)
                {
                    // Are we configured to allow new reset locations before they move to their original location, or do we simply not have an original location yet?
                    if (Options.Npc.AllowNewResetLocationBeforeFinish || AggroCenterMap == null)
                    {
                        AggroCenterMap = Map;
                        AggroCenterX = X;
                        AggroCenterY = Y;
                        AggroCenterZ = Z;
                    }
                }
            }
            else
            {
                Target = en;
            }
            
            if (Target != oldTarget)
            {
                CombatTimer = Timing.Global.Milliseconds + Options.CombatTime;
                PacketSender.SendNpcAggressionToProximity(this);
            }
            mTargetFailCounter = 0;
        }

        public void RemoveFromDamageMap(Entity en)
        {
            DamageMap.TryRemove(en, out _);
        }

        public void RemoveTarget()
        {
            AssignTarget(null);
        }

        public override int CalculateAttackTime()
        {
            if (Base.AttackSpeedModifier == 1) //Static
            {
                return Base.AttackSpeedValue;
            }

            return base.CalculateAttackTime();
        }

        public override bool CanAttack(Entity entity, SpellBase spell)
        {
            if (!base.CanAttack(entity, spell))
            {
                return false;
            }

            if (entity.GetType() == typeof(EventPageInstance))
            {
                return false;
            }

            //Check if the attacker is stunned or blinded.
            foreach (var status in CachedStatuses)
            {
                if (status.Type == StatusTypes.Stun || status.Type == StatusTypes.Sleep)
                {
                    return false;
                }
            }

            if (TargetHasStealth(entity))
            {
                return false;
            }

            if (entity.GetType() == typeof(Resource))
            {
                if (!entity.Passable)
                {
                    return false;
                }
            }
            else if (entity.GetType() == typeof(Npc))
            {
                return CanNpcCombat(entity, spell != null && spell.Combat.Friendly) || entity == this;
            }
            else if (entity.GetType() == typeof(Player))
            {
                var player = (Player) entity;
                if (player.PlayerDead)
                {
                    return false;
                }
                var friendly = spell != null && spell.Combat.Friendly;
                if (friendly && IsAllyOf(player))
                {
                    return true;
                }

                if (!friendly && !IsAllyOf(player))
                {
                    return true;
                }

                return false;
            }

            return true;
        }

        public bool CanNpcCombat(Entity enemy, bool friendly = false)
        {
            //Check for NpcVsNpc Combat, both must be enabled and the attacker must have it as an enemy or attack all types of npc.
            if (!friendly)
            {
                if (enemy != null && enemy.GetType() == typeof(Npc) && Base != null)
                {
                    if (((Npc) enemy).Base.NpcVsNpcEnabled == false)
                    {
                        return false;
                    }

                    if (Base.AttackAllies && ((Npc) enemy).Base == Base)
                    {
                        return true;
                    }

                    for (var i = 0; i < Base.AggroList.Count; i++)
                    {
                        if (NpcBase.Get(Base.AggroList[i]) == ((Npc) enemy).Base)
                        {
                            return true;
                        }
                    }

                    return false;
                }

                if (enemy != null && enemy.GetType() == typeof(Player))
                {
                    return true;
                }
            }
            else
            {
                if (enemy != null &&
                    enemy.GetType() == typeof(Npc) &&
                    Base != null &&
                    ((Npc) enemy).Base == Base &&
                    Base.AttackAllies == false)
                {
                    return true;
                }
                else if (enemy != null && enemy.GetType() == typeof(Player))
                {
                    return false;
                }
            }

            return false;
        }

        private static bool PredicateStunnedOrSleeping(Status status)
        {
            switch (status?.Type)
            {
                case StatusTypes.Sleep:
                case StatusTypes.Stun:
                    return true;
                default:
                    return false;
            }
        }

        private static bool PredicateUnableToCastSpells(Status status)
        {
            switch (status?.Type)
            {
                case StatusTypes.Silence:
                case StatusTypes.Sleep:
                case StatusTypes.Stun:
                    return true;
                default:
                    return false;
            }
        }

        public override int CanMove(int moveDir)
        {
            var canMove = base.CanMove(moveDir);

            // If configured & blocked by an entity, ignore the entity and proceed to move
            if (Options.Instance.NpcOpts.IntangibleDuringReset && canMove > -1 )
            {
                canMove = mResetting ? -1 : canMove;
            }
            if ((canMove == -1 || canMove == -4) && IsFleeing() && Options.Instance.NpcOpts.AllowResetRadius)
            {
                var yOffset = 0;
                var xOffset = 0;
                var tile = new TileHelper(MapId, X, Y);
                switch (moveDir)
                {
                    case 0: //Up
                        yOffset--;

                        break;
                    case 1: //Down
                        yOffset++;

                        break;
                    case 2: //Left
                        xOffset--;

                        break;
                    case 3: //Right
                        xOffset++;

                        break;
                    case 4: //NW
                        yOffset--;
                        xOffset--;

                        break;
                    case 5: //NE
                        yOffset--;
                        xOffset++;

                        break;
                    case 6: //SW
                        yOffset++;
                        xOffset--;

                        break;
                    case 7: //SE
                        yOffset++;
                        xOffset++;

                        break;
                }

                if (tile.Translate(xOffset, yOffset))
                {
                    //If this would move us past our reset radius then we cannot move.
                    var dist = GetDistanceBetween(AggroCenterMap, tile.GetMap(), AggroCenterX, tile.GetX(), AggroCenterY, tile.GetY());
                    if (dist > Math.Max(Options.Npc.ResetRadius, Base.ResetRadius))
                    {
                        return -2;
                    }
                }
            }
            return canMove;
        }

        private void TryCastSpells()
        {
            var target = Target;

            if (target == null || mPathFinder.GetTarget() == null)
            {
                return;
            }

            // Check if NPC is stunned/sleeping
            if (IsStunnedOrSleeping)
            {
                return;
            }

            //Check if NPC is casting a spell
            if (CastTime > Timing.Global.Milliseconds)
            {
                return; //can't move while casting
            }

            if (CastFreq >= Timing.Global.Milliseconds)
            {
                return;
            }

            // Check if the NPC is able to cast spells
            if (IsUnableToCastSpells)
            {
                return;
            }

            if (Base.Spells == null || Base.Spells.Count <= 0)
            {
                return;
            }

            // Pick a random spell
            var spellIndex = Randomization.Next(0, Spells.Count);
            var spellId = Base.Spells[spellIndex];
            var spellBase = SpellBase.Get(spellId);
            if (spellBase == null)
            {
                return;
            }

            if (spellBase.Combat == null)
            {
                Log.Warn($"Combat data missing for {spellBase.Id}.");
            }

            var range = spellBase.Combat?.CastRange ?? 0;
            var targetType = spellBase.Combat?.TargetType ?? SpellTargetTypes.Single;
            var projectileBase = spellBase.Combat?.Projectile;

            if (spellBase.SpellType == SpellTypes.CombatSpell &&
                targetType == SpellTargetTypes.Projectile &&
                projectileBase != null &&
                InRangeOf(target, projectileBase.Range))
            {
                range = projectileBase.Range;
                var dirToEnemy = DirToEnemy(target);
                if (dirToEnemy != Dir)
                {
                    if (LastRandomMove >= Timing.Global.Milliseconds)
                    {
                        return;
                    }

                    //Face the target -- next frame fire -- then go on with life
                    ChangeDir(dirToEnemy); // Gotta get dir to enemy
                    LastRandomMove = Timing.Global.Milliseconds + Randomization.Next(1000, 3000);

                    return;
                }
            }

            if (spellBase.VitalCost == null)
            {
                return;
            }

            if (spellBase.VitalCost[(int) Vitals.Mana] > GetVital(Vitals.Mana))
            {
                return;
            }

            if (spellBase.VitalCost[(int) Vitals.Health] > GetVital(Vitals.Health))
            {
                return;
            }

            var spell = Spells[spellIndex];
            if (spell == null)
            {
                return;
            }

            if (SpellCooldowns.ContainsKey(spell.SpellId) && SpellCooldowns[spell.SpellId] >= Timing.Global.MillisecondsUtc)
            {
                return;
            }

            if (!InRangeOf(target, range) && targetType == SpellTargetTypes.Single)
            {
                // ReSharper disable once SwitchStatementMissingSomeCases
                return;
            }

            CastTime = Timing.Global.Milliseconds + spellBase.CastDuration;

            if (spellBase.VitalCost[(int) Vitals.Mana] > 0)
            {
                SubVital(Vitals.Mana, spellBase.VitalCost[(int) Vitals.Mana]);
            }
            else
            {
                AddVital(Vitals.Mana, -spellBase.VitalCost[(int) Vitals.Mana]);
            }

            if (spellBase.VitalCost[(int) Vitals.Health] > 0)
            {
                SubVital(Vitals.Health, spellBase.VitalCost[(int) Vitals.Health]);
            }
            else
            {
                AddVital(Vitals.Health, -spellBase.VitalCost[(int) Vitals.Health]);
            }

            if ((spellBase.Combat?.Friendly ?? false) && spellBase.SpellType != SpellTypes.WarpTo)
            {
                CastTarget = this;
            }
            else
            {
                CastTarget = target;
            }

            switch (Base.SpellFrequency)
            {
                case 0:
                    CastFreq = Timing.Global.Milliseconds + 30000;

                    break;

                case 1:
                    CastFreq = Timing.Global.Milliseconds + 15000;

                    break;

                case 2:
                    CastFreq = Timing.Global.Milliseconds + 8000;

                    break;

                case 3:
                    CastFreq = Timing.Global.Milliseconds + 4000;

                    break;

                case 4:
                    CastFreq = Timing.Global.Milliseconds + 2000;

                    break;
            }

            SpellCastSlot = spellIndex;

            if (spellBase.CastAnimationId != Guid.Empty)
            {
                PacketSender.SendAnimationToProximity(spellBase.CastAnimationId, 1, Id, MapId, 0, 0, (sbyte) Dir, MapInstanceId);

                //Target Type 1 will be global entity
            }

            PacketSender.SendEntityCastTime(this, spellId);
        }

        public bool IsFleeing()
        {
            if (Base.FleeHealthPercentage > 0)
            {
                var fleeHpCutoff = GetMaxVital(Vitals.Health) * ((float)Base.FleeHealthPercentage / 100f);
                if (GetVital(Vitals.Health) < fleeHpCutoff || (Base.FleeHealthPercentage >= 100 && Target != default))
                {
                    return true;
                }
            }
            return false;
        }

        // TODO: Improve NPC movement to be more fluid like a player
        //General Updating
        public override void Update(long timeMs)
        {
            var lockObtained = false;
            try
            {
                Monitor.TryEnter(EntityLock, ref lockObtained);
                if (lockObtained)
                {
                    var curMapLink = MapId;
                    base.Update(timeMs);
                    var tempTarget = Target;

                    foreach (var status in CachedStatuses)
                    {
                        if (status.Type == StatusTypes.Stun || status.Type == StatusTypes.Sleep)
                        {
                            return;
                        }
                    }

                    if (timeMs > LastThreatLevelReset)
                    {
                        LastThreatLevelReset = timeMs + Options.Instance.CombatOpts.ResetThreatLevelAggroTime;
                        PlayerThreatLevels.Clear();
                    }

                    var fleeing = IsFleeing();

                    if (MoveTimer < Timing.Global.Milliseconds)
                    {
                        var targetMap = Guid.Empty;
                        var targetX = 0;
                        var targetY = 0;
                        var targetZ = 0;

                        //TODO Clear Damage Map if out of combat (target is null and combat timer is to the point that regen has started)
                        if (tempTarget != null && (Options.Instance.NpcOpts.ResetIfCombatTimerExceeded && Timing.Global.Milliseconds > CombatTimer))
                        {
                            if (CheckForResetLocation(true))
                            {
                                if (Target != tempTarget)
                                {
                                    PacketSender.SendNpcAggressionToProximity(this);
                                }
                                return;
                            }
                        }

                        // Are we resetting? If so, regenerate completely!
                        if (mResetting)
                        {
                            var distance = GetDistanceTo(AggroCenterMap, AggroCenterX, AggroCenterY);
                            // Have we reached our destination? If so, clear it.
                            if (distance < 1)
                            {
                                ResetAggroCenter(out targetMap);
                            }

                            ResetNpc(Options.Instance.NpcOpts.ContinuouslyResetVitalsAndStatuses);
                            tempTarget = Target;

                            if (distance != mResetDistance)
                            {
                                mResetDistance = distance;
                            }
                            else 
                            {
                                // Something is fishy here.. We appear to be stuck in a reset loop?
                                // Give it a few more attempts and reset the NPC's center if we're stuck!
                                mResetCounter++;
                                if (mResetCounter > mResetMax)
                                {
                                    ResetAggroCenter(out targetMap);
                                    mResetCounter = 0;
                                    mResetDistance = 0;
                                }
                            }
                            
                        }

                        if (tempTarget != null && (tempTarget.IsDead() || !InRangeOf(tempTarget, Options.MapWidth * 2)))
                        {
                            TryFindNewTarget(Timing.Global.Milliseconds, tempTarget.Id);
                            tempTarget = Target;
                        }

                        //Check if there is a target, if so, run their ass down.
                        if (tempTarget != null)
                        {
                            if (!tempTarget.IsDead() && CanAttack(tempTarget, null))
                            {
                                targetMap = tempTarget.MapId;
                                targetX = tempTarget.X;
                                targetY = tempTarget.Y;
                                targetZ = tempTarget.Z;
                                foreach (var targetStatus in tempTarget.CachedStatuses)
                                {
                                    if (targetStatus.Type == StatusTypes.Stealth)
                                    {
                                        targetMap = Guid.Empty;
                                        targetX = 0;
                                        targetY = 0;
                                        targetZ = 0;
                                    }
                                }
                            }
                        }
                        else //Find a target if able
                        {
                            // Check if attack on sight or have other npc's to target
                            TryFindNewTarget(timeMs);
                            tempTarget = Target;
                        }

                        if (targetMap != Guid.Empty)
                        {
                            //Check if target map is on one of the surrounding maps, if not then we are not even going to look.
                            if (targetMap != MapId)
                            {
                                var found = false;
                                foreach (var map in MapController.Get(MapId).SurroundingMaps)
                                {
                                    if (map.Id == targetMap)
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                                if (!found)
                                {
                                    targetMap = Guid.Empty;
                                }
                            }
                        }

                        if (targetMap != Guid.Empty)
                        {
                            if (mPathFinder.GetTarget() != null)
                            {
                                if (targetMap != mPathFinder.GetTarget().TargetMapId ||
                                    targetX != mPathFinder.GetTarget().TargetX ||
                                    targetY != mPathFinder.GetTarget().TargetY)
                                {
                                    mPathFinder.SetTarget(null);
                                }
                            }

                            if (mPathFinder.GetTarget() == null)
                            {
                                mPathFinder.SetTarget(new PathfinderTarget(targetMap, targetX, targetY, targetZ));

                                if (tempTarget != Target)
                                {
                                    tempTarget = Target;
                                }
                            }

                        }

                        if (mPathFinder.GetTarget() != null && Base.Movement != (int)NpcMovement.Static)
                        {
                            TryCastSpells();
                            // TODO: Make resetting mobs actually return to their starting location.
                            if ((!mResetting && !IsOneBlockAway(
                                mPathFinder.GetTarget().TargetMapId, mPathFinder.GetTarget().TargetX,
                                mPathFinder.GetTarget().TargetY, mPathFinder.GetTarget().TargetZ
                            )) ||
                            (mResetting && GetDistanceTo(AggroCenterMap, AggroCenterX, AggroCenterY) != 0)
                            )
                            {
                                switch (mPathFinder.Update(timeMs))
                                {
                                    case PathfinderResult.Success:

                                        var dir = mPathFinder.GetMove();
                                        if (dir > -1)
                                        {
                                            if (fleeing)
                                            {
                                                switch (dir)
                                                {
                                                    case 0:
                                                        dir = 1;

                                                        break;
                                                    case 1:
                                                        dir = 0;

                                                        break;
                                                    case 2:
                                                        dir = 3;

                                                        break;
                                                    case 3:
                                                        dir = 2;

                                                        break;
                                                }
                                            }

                                            if (CanMove(dir) == -1 || CanMove(dir) == -4)
                                            {
                                                //check if NPC is snared or stunned
                                                foreach (var status in CachedStatuses)
                                                {
                                                    if (status.Type == StatusTypes.Stun ||
                                                        status.Type == StatusTypes.Snare ||
                                                        status.Type == StatusTypes.Sleep)
                                                    {
                                                        return;
                                                    }
                                                }

                                                Move((byte)dir, null);
                                            }
                                            else
                                            {
                                                if (Base.StandStill && Target != null)
                                                {
                                                    var dirTarget = GetDirectionTo(Target);
                                                    if (mLastTargetDir < 0 || mLastTargetDir != dirTarget)
                                                    {
                                                        mLastTargetDir = dirTarget;
                                                        Dir = dirTarget;
                                                        PacketSender.SendEntityDir(this);
                                                    }
                                                    if ((Base.SpellAttackOverrideId == default && CanAttack(Target, null)) || (Base.SpellAttackOverrideId != default && CanAttack(Target, SpellBase.Get(Base.SpellAttackOverrideId))))
                                                    {
                                                        MeleeAttack(Target, false);
                                                    }
                                                }
                                                mPathFinder.PathFailed(timeMs);
                                            }

                                            // Are we resetting?
                                            if (mResetting)
                                            {
                                                // Have we reached our destination? If so, clear it.
                                                if (GetDistanceTo(AggroCenterMap, AggroCenterX, AggroCenterY) == 0)
                                                {
                                                    targetMap = Guid.Empty;

                                                    // Reset our aggro center so we can get "pulled" again.
                                                    AggroCenterMap = null;
                                                    AggroCenterX = 0;
                                                    AggroCenterY = 0;
                                                    AggroCenterZ = 0;
                                                    mPathFinder?.SetTarget(null);
                                                    mResetting = false;
                                                }
                                            }  
                                        }

                                        break;
                                    case PathfinderResult.OutOfRange:
                                        TryFindNewTarget(timeMs, tempTarget?.Id ?? Guid.Empty, true);
                                        tempTarget = Target;
                                        targetMap = Guid.Empty;

                                        break;
                                    case PathfinderResult.NoPathToTarget:
                                        TryFindNewTarget(timeMs, tempTarget?.Id ?? Guid.Empty, true);
                                        tempTarget = Target;
                                        targetMap = Guid.Empty;

                                        break;
                                    case PathfinderResult.Failure:
                                        targetMap = Guid.Empty;
                                        TryFindNewTarget(timeMs, tempTarget?.Id ?? Guid.Empty, true);
                                        tempTarget = Target;

                                        break;
                                    case PathfinderResult.Wait:
                                        targetMap = Guid.Empty;

                                        break;
                                    default:
                                        throw new ArgumentOutOfRangeException();
                                }
                            }
                            else
                            {
                                var fleed = false;
                                if (tempTarget != null && fleeing)
                                {
                                    var dir = DirToEnemy(tempTarget);
                                    switch (dir)
                                    {
                                        case 0:
                                            dir = 1;

                                            break;
                                        case 1:
                                            dir = 0;

                                            break;
                                        case 2:
                                            dir = 3;

                                            break;
                                        case 3:
                                            dir = 2;

                                            break;
                                    }

                                    if (CanMove(dir) == -1 || CanMove(dir) == -4)
                                    {
                                        //check if NPC is snared or stunned
                                        foreach (var status in CachedStatuses)
                                        {
                                            if (status.Type == StatusTypes.Stun ||
                                                status.Type == StatusTypes.Snare ||
                                                status.Type == StatusTypes.Sleep)
                                            {
                                                return;
                                            }
                                        }

                                        if (Timing.Global.Milliseconds > DashTransmissionTimer)
                                        {
                                            Move(dir, null);
                                            fleed = true;
                                        }
                                    }
                                }

                                if (!fleed)
                                {
                                    if (tempTarget != null)
                                    {
                                        if (Dir != DirToEnemy(tempTarget) && DirToEnemy(tempTarget) != -1 && timeMs > DirChangeTime)
                                        {  
                                            ChangeDir(DirToEnemy(tempTarget));
                                        }
                                        else
                                        {
                                            if (tempTarget.IsDisposed)
                                            {
                                                TryFindNewTarget(timeMs);
                                                tempTarget = Target;
                                            }
                                            else
                                            {
                                                if ((Base.SpellAttackOverrideId == default && CanAttack(Target, null)) || (Base.SpellAttackOverrideId != default && CanAttack(Target, SpellBase.Get(Base.SpellAttackOverrideId))))
                                                {
                                                    MeleeAttack(tempTarget, false);
                                                }
                                            }
                                        }
                                    }
                                }

                                // This is done to prevent an enemy from ALWAYS, immediately, facing you.
                                if (timeMs > DirChangeTime)
                                {
                                    DirChangeTime = timeMs + Options.Instance.CombatOpts.NpcDirChangeTimer - (Base.Stats[(int) Stats.Speed] * Options.Instance.CombatOpts.NpcDirChangeSpeedMult);
                                }
                            }
                        }

                        CheckForResetLocation();

                        //Move randomly
                        if (targetMap != Guid.Empty)
                        {
                            return;
                        }

                        if (LastRandomMove >= Timing.Global.Milliseconds || CastTime > 0)
                        {
                            return;
                        }

                        if (Base.Movement == (int)NpcMovement.StandStill)
                        {
                            LastRandomMove = Timing.Global.Milliseconds + Randomization.Next(1000, 3000);

                            return;
                        }
                        else if (Base.Movement == (int)NpcMovement.TurnRandomly)
                        {
                            ChangeDir((byte)Randomization.Next(0, 4));
                            LastRandomMove = Timing.Global.Milliseconds + Randomization.Next(1000, 3000);

                            return;
                        }

                        var i = Randomization.Next(0, 1);
                        if (i == 0)
                        {
                            i = Randomization.Next(0, 4);
                            if (CanMove(i) == -1)
                            {
                                //check if NPC is snared or stunned
                                foreach (var status in CachedStatuses)
                                {
                                    if (status.Type == StatusTypes.Stun ||
                                        status.Type == StatusTypes.Snare ||
                                        status.Type == StatusTypes.Sleep)
                                    {
                                        return;
                                    }
                                }

                                Move((byte)i, null);
                            }
                        }

                        LastRandomMove = Timing.Global.Milliseconds + Randomization.Next(1000, 3000);

                        if (fleeing)
                        {
                            LastRandomMove = Timing.Global.Milliseconds + (long) GetMovementTime();
                        }
                    }

                    //If we switched maps, lets update the maps
                    if (curMapLink != MapId)
                    {
                        if (curMapLink == Guid.Empty)
                        {
                            if (MapController.TryGetInstanceFromMap(curMapLink, MapInstanceId, out var instance))
                            {
                                instance.RemoveEntity(this);
                            }
                        }

                        if (MapId != Guid.Empty)
                        {
                            if (MapController.TryGetInstanceFromMap(MapId, MapInstanceId, out var instance))
                            {
                                instance.AddEntity(this);
                            }
                        }
                    }
                }
            }
            finally
            {
                if (lockObtained)
                {
                    Monitor.Exit(EntityLock);
                }
            }
        }

        /// <summary>
        /// Resets the NPCs position to be "pulled" from
        /// </summary>
        /// <param name="targetMap">For referencing the map that the enemy's target WAS on before a reset.</param>
        private void ResetAggroCenter(out Guid targetMap)
        {
            targetMap = Guid.Empty;

            // Reset our aggro center so we can get "pulled" again.
            AggroCenterMap = null;
            AggroCenterX = 0;
            AggroCenterY = 0;
            AggroCenterZ = 0;
            mPathFinder?.SetTarget(null);
            mResetting = false;
        }

        private bool CheckForResetLocation(bool forceDistance = false)
        {
            // Check if we've moved out of our range we're allowed to move from after being "aggro'd" by something.
            // If so, remove target and move back to the origin point.
            if (Options.Npc.AllowResetRadius && AggroCenterMap != null && (GetDistanceTo(AggroCenterMap, AggroCenterX, AggroCenterY) > Math.Max(Options.Npc.ResetRadius, Base.ResetRadius) || forceDistance))
            {
                ResetNpc(Options.Npc.ResetVitalsAndStatusses);

                mResetCounter = 0;
                mResetDistance = 0;

                // Try and move back to where we came from before we started chasing something.
                mResetting = true;
                mPathFinder.SetTarget(new PathfinderTarget(AggroCenterMap.Id, AggroCenterX, AggroCenterY, AggroCenterZ));
                return true;
            }
            return false;
        }

        private void ResetNpc(bool resetVitals = true, bool clearLocation = false)
        {
            // Remove our target.
            RemoveTarget();

            DamageMap.Clear();
            LootMap.Clear();
            LootMapCache = Array.Empty<Guid>();

            if (clearLocation)
            {
                mPathFinder.SetTarget(null);
                AggroCenterMap = null;
                AggroCenterX = 0;
                AggroCenterY = 0;
                AggroCenterZ = 0;
            }
            
            // Reset our vitals and statusses when configured.
            if (resetVitals)
            {
                Statuses.Clear();
                CachedStatuses = Statuses.Values.ToArray();
                DoT.Clear();
                CachedDots = DoT.Values.ToArray();
                for (var v = 0; v < (int)Vitals.VitalCount; v++)
                {
                    RestoreVital((Vitals)v);
                }
            }
        }

        public override void NotifySwarm(Entity attacker)
        {
            if (MapController.TryGetInstanceFromMap(MapId, MapInstanceId, out var instance))
            {
                foreach (var en in instance.GetEntities(true))
                {
                    if (en.GetType() == typeof(Npc))
                    {
                        var npc = (Npc)en;
                        if (npc.Target == null & npc.Base.Swarm && IsAllyOf(npc))
                        {
                            if (npc.InRangeOf(attacker, npc.Base.SightRange))
                            {
                                npc.AssignTarget(attacker);
                            }
                        }
                    }
                }
            }
        }

        public bool CanPlayerAttack(Player en)
        {
            //Check to see if the npc is a friend/protector...
            if (IsAllyOf(en))
            {
                return false;
            }

            //If not then check and see if player meets the conditions to attack the npc...
            if (Base.PlayerCanAttackConditions.Lists.Count == 0 ||
                Conditions.MeetsConditionLists(Base.PlayerCanAttackConditions, en, null))
            {
                return true;
            }

            return false;
        }

        public override bool IsAllyOf(Entity otherEntity)
        {
            switch (otherEntity)
            {
                case Npc otherNpc:
                    return Base == otherNpc.Base || !CanNpcCombat(otherEntity);
                case Player otherPlayer:
                    var conditionLists = Base.PlayerFriendConditions;
                    if ((conditionLists?.Count ?? 0) == 0)
                    {
                        return false;
                    }

                    return Conditions.MeetsConditionLists(conditionLists, otherPlayer, null);
                default:
                    return base.IsAllyOf(otherEntity);
            }
        }

        public bool ShouldAttackPlayerOnSight(Player player)
        {
            if (player == null)
            {
                return false;
            }

            if (IsAllyOf(player))
            {
                return false;
            }

            var aggroConditions = Base.AttackOnSightConditions.Lists.Count > 0 && Conditions.MeetsConditionLists(Base.AttackOnSightConditions, player, null);

            if (Base.Aggressive)
            {
                if (aggroConditions)
                {
                    return false;
                }

                return IsOverworldDefaultAggroToward(player) || IsDungeonAggroToward(player);
            }
            else
            {
                if (aggroConditions)
                {
                    return true;
                }
            }

            return false;
        }

        public void TryFindNewTarget(long timeMs, Guid avoidId = new Guid(), bool ignoreTimer = false, Entity attackedBy = null)
        {
            if (!ignoreTimer && FindTargetWaitTime > timeMs)
            {
                return;
            }

            // Are we resetting? If so, do not allow for a new target.
            var pathTarget = mPathFinder?.GetTarget();
            if (AggroCenterMap != null && pathTarget != null &&
                pathTarget.TargetMapId == AggroCenterMap.Id && pathTarget.TargetX == AggroCenterX && pathTarget.TargetY == AggroCenterY)
            {
                if (!Options.Instance.NpcOpts.AllowEngagingWhileResetting || attackedBy == null || attackedBy.GetDistanceTo(AggroCenterMap, AggroCenterX, AggroCenterY) > Math.Max(Options.Instance.NpcOpts.ResetRadius, Base.ResetRadius))
                {
                    return;
                }
                else
                {
                    //We're resetting and just got attacked, and we allow reengagement.. let's stop resetting and fight!
                    mPathFinder?.SetTarget(null);
                    mResetting = false;
                    AssignTarget(attackedBy);
                    return;
                }
            }

            var possibleTargets = new List<Entity>();
            var closestRange = Range + 1; //If the range is out of range we didn't find anything.
            var closestIndex = -1;
            var highestDmgIndex = -1;
           
            if (DamageMap.Count > 0)
            {
                // Go through all of our potential targets in order of damage done as instructed and select the first matching one.
                long highestDamage = 0;
                foreach (var en in DamageMap.ToArray())
                {
                    // Are we supposed to avoid this one?
                    if (en.Key.Id == avoidId)
                    {
                        continue;
                    }
                    
                    // Is this entry dead?, if so skip it.
                    if (en.Key.IsDead())
                    {
                        continue;   
                    }

                    // Is this entity on our instance anymore? If not skip it, but don't remove it in case they come back and need item drop determined
                    if (en.Key.MapInstanceId != MapInstanceId)
                    {
                        continue;
                    }

                    // Are we at a valid distance? (9999 means not on this map or somehow null!)
                    if (GetDistanceTo(en.Key) != 9999)
                    {
                        possibleTargets.Add(en.Key);

                        // Do we have the highest damage?
                        if (en.Value > highestDamage)
                        {
                            highestDmgIndex = possibleTargets.Count - 1;
                            highestDamage = en.Value;
                        }    
                        
                    }
                }
            }

            // Scan for nearby targets
            foreach (var instance in MapController.GetSurroundingMapInstances(MapId, MapInstanceId, true))
            {
                foreach (var entity in instance.GetCachedEntities())
                {
                    if (entity is Player pl && pl.PlayerDead)
                    {
                        continue;
                    }
                    if (entity != null && !entity.IsDead() && entity != this && entity.Id != avoidId)
                    {
                        //TODO Check if NPC is allowed to attack player with new conditions
                        if (entity.GetType() == typeof(Player))
                        {
                            // Are we aggressive towards this player or have they hit us?
                            if (ShouldAttackPlayerOnSight((Player)entity) || (DamageMap.ContainsKey(entity) && entity.MapInstanceId == MapInstanceId))
                            {
                                var dist = GetDistanceTo(entity);
                                if (dist <= Range && dist < closestRange)
                                {
                                    possibleTargets.Add(entity);
                                    closestIndex = possibleTargets.Count - 1;
                                    closestRange = dist;
                                }
                            }
                        }
                        else if (entity.GetType() == typeof(Npc))
                        {
                            if (Base.Aggressive && Base.AggroList.Contains(((Npc)entity).Base.Id))
                            {
                                var dist = GetDistanceTo(entity);
                                if (dist <= Range && dist < closestRange)
                                {
                                    possibleTargets.Add(entity);
                                    closestIndex = possibleTargets.Count - 1;
                                    closestRange = dist;
                                }
                            }
                        }
                    }
                }
            }

            // Assign our target if we've found one!
            if (Base.FocusHighestDamageDealer && highestDmgIndex != -1)
            {
                // We're focussed on whoever has the most threat! o7
                AssignTarget(possibleTargets[highestDmgIndex]);
            }
            else if (Target != null && possibleTargets.Count > 0)
            {
                // Time to randomize who we target.. Since we don't actively care who we attack!
                // 10% chance to just go for someone else.
                if (Randomization.Next(1, 101) > 90)
                {
                    if (possibleTargets.Count > 1)
                    {
                        var target = Randomization.Next(0, possibleTargets.Count - 1);
                        AssignTarget(possibleTargets[target]);
                    }
                    else
                    {
                        AssignTarget(possibleTargets[0]);
                    }
                }
            }
            else if (Target == null && Base.Aggressive && closestIndex != -1)
            {
                // Aggressively attack closest person!
                AssignTarget(possibleTargets[closestIndex]);
            }
            else if (possibleTargets.Count > 0)
            {
                // Not aggressive but no target, so just try and attack SOMEONE on the damage table!
                if (possibleTargets.Count > 1)
                {
                    var target = Randomization.Next(0, possibleTargets.Count - 1);
                    AssignTarget(possibleTargets[target]);
                }
                else
                {
                    AssignTarget(possibleTargets[0]);
                }
            }
            else
            {
                // ??? What the frick is going on here?
                // We can't find a valid target somehow, keep it up a few times and reset if this keeps failing!
                mTargetFailCounter += 1;
                if (mTargetFailCounter > mTargetFailMax)
                {
                    CheckForResetLocation(true);
                }
            }

            FindTargetWaitTime = timeMs + FindTargetDelay;
        }

        public override void ProcessRegen()
        {
            if (Base == null)
            {
                return;
            }

            foreach (Vitals vital in Enum.GetValues(typeof(Vitals)))
            {
                if (vital >= Vitals.VitalCount)
                {
                    continue;
                }

                var vitalId = (int) vital;
                var vitalValue = GetVital(vital);
                var maxVitalValue = GetMaxVital(vital);
                if (vitalValue >= maxVitalValue)
                {
                    continue;
                }

                var vitalRegenRate = Base.VitalRegen[vitalId] / 100f;
                var regenValue = (int) Math.Max(1, maxVitalValue * vitalRegenRate) *
                                 Math.Abs(Math.Sign(vitalRegenRate));

                AddVital(vital, regenValue);
            }
        }

        public override void Warp(
            Guid newMapId,
            float newX,
            float newY,
            byte newDir,
            bool adminWarp = false,
            byte zOverride = 0,
            bool mapSave = false,
            bool fromWarpEvent = false,
            MapInstanceType mapInstanceType = MapInstanceType.NoChange,
            bool fromLogin = false,
            bool forceInstanceChange = false
        )
        {
            if (!MapController.TryGetInstanceFromMap(newMapId, MapInstanceId, out var map))
            {
                return;
            }

            X = (int)newX;
            Y = (int)newY;
            Z = zOverride;
            Dir = newDir;
            if (newMapId != MapId)
            {
                if (MapController.TryGetInstanceFromMap(MapId, MapInstanceId, out var oldMap))
                {
                    oldMap.RemoveEntity(this);
                }

                PacketSender.SendEntityLeave(this);
                MapId = newMapId;
                PacketSender.SendEntityDataToProximity(this);
                PacketSender.SendEntityPositionToAll(this);
            }
            else
            {
                PacketSender.SendEntityPositionToAll(this);
                PacketSender.SendEntityStats(this);
            }
        }

        public int GetAggression(Player player)
        {
            //Determines the aggression level of this npc to send to the player
            if (this.Target != null)
            {
                return -1;
            }
            else
            {
                //Guard = 3
                //Will attack on sight = 1
                //Will attack if attacked = 0
                //Can't attack nor can attack = 2
                var ally = IsAllyOf(player);
                var attackOnSight = ShouldAttackPlayerOnSight(player);
                var canPlayerAttack = CanPlayerAttack(player);
                if (ally && !canPlayerAttack)
                {
                    return 3;
                }

                if (attackOnSight)
                {
                    return 1;
                }

                if (!ally && !attackOnSight && canPlayerAttack)
                {
                    return 0;
                }

                if (!ally && !attackOnSight && !canPlayerAttack)
                {
                    return 2;
                }
            }

            return 2;
        }

        public override EntityPacket EntityPacket(EntityPacket packet = null, Player forPlayer = null)
        {
            if (packet == null)
            {
                packet = new NpcEntityPacket();
            }

            packet = base.EntityPacket(packet, forPlayer);

            var pkt = (NpcEntityPacket) packet;
            pkt.Aggression = GetAggression(forPlayer);
            pkt.NpcId = Base.Id;

            return pkt;
        }

    }

    public partial class Npc : AttackingEntity
    {
        public override void DropItems(Entity killer, bool sendUpdate = true)
        {
            if (!(killer is Player))
            {
                return;
            }
            var playerKiller = killer as Player;
            
            // Check to see if we hit the secondary or tertiary tables
            var luck = playerKiller?.GetLuckModifier();

            Guid lootOwner = Guid.Empty;
            // Set owner to player that killed this, if there is any.
            if (playerKiller != null)
            {
                // Yes, so set the owner to the player that killed it.
                lootOwner = playerKiller.Id;
            }

            var rolledItems = new List<Item>();
            var baseDropTable = LootTableServerHelpers.GenerateDropTable(Base.Drops, playerKiller);
            rolledItems.Add(LootTableServerHelpers.GetItemFromTable(baseDropTable));

            // Check for secondary/tertiary tables
            if (Randomization.Next(1, 101) < Base.SecondaryChance * luck)
            {
                var secondaryDropTable = LootTableServerHelpers.GenerateDropTable(Base.SecondaryDrops, playerKiller);
                rolledItems.Add(LootTableServerHelpers.GetItemFromTable(secondaryDropTable));
            }
            if (Randomization.Next(1, 101) < Base.TertiaryChance * luck)
            {
                var tertiaryDropTable = LootTableServerHelpers.GenerateDropTable(Base.TertiaryDrops, playerKiller);
                rolledItems.Add(LootTableServerHelpers.GetItemFromTable(tertiaryDropTable));
            }

            LootTableServerHelpers.SpawnItemsOnMap(rolledItems, MapId, MapInstanceId, X, Y, lootOwner, sendUpdate);
        }

        public bool IsOverworldDefaultAggroToward(Player player)
        {
            if (!PlayerThreatLevels.TryGetValue(player.Id, out var threatLevel))
            {
                threatLevel = SetThreatLevelFor(player);
            }

            return Map?.ZoneType != MapZones.Safe || threatLevel < ThreatLevel.Wimpy;
        }

        public ThreatLevel SetThreatLevelFor(Player player)
        {
            PlayerThreatLevels[player.Id] = ThreatLevelUtilities.DetermineNpcThreatLevel(player.MaxVitals,
                player.StatVals,
                Base.MaxVital,
                Base.Stats,
                player.GetMeleeAttackTypes(),
                Base.AttackTypes,
                player.GetRawAttackSpeed(),
                Base.AttackSpeedValue);

            return PlayerThreatLevels[player.Id];
        }

        public bool IsDungeonAggroToward(Player player)
        {
            if (player.MapInstanceId == MapInstanceId && player.InstanceType != MapInstanceType.Overworld
                && Map.TryGetInstance(MapInstanceId, out var instance))
            {
                var dungeonVar = instance.GetInstanceVariable(Guid.Parse(Options.Combat.DungeonInstanceVarGuid ?? Guid.Empty.ToString()));
                if (dungeonVar == null || dungeonVar.Type != VariableDataTypes.Boolean)
                {
                    return false;
                }
                return dungeonVar.Boolean;
            }
            return false;
        }

        /// <summary>
        /// Adds an attacking entity to this NPC's damage map
        /// </summary>
        /// <param name="attacker">The attacking entity</param>
        /// <param name="newDamage">The amount of damage to add</param>
        public void AddToDamageAndLootMaps(Entity attacker, int newDamage)
        {
            var dmgMap = DamageMap;
            dmgMap.TryGetValue(attacker, out var damage);
            dmgMap[attacker] = damage + newDamage;

            LootMap.TryAdd(Id, true);
            LootMapCache = LootMap.Keys.ToArray();
            TryFindNewTarget(Timing.Global.Milliseconds, default, false, attacker);
        }
    }
}
