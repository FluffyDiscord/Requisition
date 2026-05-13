using System;
using System.Collections.Generic;
using System.Text;
using Terraria;
using Terraria.ModLoader;

namespace Requisition.Content.UI.Elements
{
    // Shared item search logic supporting prefix modes:
    //   (none) — search by item name
    //   #      — search by tooltip text
    //   @      — search by mod display name ("Terraria" for vanilla)
    public static class ItemSearchHelper
    {
        private static readonly Dictionary<int, string> _nameCache    = new();
        private static readonly Dictionary<int, string> _tooltipCache = new();
        private static readonly Dictionary<int, string> _modCache     = new();

        public enum SearchMode { Name, Tooltip, Mod }

        public static (SearchMode mode, string query) Parse(string search)
        {
            if (search != null && search.StartsWith("#"))
                return (SearchMode.Tooltip, search.Substring(1));
            if (search != null && search.StartsWith("@"))
                return (SearchMode.Mod, search.Substring(1));
            return (SearchMode.Name, search ?? "");
        }

        //Returns true if <paramref name="itemType"/> matches <paramref name="search"/>.
        public static bool Matches(int itemType, string search)
        {
            if (string.IsNullOrEmpty(search))
                return true;

            var (mode, query) = Parse(search);
            if (string.IsNullOrEmpty(query))
                return true;

            return mode switch
            {
                SearchMode.Tooltip => GetTooltip(itemType).Contains(query, StringComparison.OrdinalIgnoreCase),
                SearchMode.Mod     => GetModName(itemType).Contains(query, StringComparison.OrdinalIgnoreCase),
                _                  => GetName(itemType).Contains(query, StringComparison.OrdinalIgnoreCase),
            };
        }

        public static string GetName(int itemType)
        {
            if (!_nameCache.TryGetValue(itemType, out string name))
            {
                var item = new Item();
                item.SetDefaults(itemType);
                name = item.Name;
                _nameCache[itemType] = name;
            }
            return name;
        }

            // Returns all tooltip lines for <paramref name="itemType"/> concatenated
        // into a single space-separated string for substring searching.
        // Includes both the static tooltip text and dynamic stat properties
        // (damage, defense, bait power, etc.) that Terraria renders on hover.
        public static string GetTooltip(int itemType)
        {
            if (_tooltipCache.TryGetValue(itemType, out string cached))
                return cached;

            var sb = new StringBuilder();

            // Static tooltip lines
            var tip = Lang.GetTooltip(itemType);
            for (int i = 0; i < tip.Lines; i++)
                sb.Append(tip.GetLine(i)).Append(' ');

            // Key stat keywords that Terraria renders dynamically but aren't in Lang.GetTooltip
            var item = new Item();
            item.SetDefaults(itemType);
            if (item.bait > 0) sb.Append("bait ");
            if (item.defense > 0) sb.Append("defense ");
            if (item.damage > 0) sb.Append("damage ");
            if (item.pick > 0) sb.Append("pickaxe ");
            if (item.axe > 0) sb.Append("axe ");
            if (item.hammer > 0) sb.Append("hammer ");
            if (item.accessory) sb.Append("accessory ");
            if (item.vanity) sb.Append("vanity ");
            if (item.material) sb.Append("material ");
            if (item.potion) sb.Append("potion ");
            if (item.ammo > 0) sb.Append("ammo ");
            if (item.DamageType != null && item.DamageType != DamageClass.Default)
                sb.Append(item.DamageType.DisplayName).Append(' ');

            string result = sb.ToString();
            _tooltipCache[itemType] = result;
            return result;
        }

        // Returns the display name of the mod that added itemType.
        // Vanilla items (no ModItem) are reported as "Terraria" so
        // they can be found with the @terraria prefix.
        public static string GetModName(int itemType)
        {
            if (_modCache.TryGetValue(itemType, out string cached))
                return cached;

            var item = new Item();
            item.SetDefaults(itemType);
            // Null-conditional chain: ModItem is null for vanilla items.
            string name = item.ModItem?.Mod?.DisplayName ?? "Terraria";
            _modCache[itemType] = name;
            return name;
        }
    }
}
