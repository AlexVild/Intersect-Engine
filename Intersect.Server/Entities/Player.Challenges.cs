﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Intersect.GameObjects;
using Intersect.Network.Packets.Server;
using Intersect.Server.Core;
using Intersect.Server.Core.Instancing.Controller;
using Intersect.Server.Database;
using Intersect.Server.Database.PlayerData.Players;
using Intersect.Server.Entities.Events;
using Intersect.Server.Entities.PlayerData;
using Intersect.Server.Localization;
using Intersect.Server.Networking;
using Intersect.Utilities;
using Newtonsoft.Json;

namespace Intersect.Server.Entities
{
    public partial class Player : AttackingEntity
    {
        public Guid ChallengeContractId { get; set; }

        [NotMapped, JsonIgnore]
        public ChallengeDescriptor ChallengeContract
        {
            get => ChallengeDescriptor.Get(ChallengeContractId);
            set => ChallengeContractId = value?.Id ?? Guid.Empty;
        }

        [NotMapped, JsonIgnore]
        public List<ChallengeProgress> ChallengesInProgress { get; set; } = new List<ChallengeProgress>();

        public List<ChallengeInstance> Challenges { get; set; } = new List<ChallengeInstance>();

        public List<WeaponMasteryInstance> WeaponMasteries { get; set; } = new List<WeaponMasteryInstance>();

        [NotMapped, JsonIgnore]
        public long CurrWeaponExp { get; set; }

        [NotMapped, JsonIgnore]
        public int CurrWeaponLvl { get; set; }

        [NotMapped, JsonIgnore]
        public long WeaponExpTnl { get; set; }

        [NotMapped, JsonIgnore]
        public int WeaponCombo { get; set; }

        [NotMapped, JsonIgnore]
        public int WeaponComboExp { get; set; }

        [NotMapped, JsonIgnore]
        public bool InvalidateChallenge { get; set; } = false;

        [NotMapped, JsonIgnore]
        public int MissFreeStreak { get; set; }

        /// <summary>
        /// A dictionary who's job is to track when a miss free streak is started at a specific range. The range is the key, the streak is the value
        /// </summary>
        [NotMapped, JsonIgnore]
        public Dictionary<int, int> MissFreeRangeDict { get; set; } = new Dictionary<int, int>();

        [NotMapped, JsonIgnore]
        public int HitFreeStreak { get; set; }

        [NotMapped, JsonIgnore]
        public long LastWeaponSwitch { get; set; }

        /// <summary>
        /// For certain challenges, a easy cheat-proofing we can do is only start checking for timing updates when it's been at least
        /// _this_ amount of time since the last time the player switched weapons
        /// </summary>
        [NotMapped, JsonIgnore]
        public readonly long ChallengeWeaponSwitchTimer = 10000;

        [NotMapped, JsonIgnore]
        public Dictionary<Guid, Queue<KeyValuePair<long, int>>> DoTChallengeMap { get; set; } = new Dictionary<Guid, Queue<KeyValuePair<long, int>>>();
        
        [NotMapped, JsonIgnore]
        public Dictionary<Guid, Queue<KeyValuePair<long, int>>> DamageTakenMap { get; set; } = new Dictionary<Guid, Queue<KeyValuePair<long, int>>>();

        [NotMapped, JsonIgnore]
        public Dictionary<Guid, Queue<KeyValuePair<long, Guid>>> BeastsKilledOverTime { get; set; } = new Dictionary<Guid, Queue<KeyValuePair<long, Guid>>>();

        [NotMapped, JsonIgnore]
        public List<WeaponMasteryInstance> ActiveMasteries => WeaponMasteries.Where(mastery => mastery.IsActive).ToList();

        [NotMapped, JsonIgnore]
        private bool WeaponMaxedReminder { get; set; } = false;

        public Guid TrackedWeaponType { get; set; }

        public bool TryGetChallenge(Guid id, out ChallengeInstance challenge)
        {
            challenge = Challenges.Find(c => c.ChallengeId == id);
            return challenge != default;
        }

