using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using TerraStorage.Content.UI;

namespace TerraStorage.Content.UI.Elements
{
    // General-purpose flat button matching the TerraStorage UI style.
    // Hover sound, enabled/disabled state, centered text.
    public class TSButton : UIElement
    {
        private readonly string _text;
        private readonly float _textScale;
        private bool _hovered;

        public bool Enabled { get; set; } = true;

        public TSButton(string text, float textScale = 0.75f)
        {
            _text = text;
            _textScale = textScale;
        }

        public override void MouseOver(UIMouseEvent evt)
        {
            base.MouseOver(evt);
            _hovered = true;
            if (Enabled) SoundEngine.PlaySound(SoundID.MenuTick);
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

            Color bg;
            if (!Enabled)
                bg = new Color(35, 35, 50) * 0.85f;
            else if (_hovered)
                bg = new Color(90, 115, 205) * 0.95f;
            else
                bg = new Color(63, 82, 151) * 0.85f;

            Utils.DrawInvBG(sb, rect, bg);

            var textSize = FontAssets.MouseText.Value.MeasureString(_text) * _textScale;
            Utils.DrawBorderString(sb, _text,
                new Vector2(dims.X + (dims.Width - textSize.X) / 2f,
                            dims.Y + (dims.Height - textSize.Y) / 2f),
                Enabled ? Color.White : Color.Gray, _textScale);

            if (_hovered && Enabled)
                Main.LocalPlayer.mouseInterface = true;
        }
    }
}
