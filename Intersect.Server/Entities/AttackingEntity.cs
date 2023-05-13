﻿using Intersect.Enums;
using Intersect.GameObjects;
using Intersect.GameObjects.Events;
using Intersect.Server.Entities.Combat;
using Intersect.Server.Entities.Events;
using Intersect.Server.Localization;
using Intersect.Server.Maps;
using Intersect.Server.Networking;
using Intersect.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json.Serialization;

namespace Intersect.Server.Entities
{
    public abstract partial class AttackingEntity : Entity
    {
        /// <summary>
        /// Contains a mapping of offensive status -> timestamp, so that we can have a staleness applied to
        /// negative status types
        /// </summary>
        [NotMapped, JsonIgnore]
        public Dictionary<StatusTypes, Queue<long>> EffectStaleness { get; set; }

        public AttackingEntity() : this(Guid.NewGuid(), Guid.Empty)
        {
            EffectStaleness = new Dictionary<StatusTypes, Queue<long>>();
            foreach (StatusTypes status in Enum.GetValues(typeof(StatusTypes)))
            {
                if (StatusHelpers.TenacityExcluded.Contains(status))
                {
                    continue;
                }

                EffectStaleness[status] = new Queue<long>();
            }
        }

        public void CCApplied(StatusTypes status)
        {
            if (StatusHelpers.TenacityExcluded.Contains(status))
            {
                return;
            }

            EffectStaleness[status].Enqueue(Timing.Global.Milliseconds + Options.Instance.CombatOpts.CCStalenessTimer);
        }

        public void UpdateStatusStaleness()
        {
            var now = Timing.Global.Milliseconds;
            foreach (var timeQueue in EffectStaleness.Values)
            {
                while (timeQueue.Count > 0 && timeQueue.Peek() < now)
                {
                    timeQueue.Dequeue();
                }
            }
        }

        public int GetStaleTenacityMod(StatusTypes status)
        {
            if (StatusHelpers.TenacityExcluded.Contains(status) || !EffectStaleness.ContainsKey(status))
            {
                return 0;
            }
            
            // -1 because we always allow at least 1 status without staleness applied
            var recentApplications = EffectStaleness[status].Count - 1;

            return MathHelper.Clamp(recentApplications * 16, 0, Options.Instance.CombatOpts.MaxStaleTenacityBonus);
        }

        public override void Update(long timeMs)
        {
            UpdateStatusStaleness();
            base.Update(timeMs);
        }

        //Initialization
        public AttackingEntity(Guid instanceId, Guid mapInstanceId) : base(instanceId, mapInstanceId)
        {
            AttackMissed += AttackingEntity_AttackMissed;
            DamageTaken += AttackingEntity_DamageTaken;
        }

        protected virtual void AttackingEntity_DamageTaken(Entity aggressor, int damage)
        {
            // blank
        }

        protected virtual void AttackingEntity_AttackMissed(Entity target)
        {
            // Blank
        }

        protected bool IsStunnedOrSleeping => CachedStatuses.Any(PredicateStunnedOrSleeping);

        protected bool IsImmobile => CachedStatuses.Any(PredicateCantMove);

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

        private static bool PredicateCantMove(Status status)
        {
            switch (status?.Type)
            {
                case StatusTypes.Sleep:
                case StatusTypes.Stun:
                case StatusTypes.Snare:
                    return true;
                default:
                    return false;
            }
        }

        public static bool PredicateCleansed(Status status)
        {
            switch (status?.Type)
            {
                case StatusTypes.Cleanse:
                    return true;
                default:
                    return false;
            }
        }

        public abstract void MeleeAttack(Entity enemy, bool ignoreEvasion);

        public abstract void SendAttackAnimation(Entity enemy);

