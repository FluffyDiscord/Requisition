using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using TerraStorage.Content.UI;

namespace TerraStorage.Content.UI.Elements
{
    /// <summary>
    /// Item categories used by <see cref="UICategoryFilterBar"/> to classify items.
    /// The integer value of each member is used as an index into the filter-enabled
    /// array, so the order must not be changed.
    /// </summary>
    public enum ItemCategory
    {
        MeleeWeapons,
        RangedWeapons,
        MagicWeapons,
        SummonerWeapons,
        ThrowingWeapons,
        OtherWeapons,
        Ammo,
        Tools,
        Armor,
        Accessories,
        Vanity,
        Potions,
        Placables,
        BossSummoners,
        Materials,
        Miscellaneous
    }

    /// <summary>
    /// A bar of 16 toggle buttons, one per <see cref="ItemCategory"/>. Left-click toggles
    /// an individual category; right-click on the only active category re-enables all,
    /// right-click on any other category isolates it. Raises <see cref="OnFilterChanged"/>
    /// on every state change. Classification results are cached to avoid repeated
    /// <c>SetDefaults</c> calls during filtering.
    /// </summary>
    public class UICategoryFilterBar : UIElement
    {
        // Representative item IDs for each category icon
        private static readonly int[] CategoryItemIcons =
        {
            ItemID.GoldBroadsword,       // Melee
            ItemID.GoldBow,              // Ranged
            ItemID.WaterBolt,            // Magic
            ItemID.SlimeStaff,           // Summon
            ItemID.Shuriken,             // Throwing
            ItemID.Flamethrower,         // Other Weapons
            ItemID.MusketBall,           // Ammo
            ItemID.GoldPickaxe,          // Tools
            ItemID.IronChainmail,        // Armor
            ItemID.HermesBoots,          // Accessories
            ItemID.FamiliarWig,          // Vanity
            ItemID.HealingPotion,        // Potions
            ItemID.Wood,                 // Placeable
            ItemID.SuspiciousLookingEye, // Boss Summoners
            ItemID.Gel,                  // Materials
            ItemID.Torch                 // Misc
        };

        private static readonly Dictionary<int, ItemCategory> _categoryCache = new();
        private static readonly HashSet<int> _crossModBossSummoners = new();
        private static bool _crossModInitialized;

        private readonly bool[] _enabled = new bool[16];
        public event Action OnFilterChanged;

        public UICategoryFilterBar()
        {
            for (int i = 0; i < 16; i++)
                _enabled[i] = true;
        }

        public override void LeftClick(UIMouseEvent evt)
        {
            base.LeftClick(evt);
            if (UIClickBlocker.IsConsumed) return;
            int index = GetButtonAtMouse(evt.MousePosition);
            if (index >= 0)
            {
                // Check if this is the only one enabled
                bool onlyThisEnabled = _enabled[index];
                if (onlyThisEnabled)
                {
                    int enabledCount = 0;
                    for (int i = 0; i < 16; i++)
                        if (_enabled[i]) enabledCount++;
                    onlyThisEnabled = enabledCount == 1;
                }

                if (onlyThisEnabled)
                {
                    // Re-enable all
                    for (int i = 0; i < 16; i++)
                        _enabled[i] = true;
                }
                else
                {
                    // Select only this one
                    for (int i = 0; i < 16; i++)
                        _enabled[i] = i == index;
                }
                OnFilterChanged?.Invoke();
            }
        }

        public override void RightClick(UIMouseEvent evt)
        {
            base.RightClick(evt);
            if (UIClickBlocker.IsConsumed) return;
            int index = GetButtonAtMouse(evt.MousePosition);
            if (index >= 0)
            {
                _enabled[index] = !_enabled[index];
                OnFilterChanged?.Invoke();
            }
        }

        private const float BtnSize = 25f;

        private int GetButtonAtMouse(Vector2 mousePos)
        {
            var dims = GetDimensions();
            float relX = mousePos.X - dims.X;
            float relY = mousePos.Y - dims.Y;

            int columns = Math.Max(1, (int)(dims.Width / BtnSize));
            int rows = (16 + columns - 1) / columns;

            int col = (int)(relX / BtnSize);
            int row = (int)(relY / BtnSize);
            if (col < 0 || col >= columns || row < 0 || row >= rows)
                return -1;

            int index = row * columns + col;
            return index < 16 ? index : -1;
        }