        public void TrackChallenges(List<Guid> challengeIds)
        {
            var challenges = Challenges.Where(c => challengeIds.Contains(c.ChallengeId));

            ChallengesInProgress.Clear();
            foreach (var instance in challenges) 
            {
                // If the challenge is already complete, don't track it
                if (instance.Complete)
                {
                    continue;
                }
                
                if (instance.Challenge == null)
                {
                    continue;
                }

                // Only track contract challenges if we're contracted to them
                if (instance.Challenge.RequiresContract && ChallengeContractId != instance.ChallengeId)
                {
                    continue;
                }

                if (instance.Challenge.Type == ChallengeType.MissFreeAtRange)
                {
                    MissFreeRangeDict[instance.Challenge.Param] = 0;
                }

                var progress = new ChallengeProgress(instance, this);
                ChallengesInProgress.Add(progress);
            }
        }

        public bool TryGetMastery(Guid weaponTypeId, out WeaponMasteryInstance mastery)
        {
            mastery = WeaponMasteries.Find(c => c.WeaponTypeId == weaponTypeId);
            return mastery != default;
        }

        /// <summary>
        /// There are certain variables that we keep track of on the player that get reset on equipment swap, to prevent
        /// exploiting challenge completion by hot-swapping weapons or equipment. This function takes care of that, and
        /// should be called whenever the player swaps equipment.
        /// </summary>
        public void ResetChallengeTracking()
        {
            WeaponCombo = 0;
            
            if (CurrentCombo > 0)
            {
                InvalidateChallenge = true;
            }
            WeaponComboExp = 0;

            DoTChallengeMap.Clear();
            DamageTakenMap.Clear();
            BeastsKilledOverTime.Clear();

            HitFreeStreak = 0;
            MissFreeStreak = 0;

            MissFreeRangeDict.Clear();

            LastWeaponSwitch = Timing.Global.Milliseconds + ChallengeWeaponSwitchTimer;
        }

        public void TrackWeaponTypeProgress(Guid weaponTypeId)
        {
            TrackedWeaponType = weaponTypeId;
            CurrWeaponExp = 0;
            WeaponExpTnl = 0;
            CurrWeaponLvl = 0;

            if (weaponTypeId == Guid.Empty || !TryGetMastery(weaponTypeId, out var mastery))
            {
                return;
            }

            if (mastery.WeaponType != default && mastery.WeaponType.Unlocks.TryGetValue(mastery.Level + 1, out var nextUnlock))
            {
                WeaponExpTnl = nextUnlock.RequiredExp;
                CurrWeaponExp = mastery.ExpRemaining;
                CurrWeaponLvl = mastery.Level;
            }
        }

        public void SetMasteryProgress()
        {
            var weapon = GetEquippedWeapon();

            WeaponMaxedReminder = false;

            DeactivateAllMasteries();

            // Instantiate new mastery tracks/challenges in response to this change
            List<string> newTracks = new List<string>();
            List<string> newChallenges = new List<string>();
            List<Guid> challengeInstanceIds = new List<Guid>();
            foreach (var weaponType in weapon?.WeaponTypes ?? new List<Guid>())
            {
                if (!TryGetMastery(weaponType, out var mastery))
                {
                    WeaponMasteries.Add(new WeaponMasteryInstance(Id, weaponType, 0, true));
                    TryGetMastery(weaponType, out mastery);
                    newTracks.Add(WeaponTypeDescriptor.GetName(weaponType));
                }

                mastery.IsActive = true;

                if (!mastery.TryGetCurrentWeaponLevelProperties(out var levelProps))
                {
                    continue;
                }
                
                if (mastery.ExpRemaining < levelProps.RequiredExp)
                {
                    continue;
                }

                // Otherwise, initialize challenges
                if (!mastery.TryGetCurrentChallenges(out var masteryChallenges))
                {
                    LevelUpMastery(mastery);
                    continue;
                }
                if (ChallengesComplete(masteryChallenges))
                {
                    LevelUpMastery(mastery);
                    continue;
                }
                challengeInstanceIds.AddRange(masteryChallenges);
            }
            SendNewTrack(newTracks);
            SendChallengeUpdate(false, newChallenges);
            TrackChallenges(challengeInstanceIds);

            // Send weapon EXP update
            PacketSender.SendExperience(this);
        }

