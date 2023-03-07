﻿using Intersect.Attributes;
using Intersect.Enums;
using System.ComponentModel;

namespace Intersect.GameObjects.Events
{

    public enum MoveRouteEnum
    {

        MoveUp = 1,

        MoveDown,

        MoveLeft,

        MoveRight,

        MoveRandomly,

        MoveTowardsPlayer,

        MoveAwayFromPlayer,

        StepForward,

        StepBack,

        FaceUp,

        FaceDown,

        FaceLeft,

        FaceRight,

        Turn90Clockwise,

        Turn90CounterClockwise,

        Turn180,

        TurnRandomly,

        FacePlayer,

        FaceAwayFromPlayer,

        SetSpeedSlowest,

        SetSpeedSlower,

        SetSpeedNormal,

        SetSpeedFaster,

        SetSpeedFastest,

        SetFreqLowest,

        SetFreqLower,

        SetFreqNormal,

        SetFreqHigher,

        SetFreqHighest,

        WalkingAnimOn,

        WalkingAnimOff,

        DirectionFixOn,

        DirectionFixOff,

        WalkthroughOn,

        WalkthroughOff,

        ShowName,

        HideName,

        SetLevelBelow,

        SetLevelNormal,

        SetLevelAbove,

        Wait100,

        Wait500,

        Wait1000,

        SetGraphic,

        SetAnimation,

    }

    //ONLY ADD TO THE END OF THIS LIST ELSE FACE THE WRATH OF JC!!!!!
    public enum EventCommandType
    {

        Null = 0,

        //Dialog
        ShowText,

        ShowOptions,

        AddChatboxText,

        //Logic Flow
        SetVariable = 5,

        SetSelfSwitch,

        ConditionalBranch,

        ExitEventProcess,

        Label,

        GoToLabel,

        StartCommonEvent,

        //Player Control
        RestoreHp,

        RestoreMp,

        LevelUp,

        GiveExperience,

        ChangeLevel,

        ChangeSpells,

        ChangeItems,

        ChangeSprite,

        ChangeFace,

        ChangeGender,

        SetAccess,

        //Movement,
        WarpPlayer,

        SetMoveRoute,

        WaitForRouteCompletion,

        HoldPlayer,

        ReleasePlayer,

        SpawnNpc,

        //Special Effects
        PlayAnimation,

        PlayBgm,

        FadeoutBgm,

        PlaySound,

        StopSounds,

        //Etc
        Wait,

        //Shop and Bank
        OpenBank,

        OpenShop,

        OpenCraftingTable,

        //Extras
        SetClass,

        DespawnNpc,

        //Questing
        StartQuest,

        CompleteQuestTask,

        EndQuest,

        //Pictures
        ShowPicture,

        HidePicture,

        //Hide/show player
        HidePlayer,

        ShowPlayer,

        //Equip Items
        EquipItem,

        //Change Name Color
        ChangeNameColor,

        //Player Input variable.
        InputVariable,

        //Player Label
        PlayerLabel,

        // Player Color
        ChangePlayerColor,

        ChangeName,

        //Guilds
        CreateGuild,
        DisbandGuild,
        OpenGuildBank,
        SetGuildBankSlots,
        //End Guilds

        //Reset Stats
        ResetStatPointAllocations,
        
        // Flash Screen
        FlashScreen,

        // Quest lists/board
        RandomQuest,
        OpenQuestBoard = 60,

        // Vehicles
        SetVehicle,

        // NPC Guilds,
        NPCGuildManagement,
        
        // Inspiration
        AddInspiration,

        // Timers
        StartTimer,
        ModifyTimer,
        StopTimer,

        ChangeSpawnGroup,

