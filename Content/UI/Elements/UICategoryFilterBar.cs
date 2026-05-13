using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using Requisition.Content.UI;

namespace Requisition.Content.UI.Elements
{
    // Item categories used by <see cref="UICategoryFilterBar"/> to classify items.
    // The integer value of each member is used as an index into the filter-enabled
    // array, so the order must not be changed.
    public enum ItemCategory
    {
        MeleeWeapons,
        RangedWeapons,
        MagicWeapons,
        SummonerWeapons,
        ThrowingWeapons,
        RadiantWeapons,    // Thorium Healer
        SymphonicWeapons,  // Thorium Bard
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

    // A bar of 16 toggle buttons, one per <see cref="ItemCategory"/>. Left-click toggles
    // an individual category; right-click on the only active category re-enables all,
    // right-click on any other category isolates it. Raises <see cref="OnFilterChanged"/>
    // on every state change. Classification results are cached to avoid repeated
    // <c>SetDefaults</c> calls during filtering.
    public class UICategoryFilterBar : UIElement
    {
        private static readonly Dictionary<int, ItemCategory> _categoryCache = new();
        private static readonly HashSet<int> _crossModBossSummoners = new();
        private static bool _crossModInitialized;

        // Thorium damage classes, resolved at init time
        private static DamageClass _thoriumRadiant;
        private static DamageClass _thoriumSymphonic;
        private static bool _thoriumLoaded;

        // Categories that are actually active (excludes Thorium categories when not loaded)
        private static List<ItemCategory> _activeCategories;
        private static List<int> _activeCategoryIcons;
        private static List<string> _activeCategoryTooltips;

        private static int CategoryCount => _activeCategories?.Count ?? 0;

        private readonly bool[] _enabled;
        public event Action OnFilterChanged;

        public UICategoryFilterBar()
        {
            InitActiveCategories();
            _enabled = new bool[CategoryCount];
            for (int i = 0; i < CategoryCount; i++)
                _enabled[i] = true;
        }

        private static void InitActiveCategories()
        {
            if (_activeCategories != null) return;

            _thoriumLoaded = ModLoader.TryGetMod("ThoriumMod", out var thorium);
            if (_thoriumLoaded)
            {
                if (thorium.TryFind<DamageClass>("HealerDamage", out var radiant))
                    _thoriumRadiant = radiant;
                if (thorium.TryFind<DamageClass>("BardDamage", out var symphonic))
                    _thoriumSymphonic = symphonic;
            }

            _activeCategories = new List<ItemCategory>();
            _activeCategoryIcons = new List<int>();
            _activeCategoryTooltips = new List<string>();

            void Add(ItemCategory cat, int icon, string tooltip)
            {
                _activeCategories.Add(cat);
                _activeCategoryIcons.Add(icon);
                _activeCategoryTooltips.Add(tooltip);
            }

            Add(ItemCategory.MeleeWeapons,    ItemID.GoldBroadsword,       "Melee Weapons");
            Add(ItemCategory.RangedWeapons,   ItemID.GoldBow,              "Ranged Weapons");
            Add(ItemCategory.MagicWeapons,    ItemID.WaterBolt,            "Magic Weapons");
            Add(ItemCategory.SummonerWeapons, ItemID.SlimeStaff,           "Summoner Weapons");
            Add(ItemCategory.ThrowingWeapons, ItemID.Shuriken,             "Rogue/Throwing Weapons");

            if (_thoriumLoaded)
            {
                int radiantIcon = ItemID.FallenStar;
                int symphonicIcon = ItemID.Harp;
                if (thorium.TryFind<ModItem>("HolyStaff", out var rItem))
                    radiantIcon = rItem.Type;
                if (thorium.TryFind<ModItem>("OceanDrum", out var sItem))
                    symphonicIcon = sItem.Type;

                Add(ItemCategory.RadiantWeapons,   radiantIcon,   "Radiant Weapons (Healer)");
                Add(ItemCategory.SymphonicWeapons, symphonicIcon, "Symphonic Weapons (Bard)");
            }

            Add(ItemCategory.OtherWeapons,    ItemID.Flamethrower,         "Other Weapons");
            Add(ItemCategory.Ammo,            ItemID.MusketBall,           "Ammunition");
            Add(ItemCategory.Tools,           ItemID.GoldPickaxe,          "Tools (Pick/Axe/Hammer/Rod)");
            Add(ItemCategory.Armor,           ItemID.IronChainmail,        "Armor");
            Add(ItemCategory.Accessories,     ItemID.HermesBoots,          "Accessories");
            Add(ItemCategory.Vanity,          ItemID.FamiliarWig,          "Vanity Items");
            Add(ItemCategory.Potions,         ItemID.HealingPotion,        "Potions & Consumables");
            Add(ItemCategory.Placables,       ItemID.Wood,                 "Placeable Items");
            Add(ItemCategory.BossSummoners,   ItemID.SuspiciousLookingEye, "Boss Summoning Items");
            Add(ItemCategory.Materials,       ItemID.Gel,                  "Crafting Materials");
            Add(ItemCategory.Miscellaneous,   ItemID.Torch,                "Miscellaneous");
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
                    for (int i = 0; i < CategoryCount; i++)
                        if (_enabled[i]) enabledCount++;
                    onlyThisEnabled = enabledCount == 1;
                }