        /// <summary>
        /// Returns true if the item passes the current filter state. When all categories
        /// are enabled the classification is skipped entirely for performance.
        /// </summary>
        public bool PassesFilter(int itemType)
        {
            // Short-circuit: if all categories are on, nothing is filtered out.
            bool allEnabled = true;
            for (int i = 0; i < 16; i++)
            {
                if (!_enabled[i]) { allEnabled = false; break; }
            }
            if (allEnabled) return true;

            var category = ClassifyItem(itemType);
            return _enabled[(int)category];
        }

        public static ItemCategory ClassifyItem(int itemType)
        {
            if (!_crossModInitialized)
                InitCrossModOverrides();

            if (_categoryCache.TryGetValue(itemType, out var cached))
                return cached;

            var item = new Item();
            item.SetDefaults(itemType);
            var category = ClassifyItemInstance(item);
            _categoryCache[itemType] = category;
            return category;
        }

        private static void InitCrossModOverrides()
        {
            _crossModInitialized = true;

            // Thorium: boss summoners that lack standard flags
            if (ModLoader.TryGetMod("ThoriumMod", out var thorium))
            {
                TryAddBossSummoner(thorium, "GrandFlareGun");
                TryAddBossSummoner(thorium, "StormFlare");
            }
        }

        private static void TryAddBossSummoner(Mod mod, string itemName)
        {
            if (mod.TryFind<ModItem>(itemName, out var modItem))
                _crossModBossSummoners.Add(modItem.Type);
        }

        /// <summary>
        /// Classifies a fully initialized <see cref="Item"/> instance into an
        /// <see cref="ItemCategory"/>. Boss summoners are checked first because they
        /// are consumable and would otherwise match the Potions branch.
        /// </summary>
        private static ItemCategory ClassifyItemInstance(Item item)
        {
            // Boss summoners are checked before consumables/potions to avoid misclassification.
            // SortingPriorityBossSpawns is NOT used directly — vanilla puts Life/Mana Crystals
            // in that set even though they don't spawn bosses.
            if (IsBossSummonItem(item))
                return ItemCategory.BossSummoners;

            // Ammo (includes bait — ammo for fishing poles)
            if (item.ammo > 0 || item.bait > 0)
                return ItemCategory.Ammo;

            // Tools (pick/axe/hammer/fishing)
            if (item.pick > 0 || item.axe > 0 || item.hammer > 0 || item.fishingPole > 0)
                return ItemCategory.Tools;

            // Weapons by damage type
            // Also catch items with no listed damage but a non-default damage class and a use action
            // (e.g. Thorium's Healer weapons set damage=-1 but have useStyle + shoot + custom DamageType)
            bool isWeapon = item.damage > 0
                || (item.useStyle > ItemUseStyleID.None && item.shoot > ProjectileID.None && item.DamageType != DamageClass.Default);
            if (isWeapon)
            {
                if (item.DamageType.CountsAsClass(DamageClass.Melee) || item.DamageType == DamageClass.MeleeNoSpeed)
                    return ItemCategory.MeleeWeapons;
                if (item.DamageType.CountsAsClass(DamageClass.Ranged))
                    return ItemCategory.RangedWeapons;
                if (item.DamageType.CountsAsClass(DamageClass.Magic) || item.DamageType == DamageClass.MagicSummonHybrid)
                    return ItemCategory.MagicWeapons;
                if (item.DamageType.CountsAsClass(DamageClass.Summon) || item.DamageType == DamageClass.SummonMeleeSpeed)
                    return ItemCategory.SummonerWeapons;
                if (item.DamageType.CountsAsClass(DamageClass.Throwing))
                    return ItemCategory.ThrowingWeapons;
                return ItemCategory.OtherWeapons;
            }

            // Armor
            if (item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0)
            {
                if (item.vanity)
                    return ItemCategory.Vanity;
                return ItemCategory.Armor;
            }

            // Accessories
            if (item.accessory)
            {
                if (item.vanity)
                    return ItemCategory.Vanity;
                return ItemCategory.Accessories;
            }

            // Vanity items (dyes, etc.)
            if (item.vanity)
                return ItemCategory.Vanity;

            // Potions & Consumables — any consumable that isn't a placeable
            if (item.consumable && item.createTile < TileID.Dirt && item.createWall < 0)
                return ItemCategory.Potions;

            // Placables
            if (item.createTile >= TileID.Dirt || item.createWall >= 0)
                return ItemCategory.Placables;

            // Materials
            if (item.material || ItemID.Sets.IsAMaterial[item.type])
                return ItemCategory.Materials;

            return ItemCategory.Miscellaneous;
        }