        public bool TryProgressMastery(long exp, Guid weaponType, ItemBase weapon = default, bool isActive = true)
        {
            if (!TryGetMastery(weaponType, out var mastery))
            {
                return false;
            }
            mastery.IsActive = isActive;

            // Are we done with this mastery track?
            var maxLevel = mastery.WeaponType.MaxLevel;
            if (mastery.Level >= maxLevel)
            {
                if (mastery.Level > maxLevel)
                {
                    mastery.Level = maxLevel;
                }
                
                return false;
            }

            // First, attempt to give EXP to the specified mastery
            if (TryGainMasteryExp(exp, mastery))
            {
                return true;
            }

            if (!WeaponCanProgressMastery(mastery))
            {
                return false;
            }

            // Otherwise, do we have any challenges that need completing?
            if (!mastery.TryGetCurrentChallenges(out var currentChallenges))
            {
                // No challenges? Well, level up!
                LevelUpMastery(mastery);
                return true;
            }

            // Make sure our active challenges are up to date and, if not, alert the player
            List<string> newChallenges = new List<string>();
            foreach (var challengeId in currentChallenges)
            {
                if (!TryAddNewChallenge(challengeId))
                {
                    continue;
                }
                newChallenges.Add(ChallengeDescriptor.GetName(challengeId));
            }
            if (newChallenges.Count > 0)
            {
                SendChallengeUpdate(false, newChallenges);
                TrackChallenges(currentChallenges);
            }

            // If we're not done yet, then we can't level up yet!
            if (!ChallengesComplete(currentChallenges))
            {
                return false;
            }

            LevelUpMastery(mastery);
            return true;
        }

        public void LevelUpMastery(WeaponMasteryInstance mastery)
        {
            if (mastery == null)
            {
                return;
            }

            var weaponType = mastery.WeaponType;
            if (weaponType == default)
            {
#if DEBUG
                throw new InvalidOperationException($"No valid weapon type found for mastery attempting level up: {mastery.WeaponTypeId}");
#else
                Logging.Log.Debug($"No valid weapon type found for mastery attempting level up: {mastery.WeaponTypeId}");
                return;
#endif
            }

            // We're already maxed
            if (mastery.Level == weaponType.MaxLevel)
            {
                Logging.Log.Debug($"Attempted to level up a max level mastery for {Name} in mastery {weaponType.Name}");
                return;
            }

            if (mastery.TryGetCurrentWeaponLevelProperties(out var currentUnlock))
            {
                foreach (var challengeId in currentUnlock.ChallengeIds)
                {
                    ObtainChallengeUnlocks(challengeId);
                }
            }

            mastery.Level = MathHelper.Clamp(mastery.Level + 1, 0, mastery.WeaponType?.MaxLevel ?? 0);
            if (mastery.Level == weaponType.MaxLevel)
            {
                mastery.ExpRemaining = -1;
                SendMasteryUpdate(true, weaponType.Name);
            }
            else
            {
                mastery.ExpRemaining = 0L;
                SendMasteryUpdate(false, weaponType.Name);

                // Is this weapon at the end of its progress cycle?
                var weapon = GetEquippedWeapon();
                if (weapon.MaxWeaponLevels.TryGetValue(weaponType.Id, out var maxWeaponLevel) && maxWeaponLevel == mastery.Level)
                {
                    SendWeaponMaxedMessage(weaponType);

                    // Is this the first mastery of this weapon that we've maxed?
                    if (FirstMaxLevelFor(weapon, weaponType))
                    {
                        PacketSender.SendChatMsg(this, Strings.Enhancements.CanEnhance.ToString(weapon.Name), Enums.ChatMessageType.Experience, sendToast: true);
                    }
                }
            }
            PacketSender.SendExperience(this);
        }

