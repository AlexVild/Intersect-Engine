﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Intersect.Client.Core;
using Intersect.Client.Entities.Events;
using Intersect.Client.Entities.Projectiles;
using Intersect.Client.Framework.File_Management;
using Intersect.Client.Framework.GenericClasses;
using Intersect.Client.Framework.Graphics;
using Intersect.Client.General;
using Intersect.Client.Items;
using Intersect.Client.Localization;
using Intersect.Client.Maps;
using Intersect.Client.Spells;
using Intersect.Enums;
using Intersect.GameObjects;
using Intersect.GameObjects.Maps;
using Intersect.Logging;
using Intersect.Network.Packets.Server;
using Intersect.Utilities;
using static Intersect.Client.Framework.File_Management.GameContentManager;

namespace Intersect.Client.Entities
{

    public partial class Entity
    {

        public enum LabelType
        {

            Header = 0,

            Footer,

            Name,

            ChatBubble,

            Guild

        }

        public int AnimationFrame;

        //Entity Animations
        public List<Animation> Animations = new List<Animation>();

        //Animation Timer (for animated sprites)
        public long AnimationTimer;

        //Combat
        public long AttackTimer { get; set; } = 0;
        public int AttackTime { get; set; } = -1;

        public bool Blocking = false;

        //Combat Status
        public long CastTime = 0;

        //Dashing instance
        public Dash Dashing;

        public Queue<Dash> DashQueue = new Queue<Dash>();

        public long DashTimer;

        public float elapsedtime; //to be removed

        public Guid[] Equipment = new Guid[Options.EquipmentSlots.Count];

        public Animation[] EquipmentAnimations = new Animation[Options.EquipmentSlots.Count];

        //Extras
        public string Face = "";

        public Label FooterLabel;

        public Gender Gender = Gender.Male;

        public Label HeaderLabel;

        public bool HideEntity = false;

        public bool HideName;

        //Core Values
        public Guid Id;

        //Inventory/Spells/Equipment
        public Item[] Inventory = new Item[Options.MaxInvItems];

        public bool InView = true;

        public bool IsLocal = false;

        public bool IsMoving;

        //Caching
        public MapInstance LatestMap;

        public int Level = 1;

        //Vitals & Stats
        public int[] MaxVital = new int[(int) Vitals.VitalCount];

        protected Pointf mCenterPos = Pointf.Empty;

        //Chat
        private List<ChatBubble> mChatBubbles = new List<ChatBubble>();

        private byte mDir;

        protected bool mDisposed;

        private long mLastUpdate;

        protected string mMySprite = "";

        public Color Color = new Color(255,255,255,255);

        public int MoveDir = -1;

        public long MoveTimer;

        protected byte mRenderPriority = 1;

        protected string mTransformedSprite = "";

        private long mWalkTimer;

        public int[] MyEquipment = new int[Options.EquipmentSlots.Count];

        public string Name = "";

        public Color NameColor = null;

        public float OffsetX;

        public float OffsetY;

        public bool Passable;

        //Rendering Variables
        public HashSet<Entity> RenderList;

        public Guid SpellCast;

        public Spell[] Spells = new Spell[Options.MaxPlayerSkills];

        public int[] Stat = new int[(int) Stats.StatCount];

        public int[] TrueStats = new int[(int)Stats.StatCount];

        public int Target = -1;

        public Guid EntityTarget = Guid.Empty;

        public GameTexture Texture;

        #region "Animation Textures and Timing"
        public SpriteAnimations SpriteAnimation = SpriteAnimations.Normal;

        public Dictionary<SpriteAnimations,GameTexture> AnimatedTextures = new Dictionary<SpriteAnimations, GameTexture>();

        public int SpriteFrame = 0;

        public long SpriteFrameTimer = -1;

        public long LastActionTime = -1;
        #endregion

        public int Type;

        public int[] Vital = new int[(int) Vitals.VitalCount];

        public int WalkFrame;

        public FloatRect WorldPos = new FloatRect();

        public bool IsTargeted = false;

        //Location Info
        public byte X;

        public byte Y;

        public byte Z;

        public string[] MyDecors = new string[Options.DecorSlots.Count];

        public bool Flash = false; // Whether or not the entity sprite is flashing

        public Color FlashColor = null; // What color to flash the entity

        public long FlashEndTime = 0L;

        public Entity(Guid id, EntityPacket packet, bool isEvent = false)
        {
            Id = id;
            CurrentMap = Guid.Empty;
            if (id != Guid.Empty && !isEvent)
            {
                for (var i = 0; i < Options.MaxInvItems; i++)
                {
                    Inventory[i] = new Item();
                }

                for (var i = 0; i < Options.MaxPlayerSkills; i++)
                {
                    Spells[i] = new Spell();
                }

                for (var i = 0; i < Options.EquipmentSlots.Count; i++)
                {
                    Equipment[i] = Guid.Empty;
                    MyEquipment[i] = -1;
                }

                for (var i = 0; i < Options.DecorSlots.Count; i++)
                {
                    MyDecors[i] = null;
                }
            }

            AnimationTimer = Timing.Global.Milliseconds + Globals.Random.Next(0, 500);

            //TODO Remove because fixed orrrrr change the exception text
            if (Options.EquipmentSlots.Count == 0)
            {
                throw new Exception("What the fuck is going on!?!?!?!?!?!");
            }

            Load(packet);
        }

        //Status effects
        public List<Status> Status { get; private set; } = new List<Status>();

        public byte Dir
        {
            get => mDir;
            set => mDir = (byte) ((value + 4) % 4);
        }

        public virtual string TransformedSprite
        {
            get => mTransformedSprite;
            set
            {
                if (mTransformedSprite != value)
                {
                    mTransformedSprite = value;
                    if (value == "")
                    {
                        Texture = Globals.ContentManager.GetTexture(GameContentManager.TextureType.Entity, mMySprite);
                        LoadAnimationTextures(mMySprite);
                    }
                    else
                    {
                        Texture = Globals.ContentManager.GetTexture(GameContentManager.TextureType.Entity, mTransformedSprite);
                        LoadAnimationTextures(mTransformedSprite);
                    }
                }
            }
        }

        public virtual string MySprite
        {
            get => mMySprite;
            set
            {
                if (mMySprite != value)
                {
                    mMySprite = value;
                    Texture = Globals.ContentManager.GetTexture(GameContentManager.TextureType.Entity, mMySprite);
                    LoadAnimationTextures(mMySprite);
                }
            }
        }

        public virtual int SpriteFrames
        {
            get
            {
                switch (SpriteAnimation)
                {
                    case SpriteAnimations.Normal:
                        return Options.Instance.Sprites.NormalFrames;
                    case SpriteAnimations.Idle:
                        return Options.Instance.Sprites.IdleFrames;
                    case SpriteAnimations.Attack:
                        return Options.Instance.Sprites.AttackFrames;
                    case SpriteAnimations.Shoot:
                        return Options.Instance.Sprites.ShootFrames;
                    case SpriteAnimations.Cast:
                        return Options.Instance.Sprites.CastFrames;
                    case SpriteAnimations.Weapon:
                        return Options.Instance.Sprites.WeaponFrames;
                }

                return Options.Instance.Sprites.NormalFrames;

            }
        }

        public MapInstance MapInstance => MapInstance.Get(CurrentMap);

        public virtual Guid CurrentMap { get; set; }

        public virtual EntityTypes GetEntityType()
        {
            return EntityTypes.GlobalEntity;
        }

        //Deserializing
        public virtual void Load(EntityPacket packet)
        {
            if (packet == null)
            {
                return;
            }

            CurrentMap = packet.MapId;
            Name = packet.Name;
            MySprite = packet.Sprite;
            Color = packet.Color;
            Face = packet.Face;
            Level = packet.Level;
            X = packet.X;
            Y = packet.Y;
            Z = packet.Z;
            Dir = packet.Dir;
            Passable = packet.Passable;
            HideName = packet.HideName;
            HideEntity = packet.HideEntity;
            NameColor = packet.NameColor;
            HeaderLabel = new Label(packet.HeaderLabel.Label, packet.HeaderLabel.Color);
            FooterLabel = new Label(packet.FooterLabel.Label, packet.FooterLabel.Color);

            var animsToClear = new List<Animation>();
            var animsToAdd = new List<AnimationBase>();
            for (var i = 0; i < packet.Animations.Length; i++)
            {
                var anim = AnimationBase.Get(packet.Animations[i]);
                if (anim != null)
                {
                    animsToAdd.Add(anim);
                }
            }

            foreach (var anim in Animations)
            {
                animsToClear.Add(anim);
                if (!anim.InfiniteLoop)
                {
                    animsToClear.Remove(anim);
                }
                else
                {
                    foreach (var addedAnim in animsToAdd)
                    {
                        if (addedAnim.Id == anim.MyBase.Id)
                        {
                            animsToClear.Remove(anim);
                            animsToAdd.Remove(addedAnim);

                            break;
                        }
                    }

                    foreach (var equipAnim in EquipmentAnimations)
                    {
                        if (equipAnim == anim)
                        {
                            animsToClear.Remove(anim);
                        }
                    }
                }
            }

            ClearAnimations(animsToClear);
            AddAnimations(animsToAdd);

            Vital = packet.Vital;
            MaxVital = packet.MaxVital;

            //Update status effects
            Status.Clear();

            if (packet.StatusEffects == null)
            {
                Log.Warn($"'{nameof(packet)}.{nameof(packet.StatusEffects)}' is null.");
            }
            else
            {
                foreach (var status in packet.StatusEffects)
                {
                    var instance = new Status(
                        status.SpellId, status.Type, status.TransformSprite, status.TimeRemaining, status.TotalDuration
                    );

                    Status?.Add(instance);

                    if (instance.Type == StatusTypes.Shield)
                    {
                        instance.Shield = status.VitalShields;
                    }
                }
            }

            SortStatuses();
            Stat = packet.Stats;

            mDisposed = false;

            //Status effects box update
            if (Globals.Me == null)
            {
                Log.Warn($"'{nameof(Globals.Me)}' is null.");
            }
            else
            {
                if (Id == Globals.Me.Id)
                {
                    if (Interface.Interface.GameUi == null)
                    {
                        Log.Warn($"'{nameof(Interface.Interface.GameUi)}' is null.");
                    }
                    else
                    {
                        if (Interface.Interface.GameUi.PlayerBox == null)
                        {
                            Log.Warn($"'{nameof(Interface.Interface.GameUi.PlayerBox)}' is null.");
                        }
                        else
                        {
                            Interface.Interface.GameUi.PlayerBox.UpdateStatuses = true;
                        }
                    }
                }
                else if (Id != Guid.Empty && Id == Globals.Me.TargetIndex)
                {
                    if (Globals.Me.TargetBox == null)
                    {
                        Log.Warn($"'{nameof(Globals.Me.TargetBox)}' is null.");
                    }
                    else
                    {
                        Globals.Me.TargetBox.UpdateStatuses = true;
                    }
                }
            }
        }

