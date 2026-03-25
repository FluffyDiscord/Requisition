using System;
using Microsoft.Xna.Framework;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace TerraStorage.Content.UI.Elements
{
    /// <summary>
    /// UIList with smooth (lerped) scroll position instead of instant jumps.
    /// </summary>
    public class SmoothUIList : UIList
    {
        private UIScrollbar _smoothScrollbar;
        private float _scrollTarget;

        public new void SetScrollbar(UIScrollbar scrollbar)
        {
            _smoothScrollbar = scrollbar;
            base.SetScrollbar(scrollbar);
        }

        public override void ScrollWheel(UIScrollWheelEvent evt)
        {
            if (_smoothScrollbar != null)
            {
                _scrollTarget -= evt.ScrollWheelValue / 120f * 50f;
                _scrollTarget = Math.Max(0, _scrollTarget);
                // Don't call base — we drive ViewPosition from Update
            }
            else
            {
                base.ScrollWheel(evt);
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (_smoothScrollbar == null) return;
            float diff = _scrollTarget - _smoothScrollbar.ViewPosition;
            if (Math.Abs(diff) < 0.01f)
                _smoothScrollbar.ViewPosition = _scrollTarget;
            else
                _smoothScrollbar.ViewPosition += diff * 0.15f;
        }
    }
}