        private bool FirstMaxLevelFor(ItemBase weapon, WeaponTypeDescriptor levelledType)
        {
            return weapon.MaxWeaponLevels.All((kv) =>
            {
                var wepType = kv.Key;
                var maxLvl = kv.Value;

                if (wepType == levelledType.Id)
                {
                    return true;
                }

                if (!TryGetMastery(wepType, out var otherMastery))
                {
                    return true;
                }

                if (otherMastery.Level < maxLvl)
                {
                    return true;
                }

                return false;
            });
        }

        public void ObtainChallengeUnlocks(Guid challengeId)
        {
            var challenge = ChallengeDescriptor.Get(challengeId);
            if (challenge.SpellUnlockId != Guid.Empty)
            {
                _ = TryAddSkillToBook(challenge.SpellUnlockId);
                PacketSender.SendChatMsg(this,
                    Strings.Player.MasterySkillUnlock.ToString(SpellBase.GetName(challenge.SpellUnlockId)),
                    Enums.ChatMessageType.Spells,
                    CustomColors.General.GeneralCompleted);
            }
            if (challenge.CompletionEventId != Guid.Empty)
            {
                EnqueueStartCommonEvent(challenge.CompletionEvent);
            }
            if (challenge.EnhancementUnlockId != Guid.Empty)
            {
                _ = TryUnlockEnhancement(challenge.EnhancementUnlockId);
            }
        }

        public void CompleteMasteryChallenges(WeaponMasteryInstance mastery)
        {
            if (!mastery.TryGetCurrentChallenges(out var challenges))
            {
                return;
            }

            foreach (var challengeId in challenges)
            {
                if (!TryGetChallenge(challengeId, out var challenge))
                {
                    if (!TryAddNewChallenge(challengeId))
                    {
                        continue;
                    }
                    TryGetChallenge(challengeId, out challenge);

                    if (challenge == default)
                    {
                        continue;
                    }
                }

                challenge.Complete = true;
            }
        }

        public void RescendMasteryLevel(WeaponMasteryInstance mastery)
        {
            if (mastery == null || mastery.Level == 0)
            {
                return;
            }

            var weaponType = mastery.WeaponType;
            if (weaponType == default)
            {
#if DEBUG
                throw new InvalidOperationException($"No valid weapon type found for mastery attempting level up: {mastery.WeaponTypeId}");
#else
                return;
#endif
            }

            if (weaponType.Unlocks.TryGetValue(mastery.Level, out var lastUnlock))
            {
                foreach(var challenge in lastUnlock.ChallengeIds)
                {
                    RemoveChallenge(challenge);
                }
            }

            mastery.Level = MathHelper.Clamp(mastery.Level - 1, 0, int.MaxValue);
            mastery.ExpRemaining = 0L;
        }

        public bool WeaponCanProgressMastery(WeaponMasteryInstance mastery)
        {
            var equippedWeapon = GetEquippedWeapon();
            return equippedWeapon != null 
                && equippedWeapon.MaxWeaponLevels != null 
                && equippedWeapon.MaxWeaponLevels.TryGetValue(mastery.WeaponTypeId, out var maxWeaponLevel)
                && maxWeaponLevel > mastery.Level;
        }

        public bool WeaponCanProgressMastery(WeaponMasteryInstance mastery, ItemBase weapon)
        {
            return weapon != null
                && weapon.MaxWeaponLevels != null
                && weapon.MaxWeaponLevels.TryGetValue(mastery.WeaponTypeId, out var maxWeaponLevel)
                && maxWeaponLevel > mastery.Level;
        }

        public bool TryGainMasteryExp(long exp, WeaponMasteryInstance mastery, ItemBase weapon = default)
        {
            var requiredExp = 0;
            if (mastery.TryGetCurrentWeaponLevelProperties(out var unlock))
            {
                requiredExp = unlock.RequiredExp;
            }
            if (mastery == null || mastery.ExpRemaining >= requiredExp)
            {
                mastery.ExpRemaining = requiredExp;
                return false;
            }

            if (weapon == default)
            {
                // Is the current weapon at the end of its progress cycle?
                if (!WeaponCanProgressMastery(mastery))
                {
                    SendWeaponMaxedMessage(mastery.WeaponType);
                    return false;
                }
            }
            else
            {
                if (!WeaponCanProgressMastery(mastery, weapon))
                {
                    SendWeaponMaxedMessage(mastery.WeaponType);
                    return false;
                }
            }

            mastery.ExpRemaining += exp;

            if (mastery.ExpRemaining < requiredExp)
            {
                return true;
            }

            // We have reached our EXP threshold, and should continue progress processing
            mastery.ExpRemaining = unlock.RequiredExp;
            return false;
        }