        public void AddAnimations(List<AnimationBase> anims)
        {
            foreach (var anim in anims)
            {
                Animations.Add(new Animation(anim, true, false, -1, this));
            }
        }

        public void ClearAnimations(List<Animation> anims)
        {
            if (anims == null)
            {
                anims = Animations;
            }

            if (anims.Count > 0)
            {
                for (var i = 0; i < anims.Count; i++)
                {
                    anims[i].Dispose();
                    Animations.Remove(anims[i]);
                }
            }
        }

        public virtual bool IsDisposed()
        {
            return mDisposed;
        }

        public virtual void Dispose()
        {
            if (RenderList != null)
            {
                RenderList.Remove(this);
            }

            ClearAnimations(null);
            mDisposed = true;
        }

        //Returns the amount of time required to traverse 1 tile
        public virtual float GetMovementTime()
        {
            var speed = Stat[(int)Stats.Speed];
            if (this is Player player && player.InVehicle)
            {
                speed = (int) player.VehicleSpeed;
            }

            var time = 1000f / (float) (1 + Math.Log(speed * Options.AgilityMovementSpeedModifier));
            if (Blocking)
            {
                time += time * (float) Options.BlockingSlow;
            }
            
            time *= (float)Options.SpeedModifier;

            if (this is Player pl && pl.CombatMode)
            {
                var moveDir = pl.Dir;
                var faceDir = pl.FaceDirection;

                if (moveDir != faceDir)
                {
                    switch (moveDir)
                    {
                        //up
                        case 0:
                            if (faceDir == 1)
                            {
                                time *= Options.Instance.CombatOpts.CombatModeBackModifier;
                            }
                            else
                            {
                                time *= Options.Instance.CombatOpts.CombatModeStrafeModifier;
                            }

                            break;
                        //down
                        case 1:
                            if (faceDir == 0)
                            {
                                time *= Options.Instance.CombatOpts.CombatModeBackModifier;
                            }
                            else
                            {
                                time *= Options.Instance.CombatOpts.CombatModeStrafeModifier;
                            }
                            break;
                        //left
                        case 2:
                            if (faceDir == 3)
                            {
                                time *= Options.Instance.CombatOpts.CombatModeBackModifier;
                            }
                            else
                            {
                                time *= Options.Instance.CombatOpts.CombatModeStrafeModifier;
                            }
                            break;
                        //right
                        case 3:
                            if (faceDir == 2)
                            {
                                time *= Options.Instance.CombatOpts.CombatModeBackModifier;
                            }
                            else
                            {
                                time *= Options.Instance.CombatOpts.CombatModeStrafeModifier;
                            }
                            break;
                    }
                }
            }
            if (StatusIsActive(StatusTypes.Slowed))
            {
                time *= Options.Instance.CombatOpts.SlowedModifier;
            }
            else if (StatusIsActive(StatusTypes.Haste))
            {
                time /= Options.Instance.CombatOpts.HasteModifier;
            }

            return Math.Min(1000f, time);
        }
        
        //Movement Processing
        public virtual bool Update()
        {
            MapInstance map = null;
            if (mDisposed)
            {
                LatestMap = null;

                return false;
            }
            else
            {
                map = MapInstance.Get(CurrentMap);
                LatestMap = map;
                if (map == null || !map.InView())
                {
                    Globals.EntitiesToDispose.Add(Id);

                    return false;
                }
            }

            RenderList = DetermineRenderOrder(RenderList, map);
            if (mLastUpdate == 0)
            {
                mLastUpdate = Timing.Global.Milliseconds;
            }

            var ecTime = (float)(Timing.Global.Milliseconds - mLastUpdate);
            elapsedtime = ecTime;

            // Update flash timer
            if (Flash && Timing.Global.Milliseconds > FlashEndTime)
            {
                Flash = false;
            }

            if (Dashing != null)
            {
                WalkFrame = Options.Instance.Sprites.NormalSheetDashFrame; //Fix the frame whilst dashing
            }
            else if (mWalkTimer < Timing.Global.Milliseconds)
            {
                if (!IsMoving && DashQueue.Count > 0)
                {
                    Dashing = DashQueue.Dequeue();
                    Dashing.Start(this);
                    OffsetX = 0;
                    OffsetY = 0;
                    DashTimer = Timing.Global.Milliseconds + Options.MaxDashSpeed;
                }
                else
                {
                    if (IsMoving)
                    {
                        WalkFrame++;
                        if (WalkFrame >= SpriteFrames)
                        {
                            WalkFrame = 0;
                        }
                    }
                    else
                    {
                        if (WalkFrame > 0 && WalkFrame / SpriteFrames < 0.7f)
                        {
                            WalkFrame = (int)SpriteFrames / 2;
                        }
                        else
                        {
                            WalkFrame = 0;
                        }
                    }

                    mWalkTimer = Timing.Global.Milliseconds + Options.Instance.Sprites.MovingFrameDuration;
                }
            }

            if (Dashing != null)
            {
                if (Dashing.Update(this))
                {
                    OffsetX = Dashing.GetXOffset();
                    OffsetY = Dashing.GetYOffset();
                }
                else
                {
                    OffsetX = 0;
                    OffsetY = 0;
                }
            }
            else if (IsMoving)
            {
                switch (Dir)
                {
                    case 0:
                        OffsetY -= (float) ecTime * (float) Options.TileHeight / GetMovementTime();
                        OffsetX = 0;
                        if (OffsetY < 0)
                        {
                            OffsetY = 0;
                        }

                        break;

                    case 1:
                        OffsetY += (float) ecTime * (float) Options.TileHeight / GetMovementTime();
                        OffsetX = 0;
                        if (OffsetY > 0)
                        {
                            OffsetY = 0;
                        }

                        break;

                    case 2:
                        OffsetX -= (float) ecTime * (float) Options.TileHeight / GetMovementTime();
                        OffsetY = 0;
                        if (OffsetX < 0)
                        {
                            OffsetX = 0;
                        }

                        break;

                    case 3:
                        OffsetX += (float) ecTime * (float) Options.TileHeight / GetMovementTime();
                        OffsetY = 0;
                        if (OffsetX > 0)
                        {
                            OffsetX = 0;
                        }

                        break;
                }

                if (OffsetX == 0 && OffsetY == 0)
                {
                    IsMoving = false;
                }
            }

            //Check to see if we should start or stop equipment animations
            if (Equipment.Length == Options.EquipmentSlots.Count)
            {
                for (var z = 0; z < Options.EquipmentSlots.Count; z++)
                {
                    if (Equipment[z] != Guid.Empty && (this != Globals.Me || MyEquipment[z] < Options.MaxInvItems))
                    {
                        var itemId = Guid.Empty;
                        if (this == Globals.Me)
                        {
                            var slot = MyEquipment[z];
                            if (slot > -1)
                            {
                                itemId = Inventory[slot].ItemId;
                            }
                        }
                        else
                        {
                            itemId = Equipment[z];
                        }

                        var itm = ItemBase.Get(itemId);
                        AnimationBase anim = null;
                        if (itm != null)
                        {
                            anim = itm.EquipmentAnimation;
                        }

                        if (anim != null)
                        {
                            var inVehicle = this is Player player && player.InVehicle;
                            if (EquipmentAnimations[z] != null &&
                                (EquipmentAnimations[z].MyBase != anim || EquipmentAnimations[z].Disposed() || inVehicle))
                            {
                                EquipmentAnimations[z].Dispose();
                                Animations.Remove(EquipmentAnimations[z]);
                                EquipmentAnimations[z] = null;
                            }

                            if (EquipmentAnimations[z] == null && !inVehicle)
                            {
                                EquipmentAnimations[z] = new Animation(anim, true, true, -1, this);
                                Animations.Add(EquipmentAnimations[z]);
                            }
                        }
                        else
                        {
                            if (EquipmentAnimations[z] != null)
                            {
                                EquipmentAnimations[z].Dispose();
                                Animations.Remove(EquipmentAnimations[z]);
                                EquipmentAnimations[z] = null;
                            }
                        }
                    }
                    else
                    {
                        if (EquipmentAnimations[z] != null)
                        {
                            EquipmentAnimations[z].Dispose();
                            Animations.Remove(EquipmentAnimations[z]);
                            EquipmentAnimations[z] = null;
                        }
                    }
                }
            }

            var chatbubbles = mChatBubbles.ToArray();
            foreach (var chatbubble in chatbubbles)
            {
                if (!chatbubble.Update())
                {
                    mChatBubbles.Remove(chatbubble);
                }
            }

            if (AnimationTimer < Timing.Global.Milliseconds)
            {
                AnimationTimer = Timing.Global.Milliseconds + 200;
                AnimationFrame++;
                if (AnimationFrame >= SpriteFrames)
                {
                    AnimationFrame = 0;
                }
            }

            CalculateCenterPos();

            List<Animation> animsToRemove = null;
            foreach (var animInstance in Animations)
            {
                animInstance.Update();

                //If disposed mark to be removed and continue onward
                if (animInstance.Disposed())
                {
                    if (animsToRemove == null)
                    {
                        animsToRemove = new List<Animation>();
                    }

                    animsToRemove.Add(animInstance);

                    continue;
                }

                if (IsStealthed())
                {
                    animInstance.Hide();
                }
                else
                {
                    animInstance.Show();
                }

                if (animInstance.AutoRotate)
                {
                    animInstance.SetPosition(
                        (int) Math.Ceiling(GetCenterPos().X), (int) Math.Ceiling(GetCenterPos().Y), X, Y, CurrentMap,
                        Dir, Z
                    );
                }
                else
                {
                    animInstance.SetPosition(
                        (int) Math.Ceiling(GetCenterPos().X), (int) Math.Ceiling(GetCenterPos().Y), X, Y, CurrentMap,
                        -1, Z
                    );
                }
            }

            if (animsToRemove != null)
            {
                foreach (var anim in animsToRemove)
                {
                    Animations.Remove(anim);
                }
            }

            mLastUpdate = Timing.Global.Milliseconds;

            UpdateSpriteAnimation();

            return true;
        }

