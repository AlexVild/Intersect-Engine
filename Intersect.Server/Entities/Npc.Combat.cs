﻿using Intersect.Enums;
using Intersect.GameObjects;
using Intersect.Server.Database;
using Intersect.Server.Networking;
using Intersect.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Intersect.Server.Entities
{
    public partial class Npc : AttackingEntity
    {
        public override void ProjectileAttack(Entity enemy, Projectile projectile, SpellBase parentSpell, ItemBase parentWeapon, bool ignoreEvasion, byte projectileDir)
        {
            if (projectile == null || projectile.Base == null)
            {
                return;
            }
            if (!CanRangeTarget(enemy))
            {
                return;
            }

            if (!(enemy is Player) && !(enemy is Npc))
            {
                return;
            }

            base.ProjectileAttack(enemy, projectile, parentSpell, parentWeapon, false, projectileDir);
        }

        public override void CheckForSpellCast(long timeMs)
        {
            if (CastTime != 0 && !IsCasting && SpellCastSlot < Spells.Count && SpellCastSlot >= 0)
            {
                var spell = SpellBase.Get(Spells[SpellCastSlot].SpellId);
                UseSpell(spell, SpellCastSlot, CastTarget);
            }
        }

        public override void TakeDamage(Entity attacker, int damage, Vitals vital = Vitals.Health)
        {
            AddToDamageAndLootMaps(attacker, damage);
            NotifySwarm(attacker);
            base.TakeDamage(attacker, damage, vital);
        }

        public override void ReactToCombat(Entity attacker)
        {
            AddToDamageAndLootMaps(attacker, 0);
            NotifySwarm(attacker);
            base.ReactToCombat(attacker);
        }

        public override bool CanMeleeTarget(Entity target)
        {
            if (!TargetInMeleeRange(target) ||
                !IsFacingTarget(target))
            {
                return false;
            }

            if (IsAllyOf(target))
            {
                return false;
            }

            return base.CanMeleeTarget(target);
        }

        public override bool TryDealDamageTo(Entity enemy,
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
            if (enemy == null)
            {
                return false;
            }

            critMultiplier = 1.0; // override - determined by item or spell
            if (spell == null && IsCriticalHit(Base.CritChance))
            {
                critMultiplier = Base.CritMultiplier;
            }
            else if (spell != null && spell.Combat != null && IsCriticalHit(spell.Combat.CritChance))
            {
                critMultiplier = spell.Combat.CritMultiplier;
            }

            return base.TryDealDamageTo(enemy, attackTypes, dmgScaling, critMultiplier, weapon, null, ignoreEvasion, range, out damage);
        }

        // An NPC always has casting materials
        protected override bool EntityHasCastingMaterials(SpellBase spell) => true;
        protected override bool TryConsumeCastingMaterials(SpellBase spell) => true;
        protected override bool EntityMeetsCastingRequirements(SpellBase spell, bool instantCast = false) => true;

        public override bool MeetsSpellVitalReqs(SpellBase spell)
        {
            var cost = spell.VitalCost[(int)Vitals.Mana];

            if (StatusActive(StatusTypes.Attuned))
            {
                cost = (int)Math.Floor(cost / Options.Instance.CombatOpts.AttunedStatusDividend);
            }

            if (cost > GetVital(Vitals.Mana))
            {
                return false;
            }

            // Ignore this one ;), we want NPCs to be able to suicide
            /*if (spell.VitalCost[(int)Vitals.Health] > GetVital(Vitals.Health))
            {
                return false;
            }*/

            return true;
        }

        protected override void UpdateSpellCooldown(int spellSlot)
        {
            if (spellSlot < 0 || spellSlot > Spells.Count)
            {
                return;
            }

            var spell = SpellBase.Get(Spells[spellSlot].SpellId);
            if (spell == null)
            {
                return;
            }

            SpellCooldowns[Spells[spellSlot].SpellId] = Timing.Global.MillisecondsUtc + (int)(spell.CooldownDuration);
        }

        protected override void PopulateExtraSpellDamage(ref int scaling,
            ref List<AttackTypes> attackTypes,
            ref int critChance,
            ref double critMultiplier)
        { 
            // intentionally blank
        }

        public override void MeleeAttack(Entity enemy, bool ignoreEvasion)
        {
            enemy?.ReactToCombat(this);
            Unstealth();
            var spellOverride = Base?.SpellAttackOverrideId ?? default;
            if (spellOverride != default)
            {
                if (!MeleeAvailable())
                {
                    return;
                }
                var spell = SpellBase.Get(spellOverride);
                if (Timing.Global.MillisecondsUtc > mLastOverrideAttack)
                {
                    UseSpell(spell, -1, enemy, instantCast: true);
                    mLastOverrideAttack = Timing.Global.MillisecondsUtc + CalculateAttackTime();
                }

                return;
            }

            if (!MeleeAvailable())
            {
                return;
            }

            IncrementAttackTimer();
            // Attack literally misses
            if (!CanMeleeTarget(enemy))
            {
                return;
            }
            List<AttackTypes> attackTypes = new List<AttackTypes>(Base.AttackTypes);

            SendAttackAnimation(enemy);
            if (!TryDealDamageTo(enemy, attackTypes, 100, 1.0, null, null, false, 1, out int damage))
            {
                return;
            }
        }

        public override void SendAttackAnimation(Entity enemy)
        {
            PacketSender.SendEntityAttack(this, CalculateAttackTime());
            if (Base.AttackAnimation == null)
            {
                return;
            }
            
            PacketSender.SendAnimationToProximity(
                Base.AttackAnimationId, -1, Guid.Empty, enemy.MapId, (byte)enemy.X, (byte)enemy.Y,
                (sbyte)Dir, enemy.MapInstanceId
            );
        }
    }
}
