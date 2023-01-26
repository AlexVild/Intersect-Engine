﻿using Intersect.Client.Framework.Gwen.Control;
using Intersect.Client.Interface.Components;
using Intersect.GameObjects;
using Intersect.Network.Packets.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Intersect.Client.Interface.Game.Components
{
    public class ChallengeRowComponent : GwenComponent
    {
        public ChallengeProgression ProgressionDetails { get; set; }

        ChallengeDescriptor Descriptor { get; set; }
        Guid ChallengeId { get; set; }

        bool Unlocked { get; set; }
        bool Completed { get; set; }

        readonly string LockedName = "???";
        readonly string LockedTexture = "unknown.png";

        string Frame => Completed ? "challenge_frame_completed.png" : 
            Unlocked ? "challenge_frame_active.png" : "challenge_frame_locked.png";

        private ChallengeImageFrameComponent ChallengeImage { get; set; }

        private Color LockedColor => new Color(255, 100, 100, 100);
        private Color SecondaryColor => new Color(255, 180, 180, 180);
        private Color PrimaryColor => new Color(255, 255, 255, 255);

        private string BarTexture => Completed ? "challenge_progress_bar_complete.png" : "challenge_progress_bar_fg.png";

        private Color TitleColor
        {
            get
            {
                if (Unlocked)
                {
                    return Completed ? SecondaryColor : PrimaryColor;
                }
                return LockedColor;
            }
        }

        private ChallengeProgressBarComponent ProgressBar { get; set; }

        private Button DetailsButton { get; set; }
        private Label Title { get; set; }

        public int Progress { get; set; }

        public float PercentComplete { get; set; }

        public int X => ParentContainer.X;
        public int Y => ParentContainer.Y;

        public int Height => ParentContainer.Height;
        public int Width => ParentContainer.Width;

        public Label DescriptionTemplate { get; set; }
        public RichLabel Description { get; set; }

        public int RequiredLevel { get; set; }
        public string WeaponType { get; set; }
        public int CurrentLevel { get; set; }
        public long RequiredExp { get; set; }

        public ChallengeRowComponent(Base parent,
            string containerName,
            Guid challengeId,
            ChallengeProgression progression,
            int level,
            int currentLevel,
            long requiredExp,
            string weaponTypeName,
            ComponentList<GwenComponent> referenceList = null
            ) : base(parent, containerName, "ChallengeRowComponent", referenceList)
        {
            Descriptor = ChallengeDescriptor.Get(challengeId);
            ChallengeId = challengeId;
            ProgressionDetails = progression;

            Unlocked = ProgressionDetails != default;
            Progress = ProgressionDetails?.Reps ?? 0;
            Completed = ProgressionDetails?.Completed ?? false;
            RequiredLevel = level;
            WeaponType = weaponTypeName;
            RequiredExp = requiredExp;
            CurrentLevel = currentLevel;

            PercentComplete = (float)Progress / (Descriptor?.Reps ?? 1);
        }

        public override void Initialize()
        {
            SelfContainer = new ImagePanel(ParentContainer, ComponentName);

            ChallengeImage = new ChallengeImageFrameComponent(SelfContainer,
                "Icon",
                Frame,
                Descriptor?.Icon ?? LockedTexture,
                Framework.File_Management.GameContentManager.TextureType.Challenge,
                1,
                8,
                ChallengeId);

            Title = new Label(SelfContainer, "ChallengeName")
            {
                Text = Descriptor?.Name ?? "NOT FOUND"
            };

            DescriptionTemplate = new Label(SelfContainer, "Description");
            Description = new RichLabel(SelfContainer);

            var completionString = (PercentComplete * 100).ToString("N1");
            ProgressBar = new ChallengeProgressBarComponent(
                SelfContainer,
                "ProgressBar",
                "challenge_progress_bar_bg.png",
                BarTexture,
                bottomLabel: Completed ? "COMPLETE!" : $"{completionString}%",
                bottomLabelColor: TitleColor
                );
            ProgressBar.Percent = Completed ? 1.0f : PercentComplete;

            base.Initialize();
            FitParentToComponent();

            Title.SetTextColor(TitleColor, Label.ControlState.Normal);

            var requirementText = $"Requires {WeaponType} level {RequiredLevel}";
            if (RequiredLevel - 1 == CurrentLevel)
            {
                requirementText = $"Requires {RequiredExp} more EXP";
            }
            if (Unlocked)
            {
                Description.SetText(Descriptor.GetDescription(), DescriptionTemplate, 240);
            }
            else
            {
                Description.SetText(requirementText, DescriptionTemplate, 240);
            }

            ChallengeImage.Initialize();
            ProgressBar.Initialize();

            if (Completed)
            {
                ChallengeImage.SetImageRenderColor(new Color(160, 255, 255, 255));
            }
            else if (!Unlocked)
            {
                ChallengeImage.SetImageRenderColor(new Color(90, 255, 255, 255));
                ProgressBar.Hide();
            }
        }

        public virtual void SetPosition(float x, float y)
        {
            ParentContainer.SetPosition(x, y);
        }

        public override void Dispose()
        {
            ChallengeImage.Dispose();
            base.Dispose();
        }
    }
}
