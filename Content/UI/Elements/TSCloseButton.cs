using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;

namespace TerraStorage.Content.UI.Elements
{
    /// <summary>
    /// Unified close button matching the CraftingTree button style:
    /// dark blue background, centered "X" glyph, red tint on hover.
    /// </summary>
    public class TSCloseButton : UIElement
    {
        private readonly Action _onClose;
        private bool _hovered;

        public TSCloseButton(Action onClose)
        {
            _onClose = onClose;
            Width.Set(24, 0f);
            Height.Set(24, 0f);
        }

        public override void LeftClick(UIMouseEvent evt)
        {
            base.LeftClick(evt);
            SoundEngine.PlaySound(SoundID.MenuClose);
            _onClose?.Invoke();
        }

        public override void MouseOver(UIMouseEvent evt)
        {
            base.MouseOver(evt);
            _hovered = true;
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        public override void MouseOut(UIMouseEvent evt)
        {
            base.MouseOut(evt);
            _hovered = false;
        }

        protected override void DrawSelf(SpriteBatch sb)
        {
            var dims = GetDimensions();
            var rect = new Rectangle((int)dims.X, (int)dims.Y, (int)dims.Width, (int)dims.Height);
            var bgColor = _hovered
                ? new Color(120, 50, 50) * 0.95f
                : new Color(63, 82, 151) * 0.8f;
            Utils.DrawInvBG(sb, rect, bgColor);
            var size = FontAssets.MouseText.Value.MeasureString("X") * 0.35f;
            Utils.DrawBorderString(sb, "X",
                new Vector2(dims.X + (dims.Width - size.X) / 2f, dims.Y + (dims.Height - size.Y) / 2f),
                Color.White, 0.35f);
            if (_hovered)
                Main.LocalPlayer.mouseInterface = true;
        }
    }
}
