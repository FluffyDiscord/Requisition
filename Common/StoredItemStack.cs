using System.IO;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace TerraStorage.Common
{
    // Represents a single stack of items on a Storage Disk.
    // Tracks item type, stack size, prefix (modifier), and the global insertion
    // order counter used to support "recently added" sorting in the Terminal UI.
    // 
    public class StoredItemStack
    {
        public int ItemType { get; set; }
        public int Stack { get; set; }
        public int PrefixId { get; set; }
        // Monotonically increasing counter value assigned at insertion time.
        // Higher values mean more recently inserted; used for <c>RecentlyAdded</c> sort.
        public long InsertionOrder { get; set; }
        // Optional NBT data saved from the item's <see cref="Terraria.ModLoader.ModItem.SaveData"/>
        // at insertion time. Used to restore mod-specific state (e.g. a disk's GUID) when the
        // item is extracted. Null for vanilla items and mod items with no custom data.
        public TagCompound ModData { get; set; }

        // Full ItemIO-serialized tag capturing ALL per-instance data, including GlobalItem data
        // from other mods (e.g. enchantment mods that modify vanilla items). When non-null this
        // is used for world-save and extraction instead of reconstructing from scratch.
        public TagCompound FullItemTag { get; set; }

        public StoredItemStack() { }

        // Creates a <see cref="StoredItemStack"/> from a live <see cref="Item"/>,
        // capturing its type, stack count, and prefix.
        public StoredItemStack(Item item)
        {
            ItemType = item.type;
            Stack = item.stack;
            PrefixId = item.prefix;
        }

        // Reconstructs a Terraria <see cref="Item"/> from this stored stack,
        // applying the prefix so tooltips and stats are correct.
        public Item ToItem()
        {
            var item = new Item();
            item.SetDefaults(ItemType);
            item.stack = Stack;
            item.Prefix(PrefixId);
            return item;
        }

        //Serializes this stack to a <see cref="TagCompound"/> for world-save persistence.
        public TagCompound Save()
        {
            TagCompound tag;
            if (FullItemTag != null)
            {
                // Re-load the full item (preserves GlobalItem data from enchantment mods etc.)
                // and re-save with the current stack count so all per-instance data is kept intact.
                var item = ItemIO.Load(FullItemTag);
                item.stack = Stack;
                tag = ItemIO.Save(item);
            }
            else
            {
                // Reconstruct a live Item so ItemIO can write mod name + item name (not integer ID).
                // This means modlist changes won't corrupt item references, and tModLoader's
                // UnloadedItem machinery handles the case where the mod is later disabled.
                var item = ToItem();
                if (ModData != null && item.ModItem != null)
                    item.ModItem.LoadData(ModData);
                tag = ItemIO.Save(item);
            }
            tag["order"] = InsertionOrder;
            return tag;
        }

        //Deserializes a <see cref="StoredItemStack"/> from a saved <see cref="TagCompound"/>.
        public static StoredItemStack Load(TagCompound tag)
        {
            // Legacy format (pre-ItemIO migration): only has a "type" int key, no "mod" key.
            // Read it directly so old saves still load correctly.
            if (!tag.ContainsKey("mod") && tag.ContainsKey("type"))
            {
                var legacy = new Item();
                legacy.SetDefaults(tag.GetInt("type"));
                legacy.stack = tag.GetInt("stack");
                legacy.Prefix(tag.GetInt("prefix"));
                var legacyStored = new StoredItemStack(legacy)
                {
                    InsertionOrder = tag.ContainsKey("order") ? tag.GetLong("order") : 0
                };
                if (tag.ContainsKey("modData") && legacy.ModItem != null)
                    legacyStored.ModData = tag.Get<TagCompound>("modData");
                return legacyStored;
            }

            // Intermediate format: written by us earlier today with "prefix" stored as int.
            // ItemIO.Load expects byte and throws InvalidCastException on that key.
            // Detect by checking whether "prefix" exists but cannot be read as byte.
            // Once this world is re-saved with the current format this path is never hit again.
            bool isIntermediateFormat = tag.ContainsKey("prefix") && !tag.TryGet<byte>("prefix", out _);
            Item item;
            if (isIntermediateFormat)
            {
                // Convert to a proper ItemIO-format tag so ItemIO.Load handles unloaded
                // mods correctly (producing an UnloadedItem placeholder instead of air).
                var ioTag = new TagCompound();
                ioTag["mod"] = tag.GetString("mod");
                if (tag.ContainsKey("name")) ioTag["name"] = tag.GetString("name");
                int stackVal = tag.GetInt("stack");
                if (stackVal > 1) ioTag["stack"] = stackVal;
                int prefixVal = tag.GetInt("prefix");
                if (prefixVal != 0) ioTag["prefix"] = (byte)prefixVal;
                if (tag.ContainsKey("modData")) ioTag["data"] = tag.Get<TagCompound>("modData");
                item = ItemIO.Load(ioTag);
            }
            else
            {
                // Current format: delegate to ItemIO, which automatically converts items from
                // unloaded mods into UnloadedItem placeholders that preserve the full original
                // tag and self-heal back to the real item when the mod is re-enabled.
                item = ItemIO.Load(tag);
            }
            var stored = new StoredItemStack(item)
            {
                InsertionOrder = tag.ContainsKey("order") ? tag.GetLong("order") : 0
            };

            if (item.ModItem != null)
            {
                var modTag = new TagCompound();
                item.ModItem.SaveData(modTag);
                if (modTag.Count > 0)
                    stored.ModData = modTag;
            }

            // Preserve full tag when item has GlobalItem data (e.g. enchantments from other mods)
            // so subsequent Save() calls faithfully re-serialize all per-instance data.
            if (tag.ContainsKey("globalData") || stored.ModData != null)
                stored.FullItemTag = tag;

            return stored;
        }

        // Compact binary serialization for network packets. Uses integer IDs instead of
        // mod name + item name strings, keeping each stack at ~18 bytes vs ~373 bytes with TagCompound.
        // Both sides have the same mods loaded so integer IDs are safe over the wire.
        public void WriteNet(BinaryWriter writer)
        {
            writer.Write(ItemType);
            writer.Write(PrefixId);
            writer.Write(Stack);
            writer.Write(InsertionOrder);
            bool hasFullItemTag = FullItemTag != null;
            writer.Write(hasFullItemTag);
            if (hasFullItemTag)
            {
                // FullItemTag embeds ModData under the "data" key, so sending
                // ModData separately would be redundant (50-200 extra bytes).
                TagIO.Write(FullItemTag, writer);
            }
            else
            {
                bool hasModData = ModData != null;
                writer.Write(hasModData);
                if (hasModData)
                    TagIO.Write(ModData, writer);
            }
        }

        //Deserializes a compact network-format stack written by <see cref="WriteNet"/>.
        public static StoredItemStack ReadNet(BinaryReader reader)
        {
            var stack = new StoredItemStack
            {
                ItemType = reader.ReadInt32(),
                PrefixId = reader.ReadInt32(),
                Stack = reader.ReadInt32(),
                InsertionOrder = reader.ReadInt64()
            };

            if (reader.ReadBoolean())
            {
                stack.FullItemTag = TagIO.Read(reader);
                // Derive ModData from the embedded "data" key so all downstream
                // code (matching, extraction, display) works without a separate send.
                if (stack.FullItemTag.ContainsKey("data"))
                {
                    var data = stack.FullItemTag.GetCompound("data");
                    if (data.Count > 0)
                        stack.ModData = data;
                }
            }
            else
            {
                stack.ModData = reader.ReadBoolean() ? TagIO.Read(reader) : null;
            }

            return stack;
        }

        // Returns true if this stack can merge with another (same type and prefix).
        public bool CanMergeWith(StoredItemStack other)
        {
            return ItemType == other.ItemType && PrefixId == other.PrefixId;
        }

        // Returns true if this stack matches the given item type and prefix.
        public bool Matches(int itemType, int prefixId = -1)
        {
            return ItemType == itemType && (prefixId == -1 || PrefixId == prefixId);
        }
    }
}
