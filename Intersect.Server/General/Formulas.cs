﻿using System;
using System.Collections.Generic;
using System.IO;

using Intersect.Enums;
using Intersect.Server.Entities;
using Intersect.Server.Localization;
using Intersect.Utilities;

using NCalc;

using Newtonsoft.Json;

namespace Intersect.Server.General
{

    public class Formulas
    {

        private const string FORMULAS_FILE = "resources/formulas.json";

        private static Formulas mFormulas;

        public Formula ExpFormula = new Formula("BaseExp * Power(Gain, Level)");

        public string MagicDamage =
            "Random(((BaseDamage + (ScalingStat * ScaleFactor))) * CritMultiplier * .975, ((BaseDamage + (ScalingStat * ScaleFactor))) * CritMultiplier * 1.025) * (100 / (100 + V_MagicResist))";

        public string PhysicalDamage =
            "Random(((BaseDamage + (ScalingStat * ScaleFactor))) * CritMultiplier * .975, ((BaseDamage + (ScalingStat * ScaleFactor))) * CritMultiplier * 1.025) * (100 / (100 + V_Defense))";

        public string TrueDamage =
            "Random(((BaseDamage + (ScalingStat * ScaleFactor))) * CritMultiplier * .975, ((BaseDamage + (ScalingStat * ScaleFactor))) * CritMultiplier * 1.025)";

        public string Evasion =
            "V_Speed + V_Defense + 20";

        public string AccuracyRating =
            "A_Speed + (A_Attack / 5)";

        public string Resistance =
            "V_Speed + V_AbilityPwr + 20";

        public string AttunementRating =
           "A_AbilityPwr + (A_Speed / 5)";

        public static void LoadFormulas()
        {
            try
            {
                mFormulas = new Formulas();
                if (File.Exists(FORMULAS_FILE))
                {
                    mFormulas = JsonConvert.DeserializeObject<Formulas>(File.ReadAllText(FORMULAS_FILE));
                }

                File.WriteAllText(FORMULAS_FILE, JsonConvert.SerializeObject(mFormulas, Formatting.Indented));

                Expression.CacheEnabled = false;
            }
            catch (Exception ex)
            {
                throw new Exception(Strings.Formulas.missing, ex);
            }
        }


        public static int CalculateDamageMAO(
            List<AttackTypes> attackTypes,
            double critMultiplier,
            int scaling,
            Entity attacker,
            Entity defender)
        {
            if (attacker == null || defender == null)
            {
                return 0;
            }

            if (attackTypes.Count == 0)
            {
                // Default to blunt handling if nothing given, backwards compatible with old logic (def/atk used)
                attackTypes.Add(AttackTypes.Blunt);
            }

            // Go through each of the attack types that apply to the damage
            var totalDamage = 0;
            float decScaling = (float)scaling / 100; // scaling comes into this function as a percent number, i.e 110%, so we need that to be 1.1
            foreach (var element in attackTypes)
            {
                var dmg = (int)Math.Round(attacker.Stat[(int)element].Value() * decScaling);
                // If we're not gonna be doing damage, dismiss
                if (dmg == 0)
                {
                    continue;
                }

                // Otherwise, 
                var resConst = 0.2;
                var resistance = resConst * defender.Stat[(int)StatHelpers.GetResistanceStat(element)].Value();
                var resistanceMod = MathHelper.Clamp(-1.0, resistance / dmg, 1.0);

                // = MEDIAN(0, FLOOR(G6 - (G6 * (0.1 + K6))), 10000)
                var baseVariance = 0.1;
                var lowVariance = baseVariance + resistanceMod;
                var highVariance = baseVariance - resistanceMod;

                var lowestDmg = dmg - (int)Math.Floor(dmg * lowVariance);
                var highestDmg = dmg + (int)Math.Ceiling(dmg * highVariance);

                totalDamage += Randomization.Next(lowestDmg, highestDmg + 1);
            }

            return (int)Math.Round(totalDamage * critMultiplier);
        } 
            

        public static int CalculateDamage(
            int baseDamage,
            DamageType damageType,
            Stats scalingStat,
            int scaling,
            double critMultiplier,
            Entity attacker,
            Entity victim
        )
        {
            if (mFormulas == null)
            {
                throw new ArgumentNullException(nameof(mFormulas));
            }

            if (attacker == null)
            {
                throw new ArgumentNullException(nameof(attacker));
            }

            if (attacker.Stat == null)
            {
                throw new ArgumentNullException(
                    nameof(attacker.Stat), $@"{nameof(attacker)}.{nameof(attacker.Stat)} is null"
                );
            }

            if (victim == null)
            {
                throw new ArgumentNullException(nameof(victim));
            }

            if (victim.Stat == null)
            {
                throw new ArgumentNullException(
                    nameof(victim.Stat), $@"{nameof(victim)}.{nameof(victim.Stat)} is null"
                );
            }

            string expressionString;
            switch (damageType)
            {
                case DamageType.Physical:
                    expressionString = mFormulas.PhysicalDamage;

                    break;
                case DamageType.Magic:
                    expressionString = mFormulas.MagicDamage;

                    break;
                case DamageType.True:
                    expressionString = mFormulas.TrueDamage;

                    break;
                default:
                    expressionString = mFormulas.TrueDamage;

                    break;
            }

            var expression = new Expression(expressionString);
            var negate = false;
            if (baseDamage < 0)
            {
                baseDamage = Math.Abs(baseDamage);
                negate = true;
            }

            if (expression.Parameters == null)
            {
                throw new ArgumentNullException(nameof(expression.Parameters));
            }

            try
            {
                mFormulas.ReadParams(ref expression, baseDamage, attacker, victim, critMultiplier, scaling, scalingStat);

                var result = Convert.ToDouble(expression.Evaluate());
                if (negate)
                {
                    result = -result;
                }

                if (attacker.StatusActive(StatusTypes.Confused) && Randomization.Next(1, 101) <= Options.Instance.CombatOpts.ConfusionMissPercent)
                {
                    result = 0.0;
                }

                return (int) Math.Round(result);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to evaluate damage formula", ex);
            }
        }