        private static bool IsBossSummonItem(Item item)
        {
            // Vanilla: explicit list (SortingPriorityBossSpawns includes non-summoners like Life Crystal)
            if (item.type < ItemID.Count)
            {
                return item.type == ItemID.SlimeCrown
                    || item.type == ItemID.SuspiciousLookingEye
                    || item.type == ItemID.WormFood
                    || item.type == ItemID.BloodySpine
                    || item.type == ItemID.Abeemination
                    || item.type == ItemID.DeerThing
                    || item.type == ItemID.QueenSlimeCrystal
                    || item.type == ItemID.MechanicalEye
                    || item.type == ItemID.MechanicalWorm
                    || item.type == ItemID.MechanicalSkull
                    || item.type == ItemID.LihzahrdPowerCell
                    || item.type == ItemID.CelestialSigil
                    || item.type == ItemID.TruffleWorm
                    || item.type == ItemID.EmpressButterfly;
            }

            // Modded: tagged as boss spawn + doesn't place anything
            if (item.type < ItemID.Sets.SortingPriorityBossSpawns.Length
                && ItemID.Sets.SortingPriorityBossSpawns[item.type] > 0
                && item.createTile < TileID.Dirt && item.createWall < 0)
                return true;

            // Cross-mod explicit overrides for items that lack standard flags
            if (_crossModBossSummoners.Contains(item.type))
                return true;

            return false;
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            var dims = GetDimensions();
            int columns = Math.Max(1, (int)(dims.Width / BtnSize));

            for (int i = 0; i < 16; i++)
            {
                int col = i % columns;
                int row = i / columns;
                float x = dims.X + col * BtnSize;
                float y = dims.Y + row * BtnSize;
                var btnRect = new Rectangle((int)x, (int)y, (int)BtnSize - 2, (int)BtnSize - 2);

                bool hover = btnRect.Contains(Main.MouseScreen.ToPoint());
                Color bgColor;
                if (_enabled[i])
                    bgColor = hover ? new Color(73, 94, 171) : new Color(53, 74, 141) * 0.9f;
                else
                    bgColor = hover ? new Color(50, 50, 60) : new Color(30, 30, 40) * 0.9f;

                Utils.DrawInvBG(spriteBatch, btnRect, bgColor);

                // Draw item icon
                int iconItemType = CategoryItemIcons[i];
                Color iconTint = _enabled[i] ? Color.White : Color.Gray * 0.6f;
                DrawFilterIcon(spriteBatch, iconItemType, btnRect, iconTint);

                if (hover)
                {
                    Main.LocalPlayer.mouseInterface = true;
                    Main.hoverItemName = GetCategoryTooltip(i) + "\n[Left-click: isolate | Right-click: toggle]";
                }
            }
        }

        private static void DrawFilterIcon(SpriteBatch spriteBatch, int itemType, Rectangle cellRect, Color tint)
        {
            Main.instance.LoadItem(itemType);
            var texture = TextureAssets.Item[itemType].Value;
            var sourceRect = Main.itemAnimations[itemType] != null
                ? Main.itemAnimations[itemType].GetFrame(texture)
                : texture.Frame();

            float scale = 1f;
            float maxDim = Math.Max(sourceRect.Width, sourceRect.Height);
            float targetSize = cellRect.Width * 0.65f;
            if (maxDim > targetSize)
                scale = targetSize / maxDim;

            var center = new Vector2(cellRect.X + cellRect.Width / 2f, cellRect.Y + cellRect.Height / 2f);
            var origin = new Vector2(sourceRect.Width / 2f, sourceRect.Height / 2f);

            spriteBatch.Draw(texture, center, sourceRect, tint, 0f, origin, scale, SpriteEffects.None, 0f);
        }

        private static string GetCategoryTooltip(int index)
        {
            return (ItemCategory)index switch
            {
                ItemCategory.MeleeWeapons => "Melee Weapons",
                ItemCategory.RangedWeapons => "Ranged Weapons",
                ItemCategory.MagicWeapons => "Magic Weapons",
                ItemCategory.SummonerWeapons => "Summoner Weapons",
                ItemCategory.ThrowingWeapons => "Rogue/Throwing Weapons",
                ItemCategory.OtherWeapons => "Other Weapons",
                ItemCategory.Ammo => "Ammunition",
                ItemCategory.Tools => "Tools (Pick/Axe/Hammer/Rod)",
                ItemCategory.Armor => "Armor",
                ItemCategory.Accessories => "Accessories",
                ItemCategory.Vanity => "Vanity Items",
                ItemCategory.Potions => "Potions & Consumables",
                ItemCategory.Placables => "Placeable Items",
                ItemCategory.BossSummoners => "Boss Summoning Items",
                ItemCategory.Materials => "Crafting Materials",
                ItemCategory.Miscellaneous => "Miscellaneous",
                _ => ""
            };
        }
    }
}
