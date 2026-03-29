using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TerraStorage.Content.UI.Elements;
using TerraStorage.Systems;

namespace TerraStorage.Content.Items
{
    // Appends "In Storage: X" to item tooltips when the player has recently
    // opened a Terminal. Uses the last-opened terminal's disk IDs.
    // Hold Alt to show debug classification info.
    public class StorageCountTooltip : GlobalItem
    {
        public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
        {
            if (Main.gameMenu) return;

            // Storage count
            if (TerraStorageClientConfig.Instance?.InStorageTooltip == true)
            {
                var player = StoragePlayerSystem.Local;
                var diskIds = player.LastOpenedDiskIds;
                if (diskIds.Count > 0)
                {
                    int count = StorageWorldSystem.Instance.CountItem(diskIds, item.type);
                    if (count > 0)
                    {
                        tooltips.Add(new TooltipLine(Mod, "StorageCount",
                            $"In Storage: {count}") { OverrideColor = new Color(150, 200, 255) });
                    }
                }
            }

            // Debug: hold Alt to show classification properties (requires client config)
            if (TerraStorageClientConfig.Instance?.DebugTooltips == true
                && (Main.keyState.IsKeyDown(Keys.LeftAlt) || Main.keyState.IsKeyDown(Keys.RightAlt)))
            {
                var cat = UICategoryFilterBar.ClassifyItem(item.type);
                int bossPriority = item.type < ItemID.Sets.SortingPriorityBossSpawns.Length
                    ? ItemID.Sets.SortingPriorityBossSpawns[item.type] : 0;

                tooltips.Add(new TooltipLine(Mod, "DebugHeader", "--- Debug Info ---") { OverrideColor = Color.Yellow });
                tooltips.Add(new TooltipLine(Mod, "DebugCategory", $"Category: {cat}") { OverrideColor = Color.Yellow });
                tooltips.Add(new TooltipLine(Mod, "DebugType", $"type={item.type} modded={item.type >= ItemID.Count}") { OverrideColor = Color.Gray });
                tooltips.Add(new TooltipLine(Mod, "DebugDamage", $"damage={item.damage} useStyle={item.useStyle} shoot={item.shoot}") { OverrideColor = Color.Gray });
                tooltips.Add(new TooltipLine(Mod, "DebugFlags", $"consumable={item.consumable} makeNPC={item.makeNPC} bait={item.bait} ammo={item.ammo}") { OverrideColor = Color.Gray });
                tooltips.Add(new TooltipLine(Mod, "DebugSlots", $"head={item.headSlot} body={item.bodySlot} leg={item.legSlot} accessory={item.accessory}") { OverrideColor = Color.Gray });
                tooltips.Add(new TooltipLine(Mod, "DebugPlace", $"createTile={item.createTile} createWall={item.createWall}") { OverrideColor = Color.Gray });
                tooltips.Add(new TooltipLine(Mod, "DebugMisc", $"material={item.material} vanity={item.vanity} potion={item.potion}") { OverrideColor = Color.Gray });
                tooltips.Add(new TooltipLine(Mod, "DebugBoss", $"BossSpawnPriority={bossPriority}") { OverrideColor = Color.Gray });
                if (item.DamageType != null)
                    tooltips.Add(new TooltipLine(Mod, "DebugDmgType", $"DamageType={item.DamageType.DisplayName} ({item.DamageType.GetType().Name})") { OverrideColor = Color.Gray });
            }
        }
    }
}
