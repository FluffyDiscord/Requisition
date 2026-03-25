using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using TerraStorage.Content.UI;

namespace TerraStorage.Content.UI.Elements
{
    /// <summary>
    /// A row of seven icon buttons corresponding to each <see cref="SortMode"/>.
    /// Left-click selects a sort mode; right-click on the active button toggles
    /// ascending/descending order, right-click on a different button selects it.
    /// Raises <see cref="OnSortChanged"/> on every state change.
    /// </summary>
    public class UISortBar : UIElement
    {
        // Item icons chosen to be visually intuitive for each sort mode.
        private static readonly int[] SortItemIcons =
        {
            ItemID.Sign,            // ID
            ItemID.Book,            // Name
            ItemID.GoldCoin,        // Value
            ItemID.CopperShortsword,// Dmg/Def
            ItemID.Chest,           // Quantity
            ItemID.CopperBar,       // StackCount
            ItemID.GoldWatch,       // RecentlyAdded
            ItemID.Diamond          // Rarity
        };

        private static readonly string[] SortTooltips =
        {
            "Sort by ID",
            "Sort by Name",
            "Sort by Value",
            "Sort by Damage/Defense",
            "Sort by Quantity",
            "Sort by Max Stack",
            "Sort by Recently Added",
            "Sort by Rarity"
        };

        private int _selected;
        private bool _ascending = true;

        public int Selected => _selected;
        public bool Ascending => _ascending;

        public event Action OnSortChanged;

        public override void LeftClick(UIMouseEvent evt)
        {
            base.LeftClick(evt);
            if (UIClickBlocker.IsConsumed) return;
            int index = GetButtonAtMouse(evt.MousePosition);
            if (index >= 0)
            {
                _selected = index;
                OnSortChanged?.Invoke();
            }
        }

        public override void RightClick(UIMouseEvent evt)
        {
            base.RightClick(evt);
            if (UIClickBlocker.IsConsumed) return;
            int index = GetButtonAtMouse(evt.MousePosition);
            if (index >= 0)
            {
                if (index == _selected)
                    _ascending = !_ascending;
                else
                    _selected = index;
                OnSortChanged?.Invoke();
            }
        }

        private int GetButtonAtMouse(Vector2 mousePos)
        {
            var dims = GetDimensions();
            float relX = mousePos.X - dims.X;
            float relY = mousePos.Y - dims.Y;

            float btnSize = dims.Height;
            if (relY < 0 || relY >= btnSize)
                return -1;

            int col = (int)(relX / btnSize);
            return col >= 0 && col < 8 ? col : -1;
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            var dims = GetDimensions();
            float btnSize = dims.Height;

            for (int i = 0; i < 8; i++)
            {
                float x = dims.X + i * btnSize;
                var btnRect = new Rectangle((int)x, (int)dims.Y, (int)btnSize - 2, (int)btnSize - 2);

                bool hover = btnRect.Contains(Main.MouseScreen.ToPoint());
                bool active = i == _selected;
                Color bgColor;
                if (active)
                    bgColor = hover ? new Color(83, 104, 181) : new Color(73, 94, 171);
                else
                    bgColor = hover ? new Color(50, 50, 60) : new Color(30, 30, 40) * 0.9f;

                Utils.DrawInvBG(spriteBatch, btnRect, bgColor);

                Color iconTint = active ? Color.White : Color.Gray * 0.6f;
                DrawSortIcon(spriteBatch, SortItemIcons[i], btnRect, iconTint);

                // Draw direction arrow on selected
                if (active)
                {
                    string arrow = _ascending ? "^" : "v";
                    Utils.DrawBorderString(spriteBatch, arrow,
                        new Vector2(btnRect.Right - 6, btnRect.Bottom - 6),
                        Color.White, 0.5f, 1f, 1f);
                }

                if (hover)
                {
                    Main.LocalPlayer.mouseInterface = true;
                    string dir = i == _selected ? (_ascending ? " (Asc)" : " (Desc)") : "";
                    Main.hoverItemName = SortTooltips[i] + dir + "\nRight-click to toggle direction";
                }
            }
        }

        private static void DrawSortIcon(SpriteBatch spriteBatch, int itemType, Rectangle cellRect, Color tint)
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