        public virtual int CalculateAttackTime()
        {
            //If this is an npc we don't know it's attack time. Luckily the server provided it!
            if (this != Globals.Me && AttackTime > -1)
            {
                return AttackTime;
            }

            //Otherwise return the legacy attack speed calculation
            return (int) (Options.MaxAttackRate +
                          (float) ((Options.MinAttackRate - Options.MaxAttackRate) *
                                   (((float) Options.MaxStatValue - Stat[(int) Stats.Speed]) /
                                    (float) Options.MaxStatValue)));
        }

        public virtual bool IsStealthed()
        {
            //If the entity has transformed, apply that sprite instead.
            if (this == Globals.Me)
            {
                return false;
            }

            for (var n = 0; n < Status.Count; n++)
            {
                if (Status[n].Type == StatusTypes.Stealth)
                {
                    return true;
                }
            }

            return false;
        }

        public virtual HashSet<Entity> DetermineRenderOrder(HashSet<Entity> renderList, MapInstance map)
        {
            if (renderList != null)
            {
                renderList.Remove(this);
            }

            if (map == null || Globals.Me == null || Globals.Me.MapInstance == null)
            {
                return null;
            }

            var gridX = Globals.Me.MapInstance.MapGridX;
            var gridY = Globals.Me.MapInstance.MapGridY;
            for (var x = gridX - 1; x <= gridX + 1; x++)
            {
                for (var y = gridY - 1; y <= gridY + 1; y++)
                {
                    if (x >= 0 &&
                        x < Globals.MapGridWidth &&
                        y >= 0 &&
                        y < Globals.MapGridHeight &&
                        Globals.MapGrid[x, y] != Guid.Empty)
                    {
                        if (Globals.MapGrid[x, y] == CurrentMap)
                        {
                            var priority = mRenderPriority;
                            if (Z != 0)
                            {
                                priority += 3;
                            }

                            HashSet<Entity> renderSet;

                            if (y == gridY - 1)
                            {
                                renderSet = Graphics.RenderingEntities[priority, Options.MapHeight + Y];
                            }
                            else if (y == gridY)
                            {
                                renderSet = Graphics.RenderingEntities[priority, Options.MapHeight * 2 + Y];
                            }
                            else
                            {
                                renderSet = Graphics.RenderingEntities[priority, Options.MapHeight * 3 + Y];
                            }

                            renderSet.Add(this);
                            renderList = renderSet;

                            return renderList;
                        }
                    }
                }
            }

            return renderList;
        }

        public virtual int DetermineRenderDirection(Byte dir)
        {
            switch (dir)
            {
                case 0:
                    return 3;

                    break;
                case 1:
                    return 0;

                    break;
                case 2:
                    return 1;

                    break;
                case 3:
                    return 2;

                    break;
                default:
                    return 3;
            }
        }

        //Rendering Functions
        public virtual void Draw()
        {
            if (HideEntity)
            {
                return; //Don't draw if the entity is hidden
            }

            WorldPos.Reset();
            var map = MapInstance.Get(CurrentMap);
            if (map == null || !Globals.GridMaps.Contains(CurrentMap))
            {
                return;
            }

            var srcRectangle = new FloatRect();
            var destRectangle = new FloatRect();
            var dir = 0;

            var sprite = "";
            // Copy the actual render color, because we'll be editing it later and don't want to overwrite it.
            var renderColor = new Color(Color.A, Color.R, Color.G, Color. B);
            if (Flash && FlashColor != null) // Flash the sprite some color for some duration
            {
                renderColor = FlashColor;
            }

            string transformedSprite = "";

            //If the entity has transformed, apply that sprite instead.
            for (var n = 0; n < Status.Count; n++)
            {
                if (Status[n].Type == StatusTypes.Transform)
                {
                    sprite = Status[n].Data;
                    transformedSprite = sprite;
                }

                //If unit is stealthed, don't render unless the entity is the player.
                if (Status[n].Type == StatusTypes.Stealth)
                {
                    if (this != Globals.Me && !(this is Player player && Globals.Me.IsInMyParty(player)))
                    {
                        return;
                    }
                    else
                    {
                        renderColor.A /= 2;
                    }
                }
            }

            if (this is Player p && p.InVehicle)
            {
                sprite = p.VehicleSprite;
                transformedSprite = sprite;
            }

            if (transformedSprite != TransformedSprite)
            {
                TransformedSprite = transformedSprite;
            }

            //Check if there is no transformed sprite set
            if (string.IsNullOrEmpty(sprite))
            {
                sprite = MySprite;
                MySprite = sprite;
            }

            AnimatedTextures.TryGetValue(SpriteAnimation, out var texture);
            texture = texture ?? Texture;

            if (texture != null)
            {
                if (texture.GetHeight() / Options.Instance.Sprites.Directions > Options.TileHeight)
                {
                    destRectangle.X = map.GetX() + X * Options.TileWidth + OffsetX + Options.TileWidth / 2;
                    destRectangle.Y = GetCenterPos().Y - texture.GetHeight() / (Options.Instance.Sprites.Directions * 2);
                }
                else
                {
                    destRectangle.X = map.GetX() + X * Options.TileWidth + OffsetX + Options.TileWidth / 2;
                    destRectangle.Y = map.GetY() + Y * Options.TileHeight + OffsetY;
                }

                destRectangle.X -= texture.GetWidth() / (SpriteFrames * 2);

                var paperdollDir = Dir;
                if (this is Player player && player.CombatMode)
                {
                    dir = DetermineRenderDirection(player.FaceDirection);
                    paperdollDir = player.FaceDirection;
                }
                else
                {
                    dir = DetermineRenderDirection(Dir);
                }

                destRectangle.X = (int)Math.Ceiling(destRectangle.X);
                destRectangle.Y = (int)Math.Ceiling(destRectangle.Y);
                if (Options.AnimatedSprites.Contains(sprite.ToLower()))
                {
                    srcRectangle = new FloatRect(
                        AnimationFrame * (int)texture.GetWidth() / SpriteFrames, dir * (int)texture.GetHeight() / Options.Instance.Sprites.Directions,
                        (int)texture.GetWidth() / SpriteFrames, (int)texture.GetHeight() / Options.Instance.Sprites.Directions
                    );
                }
                else
                {
                    if (SpriteAnimation == SpriteAnimations.Normal)
                    {
                        bool inAction = AttackTimer - CalculateAttackTime() / 2 > Timing.Global.Ticks / TimeSpan.TicksPerMillisecond || Blocking;
                        if (inAction && !(this is Player play && play.InVehicle))
                        {
                            srcRectangle = new FloatRect(
                                Options.Instance.Sprites.NormalSheetAttackFrame * (int)texture.GetWidth() / SpriteFrames, dir * (int)texture.GetHeight() / Options.Instance.Sprites.Directions,
                                (int)texture.GetWidth() / SpriteFrames, (int)texture.GetHeight() / Options.Instance.Sprites.Directions
                            );
                        }
                        else
                        {
                            //Restore Original Attacking/Blocking Code
                            srcRectangle = new FloatRect(
                                WalkFrame * (int) texture.GetWidth() / SpriteFrames, dir * (int) texture.GetHeight() / Options.Instance.Sprites.Directions,
                                (int) texture.GetWidth() / SpriteFrames, (int) texture.GetHeight() / Options.Instance.Sprites.Directions
                            );
                        }
                    }
                    else
                    {
                        srcRectangle = new FloatRect(
                            SpriteFrame * (int)texture.GetWidth() / SpriteFrames, dir * (int)texture.GetHeight() / Options.Instance.Sprites.Directions,
                            (int)texture.GetWidth() / SpriteFrames, (int)texture.GetHeight() / Options.Instance.Sprites.Directions
                        );
                    }
                }

                destRectangle.Width = srcRectangle.Width;
                destRectangle.Height = srcRectangle.Height;

                WorldPos = destRectangle;

                //Order the layers of paperdolls and sprites
                for (var z = 0; z < Options.PaperdollOrder[paperdollDir].Count; z++)
                {
                    var stuff = Options.PaperdollOrder;
                    var paperdoll = Options.PaperdollOrder[paperdollDir][z];
                    var equipSlot = Options.EquipmentSlots.IndexOf(paperdoll);
                    var decorSlot = Options.DecorSlots.IndexOf(paperdoll);

                    //Don't render the paperdolls if they have transformed.
                    var notTransformed = sprite == MySprite && Equipment.Length == Options.EquipmentSlots.Count;

                    if (this is Player pl && pl.InVehicle)
                    {
                        notTransformed = false;
                    }

                    //Check for player
                    if (paperdoll == "Player")
                    {
                        Graphics.DrawGameTexture(
                            texture, srcRectangle, destRectangle, renderColor
                        );
                    }
                    else if (notTransformed)
                    {
                        if (equipSlot > -1)
                        {
                            if (Equipment[equipSlot] != Guid.Empty && this != Globals.Me ||
                                MyEquipment[equipSlot] < Options.MaxInvItems)
                            {
                                var itemId = Guid.Empty;
                                if (this == Globals.Me)
                                {
                                    var slot = MyEquipment[equipSlot];
                                    if (slot > -1)
                                    {
                                        itemId = Inventory[slot].ItemId;
                                    }
                                }
                                else
                                {
                                    itemId = Equipment[equipSlot];
                                }

                                var item = ItemBase.Get(itemId);
                                if (item != null)
                                {
                                    if (Gender == 0)
                                    {
                                        DrawEquipment(item.MalePaperdoll, renderColor.A, dir);
                                    }
                                    else
                                    {
                                        DrawEquipment(item.FemalePaperdoll, renderColor.A, dir);
                                    }
                                }
                            }
                        } else if (decorSlot > -1 && decorSlot < Options.Player.DecorSlots.Count)
                        {
                            var hideHair = false;
                            var hideBeard = false;
                            var hideExtra = false;
                            var shouldDraw = true;

                            if (Equipment[Options.HelmetIndex] != Guid.Empty)
                            {
                                var helmet = ItemBase.Get(Equipment[Options.HelmetIndex]);
                                hideHair = helmet.HideHair;
                                hideBeard = helmet.HideBeard;
                                hideExtra = helmet.HideExtra;

                                if (decorSlot == Options.HairSlot && hideHair
                                    || decorSlot == Options.BeardSlot && hideBeard
                                    || decorSlot == Options.ExtraSlot && hideExtra)
                                {
                                    shouldDraw = false;
                                }
                            }

                            if (MyDecors[decorSlot] != null && shouldDraw)
                            {
                                DrawEquipment(MyDecors[decorSlot], renderColor.A, dir, GameContentManager.TextureType.Decor);
                            }
                        }
                    }
                }
            }
            else
            {
                // We don't have a texture to render, but we still want this to be targetable.
                WorldPos = new FloatRect(
                    map.GetX() + X * Options.TileWidth + OffsetX,
                    map.GetY() + Y * Options.TileHeight + OffsetY,
                    Options.TileWidth,
                    Options.TileHeight);
            }
        }

