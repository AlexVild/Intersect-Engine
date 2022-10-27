﻿using Intersect.Enums;
using Intersect.GameObjects;
using Intersect.GameObjects.Events;
using Intersect.Server.Database;
using Intersect.Server.Entities.Combat;
using Intersect.Server.Entities.Events;
using Intersect.Server.General;
using Intersect.Server.Localization;
using Intersect.Server.Maps;
using Intersect.Server.Networking;
using Intersect.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace Intersect.Server.Entities
{
    public abstract partial class AttackingEntity : Entity
    {
        protected bool IsUnableToCastSpells => CachedStatuses.Any(PredicateUnableToCastSpells);

        /// <summary>
        /// Whether an entity meets casting requirements (ammo & conditions) of some spell
        /// </summary>
        /// <param name="spell">The spell to cast</param>
        /// <returns>True if the entity meets the requirements</returns>
        protected abstract bool EntityMeetsCastingRequirements(SpellBase spell);

        /// <summary>
        /// Whether an entity has the materials to cast a spell
        /// </summary>
        /// <param name="spell">The spell to cast</param>
        /// <returns>True if casting materials met</returns>
        protected abstract bool EntityHasCastingMaterials(SpellBase spell);

        /// <summary>
        /// Will check to see if we have appropriate casting materials and, if we do, will try to use them
        /// </summary>
        /// <param name="spell">The spell to cast</param>
        /// <returns>True if we successfully use the materials</returns>
        protected abstract bool TryConsumeCastingMaterials(SpellBase spell);

        /// <summary>
        /// Whether we meet the spell vital requirements
        /// </summary>
        /// <param name="spell">The spell to cast</param>
        /// <returns>True if we meet the HP/MP requirements for the spell</returns>
        public abstract bool MeetsSpellVitalReqs(SpellBase spell);

        /// <summary>
        /// An optional method that allows one to define a way to make further modifications to a spell's
        /// damage potential within the <see cref="DealDamageTo(Entity, List{AttackTypes}, int, double, ItemBase, out int)"/> method
        /// </summary>
        /// <param name="scaling"></param>
        /// <param name="attackTypes"></param>
        /// <param name="critChance"></param>
        /// <param name="critMultiplier"></param>
        protected abstract void PopulateExtraSpellDamage(ref int scaling,
            ref List<AttackTypes> attackTypes,
            ref int critChance,
            ref double critMultiplier);

        /// <summary>
        /// Whether this entity can cast some spell on some target
        /// </summary>
        /// <param name="spell">The spell to cast</param>
        /// <param name="target">The target to cast on</param>
        /// <returns>True if we can cast the spell</returns>
        public bool CanCastSpell(SpellBase spell, Entity target)
        {
            if (!MeetsSpellVitalReqs(spell))
            {
                // Not enough vitals!
                return false;
            }

            if (spell.Combat != null &&
                SpellTypeHelpers.SpellTypeRequiresTarget(spell.Combat.TargetType) &&
                !CanAttack(target, spell))
            {
                // Spell requires valid target!
                return false;
            }

            if (SpellTypeHelpers.IsMovementSpell(spell.SpellType) && IsImmobile)
            {
                // Spell requires mobility!
                return false;
            }

            if (!EntityMeetsCastingRequirements(spell))
            {
                // The entity doesn't have extraneous requirements met!
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether we can start a cast and set the entity as casting
        /// </summary>
        /// <param name="now">The timestamp we began casting at</param>
        /// <param name="spellSlot">The slot we're casting from, or -1 if not from a slot</param>
        /// <param name="target">The optional target we're casting to</param>
        /// <returns></returns>
        public bool CanStartCast(long now, int spellSlot, Entity target = null)
        {
            // Invalid params
            if (spellSlot < 0 || spellSlot >= Spells.Count)
            {
                return false;
            }

            var spellId = Spells[spellSlot].SpellId;
            Target = target;
            var spell = SpellBase.Get(spellId);

            if (spell == null)
            {
                // No spell!
                return false;
            }

            if (CastTime != 0)
            {
                // Currently casting!
                return false;
            }

            if (SpellCooldowns.ContainsKey(spellId) && SpellCooldowns[spellId] >= Timing.Global.MillisecondsUtc)
            {
                // On cooldown!
                return false;
            }

            if (!CanCastSpell(spell, target))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Starts a cast - sets the casting timer for some entity.
        /// This is a convenience override for <see cref="StartCast(SpellBase, Entity, bool)"/>
        /// </summary>
        /// <param name="spellSlot">The slot to cast from</param>
        /// <param name="target">The target entity</param>
        protected void StartCast(int spellSlot, Entity target)
        {
            if (spellSlot < 0 && spellSlot >= Spells.Count)
            {
                return;
            }
            SpellCastSlot = spellSlot;
            var spell = SpellBase.Get(Spells[spellSlot].SpellId);

            StartCast(spell, target, spell.CastDuration <= 0);
        }

        /// <summary>
        /// Starts a cast - sets the casting timer for some entity.
        /// </summary>
        /// <param name="spell">The spell we're casting</param>
        /// <param name="target">The target we're casting to</param>
        /// <param name="instant">Whether the spell should instantly cast, and not set casting timers</param>
        public void StartCast(SpellBase spell, Entity target, bool instant = false)
        {
            if (spell == null)
            {
                return;
            }

            CastTime = Timing.Global.Milliseconds + spell.CastDuration;
            Target = target;
            Unstealth();

            if (spell.CastAnimationId != Guid.Empty)
            {
                PacketSender.SendAnimationToProximity(
                    spell.CastAnimationId, 1, base.Id, MapId, 0, 0, (sbyte)Dir, MapInstanceId
                );
            }

            if (instant)
            {
                UseSpell(spell, SpellCastSlot);
                CastingFinished();
            }
            else
            {
                PacketSender.SendEntityCastTime(this, spell.Id);
            }
        }

        /// <summary>
        /// Cancels casting and informs the client of such
        /// </summary>
        public virtual void CancelCast()
        {
            CastTime = 0;
            CastTarget = null;
            SpellCastSlot = -1;
            PacketSender.SendEntityCancelCast(this);
        }

        /// <summary>
        /// Resets casting variables to defaults
        /// </summary>
        public virtual void CastingFinished()
        {
            CastTime = 0;
            CastTarget = null;
            SpellCastSlot = -1;
        }

        /// <summary>
        /// Ensures that the spell we're trying to cast, at time of casting, can still be cast.
        /// Also consumes items if necessary.
        /// </summary>
        /// <param name="spell">The spell we're about to cast</param>
        /// <param name="target">The target we're casting to</param>
        /// <param name="ignoreVitals">Whether we want to ignore vital requirements for this cast</param>
        /// <returns>True if the cast is valid and is ready to occur</returns>
        private bool ValidateCast(SpellBase spell, Entity target, bool ignoreVitals)
        {
            if (spell == null)
            {
                return false;
            }

            if (!CanAttack(target, spell))
            {
                return false;
            }

            if (!TryConsumeCastingMaterials(spell))
            {
                return false;
            }

            if (!ignoreVitals)
            {
                if (!MeetsSpellVitalReqs(spell))
                {
                    return false;
                }
                HandleSpellVitalUpdate(spell);
            }

            return true;
        }

        /// <summary>
        /// Updates a caster's vitals as needed
        /// </summary>
        /// <param name="spell">The spell being cast</param>
        private void HandleSpellVitalUpdate(SpellBase spell)
        {
            if (spell.VitalCost[(int)Vitals.Mana] > 0)
            {
                SubVital(Vitals.Mana, spell.VitalCost[(int)Vitals.Mana]);
            }
            else
            {
                AddVital(Vitals.Mana, -spell.VitalCost[(int)Vitals.Mana]);
            }

            if (spell.VitalCost[(int)Vitals.Health] > 0)
            {
                SubVital(Vitals.Health, spell.VitalCost[(int)Vitals.Health]);
            }
            else
            {
                AddVital(Vitals.Health, -spell.VitalCost[(int)Vitals.Health]);
            }
        }

        /// <summary>
        /// Applies any buffs a spell contains to some target entity
        /// </summary>
        /// <param name="spell">The spell being cast</param>
        /// <param name="target">The target being cast to</param>
        private void ApplySpellBuffsTo(SpellBase spell, Entity target)
        {
            if (spell.Combat?.Duration <= 0)
            {
                return;
            }

            var statBuffTime = -1;
            var expireTime = Timing.Global.Milliseconds + spell.Combat.Duration;
            for (var i = 0; i < (int)Stats.StatCount; i++)
            {
                if (spell.Combat.StatDiff[i] == 0 && spell.Combat.PercentageStatDiff[i] == 0)
                {
                    continue;
                }

                var buff = new Buff(spell, spell.Combat.StatDiff[i], spell.Combat.PercentageStatDiff[i], expireTime);
                target.Stat[i].AddBuff(buff);
                statBuffTime = spell.Combat.Duration;
            }

            if (spell.Combat.Effect > 0) //Handle status effects
            {
                // If the entity is immune to some status, then just inform the client of such
                if (target.IsImmuneTo(StatusToImmunity(spell.Combat.Effect)))
                {
                    PacketSender.SendActionMsg(target, Strings.Combat.immunetoeffect, CustomColors.Combat.Status);
                }
                else
                {
                    // Else, apply the status
                    _ = new Status(
                        target, this, spell, spell.Combat.Effect, spell.Combat.Duration,
                        spell.Combat.TransformSprite
                    );

                    PacketSender.SendActionMsg(
                        target, Strings.Combat.status[(int)spell.Combat.Effect], CustomColors.Combat.Status
                    );
                }
            }
        }

        /// <summary>
        /// Will apply spell buffs, DoTs, and damage if we can
        /// </summary>
        /// <param name="target">The target of the spell</param>
        /// <param name="spell">The spell that's being used</param>
        /// <param name="attackAnimDir">The direction of animation</param>
        /// <param name="projectile">A projectile attacked to the spell, if there is one</param>
        public void SpellAttack(Entity target, SpellBase spell, sbyte attackAnimDir, Projectile projectile)
        {
            if ((spell.Combat?.TargetType == SpellTargetTypes.AoE ||
                spell.Combat?.TargetType == SpellTargetTypes.Single) &&
                IsInvalidTauntTarget(target))
            {
                return;
            }

            ApplySpellBuffsTo(spell, target);
            Combat.DoT.AddSpellDoTsTo(spell, this, target);

            // Determine if we want to apply effects to otherwise invulnerable NPCs
            if (this is Player pl && target is Npc np && !pl.CanAttackNpc(np))
            {
                if (spell.Combat.IsDamaging)
                {
                    return;
                }
                else if (!Options.Instance.CombatOpts.InvulnerableNpcsAffectedByNonDamaging)
                {
                    return;
                }
            }

            var scaling = spell.Combat.Scaling;
            List<AttackTypes> attackTypes = new List<AttackTypes>(spell.Combat.DamageTypes);
            var critChance = spell.Combat.CritChance;
            var critMultiplier = spell.Combat.CritMultiplier;

            PopulateExtraSpellDamage(ref scaling, ref attackTypes, ref critChance, ref critMultiplier);

            int damage;
            if (this is Player player && spell.WeaponSpell)
            {
                if (!TryDealDamageTo(target, attackTypes, scaling, critMultiplier, player.CastingWeapon, spell, out damage))
                {
                    return;
                }
            }
            else if (!TryDealDamageTo(target, attackTypes, scaling, critMultiplier, null, spell, out damage))
            {
                return;
            }
        }

        /// <summary>
        /// Validates the final casting of and handles the actions of a spell
        /// </summary>
        /// <param name="spell">The spell to use</param>
        /// <param name="spellSlot">The slot it is being used from, or -1 if not from a spell slot</param>
        /// <param name="ignoreVitals">Whether we want to ignore vital costs</param>
        /// <param name="prayerSpell">Whether this spell is from a prayer</param>
        /// <param name="prayerSpellDir">What direction the prayer was</param>
        /// <param name="prayerTarget">The prayer's target</param>
        public virtual void UseSpell(SpellBase spell, int spellSlot, bool ignoreVitals = false, bool prayerSpell = false, byte prayerSpellDir = 0, Entity prayerTarget = null)
        {
            CastTarget = Target;
            // We're actually doing the spell now - use our mats and if we fail, end
            if (!ValidateCast(spell, CastTarget, ignoreVitals))
            {
                return;
            }

            switch (spell.SpellType)
            {
                case SpellTypes.CombatSpell:
                case SpellTypes.Event:

                    switch (spell.Combat.TargetType)
                    {
                        case SpellTargetTypes.Self:
                            if (spell.HitAnimationId != Guid.Empty && spell.Combat.Effect != StatusTypes.OnHit)
                            {
                                SendSpellHitAnimation(spell, this);
                            }
                            SpellAttack(this, spell, (sbyte)Dir, null);

                            break;
                        case SpellTargetTypes.Single:
                            if (spell.Combat.HitRadius > 0) //Single target spells with AoE hit radius'
                            {
                                HandleAoESpell(
                                    spell.Id, spell.Combat.HitRadius, CastTarget.MapId, CastTarget.X, CastTarget.Y,
                                    null, false, false, true
                                );
                            }
                            else
                            {
                                SpellAttack(CastTarget, spell, (sbyte)Dir, null);
                                SendSpellHitAnimation(spell, CastTarget);
                            }

                            break;
                        case SpellTargetTypes.AoE:
                            HandleAoESpell(spell.Id, spell.Combat.HitRadius, MapId, X, Y, null);
                            break;
                        case SpellTargetTypes.Projectile:
                            var projectileBase = spell.Combat.Projectile;
                            if (projectileBase != null)
                            {
                                if (this is Player player)
                                {
                                    PacketSender.SendProjectileCastDelayPacket(player, Options.Instance.CombatOpts.ProjectileSpellMovementDelay);
                                }

                                if (MapController.TryGetInstanceFromMap(MapId, MapInstanceId, out var mapInstance))
                                {
                                    if (prayerSpell && prayerTarget != null && prayerSpellDir >= 0)
                                    {
                                        mapInstance.SpawnMapProjectile(
                                                this, projectileBase, spell, null, prayerTarget.MapId, (byte)prayerTarget.X, (byte)prayerTarget.Y, (byte)prayerTarget.Z,
                                                prayerSpellDir, CastTarget
                                            );
                                    }
                                    else
                                    {
                                        mapInstance.SpawnMapProjectile(
                                            this, projectileBase, spell, null, MapId, (byte)X, (byte)Y, (byte)Z,
                                            (byte)Dir, CastTarget
                                        );
                                    }

                                    // Leveraging trap animation for projectile spawner animation
                                    if (spell.TrapAnimationId != Guid.Empty)
                                    {
                                        PacketSender.SendAnimationToProximity(spell.TrapAnimationId, -1, Guid.Empty, MapId, (byte)X, (byte)Y, (sbyte)Dir, MapInstanceId);
                                    }
                                }
                            }

                            break;
                        case SpellTargetTypes.OnHit:
                            if (spell.Combat.Effect == StatusTypes.OnHit)
                            {
                                new Status(
                                    this, this, spell, StatusTypes.OnHit, spell.Combat.OnHitDuration,
                                    spell.Combat.TransformSprite
                                );

                                PacketSender.SendActionMsg(
                                    this, Strings.Combat.status[(int)spell.Combat.Effect],
                                    CustomColors.Combat.Status
                                );
                                SendSpellHitAnimation(spell, this);
                            }

                            break;
                        case SpellTargetTypes.Trap:
                            if (MapController.TryGetInstanceFromMap(MapId, MapInstanceId, out var instance))
                            {
                                instance.SpawnTrap(this, spell, (byte)X, (byte)Y, (byte)Z);
                            }

                            break;
                        default:
                            break;
                    }

                    break;
                case SpellTypes.Warp:
                    if (this is Player)
                    {
                        SendSpellHitAnimation(spell, this);
                        Warp(
                            spell.Warp.MapId, spell.Warp.X, spell.Warp.Y,
                            spell.Warp.Dir - 1 == -1 ? (byte)this.Dir : (byte)(spell.Warp.Dir - 1)
                        );
                    }

                    break;
                case SpellTypes.WarpTo:
                    if (CastTarget != null)
                    {
                        HandleAoESpell(spell.Id, spell.Combat.CastRange, MapId, X, Y, CastTarget, false, false, true);
                    }
                    break;
                case SpellTypes.Dash:
                    PacketSender.SendActionMsg(this, Strings.Combat.dash, CustomColors.Combat.Dash);
                    _ = new Dash(
                        this, spell.Combat.CastRange, (byte)Dir, Convert.ToBoolean(spell.Dash.IgnoreMapBlocks),
                        Convert.ToBoolean(spell.Dash.IgnoreActiveResources),
                        Convert.ToBoolean(spell.Dash.IgnoreInactiveResources),
                        Convert.ToBoolean(spell.Dash.IgnoreZDimensionAttributes),
                        Convert.ToBoolean(spell.Dash.IgnoreEntites),
                        spell.Dash.Spell
                    );

                    break;
                default:
                    break;
            }

            if (spellSlot >= 0 && spellSlot < Options.MaxPlayerSkills)
            {
                UpdateSpellCooldown(spellSlot);
            }

            if (GetVital(Vitals.Health) <= 0) // if the spell has killed the entity
            {
                Die();
            }
        }

        /// <summary>
        /// Checks to see, during an entity's update loop, whether it's time to use some spell
        /// </summary>
        /// <param name="timeMs"></param>
        public override void CheckForSpellCast(long timeMs)
        {
            if (CastTime != 0 && CastTime < timeMs && SpellCastSlot < Spells.Count && SpellCastSlot >= 0)
            {
                var spell = SpellBase.Get(Spells[SpellCastSlot].SpellId);
                UseSpell(spell, SpellCastSlot);
                CastingFinished();
            }
        }

        /// <summary>
        /// Checks the current map params for surrounding entities and, if we can cast the spell on them,
        /// casts it
        /// </summary>
        public void HandleAoESpell(
            Guid spellId,
            int range,
            Guid startMapId,
            int startX,
            int startY,
            Entity spellTarget,
            bool ignoreEvasion = false,
            bool isProjectileTool = false,
            bool ignoreMissMessage = false
        )
        {
            var spellBase = SpellBase.Get(spellId);
            if (spellBase != null)
            {
                int entitiesHit = 0;
                var startMap = MapController.Get(startMapId);
                foreach (var instance in MapController.GetSurroundingMapInstances(startMapId, MapInstanceId, true))
                {
                    foreach (var entity in instance.GetCachedEntities())
                    {
                        if (entity == null || (!(entity is Player) && !(entity is Npc)))
                        {
                            continue;
                        }

                        if (spellTarget != null && spellTarget != entity)
                        {
                            continue;
                        }

                        if (entity.GetDistanceTo(startMap, startX, startY) > range)
                        {
                            continue;
                        }

                        if (entity.Id == Id)
                        {
                            continue;
                        }

                        //Check to handle a warp to spell
                        if (spellBase.SpellType == SpellTypes.WarpTo)
                        {
                            if (spellTarget != null)
                            {
                                //Spelltarget used to be Target. I don't know if this is correct or not.
                                int[] position = GetPositionNearTarget(spellTarget.MapId, spellTarget.X, spellTarget.Y, spellTarget.Dir);
                                Warp(spellTarget.MapId, (byte)position[0], (byte)position[1], (byte)Dir);
                                ChangeDir(DirToEnemy(spellTarget));
                            }
                        }

                        SpellAttack(entity, spellBase, (sbyte)Directions.Up, null); //Handle damage
                        SendSpellHitAnimation(spellBase, entity);
                        entitiesHit++;
                    }
                }
                if (!spellBase.Combat.Friendly && entitiesHit < 1 && !isProjectileTool && !ignoreMissMessage) // Will count yourself - which is FINE in the case of a friendly spell, otherwise ignore it
                {
                    if (this is Player)
                    {
                        PacketSender.SendChatMsg((Player)this, "There weren't any targets in your spell's AoE range.", ChatMessageType.Spells, CustomColors.General.GeneralWarning);
                    }
                    SendMissedAttackMessage(this, DamageType.True);
                }
            }
        }

        /// <summary>
        /// Sends a hit animation to some entity using a spell's properties
        /// </summary>
        /// <param name="spell"></param>
        /// <param name="target"></param>
        /// <param name="dirOverride"></param>
        public void SendSpellHitAnimation(SpellBase spell, Entity target, sbyte? dirOverride = null)
        {
            if (target == null)
            {
                return;
            }

            var dir = Dir;
            if (dirOverride.HasValue)
            {
                dir = dirOverride.Value;
            }

            PacketSender.SendAnimationToProximity(
                spell?.HitAnimationId ?? Guid.Empty, -1, Id, target.MapId, (byte)target.X, (byte)target.Y, (sbyte)dir, target.MapInstanceId
            );
        }

        /// <summary>
        /// Used to tell the entity how to update its spell cooldowns for some spell slot
        /// </summary>
        /// <param name="spellSlot">The slot under cooldown</param>
        protected abstract void UpdateSpellCooldown(int spellSlot);

        /// <summary>
        /// Used to determine if statuses are affecting our spellcasting abilities
        /// </summary>
        /// <param name="status"></param>
        /// <returns></returns>
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
    }
}