        public bool TryAddNewChallenge(Guid challengeId, bool isActive = true)
        {
            if (TryGetChallenge(challengeId, out _))
            {
                return false;
            }

            var challenge = new ChallengeInstance(Id, challengeId, false, 0, isActive);
            Challenges.Add(challenge);
            return true;
        }

        /// <summary>
        /// Adds weapon EXP when a weapon is crafted/deconstructed
        /// </summary>
        /// <param name="item">The item that is being crafted/deconstructed</param>
        /// <param name="expMultiplier">The multiplier applied to EXP received</param>
        /// <param name="sendPackets">Whether or not we will send messaging/exp packets during this. Useful to turn to false if this is part of an iteration</param>
        /// <returns>A dictionary of weaponType -> exp received</returns>
        public Dictionary<Guid, long> AddCraftWeaponExp(ItemBase item, float expMultiplier, bool sendPackets = true)
        {
            var expEarned = new Dictionary<Guid, long>();
            var exp = (long)Math.Floor(item.CraftWeaponExp * expMultiplier);
            if (exp <= 0)
            {
                return expEarned;
            }

            var equippedWeapon = GetEquippedWeapon();
            var sendExpNotification = false;
            foreach (var weaponType in item.WeaponTypes)
            {
                if (!item.MaxWeaponLevels.TryGetValue(weaponType, out var maxLevel) || !TryGetMastery(weaponType, out var mastery))
                {
                    continue;
                }

                if (mastery.Level >= maxLevel)
                {
                    continue;
                }

                if (mastery.Level == mastery.WeaponType?.MaxLevel)
                {
                    continue;
                }

                if (TryProgressMastery(exp, weaponType, item, (equippedWeapon?.Id ?? Guid.Empty) == item.Id) && (weaponType == TrackedWeaponType))
                {
                    sendExpNotification = true;
                    if (!expEarned.ContainsKey(weaponType))
                    {
                        expEarned[weaponType] = exp;
                    }
                    else
                    {
                        expEarned[weaponType] += exp;
                    }

                    if (sendPackets)
                    {
                        var name = WeaponTypeDescriptor.Get(weaponType)?.DisplayName ?? WeaponTypeDescriptor.GetName(weaponType);
                        PacketSender.SendExpToast(this, $"{exp} {name} EXP", false, false, true);
                    }
                }
            }

            if (sendExpNotification && sendPackets)
            {
                TrackWeaponTypeProgress(TrackedWeaponType);
                PacketSender.SendExperience(this);
            }

            return expEarned;
        }

        public bool ChallengesComplete(List<Guid> challengeIds)
        {
            return challengeIds.All((challengeId) =>
            {
                if (!TryGetChallenge(challengeId, out var challenge))
                {
                    if (!TryAddNewChallenge(challengeId))
                    {
                        return true;
                    }
                    SendChallengeUpdate(false, ChallengeDescriptor.GetName(challengeId));
                    return false;
                }

                return challenge.Complete;
            });
        }

        public void DeactivateAllMasteries()
        {
            foreach (var mastery in WeaponMasteries.Where(m => m.IsActive))
            {
                mastery.IsActive = false;
            }
        }

        public void RemoveChallenge(Guid challengeId)
        {
            var challengesToRemove = Challenges.FindAll(p => p.ChallengeId == challengeId);
            Challenges.RemoveAll(p => p.ChallengeId == challengeId);

            foreach (var challenge in challengesToRemove)
            {
                DbInterface.Pool.QueueWorkItem(challenge.RemoveFromDb);
            }
        }