        public void DrawChatBubbles()
        {
            //Don't draw if the entity is hidden
            if (HideEntity)
            {
                return; 
            }

            //If unit is stealthed, don't render unless the entity is the player or party member.
            if (this != Globals.Me && !(this is Player player && Globals.Me.IsInMyParty(player)))
            {
                for (var n = 0; n < Status.Count; n++)
                {
                    if (Status[n].Type == StatusTypes.Stealth)
                    {
                        return;
                    }
                }
            }

            var chatbubbles = mChatBubbles.ToArray();
            var bubbleoffset = 0f;
            for (var i = chatbubbles.Length - 1; i > -1; i--)
            {
                bubbleoffset = chatbubbles[i].Draw(bubbleoffset);
            }
        }

        public virtual void DrawEquipment(string filename, int alpha, int dir, GameContentManager.TextureType textureType = GameContentManager.TextureType.Paperdoll)
        {
            var map = MapInstance.Get(CurrentMap);
            if (map == null)
            {
                return;
            }

            var srcRectangle = new FloatRect();
            var destRectangle = new FloatRect();

            GameTexture paperdollTex = null;
            var filenameNoExt = Path.GetFileNameWithoutExtension(filename);
            paperdollTex = Globals.ContentManager.GetTexture(
                textureType, $"{filenameNoExt}_{SpriteAnimation.ToString()}.png"
            );

            var spriteFrames = SpriteFrames;

            if (paperdollTex == null)
            {
                paperdollTex = Globals.ContentManager.GetTexture(textureType, filename);
                spriteFrames = Options.Instance.Sprites.NormalFrames;
            }

            if (paperdollTex != null)
            {
                if (paperdollTex.GetHeight() / Options.Instance.Sprites.Directions > Options.TileHeight)
                {
                    destRectangle.X = map.GetX() + X * Options.TileWidth + OffsetX;
                    destRectangle.Y = GetCenterPos().Y - paperdollTex.GetHeight() / (Options.Instance.Sprites.Directions * 2);
                }
                else
                {
                    destRectangle.X = map.GetX() + X * Options.TileWidth + OffsetX;
                    destRectangle.Y = map.GetY() + Y * Options.TileHeight + OffsetY;
                }

                if (paperdollTex.GetWidth() / spriteFrames > Options.TileWidth)
                {
                    destRectangle.X -= (paperdollTex.GetWidth() / spriteFrames - Options.TileWidth) / 2;
                }

                destRectangle.X = (int) Math.Ceiling(destRectangle.X);
                destRectangle.Y = (int) Math.Ceiling(destRectangle.Y);
                if (SpriteAnimation == SpriteAnimations.Normal)
                {
                    if (AttackTimer - CalculateAttackTime() / 2 > Timing.Global.Ticks / TimeSpan.TicksPerMillisecond || Blocking)
                    {
                        srcRectangle = new FloatRect(
                            3 * (int)paperdollTex.GetWidth() / spriteFrames, dir * (int)paperdollTex.GetHeight() / Options.Instance.Sprites.Directions,
                            (int)paperdollTex.GetWidth() / spriteFrames, (int)paperdollTex.GetHeight() / Options.Instance.Sprites.Directions
                        );
                    }
                    else
                    {
                        srcRectangle = new FloatRect(
                            WalkFrame * (int) paperdollTex.GetWidth() / spriteFrames, dir * (int) paperdollTex.GetHeight() / Options.Instance.Sprites.Directions,
                            (int) paperdollTex.GetWidth() / spriteFrames, (int) paperdollTex.GetHeight() / Options.Instance.Sprites.Directions
                        );
                    }
                }
                else
                {
                    srcRectangle = new FloatRect(
                        SpriteFrame * (int)paperdollTex.GetWidth() / spriteFrames, dir * (int)paperdollTex.GetHeight() / Options.Instance.Sprites.Directions,
                        (int)paperdollTex.GetWidth() / spriteFrames, (int)paperdollTex.GetHeight() / Options.Instance.Sprites.Directions
                    );
                }

                destRectangle.Width = srcRectangle.Width;
                destRectangle.Height = srcRectangle.Height;
                var renderColor = new Color(alpha, 255, 255, 255);
                if (Flash)
                {
                    renderColor = FlashColor;
                    renderColor.A = (byte) alpha;
                }

                Graphics.DrawGameTexture(
                    paperdollTex, srcRectangle, destRectangle, renderColor
                );
            }
        }

        protected virtual void CalculateCenterPos()
        {
            var pos = new Pointf(
                LatestMap.GetX() + X * Options.TileWidth + OffsetX + Options.TileWidth / 2,
                LatestMap.GetY() + Y * Options.TileHeight + OffsetY + Options.TileHeight / 2
            );

            if (Texture != null)
            {
                pos.Y += Options.TileHeight / 2;
                pos.Y -= Texture.GetHeight() / (Options.Instance.Sprites.Directions * 2);
            }

            mCenterPos = pos;
        }

        //returns the point on the screen that is the center of the player sprite
        public Pointf GetCenterPos()
        {
            if (LatestMap == null)
            {
                return new Pointf(0, 0);
            }

            return mCenterPos;
        }

        public virtual float GetTopPos(int overrideHeight = 0)
        {
            var map = LatestMap;
            if (map == null)
            {
                return 0f;
            }

            var y = (int) Math.Ceiling(GetCenterPos().Y);
            if (overrideHeight != 0)
            {
                y = y - (int) (overrideHeight / (Options.Instance.Sprites.Directions * 2));
                y -= 12;
            }
            else
            {
                if (Texture != null)
                {
                    y = y - (int) (Texture.GetHeight() / (Options.Instance.Sprites.Directions * 2));
                    y -= 12;
                }
            }

            if (GetType() != typeof(Event))
            {
                y -= 10;
            } //Need room for HP bar if not an event.

            return y;
        }

        public void DrawLabels(
            string label,
            int position,
            Color labelColor,
            Color textColor,
            Color borderColor = null,
            Color backgroundColor = null
        )
        {
            if (string.IsNullOrEmpty(label))
            {
                return;
            }

            if (label.Trim().Length == 0)
            {
                return;
            }

            if (HideName)
            {
                return;
            }

            if (borderColor == null)
            {
                borderColor = Color.Transparent;
            }

            if (backgroundColor == null)
            {
                backgroundColor = Color.Transparent;
            }

            //If we have a non-transparent label color then use it, otherwise use the players name color
            if (labelColor != null && labelColor.A != 0)
            {
                textColor = labelColor;
            }

            //Check for stealth amoungst status effects.
            for (var n = 0; n < Status.Count; n++)
            {
                //If unit is stealthed, don't render unless the entity is the player.
                if (Status[n].Type == StatusTypes.Stealth)
                {
                    if (this != Globals.Me && !(this is Player player && Globals.Me.IsInMyParty(player)))
                    {
                        return;
                    }
                }
            }

            var map = MapInstance;
            if (map == null)
            {
                return;
            }

            var textSize = Graphics.Renderer.MeasureText(label, Graphics.EntityNameFont, 1);

            var x = (int) Math.Ceiling(GetCenterPos().X);
            var y = position == 0 ? GetLabelLocation(LabelType.Header) : GetLabelLocation(LabelType.Footer);

            if (backgroundColor != Color.Transparent)
            {
                Graphics.DrawGameTexture(
                    Graphics.Renderer.GetWhiteTexture(), new FloatRect(0, 0, 1, 1),
                    new FloatRect(x - textSize.X / 2f - 4, y, textSize.X + 8, textSize.Y), backgroundColor
                );
            }

            Graphics.Renderer.DrawString(
                label, Graphics.EntityNameFont, (int) (x - (int) Math.Ceiling(textSize.X / 2f)), (int) y, 1,
                Color.FromArgb(textColor.ToArgb()), true, null, Color.FromArgb(borderColor.ToArgb())
            );
        }

        public virtual void DrawName(Color textColor, Color borderColor = null, Color backgroundColor = null)
        {
            if (!Globals.Database.DisplayNpcNames && !(this is Player))
            {
                return;
            }

            // Always draw name if event, player, is targeted, or is aggressive.
            if (!(this is Event) && !(this is Player) && !IsTargeted && Type != -1)
            {
                return;
            }

            if (HideName || Name.Trim().Length == 0)
            {
                return;
            }

            if (borderColor == null)
            {
                borderColor = Color.Transparent;
            }

            if (backgroundColor == null)
            {
                backgroundColor = Color.Transparent;
            }

            //Check for npc colors
            if (textColor == null)
            {
                LabelColor? color = null;
                switch (Type)
                {
                    case -1: //When entity has a target (showing aggression)
                        if (EntityTarget == Globals.Me.Id) // If that target is YOU!
                        {
                            color = CustomColors.Names.Npcs["PlayerAggro"];
                        }
                        else
                        {
                            color = CustomColors.Names.Npcs["Aggressive"];
                        }

                        break;
                    case 0: //Attack when attacked
                        color = CustomColors.Names.Npcs["AttackWhenAttacked"];

                        break;
                    case 1: //Attack on sight
                        color = CustomColors.Names.Npcs["AttackOnSight"];

                        break;
                    case 3: //Guard
                        color = CustomColors.Names.Npcs["Guard"];

                        break;
                    case 2: //Neutral
                    default:
                        color = CustomColors.Names.Npcs["Neutral"];

                        break;
                }

                if (color != null)
                {
                    textColor = color?.Name;
                    backgroundColor = color?.Background;
                    borderColor = color?.Outline;
                }
            }

            //Check for stealth amoungst status effects.
            for (var n = 0; n < Status.Count; n++)
            {
                //If unit is stealthed, don't render unless the entity is the player.
                if (Status[n].Type == StatusTypes.Stealth)
                {
                    if (this != Globals.Me && !(this is Player player && Globals.Me.IsInMyParty(player)))
                    {
                        return;
                    }
                }
            }

            var map = MapInstance;
            if (map == null)
            {
                return;
            }

            var name = Name;
            if ((this is Player && Options.Player.ShowLevelByName) || (!(this is Player) && Options.Npc.ShowLevelByName))
            {
                name = Strings.GameWindow.EntityNameAndLevel.ToString(Name, Level);
            }

            var textSize = Graphics.Renderer.MeasureText(name, Graphics.EntityNameFont, 1);

            var x = (int) Math.Ceiling(GetCenterPos().X);
            var y = GetLabelLocation(LabelType.Name);

            if (backgroundColor != Color.Transparent)
            {
                Graphics.DrawGameTexture(
                    Graphics.Renderer.GetWhiteTexture(), new FloatRect(0, 0, 1, 1),
                    new FloatRect(x - textSize.X / 2f - 4, y, textSize.X + 8, textSize.Y), backgroundColor
                );
            }

            Graphics.Renderer.DrawString(
                name, Graphics.EntityNameFont, (int) (x - (int) Math.Ceiling(textSize.X / 2f)), (int) y, 1,
                Color.FromArgb(textColor.ToArgb()), true, null, Color.FromArgb(borderColor.ToArgb())
            );

            IsTargeted = false; // allow resetting of target-only name display
        }