        OpenLeaderboard,
        ClearRecord,
        RollLoot,
        UnlockLabel,
        VarGroupReset, // 72
        ResetPermadeadNpcs, // 73
        FadeIn, // 74
        FadeOut, // 75
        ShakeScreen, // 76
        ChangeSpawn, // 77
        ChangeRecipeStatus, // 78
        ChangeBestiary, // 79
        ChangeWeaponTrack, // 80
        ChangeChallenge, // 81
        ChangeDungeon, // 82
        ObtainGnome, // 83
        RollDungeonLoot, // 84
        OpenDeconstructor, // 85
        ChangeEnhancements, // 86
        OpenEnhancementWindow, // 87
        OpenUpgradeStation, // 88
    }

    public enum NPCGuildManagementSelection
    {
        ChangeComplete,
        ClearCooldown,
        ChangeRank,
        ChangeGuildStatus,
        ChangeSpecialAssignment,
        ChangeTasksRemaining,
    }

    /// <summary>
    /// Used for if a record should store the highest value (high), or lowest (low)
    /// </summary>
    public enum RecordScoring
    {
        High = 0,
        Low,
    }

    public enum RecordType
    {
        [Description("NPCs Killed"), RelatedTable(GameObjectType.Npc)]
        NpcKilled = 0,

        [Description("Items Crafted"), RelatedTable(GameObjectType.Item)]
        ItemCrafted,

        [Description("Resources Gathered"), RelatedTable(GameObjectType.Resource)]
        ResourceGathered,

        [Description("Player Variable Set"), RelatedTable(GameObjectType.PlayerVariable)]
        PlayerVariable,
        
        [Description("Highest Combo")]
        Combo,

        [Description("TotalDungeonCompletions")]
        TotalDungeonCompletions,

        [Description("Group Dungeon Times")]
        GroupDungeonTimes,

        [Description("Solo Dungeon Times")]
        SoloDungeonTimes
    }

    public enum RespawnChangeType
    {
        Default,
        Arena
    }

    public enum DeathType
    {
        PvE = 0,
        PvP,
        Dungeon,
        Safe,
    }

    public enum CombatNumberType
    {
        DamageHealth,
        DamageMana,
        DamageCritical,
        HealHealth,
        HealMana,
        Neutral,
    }

    public enum BestiaryUnlock
    {
        [Description("HP"), DefaultKillCount(5)]
        HP = 0,

        [Description("Name & Description"), DefaultKillCount(1)]
        NameAndDescription,

        [Description("MP"), DefaultKillCount(10)]
        MP,

        [Description("Stats"), DefaultKillCount(15)]
        Stats,

        [Description("Spells"), DefaultKillCount(25)]
        Spells,

        [Description("Loot"), DefaultKillCount(100)]
        Loot,

        [Description("Spell Combat Info"), DefaultKillCount(45)]
        SpellCombatInfo,
    }

    public enum ThreatLevel
    {
        [Description("MIDNIGHT")]
        Midnight = 0,

        [Description("EXTREME")]
        Extreme,

        [Description("DEADLY")]
        Deadly,

        [Description("THREATENING")]
        Threatening,

        [Description("FAIR")]
        Fair,

        [Description("WIMPY")]
        Wimpy,

        [Description("TRIVIAL")]
        Trivial,
    }

    public enum WeaponTrackUpdate
    {
        [Description("Set Level to")]
        SetLevel,

        [Description("Gain Levels: ")]
        GainLevel,

        [Description("Change Exp: ")]
        ChangeExp,

        [Description("Lose Levels: ")]
        LoseLevel,

        [Description("Unlearn")]
        Unlearn,
    }

    public enum ChallengeUpdate
    {
        [Description("Change Sets")]
        ChangeSets,

        [Description("Reset")]
        Reset,

        [Description("Complete")]
        Complete,
    }


    public enum LevelUpAssignments
    {
        Health,
        Mana,
        Evasion,
        Accuracy,
        Speed
    }

    public enum DungeonState
    {
        Null,
        Inactive,
        Active,
        Complete,
    }

    public enum LootAnimType
    {
        Chest,
        Deconstruct,
    }
}