        public ChallengeProgressPacket GenerateChallengeProgressPacket()
        {
            List<WeaponTypeProgress> weaponTypeProgresses = new List<WeaponTypeProgress>();

            foreach (var weaponType in WeaponMasteries)
            {
                var challenges = weaponType.GetAllChallengeIds();
                if (challenges.Count == 0)
                {
                    continue;
                }

                List<ChallengeProgression> challengeProgressions = new List<ChallengeProgression>();
                foreach (var challenge in challenges)
                {
                    var playerProgress = Challenges.Find(c => c.ChallengeId == challenge);
                    if (playerProgress == default)
                    {
                        continue;
                    }
                    challengeProgressions.Add(new ChallengeProgression(challenge, playerProgress.Progress, playerProgress.Complete));
                }

                var progress = new WeaponTypeProgress(weaponType.WeaponTypeId, weaponType.Level, weaponType.ExpRemaining, challengeProgressions);
                weaponTypeProgresses.Add(progress);
            }

            return new ChallengeProgressPacket(weaponTypeProgresses, TrackedWeaponType, ChallengeContractId);
        }

        public bool TryAcceptChallengeContract(Guid contractId)
        {
            // Player doesn't know challenge
            var challengeInstance = Challenges.Find(c => c.ChallengeId == contractId);
            if (challengeInstance == default)
            {
                return false;
            }

            if (challengeInstance.Complete)
            {
                return false;
            }

            if (ChallengeContractId == contractId && TryVoidCurrentContract(out var cancelledContract, true))
            {
                PacketSender.SendChatMsg(this,
                    $"You've cancelled the contract: {cancelledContract.Name}",
                    Enums.ChatMessageType.Experience,
                    CustomColors.General.GeneralWarning,
                    sendToast: true);
                return false;
            }

            var newContract = ChallengeDescriptor.Get(contractId);
            if (newContract == null)
            {
                PacketSender.SendEventDialog(this, "This challenge no longer exists.", string.Empty, Guid.Empty);
            }

            if (InstanceProcessor.TryGetInstanceController(MapInstanceId, out var instanceController) && instanceController.InstanceIsDungeon)
            {
                PacketSender.SendEventDialog(this, "You can not accept contracts while in a dungeon!", string.Empty, Guid.Empty);
                return false;
            }

            if (!Conditions.MeetsConditionLists(newContract.ContractRequirements, this, null))
            {
                var reason = string.IsNullOrWhiteSpace(newContract.RequirementsString) ?
                    "You do not meet the requirements of this challenge contract" :
                    newContract.RequirementsString;
                PacketSender.SendEventDialog(this, reason, string.Empty, Guid.Empty);
                return false;
            }

            TryVoidCurrentContract(out var oldContract);
            ChallengeContractId = contractId;

            if (oldContract != null)
            {
                PacketSender.SendChatMsg(this,
                    $"You have accepted a new challenge contract: {ChallengeContract.Name}, voiding your previous contract. Changing equipment will void this new contract.",
                    Enums.ChatMessageType.Experience,
                    CustomColors.General.GeneralWarning,
                    sendToast: true);
            }
            else
            {
                PacketSender.SendChatMsg(this,
                    $"You have accepted a new challenge contract: {ChallengeContract.Name}. Changing equipment will void the contract.",
                    Enums.ChatMessageType.Experience,
                    CustomColors.General.GeneralWarning,
                    sendToast: true);
            }

            // Re-Track current challenges
            RetrackChallenges();
            SendPacket(GenerateChallengeProgressPacket());
            return true;
        }

        public void RetrackChallenges()
        {
            var currentChallenges = ChallengesInProgress.Select(c => c.ChallengeId).ToList();
            currentChallenges.Add(ChallengeContractId);
            TrackChallenges(currentChallenges.Distinct().ToList());
        }

        public bool TryVoidCurrentContract(out ChallengeDescriptor oldContract, bool sendPacket = false)
        {
            oldContract = ChallengeContract;
            if (ChallengeContract != null)
            {
                ChallengeContract = null;
                RetrackChallenges();

                if (sendPacket)
                {
                    SendPacket(GenerateChallengeProgressPacket());
                }

                return true;
            }

            return false;
        }
    }
}
