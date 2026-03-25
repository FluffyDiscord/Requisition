using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace TerraStorage.Content.Items
{
    /// <summary>
    /// Adds Crafting Core condition tooltips to vanilla items that can be placed
    /// in the Crafting Core to provide crafting conditions.
    /// </summary>
    public class ConditionGlobalItem : GlobalItem
    {
        private static Dictionary<int, LocalizedText> _conditionTooltips;

        public override void SetStaticDefaults()
        {
            _conditionTooltips = new Dictionary<int, LocalizedText>
            {
                [ItemID.BottomlessBucket]      = Mod.GetLocalization("ConditionTooltips.BottomlessBucket"),
                [ItemID.BottomlessLavaBucket]  = Mod.GetLocalization("ConditionTooltips.BottomlessLavaBucket"),
                [ItemID.BottomlessHoneyBucket] = Mod.GetLocalization("ConditionTooltips.BottomlessHoneyBucket"),
                [ItemID.IceMachine]            = Mod.GetLocalization("ConditionTooltips.IceMachine"),
                [ItemID.Tombstone]        = Mod.GetLocalization("ConditionTooltips.Gravestone"),
                [ItemID.GraveMarker]      = Mod.GetLocalization("ConditionTooltips.Gravestone"),
                [ItemID.CrossGraveMarker] = Mod.GetLocalization("ConditionTooltips.Gravestone"),
                [ItemID.Headstone]        = Mod.GetLocalization("ConditionTooltips.Gravestone"),
                [ItemID.Gravestone]       = Mod.GetLocalization("ConditionTooltips.Gravestone"),
                [ItemID.Obelisk]          = Mod.GetLocalization("ConditionTooltips.Gravestone"),
            };
        }

        public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
        {
            if (_conditionTooltips != null && _conditionTooltips.TryGetValue(item.type, out var tip))
                tooltips.Add(new TooltipLine(Mod, "CraftingCondition", tip.Value));
        }
    }
}
