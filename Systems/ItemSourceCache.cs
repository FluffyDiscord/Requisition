using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerraStorage.Systems
{
    public readonly struct DropSource
    {
        public readonly int NpcType;
        public readonly float DropRate;
        public readonly int StackMin;
        public readonly int StackMax;

        public DropSource(int npcType, float dropRate, int stackMin = 1, int stackMax = 1)
        {
            NpcType = npcType;
            DropRate = dropRate;
            StackMin = stackMin;
            StackMax = stackMax;
        }
    }

    public readonly struct ShopSource
    {
        public readonly int NpcType;
        public readonly int Price; // base buy price in copper coins

        public ShopSource(int npcType, int price = 0)
        {
            NpcType = npcType;
            Price = price;
        }
    }

    /// <summary>
    /// Builds reverse indices for non-crafting item sources: NPC drops, NPC shops, shimmer.
    /// Built once after recipes are set up and cached for the session.
    /// </summary>
    public class ItemSourceCache : ModSystem
    {
        private Dictionary<int, List<DropSource>> _dropSources = new();
        private Dictionary<int, List<ShopSource>> _shopSources = new();
        private Dictionary<int, List<int>> _shimmerFrom = new(); // targetItem → list of source items

        private bool _built;

        public static ItemSourceCache Instance => ModContent.GetInstance<ItemSourceCache>();

        public List<DropSource> GetDropSources(int itemType)
            => _dropSources.TryGetValue(itemType, out var list) ? list : null;

        public List<ShopSource> GetShopSources(int itemType)
            => _shopSources.TryGetValue(itemType, out var list) ? list : null;

        public List<int> GetShimmerSources(int itemType)
            => _shimmerFrom.TryGetValue(itemType, out var list) ? list : null;

        /// <summary>What this item shimmers into (direct lookup).</summary>
        public int GetShimmerResult(int itemType)
        {
            if (itemType > 0 && itemType < ItemID.Sets.ShimmerTransformToItem.Length)
                return ItemID.Sets.ShimmerTransformToItem[itemType];
            return -1;
        }

        public override void PostSetupRecipes()
        {
            if (!_built)
                BuildAll();
        }

        public override void Unload()
        {
            _dropSources = null;
            _shopSources = null;
            _shimmerFrom = null;
            _built = false;
        }

        private void BuildAll()
        {
            _built = true;
            BuildDropIndex();
            BuildShopIndex();
            BuildShimmerIndex();
        }

        private void BuildDropIndex()
        {
            _dropSources.Clear();

            // Iterate all NPC types including modded
            int maxNpc = NPCLoader.NPCCount;
            for (int npcId = NPCID.NegativeIDCount; npcId < maxNpc; npcId++)
            {
                if (npcId == 0) continue;

                List<IItemDropRule> rules;
                try
                {
                    rules = Main.ItemDropsDB.GetRulesForNPCID(npcId, false);
                }
                catch
                {
                    continue;
                }

                if (rules == null || rules.Count == 0) continue;

                foreach (var rule in rules)
                    ExtractDrops(rule, npcId, 1f);
            }
        }

        private void ExtractDrops(IItemDropRule rule, int npcId, float parentChance)
        {
            if (rule is CommonDrop cd)
            {
                float rate = parentChance * cd.chanceNumerator / (float)cd.chanceDenominator;
                AddDrop(cd.itemId, npcId, rate, cd.amountDroppedMinimum, cd.amountDroppedMaximum);
            }
            else if (rule is ItemDropWithConditionRule cdr)
            {
                float rate = parentChance * cdr.chanceNumerator / (float)cdr.chanceDenominator;
                AddDrop(cdr.itemId, npcId, rate, cdr.amountDroppedMinimum, cdr.amountDroppedMaximum);
            }
            else if (rule is OneFromOptionsDropRule ofr)
            {
                float rate = parentChance * ofr.chanceNumerator / (float)ofr.chanceDenominator / ofr.dropIds.Length;
                foreach (int id in ofr.dropIds)
                    AddDrop(id, npcId, rate);
            }
            else if (rule is OneFromOptionsNotScaledWithLuckDropRule ofnr)
            {
                float rate = parentChance * ofnr.chanceNumerator / (float)ofnr.chanceDenominator / ofnr.dropIds.Length;
                foreach (int id in ofnr.dropIds)
                    AddDrop(id, npcId, rate);
            }
            else if (rule is DropBasedOnExpertMode ebm)
            {
                ExtractDrops(ebm.ruleForNormalMode, npcId, parentChance);
            }
            else if (rule is DropBasedOnMasterMode mbm)
            {
                ExtractDrops(mbm.ruleForDefault, npcId, parentChance);
            }

            // Traverse chained rules
            if (rule.ChainedRules != null)
            {
                foreach (var chain in rule.ChainedRules)
                    ExtractDrops(chain.RuleToChain, npcId, parentChance);
            }
        }

        private void AddDrop(int itemId, int npcId, float rate, int stackMin = 1, int stackMax = 1)
        {
            if (itemId <= 0) return;

            if (!_dropSources.TryGetValue(itemId, out var list))
            {
                list = new List<DropSource>();
                _dropSources[itemId] = list;
            }

            // Avoid duplicate NPC entries for the same item
            foreach (var existing in list)
            {
                if (existing.NpcType == npcId)
                    return;
            }

            list.Add(new DropSource(npcId, rate, stackMin, stackMax));
        }

        private void BuildShopIndex()
        {
            _shopSources.Clear();

            foreach (var shop in NPCShopDatabase.AllShops)
            {
                if (shop is not NPCShop npcShop) continue;

                int npcType = npcShop.NpcType;
                foreach (var entry in npcShop.ActiveEntries)
                {
                    int itemType = entry.Item.type;
                    if (itemType <= 0) continue;

                    if (!_shopSources.TryGetValue(itemType, out var list))
                    {
                        list = new List<ShopSource>();
                        _shopSources[itemType] = list;
                    }

                    // Avoid duplicate
                    bool exists = false;
                    foreach (var s in list)
                    {
                        if (s.NpcType == npcType)
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                        list.Add(new ShopSource(npcType, entry.Item.value));
                }
            }
        }

        private void BuildShimmerIndex()
        {
            _shimmerFrom.Clear();

            var arr = ItemID.Sets.ShimmerTransformToItem;
            for (int i = 1; i < arr.Length; i++)
            {
                int target = arr[i];
                if (target <= 0) continue;

                if (!_shimmerFrom.TryGetValue(target, out var list))
                {
                    list = new List<int>();
                    _shimmerFrom[target] = list;
                }
                list.Add(i);
            }
        }
    }
}