        public static bool AttackEvaded(int baseDamage, DamageType damageType, Entity attacker, Entity victim, double critMultiplier, int scaling, Stats scalingStat)
        {
            if (damageType == DamageType.True)
            {
                return false;
            }

            var victimsEvasionExpr = damageType == DamageType.Physical ? new Expression(mFormulas.Evasion) : new Expression(mFormulas.Resistance);
            mFormulas.ReadParams(ref victimsEvasionExpr, baseDamage, attacker, victim, critMultiplier, scaling, scalingStat);
            var evasion = (int) Math.Round(Convert.ToDouble(victimsEvasionExpr.Evaluate()));

            var attackersAccuracyExpr = damageType == DamageType.Physical ? new Expression(mFormulas.AccuracyRating) : new Expression(mFormulas.AttunementRating);
            mFormulas.ReadParams(ref attackersAccuracyExpr, baseDamage, attacker, victim, critMultiplier, scaling, scalingStat);
            var accuracy = (int) Math.Round(Convert.ToDouble(attackersAccuracyExpr.Evaluate()));

            FormattableString evadeExpString = $"if ( Random(1.0, 400.0) <= ({evasion}) - ({accuracy}), 1, 0 )";
            var evasionExpression = new Expression(evadeExpString.ToString());
            mFormulas.ReadParams(ref evasionExpression, baseDamage, attacker, victim, critMultiplier, scaling, scalingStat);

            bool hasEvaded = Convert.ToBoolean(Convert.ToInt32(evasionExpression.Evaluate()));
            return hasEvaded;
        }

        private void ReadParams(ref Expression expression, int baseDamage, Entity attacker, Entity victim, double critMultiplier, int scaling, Stats scalingStat)
        {
            expression.Parameters["BaseDamage"] = baseDamage;
            expression.Parameters["ScalingStat"] = attacker.Stat[(int)scalingStat].Value();
            expression.Parameters["ScaleFactor"] = scaling / 100f;
            expression.Parameters["CritMultiplier"] = critMultiplier;
            expression.Parameters["A_Attack"] = attacker.Stat[(int)Stats.Attack].Value();
            expression.Parameters["A_Defense"] = attacker.Stat[(int)Stats.Defense].Value();
            expression.Parameters["A_Speed"] = attacker.Stat[(int)Stats.Speed].Value();
            expression.Parameters["A_AbilityPwr"] = attacker.Stat[(int)Stats.AbilityPower].Value();
            expression.Parameters["A_MagicResist"] = attacker.Stat[(int)Stats.MagicResist].Value();
            expression.Parameters["V_Attack"] = victim.Stat[(int)Stats.Attack].Value();
            expression.Parameters["V_Defense"] = victim.Stat[(int)Stats.Defense].Value();
            expression.Parameters["V_Speed"] = victim.Stat[(int)Stats.Speed].Value();
            expression.Parameters["V_AbilityPwr"] = victim.Stat[(int)Stats.AbilityPower].Value();
            expression.Parameters["V_MagicResist"] = victim.Stat[(int)Stats.MagicResist].Value();

            expression.EvaluateFunction += delegate (string name, FunctionArgs args)
            {
                if (args == null)
                {
                    throw new ArgumentNullException(nameof(args));
                }

                if (name == "Random")
                {
                    args.Result = Random(args);
                }
            };
        }

        private static int Random(FunctionArgs args)
        {
            if (args.Parameters == null)
            {
                throw new ArgumentNullException(nameof(args.Parameters));
            }

            var parameters = args.EvaluateParameters() ??
                             throw new NullReferenceException($"{nameof(args.EvaluateParameters)}() returned null.");

            if (parameters.Length < 2)
            {
                throw new ArgumentException($"{nameof(Random)}() requires 2 numerical parameters.");
            }

            var min = (int) Math.Round(
                (double) (parameters[0] ?? throw new NullReferenceException("First parameter is null."))
            );

            var max = (int) Math.Round(
                (double) (parameters[1] ?? throw new NullReferenceException("First parameter is null."))
            );

            return min >= max ? min : Randomization.Next(min, max + 1);
        }

    }

}
