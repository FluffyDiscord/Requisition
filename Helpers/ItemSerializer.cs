using Terraria;
using Terraria.ModLoader.IO;

namespace TerraStorage.Helpers
{
    /// <summary>
    /// Lightweight NBT serializer for vanilla Item references (type, stack, prefix only).
    /// Used when full mod-data round-tripping via <see cref="Terraria.ModLoader.IO.ItemIO"/>
    /// is not required, such as for simple slot arrays that don't hold modded items.
    /// </summary>
    public static class ItemSerializer
    {
        /// <summary>
        /// Saves an item to a <see cref="TagCompound"/>. Air or null items are saved
        /// with <c>isAir = true</c> so they deserialize back to an empty slot.
        /// </summary>
        public static TagCompound SaveItem(Item item)
        {
            if (item == null || item.IsAir)
                return new TagCompound { ["isAir"] = true };

            return new TagCompound
            {
                ["type"] = item.type,
                ["stack"] = item.stack,
                ["prefix"] = (int)item.prefix,
                ["isAir"] = false
            };
        }

        /// <summary>Loads an item from a <see cref="TagCompound"/>, returning a new air Item if absent.</summary>
        public static Item LoadItem(TagCompound tag)
        {
            if (tag == null || tag.GetBool("isAir"))
                return new Item();

            var item = new Item();
            item.SetDefaults(tag.GetInt("type"));
            item.stack = tag.GetInt("stack");
            item.Prefix(tag.GetInt("prefix"));
            return item;
        }

        public static TagCompound[] SaveItemArray(Item[] items)
        {
            var tags = new TagCompound[items.Length];
            for (int i = 0; i < items.Length; i++)
                tags[i] = SaveItem(items[i]);
            return tags;
        }

        /// <summary>
        /// Loads an item array from a <see cref="TagCompound"/> array, padding with air items
        /// if the saved array is shorter than the requested <paramref name="length"/>
        /// (handles format upgrades that add more slots).
        /// </summary>
        public static Item[] LoadItemArray(TagCompound[] tags, int length)
        {
            var items = new Item[length];
            for (int i = 0; i < length; i++)
            {
                if (i < tags.Length)
                    items[i] = LoadItem(tags[i]);
                else
                {
                    items[i] = new Item();
                    items[i].TurnToAir();
                }
            }
            return items;
        }
    }
}
