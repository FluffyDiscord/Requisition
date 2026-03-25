using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ModLoader.IO;

namespace TerraStorage.Common
{
    /// <summary>
    /// Holds all persistent data for a single Storage Disk: its unique identity,
    /// capacity tier, and the list of item stacks currently stored on it.
    /// </summary>
    public class DiskData
    {
        /// <summary>Unique identifier used to look up this disk in <see cref="TerraStorage.Systems.StorageWorldSystem"/>.</summary>
        public Guid DiskId { get; set; }
        public DiskTier Tier { get; set; }
        public List<StoredItemStack> Items { get; set; } = new();

        /// <summary>Maximum number of distinct item stacks this disk can hold, determined by its tier.</summary>
        public int MaxStacks => Tier.GetCapacity();
        /// <summary>Number of item stacks currently occupying slots on this disk.</summary>
        public int UsedStacks => Items.Count;
        public bool IsFull => UsedStacks >= MaxStacks;

        /// <summary>
        /// Try to insert an item into this disk. Returns the leftover count (0 if fully inserted).
        /// </summary>
        /// <param name="preSerializedTag">
        /// Optional full ItemIO tag captured from the <em>original</em> item (before any
        /// <c>Clone()</c> call) by the caller. Supplying it ensures that GlobalItem data from
        /// other mods (e.g. enchantment mods using <c>GlobalItem.SaveData</c>) is preserved,
        /// because <c>Item.Clone()</c> may not deep-copy per-instance GlobalItem state.
        /// </param>
        public int InsertItem(Item item, long insertionOrder = 0, TagCompound preSerializedTag = null)
        {
            if (item == null || item.IsAir)
                return 0;

            int remaining = item.stack;

            // Capture mod item NBT up front — we need it before the merge step to decide
            // whether merging is safe. For most items this will be null; for mod items with
            // custom per-instance data (e.g. a disk's GUID) it preserves that data so it
            // can be restored on extraction.
            TagCompound modData = null;
            if (item.ModItem != null)
            {
                var tempTag = new TagCompound();
                item.ModItem.SaveData(tempTag);
                if (tempTag.Count > 0)
                    modData = tempTag;
            }

            // Always capture the full serialized tag so globalData (enchantments from mods,
            // e.g. CalamityGlobalItem, Entropy enchantments) is preserved on extraction.
            var fullSave = preSerializedTag ?? ItemIO.Save(item);
            TagCompound fullItemTag = (modData != null || fullSave.ContainsKey("globalData"))
                ? fullSave : null;

            // Merge with an existing stack only if per-instance data matches.
            // Two vanilla iron bars have identical CalamityGlobalItem data → they merge.
            // Two iron bars with different enchantments have different globalData → they don't.
            foreach (var stored in Items)
            {
                if (stored.Matches(item.type, item.prefix) && stored.Stack < item.maxStack
                    && PerInstanceDataMatches(stored.FullItemTag, fullItemTag, stored.ModData, modData))
                {
                    int canAdd = Math.Min(remaining, item.maxStack - stored.Stack);
                    stored.Stack += canAdd;
                    if (insertionOrder > 0)
                        stored.InsertionOrder = insertionOrder;
                    remaining -= canAdd;
                    if (remaining <= 0)
                        return 0;
                }
            }

            // Add new stacks
            while (remaining > 0 && !IsFull)
            {
                int stackSize = Math.Min(remaining, item.maxStack);
                Items.Add(new StoredItemStack
                {
                    ItemType = item.type,
                    Stack = stackSize,
                    PrefixId = item.prefix,
                    InsertionOrder = insertionOrder,
                    ModData = modData,
                    FullItemTag = fullItemTag
                });
                remaining -= stackSize;
            }

            return remaining;
        }

        /// <summary>
        /// Extract up to 'count' of the given item type. Returns the items extracted.
        /// </summary>
        public Item ExtractItem(int itemType, int count, int prefixId = -1)
        {
            int extracted = 0;
            var toRemove = new List<StoredItemStack>();
            // Track the mod data from the last fully-consumed stack. For items with unique
            // per-instance mod data (e.g. disks, maxStack=1) only one stack is ever consumed,
            // so this always captures the correct data. For regular stackable items it stays null.
            TagCompound extractedModData = null;
            TagCompound extractedFullTag = null;

            foreach (var stored in Items)
            {
                if (!stored.Matches(itemType, prefixId))
                    continue;

                int canTake = Math.Min(count - extracted, stored.Stack);
                stored.Stack -= canTake;
                extracted += canTake;

                if (stored.Stack <= 0)
                {
                    toRemove.Add(stored);
                    if (stored.ModData != null)
                        extractedModData = stored.ModData;
                    if (stored.FullItemTag != null)
                        extractedFullTag = stored.FullItemTag;
                }

                if (extracted >= count)
                    break;
            }

            foreach (var r in toRemove)
                Items.Remove(r);

            if (extracted == 0)
                return new Item();

            Item result;
            if (extractedFullTag != null)
            {
                // Restore full item including GlobalItem data from other mods.
                result = ItemIO.Load(extractedFullTag);
                result.stack = extracted;
            }
            else
            {
                result = new Item();
                result.SetDefaults(itemType);
                result.stack = extracted;
                if (prefixId > 0)
                    result.Prefix(prefixId);

                // Restore mod item data (e.g. the DiskId GUID).
                if (extractedModData != null && result.ModItem != null)
                    result.ModItem.LoadData(extractedModData);
            }

            return result;
        }