        public virtual void ProjectileAttack(Entity enemy,
            Projectile projectile,
            SpellBase parentSpell,
            ItemBase parentWeapon,
            bool ignoreEvasion,
            byte projectileDir)
        {
            enemy?.ReactToCombat(this);
            if (projectile == null || projectile.Base == null)
            {
                return;
            }
            if (!CanRangeTarget(enemy))
            {
                return;
            }

            if (!ignoreEvasion && CombatUtilities.AttackMisses(Accuracy, enemy?.Evasion ?? 0, parentSpell != null && this is Player))
            {
                AttackMissed.Invoke(enemy);
                SendMissedAttackMessage(enemy, DamageType.Physical);
                enemy?.ReactToCombat(this);
                return;
            }

            var descriptor = projectile.Base;
            // Handle the _projectile's_ spell
            projectile.HandleProjectileSpell(enemy, false);

            var willDamage = false;
            if (parentSpell != null)
            {
                if ((parentSpell.Combat.Friendly && enemy.IsAllyOf(this)) ||
                (!parentSpell.Combat.Friendly && !enemy.IsAllyOf(this)))
                {
                    willDamage = true;
                }
            }

            // Handle the spell cast from the parent
            if (parentSpell != null)
            {
                if (willDamage)
                {
                    SpellAttack(enemy, parentSpell, (sbyte)projectile.Dir, projectile, true);
                }
            }
            // Otherwise, handle the weapon
            else if (parentWeapon != null)
            {
                if (enemy.IsAllyOf(this) || !TryDealDamageTo(enemy, parentWeapon.AttackTypes, 100, 1.0, parentWeapon, null, true, GetDistanceTo(enemy), out int weaponDamage))
                {
                    return;
                }
                if (TryLifesteal(weaponDamage, enemy, out var healthRecovered))
                {
                    PacketSender.SendCombatNumber(CombatNumberType.HealHealth, this, (int)healthRecovered);
                }
                willDamage = true;
            }

            var animation = parentWeapon?.AttackAnimationId ?? parentSpell?.HitAnimationId ?? Guid.Empty;
            // If we're faking a melee attack as a player, we already played the attacking animation, so don't send anything now
            if (this is Player && projectile.Base.FakeMelee)
            {
                animation = Guid.Empty;
            }

            if (willDamage)
            {
                var targetType = (enemy.IsDead() || enemy.IsDisposed) ? -1 : 1;
                PacketSender.SendAnimationToProximity(
                    animation, targetType, enemy.Id, enemy.MapId, (byte)enemy.X, (byte)enemy.Y, (sbyte)projectileDir, MapInstanceId, true
                );
            }

            if (descriptor.Knockback > 0 && projectileDir < 4 && !enemy.IsImmuneTo(Immunities.Knockback) && !StatusActive(StatusTypes.Steady))
            {
                if (!(enemy is AttackingEntity) || enemy.IsDead() || enemy.IsDisposed)
                {
                    return;
                }

                if (parentSpell != null && parentSpell.Combat.Friendly && enemy.IsAllyOf(this))
                {
                    return;
                }

                ((AttackingEntity)enemy).Knockback(projectileDir, descriptor.Knockback);
            }

            CheckForOnhitAttack(enemy);
        }

        /// <summary>
        /// Determines whether or not an entity should even be allowed to begin a melee action
        /// </summary>
        /// <returns>True if the player can initiate a melee attack</returns>
        public virtual bool MeleeAvailable()
        {
            if (AttackTimer > Timing.Global.Milliseconds)
            {
                return false;
            }

            if (IsCasting)
            {
                return false;
            }

            return true;
        }

