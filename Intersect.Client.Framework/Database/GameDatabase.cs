﻿using System;

namespace Intersect.Client.Framework.Database
{

    public abstract class GameDatabase
    {

        public bool FullScreen;

        public bool HideOthersOnWindowOpen;

        public bool TapToTurn;

        public bool FaceOnLock;

        public bool CombatFlash;

        public bool CombatShake;
        
        public bool EnableScanlines;
        
        public bool FadeTransitions;

        public bool TargetAccountDirection;

        //Preferences
        public int MusicVolume;

        public int SoundVolume;

        public int TargetFps;

        public int TargetResolution;

        public bool StickyTarget;

        public bool LeftClickTarget;

        public bool DisplayPartyMembers;

        public bool DisplayClanMembers;

        public bool DisplayNpcNames;
        
        public bool DisplayPlayerNames;
        
        public bool AttackCancelsCast;

        public bool EnterCombatOnTarget;

        public bool ChangeTargetOnDeath;

        public bool ClassicMode;

        public bool ChatHidden;

        public bool CastingIndicator;

        public bool HostileTileMarkers;
        
        public bool PartyTileMarkers;
        
        public bool SelfTileMarkers;

        //Saving password, other stuff we don't want in the games directory
        public abstract void SavePreference(string key, object value);

        public abstract string LoadPreference(string key);

        public T LoadPreference<T>(string key, T defaultValue)
        {
            var value = LoadPreference(key);
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            return (T) Convert.ChangeType(value, typeof(T));
        }

        //Load all preferences when the game starts
        public virtual void LoadPreferences()
        {
            MusicVolume = LoadPreference("MusicVolume", 25);
            SoundVolume = LoadPreference("SoundVolume", 25);
            TargetResolution = LoadPreference("Resolution", 0);
            TargetFps = LoadPreference("Fps", 0);
            FullScreen = LoadPreference("Fullscreen", false);
            HideOthersOnWindowOpen = LoadPreference("HideOthersOnWindowOpen", true);
            TapToTurn = LoadPreference("TapToTurn", false);
            FaceOnLock = LoadPreference("FaceOnLock", true);
            LeftClickTarget = LoadPreference("LeftClickTarget", false);
            TargetAccountDirection = LoadPreference("TargetAccountDirection", false);
            StickyTarget = LoadPreference("StickyTarget", true);
            CombatShake = LoadPreference("CombatShake", true);
            CombatFlash = LoadPreference("CombatFlash", true);
            EnableScanlines = LoadPreference("EnableScanlines", true);
            FadeTransitions = LoadPreference("FadeTransitions", false);
            DisplayPartyMembers = LoadPreference("DisplayPartyMembers", true);
            DisplayClanMembers = LoadPreference("DisplayClanMembers", true);
            DisplayNpcNames = LoadPreference("DisplayNpcNames", true);
            DisplayPlayerNames = LoadPreference("DisplayPlayerNames", true);
            AttackCancelsCast = LoadPreference("AttackCancelsCast", true);
            EnterCombatOnTarget = LoadPreference("EnterCombatOnTarget", false);
            ChangeTargetOnDeath = LoadPreference("ChangeTargetOnDeath", true);
            ClassicMode = LoadPreference("ClassicMode", false);
            ChatHidden = LoadPreference("ChatHidden", false);
            CastingIndicator = LoadPreference("CastingIndicator", true);
            HostileTileMarkers = LoadPreference("EnemyTileMarkers", true);
            PartyTileMarkers = LoadPreference("PartyTileMarkers", true);
            SelfTileMarkers = LoadPreference("SelfTileMarkers", true);
        }

        public virtual void SavePreferences()
        {
            SavePreference("MusicVolume", MusicVolume.ToString());
            SavePreference("SoundVolume", SoundVolume.ToString());
            SavePreference("Fullscreen", FullScreen.ToString());
            SavePreference("Resolution", TargetResolution.ToString());
            SavePreference("Fps", TargetFps.ToString());
            SavePreference("HideOthersOnWindowOpen", HideOthersOnWindowOpen.ToString());
            SavePreference("TargetAccountDirection", TargetAccountDirection.ToString());
            SavePreference("StickyTarget", StickyTarget.ToString());
            SavePreference("TapToTurn", TapToTurn.ToString());
            SavePreference("FaceOnLock", FaceOnLock.ToString());
            SavePreference("LeftClickTarget", LeftClickTarget.ToString());
            SavePreference("CombatShake", CombatShake.ToString());
            SavePreference("CombatFlash", CombatFlash.ToString());
            SavePreference("FadeTransitions", FadeTransitions.ToString());
            SavePreference("DisplayPartyMembers", DisplayPartyMembers.ToString());
            SavePreference("DisplayClanMembers", DisplayClanMembers.ToString());
            SavePreference("DisplayNpcNames", DisplayNpcNames.ToString());
            SavePreference("DisplayPlayerNames", DisplayPlayerNames.ToString());
            SavePreference("AttackCancelsCast", AttackCancelsCast.ToString());
            SavePreference("EnterCombatOnTarget", EnterCombatOnTarget.ToString());
            SavePreference("ChangeTargetOnDeath", ChangeTargetOnDeath.ToString());
            SavePreference("ClassicMode", ClassicMode.ToString());
            SavePreference("ChatHidden", ChatHidden.ToString());
            SavePreference("CastingIndicator", CastingIndicator.ToString());
            SavePreference("EnemyTileMarkers", HostileTileMarkers.ToString());
            SavePreference("PartyTileMarkers", PartyTileMarkers.ToString());
            SavePreference("SelfTileMarkers", SelfTileMarkers.ToString());
        }

        public abstract bool LoadConfig();

    }

}