        /// <summary>
        /// Extract the specific per-instance stack whose ModData matches <paramref name="targetModData"/>
        /// byte-for-byte. Used to pull the exact UnloadedItem (or other unique item) the user clicked.
        /// </summary>
        public Item ExtractItemWithModData(TagCompound targetModData)
        {
            StoredItemStack match = null;
            foreach (var stored in Items)
            {
                if (stored.ModData != null && TagCompoundEquals(stored.ModData, targetModData))
                {
                    match = stored;
                    break;
                }
            }

            if (match == null)
                return new Item();

            Items.Remove(match);

            var result = new Item();
            result.SetDefaults(match.ItemType);
            result.stack = match.Stack;
            if (match.PrefixId > 0)
                result.Prefix(match.PrefixId);
            if (result.ModItem != null)
                result.ModItem.LoadData(match.ModData);
            return result;
        }

        /// <summary>
        /// Extract the specific per-instance stack whose FullItemTag matches <paramref name="targetFullTag"/>
        /// byte-for-byte. Used for items with GlobalItem data (e.g. Entropy enchantments) that have no ModData.
        /// </summary>
        public Item ExtractItemWithFullItemTag(TagCompound targetFullTag)
        {
            StoredItemStack match = null;
            foreach (var stored in Items)
            {
                if (stored.FullItemTag != null && TagCompoundEquals(stored.FullItemTag, targetFullTag))
                {
                    match = stored;
                    break;
                }
            }

            if (match == null)
                return new Item();

            Items.Remove(match);
            var result = ItemIO.Load(match.FullItemTag);
            result.stack = match.Stack;
            return result;
        }

        private static bool TagCompoundEquals(TagCompound a, TagCompound b)
        {
            using var msA = new MemoryStream();
            using var msB = new MemoryStream();
            TagIO.Write(a, new BinaryWriter(msA));
            TagIO.Write(b, new BinaryWriter(msB));
            return msA.ToArray().SequenceEqual(msB.ToArray());
        }

        /// <summary>
        /// Returns true if two items have compatible per-instance data for merging.
        /// Compares globalData and modData independently so that items with identical
        /// mod-attached state (e.g. two unenchanted items both carrying default CalamityGlobalItem)
        /// are still allowed to merge, while items with differing enchantments are not.
        /// </summary>
        private static bool PerInstanceDataMatches(
            TagCompound storedFullTag, TagCompound incomingFullTag,
            TagCompound storedModData, TagCompound incomingModData)
        {
            // Compare modData (ModItem.SaveData)
            if (storedModData == null != (incomingModData == null)) return false;
            if (storedModData != null && !TagCompoundEquals(storedModData, incomingModData)) return false;

            // Compare globalData (GlobalItem state from mods — extract just that key)
            bool storedHas  = storedFullTag?.ContainsKey("globalData")   == true;
            bool incomingHas = incomingFullTag?.ContainsKey("globalData") == true;
            if (storedHas != incomingHas) return false;
            if (!storedHas) return true; // neither has globalData

            try
            {
                var wrapA = new TagCompound(); wrapA["g"] = storedFullTag["globalData"];
                var wrapB = new TagCompound(); wrapB["g"] = incomingFullTag["globalData"];
                return TagCompoundEquals(wrapA, wrapB);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Count how many of a given item type are stored.
        /// </summary>
        public int CountItem(int itemType, int prefixId = -1)
        {
            int total = 0;
            foreach (var s in Items)
            {
                if (s.Matches(itemType, prefixId))
                    total += s.Stack;
            }
            return total;
        }

        /// <summary>
        /// Compact binary serialization for network packets. ~18 bytes/stack vs ~373 bytes
        /// with the TagCompound world-save format.
        /// </summary>
        public void WriteNet(BinaryWriter writer)
        {
            writer.Write(DiskId.ToByteArray());
            writer.Write((byte)Tier);
            writer.Write(Items.Count);
            foreach (var item in Items)
                item.WriteNet(writer);
        }

        /// <summary>Deserializes a compact network-format disk written by <see cref="WriteNet"/>.</summary>
        public static DiskData ReadNet(BinaryReader reader)
        {
            var data = new DiskData
            {
                DiskId = new Guid(reader.ReadBytes(16)),
                Tier = (DiskTier)reader.ReadByte()
            };
            int count = reader.ReadInt32();
            data.Items = new List<StoredItemStack>(count);
            for (int i = 0; i < count; i++)
                data.Items.Add(StoredItemStack.ReadNet(reader));
            return data;
        }

        /// <summary>
        /// Serializes this disk's GUID, tier, and all stored item stacks to a
        /// <see cref="TagCompound"/> for world-save persistence.
        /// </summary>
        public TagCompound Save()
        {
            return new TagCompound
            {
                ["guid"] = DiskId.ToByteArray(),
                ["tier"] = (int)Tier,
                ["items"] = Items.Select(i => i.Save()).ToList()
            };
        }

        /// <summary>
        /// Deserializes a <see cref="DiskData"/> from a <see cref="TagCompound"/>,
        /// reconstructing the GUID, tier, and item stacks.
        /// </summary>
        public static DiskData Load(TagCompound tag)
        {
            var data = new DiskData
            {
                // GUIDs are stored as 16-byte arrays to avoid string parsing overhead
                DiskId = new Guid(tag.GetByteArray("guid")),
                Tier = (DiskTier)tag.GetInt("tier")
            };

            if (tag.ContainsKey("items"))
            {
                var itemTags = tag.GetList<TagCompound>("items");
                data.Items = itemTags.Select(StoredItemStack.Load).ToList();
            }

            return data;
        }
    }
}