        public float GetLabelLocation(LabelType type)
        {
            var y = GetTopPos() - 4;
            switch (type)
            {
                case LabelType.Header:
                    if (string.IsNullOrWhiteSpace(HeaderLabel.Text))
                    {
                        return GetLabelLocation(LabelType.Name);
                    }

                    y = GetLabelLocation(LabelType.Name);
                    var headerSize = Graphics.Renderer.MeasureText(HeaderLabel.Text, Graphics.EntityNameFont, 1);
                    y -= headerSize.Y;

                    break;
                case LabelType.Footer:
                    if (string.IsNullOrWhiteSpace(FooterLabel.Text))
                    {
                        break;
                    }

                    var footerSize = Graphics.Renderer.MeasureText(FooterLabel.Text, Graphics.EntityNameFont, 1);
                    y -= footerSize.Y - 8;

                    break;
                case LabelType.Name:
                    y = GetLabelLocation(LabelType.Footer);
                    var nameSize = Graphics.Renderer.MeasureText(Name, Graphics.EntityNameFont, 1);
                    if (string.IsNullOrEmpty(FooterLabel.Text))
                    {
                        y -= nameSize.Y - 8;
                    }
                    else
                    {
                        y -= nameSize.Y;
                    }

                    break;
                case LabelType.ChatBubble:
                    y = GetLabelLocation(LabelType.Header) - 4;

                    break;
                case LabelType.Guild:
                    // ???? This should never NOT run on a player ????
                    if (this is Player player)
                    {
                        if (string.IsNullOrWhiteSpace(player.Guild))
                        {
                            return GetLabelLocation(LabelType.Name);
                        }

                        // Do we have a header? If so, slightly change the position!
                        if (string.IsNullOrWhiteSpace(HeaderLabel.Text))
                        {
                            y = GetLabelLocation(LabelType.Name);
                        }
                        else
                        {
                            y = GetLabelLocation(LabelType.Header);
                        }

                        var guildSize = Graphics.Renderer.MeasureText(player.Guild, Graphics.EntityNameFont, 1);
                        y -= guildSize.Y;
                    }
                    break;
            }

            return y;
        }
        public int GetShieldSize()
        {
            var shieldSize = 0;
            foreach (var status in Status)
            {
                if (status.Type == StatusTypes.Shield)
                {
                    shieldSize += status.Shield[(int)Vitals.Health];
                }
            }
            return shieldSize;
        }

        public void DrawHpBar()
        {
            if (HideName && HideEntity)
            {
                return;
            }

            if (this is Resource && Options.HideResourceHealthBars)
            {
                return;
            }

            if (Vital[(int) Vitals.Health] <= 0)
            {
                return;
            }

            var maxVital = MaxVital[(int) Vitals.Health];
            var shieldSize = 0;

            //Check for shields
            foreach (var status in Status)
            {
                if (status.Type == StatusTypes.Shield)
                {
                    shieldSize += status.Shield[(int) Vitals.Health];
                    maxVital += status.Shield[(int) Vitals.Health];
                }
            }

            if (shieldSize + Vital[(int) Vitals.Health] > maxVital)
            {
                maxVital = shieldSize + Vital[(int) Vitals.Health];
            }

            if (Vital[(int) Vitals.Health] == MaxVital[(int) Vitals.Health] && shieldSize <= 0)
            {
                return;
            }

            //Check for stealth amoungst status effects.
            for (var n = 0; n < Status.Count; n++)
            {
                //If unit is stealthed, don't render unless the entity is the player.
                if (Status[n].Type == StatusTypes.Stealth)
                {
                    if (this != Globals.Me && !(this is Player player && Globals.Me.IsInMyParty(player)))
                    {
                        return;
                    }
                }
            }

            var map = MapInstance.Get(CurrentMap);
            if (map == null)
            {
                return;
            }

            var width = Options.TileWidth - 8;

            var hpfillRatio = (float) Vital[(int) Vitals.Health] / maxVital;
            hpfillRatio = Math.Min(1, Math.Max(0, hpfillRatio));
            var hpfillWidth = (float) Math.Ceiling(hpfillRatio * width);

            var shieldfillRatio = (float) shieldSize / maxVital;
            shieldfillRatio = Math.Min(1, Math.Max(0, shieldfillRatio));
            var shieldfillWidth = (float) Math.Floor(shieldfillRatio * width);

            var y = (int) Math.Ceiling(GetCenterPos().Y);
            var x = (int) Math.Ceiling(GetCenterPos().X);
            var entityTex = Globals.ContentManager.GetTexture(GameContentManager.TextureType.Entity, MySprite);
            if (entityTex != null)
            {
                y = y - (int) (entityTex.GetHeight() / (Options.Instance.Sprites.Directions * 2));
                y -= 8;
            }

            var hpBackground = Globals.ContentManager.GetTexture(
                GameContentManager.TextureType.Misc, "hpbackground.png"
            );

            var hpForeground = Globals.ContentManager.GetTexture(GameContentManager.TextureType.Misc, "hpbar.png");
            var shieldForeground = Globals.ContentManager.GetTexture(
                GameContentManager.TextureType.Misc, "shieldbar.png"
            );

            if (hpBackground != null)
            {
                Graphics.DrawGameTexture(
                    hpBackground, new FloatRect(0, 0, hpBackground.GetWidth(), hpBackground.GetHeight()),
                    new FloatRect((int) (x - width / 2), (int) (y - 1), width, 9), Color.White
                );
            }

            if (hpForeground != null)
            {
                Graphics.DrawGameTexture(
                    hpForeground, 
                    new FloatRect(0, 0, hpForeground.GetWidth(), hpForeground.GetHeight()),
                    new FloatRect((int) (x - width / 2), (int) (y - 1), hpfillWidth, 9), Color.White
                );
            }

            if (shieldSize > 0 && shieldForeground != null) //Check for a shield to render
            {
                Graphics.DrawGameTexture(
                    shieldForeground,
                    new FloatRect(0, 0, shieldfillWidth, shieldForeground.GetHeight()),
                    new FloatRect((int) (x - width / 2) + hpfillWidth, (int) (y - 1), shieldfillWidth, 9), Color.White
                );
            }
        }

        public void DrawCastingBar()
        {
            if (CastTime < Timing.Global.Milliseconds)
            {
                SpellCast = default;
                return;
            }

            if (CurrentMap == default)
            {
                return;
            }

            var map = MapInstance.Get(CurrentMap);
            var castSpell = SpellBase.Get(SpellCast);
            if (castSpell != null)
            {
                var width = Options.TileWidth - 8;
                var fillratio = (castSpell.CastDuration - (CastTime - Timing.Global.Milliseconds)) /
                                (float)castSpell.CastDuration;

                var castFillWidth = fillratio * width;
                var y = (int) Math.Ceiling(GetCenterPos().Y);
                var x = (int) Math.Ceiling(GetCenterPos().X);
                var entityTex = Globals.ContentManager.GetTexture(GameContentManager.TextureType.Entity, MySprite);
                if (entityTex != null)
                {
                    y = y + (int) (entityTex.GetHeight() / (Options.Instance.Sprites.Directions * 2));
                    y += 3;
                }

                var castBackground = Globals.ContentManager.GetTexture(TextureType.Misc, "castbackground.png");

                var castForeground = Globals.ContentManager.GetTexture(TextureType.Misc, "castbar.png");

                if (castBackground != null)
                {
                    Graphics.DrawGameTexture(
                        castBackground, new FloatRect(0, 0, castBackground.GetWidth(), castBackground.GetHeight()),
                        new FloatRect((int) (x - width / 2), (int) (y - 1), width, 9), Color.White
                    );
                }

                if (castForeground != null)
                {
                    Graphics.DrawGameTexture(
                        castForeground,
                        new FloatRect(0, 0, castForeground.GetWidth() * fillratio, castForeground.GetHeight()),
                        new FloatRect((int) (x - width / 2), (int) (y - 1), castFillWidth, 9), Color.White
                    );
                }

                if (!string.IsNullOrEmpty(castSpell.Icon) && castSpell.Icon != Strings.General.none)
                {
                    DrawSpellIcon(x, y, castSpell.Icon);
                }

                if (castSpell.Combat.TargetType == SpellTargetTypes.Single && EntityTarget == Globals.Me?.Id)
                {
                    DrawCasterIndicator(castSpell.Combat.CastRange);
                }

                /*if (castSpell.Combat.TargetType == SpellTargetTypes.AoE)
                {
                    DrawAoe(castSpell.Combat.CastRange, map, X, Y, false);
                }*/
            }
        }

        //
        public void DrawTarget(int priority)
        {
            IsTargeted = true;
            if (GetType() == typeof(Projectile))
            {
                return;
            }

            var map = MapInstance.Get(CurrentMap);
            if (map == null)
            {
                return;
            }

            var targetTex = Globals.ContentManager.GetTexture(TextureType.Misc, "target.png");
            if (targetTex != null)
            {
                var srcRectangle = new FloatRect(
                    priority * targetTex.GetWidth() / 2f,
                    0,
                    targetTex.GetWidth() / 2f,
                    targetTex.GetHeight()
                );

                var destRectangle = new FloatRect(
                    (float) Math.Ceiling(GetCenterPos().X - targetTex.GetWidth() / 4f),
                    (float) Math.Ceiling(GetCenterPos().Y - targetTex.GetHeight() / 2f),
                    srcRectangle.Width,
                    srcRectangle.Height
                );

                Graphics.DrawGameTexture(targetTex, srcRectangle, destRectangle, Color.White);
            }
        }

        public virtual bool CanBeAttacked()
        {
            return true;
        }

