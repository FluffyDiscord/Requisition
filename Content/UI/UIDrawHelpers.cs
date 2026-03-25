using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace TerraStorage.Content.UI
{
    /// <summary>
    /// Shared drawing helpers for all TerraStorage UI panels.
    /// </summary>
    internal static class UIDrawHelpers
    {
        private static Texture2D _pixel;

        private static Texture2D GetPixel(SpriteBatch sb)
        {
            if (_pixel == null || _pixel.IsDisposed)
            {
                _pixel = new Texture2D(sb.GraphicsDevice, 1, 1);
                _pixel.SetData(new[] { Color.White });
            }
            return _pixel;
        }

        private static Color UnderlayColor
        {
            get
            {
                int opacity = TerraStorageClientConfig.Instance?.PanelUnderlayOpacity ?? 85;
                int alpha = (int)(opacity / 100f * 255);
                return new Color(10, 10, 20, alpha);
            }
        }

        /// <summary>
        /// Draws a dark underlay behind a UIPanel to reduce transparency bleed-through.
        /// Call before base.Draw() or base.DrawSelf().
        /// </summary>
        public static void DrawPanelUnderlay(SpriteBatch sb, UIElement panel)
        {
            var dims = panel.GetDimensions();
            sb.Draw(GetPixel(sb),
                new Rectangle((int)dims.X, (int)dims.Y, (int)dims.Width, (int)dims.Height),
                UnderlayColor);
        }

        /// <summary>
        /// Draws a dark underlay at a specific rectangle.
        /// </summary>
        public static void DrawUnderlay(SpriteBatch sb, Rectangle rect)
        {
            sb.Draw(GetPixel(sb), rect, UnderlayColor);
        }

        /// <summary>
        /// Draws a dark underlay at a specific position and size.
        /// </summary>
        public static void DrawUnderlay(SpriteBatch sb, float x, float y, float w, float h)
        {
            sb.Draw(GetPixel(sb),
                new Rectangle((int)x, (int)y, (int)w, (int)h),
                UnderlayColor);
        }

        /// <summary>
        /// Draws a solid colored rectangle.
        /// </summary>
        public static void DrawSolidRect(SpriteBatch sb, Rectangle rect, Color color)
        {
            sb.Draw(GetPixel(sb), rect, color);
        }

        /// <summary>
        /// Draws a diagonal-stripe resize grip inside <paramref name="rect"/>.
        /// Four parallel stripes run at 45° from the bottom-right corner outward,
        /// matching the classic OS resize handle appearance.
        /// </summary>
        public static void DrawResizeHandle(SpriteBatch sb, Rectangle rect, Color color)
        {
            var pixel = GetPixel(sb);
            // Each stripe is a 2px-wide rotated segment anchored to the bottom-right corner.
            // Offset d controls how far from the corner each stripe sits.
            float[] offsets = { 4f, 8f, 12f, 16f };
            float angle = -(float)(Math.PI / 4); // -45° = lower-left to upper-right
            foreach (float d in offsets)
            {
                if (d > rect.Width || d > rect.Height) continue;
                float cx = rect.Right - d * 0.5f;
                float cy = rect.Bottom - d * 0.5f;
                float len = d * 1.4142f; // d × √2
                sb.Draw(pixel, new Vector2(cx, cy), null, color, angle,
                    new Vector2(0.5f, 0.5f), new Vector2(len, 2f), SpriteEffects.None, 0f);
            }
        }

        /// <summary>
        /// Draws an NPC sprite scaled to fit inside a cell rectangle.
        /// </summary>
        public static void DrawNpcInSlot(SpriteBatch sb, int npcType, Rectangle cellRect)
        {
            int absType = Math.Abs(npcType);
            if (absType <= 0 || absType >= TextureAssets.Npc.Length) return;

            Main.instance.LoadNPC(absType);
            var tex = TextureAssets.Npc[absType].Value;
            int frameCount = Main.npcFrameCount[absType];
            if (frameCount <= 0) frameCount = 1;
            var frame = new Rectangle(0, 0, tex.Width, tex.Height / frameCount);

            float maxDim = Math.Max(frame.Width, frame.Height);
            float scale = (cellRect.Width - 4f) / maxDim;

            var center = new Vector2(cellRect.X + cellRect.Width / 2f, cellRect.Y + cellRect.Height / 2f);
            var origin = new Vector2(frame.Width / 2f, frame.Height / 2f);
            sb.Draw(tex, center, frame, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
        }

        private static Dictionary<int, int> _tileToItemCache;

        /// <summary>
        /// Returns the item type that places a given tile, or 0 if none found.
        /// </summary>
        public static int GetItemForTile(int tileId)
        {
            if (_tileToItemCache == null)
            {
                _tileToItemCache = new Dictionary<int, int>();
                for (int i = 1; i < ItemLoader.ItemCount; i++)
                {
                    try
                    {
                        var item = new Item();
                        item.SetDefaults(i);
                        if (item.createTile >= TileID.Dirt && !_tileToItemCache.ContainsKey(item.createTile))
                            _tileToItemCache[item.createTile] = i;
                    }
                    catch { }
                }
            }
            return _tileToItemCache.TryGetValue(tileId, out int itemType) ? itemType : 0;
        }

        /// <summary>
        /// Calls UserInterface.Update while suppressing mouse clicks if UIClickBlocker
        /// has already consumed the click this frame. Prevents click-through between
        /// overlapping mod UIs.
        /// </summary>
        public static void SafeUpdate(UserInterface ui, GameTime gameTime)
        {
            if (!UIClickBlocker.IsConsumed)
            {
                ui.Update(gameTime);
                return;
            }

            // Another UI consumed the click — suppress mouse buttons during Update
            bool savedLeft = Main.mouseLeft;
            bool savedRight = Main.mouseRight;
            Main.mouseLeft = false;
            Main.mouseRight = false;
            try
            {
                ui.Update(gameTime);
            }
            finally
            {
                Main.mouseLeft = savedLeft;
                Main.mouseRight = savedRight;
            }
        }

        /// <summary>Draws a 1px border rectangle.</summary>
        public static void DrawRectBorder(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
        {
            int t = Math.Max(1, thickness);
            DrawSolidRect(sb, new Rectangle(rect.X,             rect.Y,              rect.Width, t),          color); // top
            DrawSolidRect(sb, new Rectangle(rect.X,             rect.Bottom - t,     rect.Width, t),          color); // bottom
            DrawSolidRect(sb, new Rectangle(rect.X,             rect.Y,              t,          rect.Height), color); // left
            DrawSolidRect(sb, new Rectangle(rect.Right - t,     rect.Y,              t,          rect.Height), color); // right
        }

        /// <summary>Draws an item icon scaled to fit a cell rectangle, with optional stack count.</summary>
        public static void DrawItemInCell(SpriteBatch sb, int itemType, int stack, Rectangle cell)
        {
            Main.instance.LoadItem(itemType);
            var texture = TextureAssets.Item[itemType].Value;
            var sourceRect = Main.itemAnimations[itemType] != null
                ? Main.itemAnimations[itemType].GetFrame(texture)
                : texture.Frame();

            float scale = 1f;
            float maxDim = Math.Max(sourceRect.Width, sourceRect.Height);
            if (maxDim > cell.Width - 8)
                scale = (cell.Width - 8f) / maxDim;

            var center = new Vector2(cell.X + cell.Width / 2f, cell.Y + cell.Height / 2f);
            var origin = new Vector2(sourceRect.Width / 2f, sourceRect.Height / 2f);
            sb.Draw(texture, center, sourceRect, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);

            if (stack > 1)
                Utils.DrawBorderString(sb, stack.ToString(),
                    new Vector2(cell.Right - 4, cell.Bottom - 4),
                    Color.White, 0.6f, 1f, 1f);
        }
    }
}