                if (onlyThisEnabled)
                {
                    // Re-enable all
                    for (int i = 0; i < CategoryCount; i++)
                        _enabled[i] = true;
                }
                else
                {
                    // Select only this one
                    for (int i = 0; i < CategoryCount; i++)
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

        private int GetButtonAtMouse(Vector2 mousePos)
        {
            var dims = GetDimensions();
            float btnSize = dims.Height;
            float relX = mousePos.X - dims.X;
            float relY = mousePos.Y - dims.Y;

            int columns = Math.Max(1, (int)(dims.Width / btnSize));
            int rows = (CategoryCount + columns - 1) / columns;

            int col = (int)(relX / btnSize);
            int row = (int)(relY / btnSize);
            if (col < 0 || col >= columns || row < 0 || row >= rows)
                return -1;

            int index = row * columns + col;
            return index < CategoryCount ? index : -1;
        }

        // Returns true if the item passes the current filter state. When all categories
        // are enabled the classification is skipped entirely for performance.
        public bool PassesFilter(int itemType)
        {
            // Short-circuit: if all categories are on, nothing is filtered out.
            bool allEnabled = true;
            for (int i = 0; i < CategoryCount; i++)
            {
                if (!_enabled[i]) { allEnabled = false; break; }
            }
            if (allEnabled) return true;

            var category = ClassifyItem(itemType);
            int idx = _activeCategories.IndexOf(category);
            return idx < 0 || _enabled[idx];
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

        // Classifies a fully initialized <see cref="Item"/> instance into an
        // <see cref="ItemCategory"/>. Boss summoners are checked first because they
        // are consumable and would otherwise match the Potions branch.
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
                if (_thoriumRadiant != null && item.DamageType.CountsAsClass(_thoriumRadiant))
                    return ItemCategory.RadiantWeapons;
                if (_thoriumSymphonic != null && item.DamageType.CountsAsClass(_thoriumSymphonic))
                    return ItemCategory.SymphonicWeapons;
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
            float btnSize = dims.Height;
            int columns = Math.Max(1, (int)(dims.Width / btnSize));

            for (int i = 0; i < CategoryCount; i++)
            {
                int col = i % columns;
                int row = i / columns;
                float x = dims.X + col * btnSize;
                float y = dims.Y + row * btnSize;
                var btnRect = new Rectangle((int)x, (int)y, (int)btnSize - 2, (int)btnSize - 2);

                bool hover = btnRect.Contains(Main.MouseScreen.ToPoint());
                Color bgColor;
                if (_enabled[i])
                    bgColor = hover ? new Color(73, 94, 171) : new Color(53, 74, 141) * 0.9f;
                else
                    bgColor = hover ? new Color(50, 50, 60) : new Color(30, 30, 40) * 0.9f;

                Utils.DrawInvBG(spriteBatch, btnRect, bgColor);

                // Draw item icon
                int iconItemType = _activeCategoryIcons[i];
                Color iconTint = _enabled[i] ? Color.White : Color.Gray * 0.6f;
                DrawFilterIcon(spriteBatch, iconItemType, btnRect, iconTint);

                if (hover)
                {
                    Main.LocalPlayer.mouseInterface = true;
                    Main.hoverItemName = _activeCategoryTooltips[i] + "\n[Left-click: isolate | Right-click: toggle]";
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

    }
}