        //Chatting
        public void AddChatBubble(string text, ChatBubbleType type)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            mChatBubbles.Add(new ChatBubble(this, text, type));
        }

        //Statuses
        public bool StatusActive(Guid guid)
        {
            foreach (var status in Status)
            {
                if (status.SpellId == guid && status.IsActive())
                {
                    return true;
                }
            }

            return false;
        }

        public bool StatusIsActive(StatusTypes status, Action action = default)
        {
            bool statusFound = false;
            for (var n = 0; n < Status.Count; n++)
            {
                if (Status[n].Type == status)
                {
                    statusFound = true;
                    if (action != default)
                    {
                        action();
                    }
                }
            }

            return statusFound;
        }

        public Status GetStatus(Guid guid)
        {
            foreach (var status in Status)
            {
                if (status.SpellId == guid && status.IsActive())
                {
                    return status;
                }
            }

            return null;
        }

        public void SortStatuses()
        {
            //Sort Status effects by remaining time
            Status = Status.OrderByDescending(x => x.RemainingMs).ToList();
        }

        public void UpdateSpriteAnimation()
        {
            var oldAnim = SpriteAnimation;

            //Exit if textures haven't been loaded yet
            if (AnimatedTextures.Count == 0)
            {
                return;
            }

            SpriteAnimation = AnimatedTextures[SpriteAnimations.Idle] != null && LastActionTime + Options.Instance.Sprites.TimeBeforeIdle < Timing.Global.Milliseconds ? SpriteAnimations.Idle : SpriteAnimations.Normal;
            if (IsMoving)
            {
                SpriteAnimation = SpriteAnimations.Normal;
                LastActionTime = Timing.Global.Milliseconds;
            }
            else if (AttackTimer > Timing.Global.Ticks / TimeSpan.TicksPerMillisecond) //Attacking
            {
                var timeIn = CalculateAttackTime() - (AttackTimer - Timing.Global.Ticks / TimeSpan.TicksPerMillisecond);
                LastActionTime = Timing.Global.Milliseconds;

                if (AnimatedTextures[SpriteAnimations.Attack] != null)
                {
                    SpriteAnimation = SpriteAnimations.Attack;
                }

                if (Options.WeaponIndex > -1 && Options.WeaponIndex < Equipment.Length)
                {
                    if (Equipment[Options.WeaponIndex] != Guid.Empty && this != Globals.Me ||
                        MyEquipment[Options.WeaponIndex] < Options.MaxInvItems)
                    {
                        var itemId = Guid.Empty;
                        if (this == Globals.Me)
                        {
                            var slot = MyEquipment[Options.WeaponIndex];
                            if (slot > -1)
                            {
                                itemId = Inventory[slot].ItemId;
                            }
                        }
                        else
                        {
                            itemId = Equipment[Options.WeaponIndex];
                        }

                        var item = ItemBase.Get(itemId);
                        if (item != null)
                        {
                            if (AnimatedTextures[SpriteAnimations.Weapon] != null)
                            {
                                SpriteAnimation = SpriteAnimations.Weapon;
                            }

                            if (AnimatedTextures[SpriteAnimations.Shoot] != null && item.ProjectileId != Guid.Empty)
                            {
                                SpriteAnimation = SpriteAnimations.Shoot;
                            }
                        }
                    }
                }

                if (SpriteAnimation != SpriteAnimations.Normal && SpriteAnimation != SpriteAnimations.Idle)
                {
                    SpriteFrame = (int)Math.Floor((timeIn / (CalculateAttackTime() / (float)SpriteFrames)));
                }
            }
            else if (CastTime > Timing.Global.Milliseconds)
            {
                var spell = SpellBase.Get(SpellCast);
                if (spell != null)
                {
                    var duration = spell.CastDuration;
                    var timeIn = duration - (CastTime - Timing.Global.Milliseconds);

                    if (AnimatedTextures[SpriteAnimations.Cast] != null)
                    {
                        SpriteAnimation = SpriteAnimations.Cast;
                    }

                    if (spell.SpellType == SpellTypes.CombatSpell &&
                        spell.Combat.TargetType == SpellTargetTypes.Projectile && AnimatedTextures[SpriteAnimations.Shoot] != null)
                    {
                        SpriteAnimation = SpriteAnimations.Shoot;
                    }

                    SpriteFrame = (int)Math.Floor((timeIn / (duration / (float)SpriteFrames)));
                }
                LastActionTime = Timing.Global.Milliseconds;
            }

            if (SpriteAnimation == SpriteAnimations.Normal)
            {
                ResetSpriteFrame();
            }
            else if (SpriteAnimation == SpriteAnimations.Idle)
            {
                if (SpriteFrameTimer + Options.Instance.Sprites.IdleFrameDuration < Timing.Global.Milliseconds)
                {
                    SpriteFrame++;
                    if (SpriteFrame >= SpriteFrames)
                    {
                        SpriteFrame = 0;
                    }
                    SpriteFrameTimer = Timing.Global.Milliseconds;
                }
            }
        }

        public void ResetSpriteFrame()
        {
            SpriteFrame = 0;
            SpriteFrameTimer = Timing.Global.Milliseconds;
        }

        public void LoadAnimationTextures(string tex)
        {
            var file = Path.GetFileNameWithoutExtension(tex);
            var ext = Path.GetExtension(tex);

            AnimatedTextures.Clear();
            foreach (var anim in Enum.GetValues(typeof(SpriteAnimations)))
            {
                AnimatedTextures.Add((SpriteAnimations)anim, Globals.ContentManager.GetTexture(GameContentManager.TextureType.Entity, $@"{file}_{anim}.png"));
            }
        }

        public byte GetDirectionTo(Entity otherEntity)
        {
            if (otherEntity == null) return Dir;

            bool preferY;
            int yOffset, xOffset;
            byte newDir = Dir;

            if (otherEntity.MapInstance.Id != MapInstance.Id)
            {
                int relX = X;
                int relY = Y;
                int relEntityX = otherEntity.X;
                int relEntityY = otherEntity.Y;

                if (otherEntity.MapInstance.MapGridX < MapInstance.MapGridX)
                {
                    relX += Options.MapWidth - 1;
                } else if (otherEntity.MapInstance.MapGridX > MapInstance.MapGridX)
                {
                    relEntityX += (Options.MapWidth - 1);
                }
                
                if (otherEntity.MapInstance.MapGridY < MapInstance.MapGridY)
                {
                    relY += Options.MapHeight - 1;
                } else if (otherEntity.MapInstance.MapGridY > MapInstance.MapGridY)
                {
                    relEntityY += Options.MapHeight - 1;
                }

                yOffset = relY - relEntityY;
                xOffset = relX - relEntityX;
                preferY = (Math.Abs(yOffset) - Math.Abs(xOffset)) > 0;
            } else
            {
                yOffset = Y - otherEntity.Y;
                xOffset = X - otherEntity.X;
                preferY = (Math.Abs(yOffset) - Math.Abs(xOffset)) > 0;
            }

            if (preferY)
            {
                if (yOffset > 0) newDir = (byte)Directions.Up;
                else if (yOffset < 0) newDir = (byte)Directions.Down;
                else if (xOffset > 0) newDir = (byte)Directions.Left;
                else if (xOffset < 0) newDir = (byte)Directions.Right;
            }
            else
            {
                if (xOffset > 0) newDir = (byte)Directions.Left;
                else if (xOffset < 0) newDir = (byte)Directions.Right;
                else if (yOffset > 0) newDir = (byte)Directions.Up;
                else if (yOffset < 0) newDir = (byte)Directions.Down;
            }

            return newDir;
        }

        //Movement
        /// <summary>
        ///     Returns -6 if the tile is blocked by a global (non-event) entity
        ///     Returns -5 if the tile is completely out of bounds.
        ///     Returns -4 if a tile is blocked because of a local event.
        ///     Returns -3 if a tile is blocked because of a Z dimension tile
        ///     Returns -2 if a tile does not exist or is blocked by a map attribute.
        ///     Returns -1 is a tile is passable.
        ///     Returns any value zero or greater matching the entity index that is in the way.
        /// </summary>
        /// <returns></returns>
        public int IsTileBlocked(
            int x,
            int y,
            int z,
            Guid mapId,
            ref Entity blockedBy,
            bool ignoreAliveResources = true,
            bool ignoreDeadResources = true,
            bool ignoreNpcAvoids = true,
            bool grounded = true
        )
        {
            var mapInstance = MapInstance.Get(mapId);
            if (mapInstance == null)
            {
                return -2;
            }

            var gridX = mapInstance.MapGridX;
            var gridY = mapInstance.MapGridY;
            try
            {
                var tmpX = x;
                var tmpY = y;
                var tmpMapId = Guid.Empty;
                if (x < 0)
                {
                    gridX--;
                    tmpX = Options.MapWidth - x * -1;
                }

                if (y < 0)
                {
                    gridY--;
                    tmpY = Options.MapHeight - y * -1;
                }

                if (x > Options.MapWidth - 1)
                {
                    gridX++;
                    tmpX = x - Options.MapWidth;
                }

                if (y > Options.MapHeight - 1)
                {
                    gridY++;
                    tmpY = y - Options.MapHeight;
                }

                if (gridX < 0 || gridY < 0 || gridX >= Globals.MapGridWidth || gridY >= Globals.MapGridHeight)
                {
                    return -2;
                }

                tmpMapId = Globals.MapGrid[gridX, gridY];

                foreach (var en in Globals.Entities)
                {
                    if (en.Value == null)
                    {
                        continue;
                    }

                    if (en.Value == Globals.Me)
                    {
                        continue;
                    }
                    else
                    {
                        if (en.Value.CurrentMap == tmpMapId &&
                            en.Value.X == tmpX &&
                            en.Value.Y == tmpY &&
                            en.Value.Z == Z)
                        {
                            if (en.Value.GetType() != typeof(Projectile))
                            {
                                if (en.Value.GetType() == typeof(Resource))
                                {
                                    var resourceBase = ((Resource)en.Value).GetResourceBase();
                                    if (resourceBase != null)
                                    {
                                        if (!ignoreAliveResources && !((Resource)en.Value).IsDead)
                                        {
                                            blockedBy = en.Value;

                                            return -6;
                                        }

                                        if (!ignoreDeadResources && ((Resource)en.Value).IsDead)
                                        {
                                            blockedBy = en.Value;

                                            return -6;
                                        }

                                        if (resourceBase.WalkableAfter && ((Resource)en.Value).IsDead ||
                                            resourceBase.WalkableBefore && !((Resource)en.Value).IsDead)
                                        {
                                            continue;
                                        }
                                    }
                                }
                                else if (en.Value.GetType() == typeof(Player))
                                {
                                    //Return the entity key as this should block the player.  Only exception is if the MapZone this entity is on is passable.
                                    var entityMap = MapInstance.Get(en.Value.CurrentMap);
                                    if (Options.Instance.Passability.Passable[(int)entityMap.ZoneType])
                                    {
                                        continue;
                                    }
                                }

                                blockedBy = en.Value;

                                return -6;
                            }
                        }
                    }
                }

                if (MapInstance.Get(tmpMapId) != null)
                {
                    foreach (var en in MapInstance.Get(tmpMapId).LocalEntities)
                    {
                        if (en.Value == null)
                        {
                            continue;
                        }

                        if (en.Value.CurrentMap == tmpMapId &&
                            en.Value.X == tmpX &&
                            en.Value.Y == tmpY &&
                            en.Value.Z == Z &&
                            !en.Value.Passable)
                        {
                            var entityMap = MapInstance.Get(en.Value.CurrentMap);
                            blockedBy = en.Value;
                            if (!(this is Projectile))
                            {
                                return -4;
                            }
                        }
                    }

                    foreach (var en in MapInstance.Get(tmpMapId).Critters)
                    {
                        if (en.Value == null)
                        {
                            continue;
                        }

                        if (en.Value.CurrentMap == tmpMapId &&
                            en.Value.X == tmpX &&
                            en.Value.Y == tmpY &&
                            en.Value.Z == Z &&
                            !en.Value.Passable)
                        {
                            blockedBy = en.Value;

                            return -4;
                        }
                    }
                }

                var gameMap = MapInstance.Get(Globals.MapGrid[gridX, gridY]);
                if (gameMap != null)
                {
                    if (gameMap.Attributes[tmpX, tmpY] != null)
                    {
                        if (gameMap.Attributes[tmpX, tmpY] is MapBlockedAttribute blockAttr)
                        {
                            if (grounded)
                            {
                                return -2;
                            } else
                            {
                                if (!blockAttr.GroundLevel)
                                {
                                    return -2;
                                }
                            }
                        }

                        if ((gameMap.Attributes[tmpX, tmpY].Type == MapAttributes.Animation && ((MapAnimationAttribute)gameMap.Attributes[tmpX, tmpY]).IsBlock))
                        {
                            return -2;
                        }
                        else if (gameMap.Attributes[tmpX, tmpY].Type == MapAttributes.ZDimension)
                        {
                            if (((MapZDimensionAttribute)gameMap.Attributes[tmpX, tmpY]).BlockedLevel - 1 == z)
                            {
                                return -3;
                            }
                        }
                        else if (gameMap.Attributes[tmpX, tmpY].Type == MapAttributes.NpcAvoid)
                        {
                            if (!ignoreNpcAvoids)
                            {
                                return -2;
                            }
                        }
                    }
                }
                else
                {
                    return -5;
                }

                return -1;
            }
            catch
            {
                return -2;
            }
        }

