using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using Requisition.Content.UI;

namespace Requisition.Content.UI.Elements
{
    // Flat, modern tab button. Active state shown with a brighter background
    // and a 2 px accent line at the bottom edge.
    public class TSTab : UIElement
    {
        private readonly string _text;
        private bool _hovered;

        public bool Active { get; set; }

        public TSTab(string text)
        {
            _text = text;
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

            Color bg;
            if (Active)
                bg = new Color(80, 105, 200) * 0.95f;
            else if (_hovered)
                bg = new Color(60, 80, 160) * 0.85f;
            else
                bg = new Color(40, 52, 110) * 0.90f;

            Utils.DrawInvBG(sb, rect, bg);

            // Bright accent line at the bottom of the active tab
            if (Active)
                UIDrawHelpers.DrawSolidRect(sb,
                    new Rectangle(rect.X + 3, rect.Bottom - 2, rect.Width - 6, 2),
                    new Color(150, 185, 255));

            var textSize = FontAssets.MouseText.Value.MeasureString(_text) * 0.75f;
            Color textColor = Active ? Color.White
                            : _hovered ? new Color(210, 225, 255)
                            : new Color(150, 170, 215);
            Utils.DrawBorderString(sb, _text,
                new Vector2(dims.X + (dims.Width - textSize.X) / 2f,
                            dims.Y + (dims.Height - textSize.Y) / 2f),
                textColor, 0.75f);

            if (_hovered)
                Main.LocalPlayer.mouseInterface = true;
        }
    }
}
