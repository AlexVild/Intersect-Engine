﻿using System;
using System.ComponentModel.DataAnnotations.Schema;

using Intersect.Enums;
using Intersect.GameObjects;
using Intersect.Network.Packets.Server;
using Intersect.Server.Database.PlayerData.Players;
using Intersect.Utilities;

using Newtonsoft.Json;

namespace Intersect.Server.Database
{

    public class Item
    {

        [JsonIgnore] [NotMapped] public double DropChance = 100;

        public Item()
        {
            Properties = new ItemProperties();
        }

        public Item(Guid itemId, int quantity, ItemProperties properties = null) : this(
            itemId, quantity, null, null, properties
        )
        {
        }

        public Item(Guid itemId, int quantity, Guid? bagId, Bag bag, ItemProperties properties = null)
        {
            ItemId = itemId;
            Quantity = quantity;
            BagId = bagId;
            Bag = bag;
            Properties = properties ?? new ItemProperties();

            var descriptor = ItemBase.Get(ItemId);
            if (descriptor == null)
            {
                return;
            }

            if (descriptor.ItemType == ItemTypes.Equipment)
            {
                GenerateStatMods(descriptor);
            }
        }

        void GenerateStatMods(ItemBase descriptor)
        {
            for (var i = 0; i < (int)Stats.StatCount; i++)
            {
                if (!descriptor.StatLocks[(Stats)i])
                {
                    Properties.StatModifiers[i] = Randomization.Next(-descriptor.StatGrowth, descriptor.StatGrowth + 1);
                }
            }
        }

        public Item(Item item) : this(item.ItemId, item.Quantity, item.BagId, item.Bag)
        {
            Properties = new ItemProperties(item.Properties);
            DropChance = item.DropChance;
        }

        [NotMapped]
        public ItemProperties Properties { get; set; }

        [Column("ItemProperties")]
        [JsonIgnore]
        public string ItemPropertiesJson
        {
            get => JsonConvert.SerializeObject(Properties);
            set => Properties = JsonConvert.DeserializeObject<ItemProperties>(value ?? string.Empty) ?? new ItemProperties();
        }

        // TODO: THIS SHOULD NOT BE A NULLABLE. This needs to be fixed.
        public Guid? BagId { get; set; }

        [JsonIgnore]
        public virtual Bag Bag { get; set; }

        public Guid ItemId { get; set; } = Guid.Empty;

        [NotMapped]
        public string ItemName => ItemBase.GetName(ItemId);

        public int Quantity { get; set; }

        [Column("StatBuffs")]
        [JsonIgnore]
        public string StatBuffsJson
        {
            get => DatabaseUtils.SaveIntArray(StatBuffs, (int) Stats.StatCount);
            set => StatBuffs = DatabaseUtils.LoadIntArray(value, (int) Stats.StatCount);
        }

        [NotMapped]
        public int[] StatBuffs { get; set; } = new int[(int) Stats.StatCount];

        [JsonIgnore]
        [NotMapped]
        public ItemBase Descriptor => ItemBase.Get(ItemId);

        public static Item None => new Item();

        public virtual void Set(Item item)
        {
            ItemId = item.ItemId;
            Quantity = item.Quantity;
            BagId = item.BagId;
            Bag = item.Bag;
            Properties = new ItemProperties(item.Properties);
        }

        public string Data()
        {
            return JsonConvert.SerializeObject(this);
        }

        public Item Clone()
        {
            return new Item(this);
        }

        /// <summary>
        /// Try to get the bag, with an additional attempt to load it if it is not already loaded (it should be if this is even a bag item).
        /// </summary>
        /// <param name="bag">the bag if there is one associated with this <see cref="Item"/></param>
        /// <returns>if <paramref name="bag"/> is not <see langword="null"/></returns>
        public bool TryGetBag(out Bag bag)
        {
            bag = Bag;

            if (bag != null)
            {
                //Remove any items from this bag that have been removed from the game
                foreach (var itm in bag.Slots)
                {
                    if (itm.ItemId != Guid.Empty && ItemBase.Get(itm.ItemId) == null)
                    {
                        itm.Set(Item.None);
                    }
                }
            }

            // ReSharper disable once InvertIf Justification: Do not introduce two different return points that assert a value state
            if (bag == null)
            {
                var descriptor = Descriptor;
                // ReSharper disable once InvertIf Justification: Do not introduce two different return points that assert a value state
                if (descriptor?.ItemType == ItemTypes.Bag)
                {
                    bag = Bag.GetBag(BagId ?? Guid.Empty);
                    bag?.ValidateSlots();
                    Bag = bag;
                }
            }

            return default != bag;
        }

    }
}