        ~Entity()
        {
            Dispose();
        }
    }

    public partial class Entity
    {
        public bool IsAllyOf(Entity en)
        {
            if (en == null || CurrentMap == default || en.CurrentMap == default)
            {
                return false;
            }

            var myMap = MapInstance.Get(CurrentMap);
            var targetMap = MapInstance.Get(en.CurrentMap);

            var entityType = en.GetEntityType();
            if (entityType == EntityTypes.Resource || entityType == EntityTypes.Event)
            {
                return true;
            }

            if (Id == Globals.Me.Id)
            {
                return true;
            }

            if (en is Player targetPlayer)
            {
                // Player V Player
                if (this is Player me)
                {
                    // Always a friend in a safe zone!
                    if (myMap.ZoneType == MapZones.Safe)
                    {
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"Player {targetPlayer.Name} in {me.Name}'s party: {me.IsInMyParty(targetPlayer.Id)}");
                        return me.IsInMyParty(targetPlayer.Id) || targetPlayer.IsInMyParty(me.Id) || (!string.IsNullOrEmpty(me.Guild) && me.Guild == targetPlayer.Guild);
                    }
                }
                // Entity V Player
                else
                {
                    return Type != -1;
                }
            }
            else
            {
                return Type != -1;
            }
        }
    }

    // New drawing functions
    public partial class Entity
    {
        private bool IndicatorFlash = false;
        private const int IndicatorFrames = 2;
        private long LastFlash;
        private const int IndicatorRadius = 48;
        private GameTexture CASTER_INDICATOR_TEXTURE = Globals.ContentManager.GetTexture(TextureType.Misc, "caster_indicator.png");
        
        private int AoeAlpha = MAX_AOE_ALPHA;
        private int AoeAlphaDir = -1;
        private long AoeAlphaUpdate;
        private GameTexture COMBAT_TILE = Globals.ContentManager.GetTexture(TextureType.Misc, "aoe.png");
        private GameTexture COMBAT_TILE_NEUTRAL = Globals.ContentManager.GetTexture(TextureType.Misc, "aoe_neutral.png");
        private GameTexture COMBAT_TILE_FRIENDLY = Globals.ContentManager.GetTexture(TextureType.Misc, "aoe_heal.png");

        public double CalculateDirectionTo(Entity en)
        {
            var selfTile = GetCenterPos();
            var selfX = selfTile.X; 
            var selfY = selfTile.Y;

            var otherTile = en.GetCenterPos();
            var otherX = otherTile.X;
            var otherY = otherTile.Y;

            return CalculateDirectionToPoint(selfX, selfY, otherX, otherY);
        }

        public static double CalculateDirectionToPoint(float selfX, float selfY, float otherX, float otherY)
        {
            return Math.Atan2(otherY - selfY, otherX - selfX) * (180 / Math.PI);
        }

        public double CalculateDistanceTo(Entity en)
        {
            if (en == null)
            {
                throw new ArgumentNullException(nameof(en));
            }

            var selfTile = GetCurrentTileRectangle();
            var selfX = selfTile.CenterX;
            var selfY = selfTile.CenterY;

            var otherTile = en.GetCurrentTileRectangle();
            var otherX = otherTile.CenterX;
            var otherY = otherTile.CenterY;

            return CalculateDistanceToPoint(selfX, selfY, otherX, otherY);
        }

        public static double CalculateDistanceToPoint(float selfX, float selfY, float otherX, float otherY)
        {
            var a = Math.Pow(otherX - selfX, 2);
            var b = Math.Pow(otherY - selfY, 2);

            return Math.Sqrt(a + b);
        }

        private void DrawCasterIndicator(int castRange)
        {
            if (Globals.Me == null || Globals.Me.Id == Id)
            {
                return;
            }

            var angle = Globals.Me.CalculateDirectionTo(this);
            var width = CASTER_INDICATOR_TEXTURE.GetWidth() / IndicatorFrames;
            var height = CASTER_INDICATOR_TEXTURE.GetHeight();
            
            var x = Globals.Me.GetCenterPos().X - (width / 2);
            var y = Globals.Me.GetCenterPos().Y;

            if (Timing.Global.Milliseconds > LastFlash)
            {
                LastFlash = Timing.Global.Milliseconds + 150;
                IndicatorFlash = !IndicatorFlash;
            }

            // Add 1 to cast range here as just a simple error buffer, since center point isn't exact
            var maxCastingDistance = (castRange + 1) * Options.Map.TileWidth; 
            var distanceBetween = CalculateDistanceTo(Globals.Me);
            var alpha = 255 - (int)Math.Round((distanceBetween / maxCastingDistance) * 255f);

            // Cap alpha values to prevent opacity jitter
            switch (alpha)
            {
                case var _ when alpha >= 150:
                    alpha = 255;
                    break;
                case var _ when alpha >= 20:
                    alpha = 185;
                    break;
                case var _ when alpha < 20 && alpha > 0:
                    alpha = 115;
                    break;
                default:
                    alpha = 0;
                    break;
            }

            var rcos = IndicatorRadius * MathHelper.DCos(angle);
            var rsin = IndicatorRadius * MathHelper.DSin(angle);
            var xPos = x + rcos;
            var yPos = y + rsin;

            var frame = Convert.ToInt32(IndicatorFlash);
            Graphics.DrawGameTexture(
               CASTER_INDICATOR_TEXTURE, new FloatRect(width * frame, 0, width, height),
               new FloatRect((float)xPos, (float)yPos, width * 4, height * 4), new Color(alpha, 255, 255, 255),
               rotationDegrees: (float)angle
           );
        }

        public FloatRect GetCurrentTileRectangle()
        {
            var selfMap = MapInstance.Get(CurrentMap);
            return GetTileRectangle(selfMap, X, Y);
        }


        public static FloatRect GetTileRectangle(MapInstance map, byte x, byte y)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            return new FloatRect(
                map.GetX() + x * Options.TileWidth,
                map.GetY() + y * Options.TileHeight,
                Options.TileWidth,
                Options.TileHeight);
        }

        private const int MAX_AOE_ALPHA = 200;
        private const int MIN_AOE_ALPHA = 125;
        private const int AOE_UPDATE_MS = 100;
        private const int AOE_UPDATE_AMT = 15;
        public void DrawAoe(SpellBase spell, MapInstance spawnMap, byte spawnX, byte spawnY, bool friendlyAoe)
        {
            if (Timing.Global.Milliseconds > AoeAlphaUpdate)
            {
                AoeAlphaUpdate = Timing.Global.Milliseconds + AOE_UPDATE_MS;

                if (AoeAlpha <= MIN_AOE_ALPHA)
                {
                    AoeAlphaDir = 1;
                }
                else if (AoeAlpha >= MAX_AOE_ALPHA)
                {
                    AoeAlphaDir = -1;
                }
                AoeAlpha = MathHelper.Clamp(AoeAlpha + (AOE_UPDATE_AMT * AoeAlphaDir), MIN_AOE_ALPHA, MAX_AOE_ALPHA);
            }

            var hitRadius = spell.Combat.HitRadius;
            // The start coordinates are calculated knowing that the AoE spawn is always the center
            int left = spawnX - hitRadius;
            int right = spawnX + hitRadius;
            int top = spawnY - hitRadius;
            int bottom = spawnY + hitRadius;

            // Determine texture
            GameTexture texture;
            if (friendlyAoe)
            {
                texture = spell.Combat.Friendly ? COMBAT_TILE_FRIENDLY : COMBAT_TILE_NEUTRAL;   
            }
            else
            {
                texture = spell.Combat.Friendly ? COMBAT_TILE_NEUTRAL : COMBAT_TILE;
            }

            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    var distanceFromCaster = CalculateDistanceToPoint(spawnX, spawnY, x, y);
                    if (Math.Floor(distanceFromCaster) > hitRadius)
                    {
                        continue;
                    }

                    if (!MapInstance.TryGetMapInstanceFromCoords(CurrentMap, x, y, out var currMap, out var mapX, out var mapY))
                    {
                        continue;
                    }

                    var tile = GetTileRectangle(currMap, (byte)mapX, (byte)mapY);

                    if (texture == COMBAT_TILE) // If we're drawing the "DANGER" texture, give it a light so we can see it in darkness
                    {
                        Graphics.AddLight((int)tile.CenterX, (int)tile.CenterY, 100, 200, 1.0f, new Color(255, 222, 124, 112));
                    }
                    Graphics.DrawGameTexture(
                        texture, new FloatRect(0, 0, texture.Width, texture.Height),
                        tile, new Color(AoeAlpha, 255, 255, 255)
                    );
                }
            }
        }

        public void DrawProjectileSpawns(SpellBase spell, MapInstance spawnMap, byte spawnX, byte spawnY, bool friendly)
        {
            if (Timing.Global.Milliseconds > AoeAlphaUpdate)
            {
                AoeAlphaUpdate = Timing.Global.Milliseconds + AOE_UPDATE_MS;

                if (AoeAlpha <= MIN_AOE_ALPHA)
                {
                    AoeAlphaDir = 1;
                }
                else if (AoeAlpha >= MAX_AOE_ALPHA)
                {
                    AoeAlphaDir = -1;
                }
                AoeAlpha = MathHelper.Clamp(AoeAlpha + (AOE_UPDATE_AMT * AoeAlphaDir), MIN_AOE_ALPHA, MAX_AOE_ALPHA);
            }

            var dir = Dir;
            if (this is Player player && player.CombatMode)
            {
                dir = player.FaceDirection;
            }

            // Get our information
            var projectile = spell.Combat.Projectile;
            var range = projectile.Range;

            // Get the possible bounds for the projectile spawner
            int left = spawnX - (ProjectileBase.SPAWN_LOCATIONS_WIDTH / 2);
            int right = spawnX + (ProjectileBase.SPAWN_LOCATIONS_WIDTH / 2);
            int top = spawnY - (ProjectileBase.SPAWN_LOCATIONS_HEIGHT / 2);
            int bottom = spawnY + (ProjectileBase.SPAWN_LOCATIONS_HEIGHT / 2);

            // Loop through the possible projectile spawner locations
            var projectileX = 0;
            var projectileY = 0;

            List<FloatRect> tilesDrawn = new List<FloatRect>();

            // We have to rotate the spawn locations in accordance to the direction of the caster
            Location[,] rotatedLocations = (Location[,]) projectile.SpawnLocations.Clone();
            switch (dir)
            {
                // Left
                case 2:
                    rotatedLocations = MathHelper.RotateArray90CW(5, rotatedLocations);
                    rotatedLocations = MathHelper.RotateArray90CW(5, rotatedLocations);
                    rotatedLocations = MathHelper.RotateArray90CW(5, rotatedLocations);
                    break;
                // Down
                case 1:
                    rotatedLocations = MathHelper.RotateArray90CW(5, rotatedLocations);
                    rotatedLocations = MathHelper.RotateArray90CW(5, rotatedLocations);
                    break;
                // Right
                case 3:
                    rotatedLocations = MathHelper.RotateArray90CW(5, rotatedLocations);
                    break;
            }

            for (int y = top; y <= bottom; y++, projectileY++)
            {
                projectileX = 0;
                for (int x = left; x <= right; x++, projectileX++)
                {
                    var directions = rotatedLocations[projectileX, projectileY].Directions;

                    // If this projectile spawn location doesn't have anything to show, don't bother
                    if (!directions.Contains(true))
                    {
                        continue;
                    }
                    // Otherwise, draw 'em... first, get our spawner's tile
                    if (!MapInstance.TryGetMapInstanceFromCoords(CurrentMap, x, y, out var currMap, out var mapX, out var mapY))
                    {
                        continue;
                    }
                    // Then, for each direction, draw a tile for each in the range
                    for (var dirId = 0; dirId < directions.Length; dirId++)
                    {
                        if (!directions[dirId])
                        {
                            continue;
                        }
                        DrawProjectilePath(dir, dirId, range, (int)mapX, (int)mapY, currMap, ref tilesDrawn, friendly, spell.Combat.Friendly);
                    }
                }
            }
        }

        private void DrawProjectilePath(byte dir, int dirId, int range, int x, int y, MapInstance map, ref List<FloatRect> tilesDrawn, bool friendly, bool combatFriendly)
        {
            for (var pathIdx = 0; pathIdx < range; pathIdx++)
            {
                var right = x + pathIdx;
                var left = x - pathIdx;
                var up = y - pathIdx;
                var down = y + pathIdx;

                switch (dirId)
                {
                    // Up
                    case 0:
                        switch (dir)
                        {
                            case 0:
                                DrawProjectileTile(map.Id, x, up, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 1:
                                DrawProjectileTile(map.Id, x, down, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 2:
                                DrawProjectileTile(map.Id, left, y, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 3:
                                DrawProjectileTile(map.Id, right, y, ref tilesDrawn, friendly, combatFriendly);
                                break;
                        }
                        break;
                    // Down
                    case 1:
                        switch (dir)
                        {
                            case 0:
                                DrawProjectileTile(map.Id, x, down, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 1:
                                DrawProjectileTile(map.Id, x, up, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 2:
                                DrawProjectileTile(map.Id, right, y, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 3:
                                DrawProjectileTile(map.Id, left, y, ref tilesDrawn, friendly, combatFriendly);
                                break;
                        }
                        break;
                    // Left
                    case 2:
                        switch (dir)
                        {
                            case 0:
                                DrawProjectileTile(map.Id, left, y, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 1:
                                DrawProjectileTile(map.Id, right, y, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 2:
                                DrawProjectileTile(map.Id, x, up, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 3:
                                DrawProjectileTile(map.Id, x, down, ref tilesDrawn, friendly, combatFriendly);
                                break;
                        }
                        break;
                    // Right
                    case 3:
                        switch (dir)
                        {
                            case 0:
                                DrawProjectileTile(map.Id, right, y, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 1:
                                DrawProjectileTile(map.Id, left, y, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 2:
                                DrawProjectileTile(map.Id, x, down, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 3:
                                DrawProjectileTile(map.Id, x, up, ref tilesDrawn, friendly, combatFriendly);
                                break;
                        }
                        break;
                    // Top-left
                    case 4:
                        switch (dir)
                        {
                            case 0:
                                DrawProjectileTile(map.Id, left, up, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 1:
                                DrawProjectileTile(map.Id, right, down, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 2:
                                DrawProjectileTile(map.Id, left, up, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 3:
                                DrawProjectileTile(map.Id, right, down, ref tilesDrawn, friendly, combatFriendly);
                                break;
                        }
                        break;
                    // Top-right
                    case 5:
                        switch (dir)
                        {
                            case 0:
                                DrawProjectileTile(map.Id, right, up, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 1:
                                DrawProjectileTile(map.Id, left, down, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 2:
                                DrawProjectileTile(map.Id, left, down, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 3:
                                DrawProjectileTile(map.Id, right, up, ref tilesDrawn, friendly, combatFriendly);
                                break;
                        }
                        break;
                    // Bottom-left
                    case 6:
                        switch (dir)
                        {
                            case 0:
                                DrawProjectileTile(map.Id, left, down, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 1:
                                DrawProjectileTile(map.Id, right, up, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 2:
                                DrawProjectileTile(map.Id, right, up, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 3:
                                DrawProjectileTile(map.Id, left, down, ref tilesDrawn, friendly, combatFriendly);
                                break;
                        }
                        break;
                    // Bottom-right
                    case 7:
                        switch (dir)
                        {
                            case 0:
                                DrawProjectileTile(map.Id, right, down, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 1:
                                DrawProjectileTile(map.Id, left, up, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 2:
                                DrawProjectileTile(map.Id, right, down, ref tilesDrawn, friendly, combatFriendly);
                                break;
                            case 3:
                                DrawProjectileTile(map.Id, left, up, ref tilesDrawn, friendly, combatFriendly);
                                break;
                        }
                        break;
                }
            }
        }

        private void DrawProjectileTile(Guid mapId, int x, int y, ref List<FloatRect> tilesDrawn, bool friendly, bool combatFriendly)
        {
            if (!MapInstance.TryGetMapInstanceFromCoords(mapId, x, y, out var currMap, out var mapX, out var mapY))
            {
                return;
            }
            var tile = GetTileRectangle(currMap, (byte)mapX, (byte)mapY);

            if (tilesDrawn.Any(t => t.X == tile.X && t.Y == tile.Y))
            {
                return;
            }
            tilesDrawn.Add(tile);

            GameTexture texture;
            if (friendly)
            {
                texture = combatFriendly ? COMBAT_TILE_FRIENDLY : COMBAT_TILE_NEUTRAL;
            }
            else
            {
                texture = combatFriendly ? COMBAT_TILE_NEUTRAL : COMBAT_TILE;
            }

            if (texture == COMBAT_TILE) // If we're drawing the "DANGER" texture, give it a light so we can see it in darkness
            {
                Graphics.AddLight((int)tile.CenterX, (int)tile.CenterY, 100, 200, 1.0f, new Color(255, 222, 124, 112));
            }

            Graphics.DrawGameTexture(
                texture, new FloatRect(0, 0, texture.GetWidth(), texture.GetHeight()),
                tile, new Color(AoeAlpha, 255, 255, 255)
            );
        }

        private static void DrawSpellIcon(int x, int y, string icon)
        {
            var backgroundTex = Globals.ContentManager.GetTexture(TextureType.Misc, "spellcast.png");
            var texture = Globals.ContentManager.GetTexture(TextureType.Spell, icon);
            var iconWidth = 40;
            var iconHeight = 40;

            var iconX = x - iconWidth * 2;
            var iconY = y - iconHeight / 2;

            // Draw BG
            Graphics.DrawGameTexture(
                backgroundTex, new FloatRect(0, 0, backgroundTex.GetWidth(), backgroundTex.GetHeight()),
                new FloatRect(iconX, iconY, iconWidth, iconHeight), Color.White
            );

            Graphics.DrawGameTexture(
                texture, new FloatRect(0, 0, texture.GetWidth(), texture.GetHeight()),
                new FloatRect(iconX + 4, iconY + 4, 32, 32), Color.White
            );
        }
    }
}