        public virtual bool CanRangeTarget(Entity target)
        {
            if (target == null || target is EventPageInstance)
            {
                return false;
            }

            if (IsInvalidTauntTarget(target))
            {
                PacketSender.SendActionMsg(this, Strings.Combat.miss, CustomColors.Combat.Missed);

                return false;
            }

            if (StatusActive(StatusTypes.Blind))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines whether or not an entity can do a melee attack to another entity
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public virtual bool CanMeleeTarget(Entity target)
        {
            if (target == null || target is EventPageInstance)
            {
                return false;
            }

            if (IsInvalidTauntTarget(target))
            {
                PacketSender.SendActionMsg(this, Strings.Combat.miss, CustomColors.Combat.Missed);

                return false;
            }

            // "attempt" an attack if status active that prevents it, but miss 100% of the time
            if (TargetInMeleeRange(target) && StatusPreventsAttack())
            {
                PacketSender.SendActionMsg(this, Strings.Combat.miss, CustomColors.Combat.Missed);
                PacketSender.SendEntityAttack(this, CalculateAttackTime());

                return false;
            }

            return true;
        }

        public void IncrementAttackTimer()
        {
            AttackTimer = Timing.Global.Milliseconds + CalculateAttackTime();
        }

        public virtual bool IsCriticalHit(int critChance)
        {
            if (StatusActive(StatusTypes.Accurate))
            {
                critChance *= Options.Instance.CombatOpts.AccurateCritChanceMultiplier;
            }

            if (Randomization.Next(1, 101) > critChance)
            {
                return false;
            }

            return true;
        }

        public void Knockback(byte dir, int amount)
        {
            _ = new Dash(this, amount, dir, false, false, false, false, stunMs: 250);
        }

        protected TileHelper GetMeleeAttackTile()
        {
            var attackingTile = new TileHelper(MapId, X, Y);
            switch (Dir)
            {
                case 0:
                    attackingTile.Translate(0, -1);

                    break;

                case 1:
                    attackingTile.Translate(0, 1);

                    break;

                case 2:
                    attackingTile.Translate(-1, 0);

                    break;

                case 3:
                    attackingTile.Translate(1, 0);

                    break;
            }

            return attackingTile;
        }

        public virtual bool TryDealManaDamageTo(Entity enemy,
            int dmg,
            int dmgScaling,
            double critMultiplier,
            out int damage)
        {
            damage = 0;
            if (enemy == null || !enemy.CanHaveVitalDamaged(Vitals.Mana))
            {
                return false;
            }

            damage = dmg;
            DealTrueDamageTo(enemy, dmgScaling, dmg, true, false);

            return true;
        }

        public virtual bool TryDealDamageTo(Entity enemy,
            List<AttackTypes> attackTypes,
            int dmgScaling,
            double critMultiplier,
            ItemBase weapon,
            SpellBase spell,
            bool ignoreEvasion,
            int range,
            out int damage)
        {
            damage = 0;
            if (enemy == null || !enemy.CanHaveVitalDamaged(Vitals.Health))
            {
                return false;
            }

            if (!ignoreEvasion && CombatUtilities.AttackMisses(Accuracy, enemy.Evasion, spell != null && this is Player))
            {
                SendMissedAttackMessage(enemy, DamageType.Physical);
                AttackMissed.Invoke(enemy);
                return false;
            }

            var manaDamage = 0;
            if (spell != null && spell.Combat != null && spell.Combat.VitalDiff[(int)Vitals.Mana] != 0)
            {
                _ = TryDealManaDamageTo(enemy, spell.Combat.VitalDiff[(int)Vitals.Mana], dmgScaling, critMultiplier, out manaDamage);
            }

            // If we have a true damage override in our attack somewhere...
            if (weapon?.DamageType == (int)DamageType.True || spell?.Combat?.DamageType == (int)DamageType.True)
            {
                var trueDamage = weapon?.Damage ?? 0;
                if (spell != null)
                {
                    if (spell.WeaponSpell && weapon != null)
                    {
                        trueDamage += spell.Combat?.VitalDiff[(int)Vitals.Health] ?? 0;
                    }
                    else
                    {
                        trueDamage = spell.Combat?.VitalDiff[(int)Vitals.Health] ?? 0;
                    }
                }
                damage = DealTrueDamageTo(enemy, dmgScaling, trueDamage, false, false);
            }
            // Otherwise, we're dealing non-true damage and need to do some calcs
            else
            {
                DealDamageTo(enemy, attackTypes, dmgScaling, critMultiplier, weapon, false, spell?.Combat?.Friendly ?? false, spell?.DamageOverrides, range, out damage);
            }
            return damage != 0 || manaDamage != 0;
        }

        public int DealTrueDamageTo(Entity enemy, int scaling, int damage, bool isSecondary, bool isNeutral)
        {
            if (enemy == null)
            {
                return 0;
            }

            UpdateCombatTimers(this, enemy);

            float decScaling = (float)scaling / 100; // scaling comes into this function as a percent number, i.e 110%, so we need that to be 1.1
            var dmg = (int)Math.Round(damage * decScaling);
            enemy.TakeDamage(this, dmg, isSecondary ? Vitals.Mana : Vitals.Health);

            if (enemy is Npc npc && npc.Base.CannotBeHealed && damage < 0)
            {
                return dmg;
            }

            SendCombatEffects(enemy, false, dmg);
            PacketSender.SendCombatNumber(DetermineCombatNumberType(damage, isSecondary, isNeutral, 1.0), enemy, dmg);

            return dmg;
        }

        private void DealDamageTo(Entity enemy,
            List<AttackTypes> damageTypes,
            int scaling,
            double critMultiplier,
            ItemBase weaponMetadata,
            bool secondaryDamage,
            bool friendly,
            Dictionary<int, int> damageTypeOverrides,
            int range,
            out int damage)
        {
            damage = 0;
            if (enemy == null)
            {
                return;
            }

            var atkStats = CombatUtilities.GetOverriddenStats(damageTypeOverrides, StatVals);
            
            var defStats = new int[(int)Stats.StatCount];
            Array.Copy(enemy.StatVals, defStats, atkStats.Length);

            UpdateCombatTimers(this, enemy);

            var maxHit = 0;
            if (friendly)
            {
                damage = CombatUtilities.CalculateFriendlyDamage(damageTypes, critMultiplier, scaling, atkStats, out maxHit);
            }
            else
            {
                damage = CombatUtilities.CalculateDamage(damageTypes, critMultiplier, scaling, atkStats, defStats, out maxHit);
            }

            if (damage != 0)
            {
                // Invulnerable?
                if (!friendly && enemy.CachedStatuses.Any(status => status.Type == StatusTypes.Invulnerable))
                {
                    PacketSender.SendActionMsg(enemy, Strings.Combat.invulnerable, CustomColors.Combat.Invulnerable, Options.BlockSound);
                    return;
                }

                damage = CalculateSpecialDamage(damage, range, weaponMetadata, enemy);

                if (damage > enemy.GetVital((int)Vitals.Health))
                {
                    damage = enemy.GetVital((int)Vitals.Health);
                }
                enemy.TakeDamage(this, damage, secondaryDamage ? Vitals.Mana : Vitals.Health);

                if (enemy is Npc npc && npc.Base.CannotBeHealed && damage < 0)
                {
                    return;
                }
                SendCombatEffects(enemy, critMultiplier > 1, damage);
                PacketSender.SendCombatNumber(DetermineCombatNumberType(damage, secondaryDamage, enemy is Resource, critMultiplier), enemy, damage);
            }
            else if (!friendly && maxHit > 0)
            {
                SendBlockedAttackMessage(enemy);
            }
        }

        protected static CombatNumberType DetermineCombatNumberType(int damage, bool isSecondary, bool isNeutral, double critMultiplier)
        {
            if (isNeutral)
            {
                return CombatNumberType.Neutral;
            }
            if (critMultiplier > 1.0f && damage > 0)
            {
                return CombatNumberType.DamageCritical;
            }
            if (!isSecondary)
            {
                if (damage >= 0)
                {
                    return CombatNumberType.DamageHealth;
                }
                else
                {
                    return CombatNumberType.HealHealth;
                }
            }
            else
            {
                if (damage >= 0)
                {
                    return CombatNumberType.DamageMana;
                }
                else
                {
                    return CombatNumberType.HealMana;
                }
            }
        }

        public static void UpdateCombatTimers(Entity attacker, Entity defender)
        {
            // Resource combat ain't combat
            if (attacker != null && !(defender is Resource))
            {
                attacker.CombatTimer = Timing.Global.Milliseconds + Options.CombatTime;
            }
            if (defender != null)
            {
                defender.CombatTimer = Timing.Global.Milliseconds + Options.CombatTime;
            }
        }

        /// <summary>
        /// Things that should happen TO some entity when being attacked FROM some entity
        /// </summary>
        /// <param name="attacker">The attacking entity dealing damage to this entity</param>
        /// <param name="damage">The amount of damage to take</param>
        /// <param name="vital">The affected vital</param>
        public override void TakeDamage(Entity attacker, int damage, Vitals vital = Vitals.Health)
        {
            DamageTaken.Invoke(attacker, damage);
            base.TakeDamage(attacker, damage, vital);
        }

        protected delegate void AttackMissedEvent(Entity target);
        protected event AttackMissedEvent AttackMissed;

        protected delegate void DamageTakenEvent(Entity aggressor, int damage);
        protected event DamageTakenEvent DamageTaken;

        public virtual void OnAttackMissed(Entity target)
        {
            // Safely raise the event for all subscribers
            AttackMissed?.Invoke(target);
        }

        public override void ProcessManaRegen(long timeMs)
        {
            if (IsDead() || ManaRegenTimer > timeMs || StatusActive(StatusTypes.Enfeebled))
            {
                return;
            }

            var manaValue = GetVital((int)Vitals.Mana);
            var maxMana = GetMaxVital((int)Vitals.Mana);
            
            // Don't regen if at max
            if (manaValue >= maxMana)
            {
                return;
            }

            var manaRegenRate = GetVitalRegenRate((int)Vitals.Mana);
            var regenValue = (int)Math.Max(1, maxMana * manaRegenRate) *
                                 Math.Abs(Math.Sign(manaRegenRate));

            AddVital(Vitals.Mana, regenValue);
            ManaRegenTimer = timeMs + Options.Instance.CombatOpts.ManaRegenTime;
        }

        public abstract float GetVitalRegenRate(int vital);

        protected virtual bool TryLifesteal(int damage, Entity target, out float recovered)
        {
            recovered = 0f;
            return false;
        }
    }
}
