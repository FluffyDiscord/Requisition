using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.UI;
using TerraStorage.Content.UI;

namespace TerraStorage.Content.UI.Elements
{
    // A text input panel for the Terminal's item/recipe search. Supports three search
    // modes dispatched by <see cref="ItemSearchHelper.Parse"/>: plain name search,
    // <c>#</c>-prefixed tooltip search, and <c>@</c>-prefixed mod-name search.
    // The border color changes per mode to give visual feedback. Right-click clears
    // the field; Escape unfocuses and clears. 
    public class StorageSearchBar : UIPanel
    {
        public string SearchText { get; private set; } = "";
        public event Action<string> OnTextChanged;
        public event Action OnFocused;
        private bool _focused;
        private int _cursorBlink;
        private readonly TextInputHelper _input = new();
        private string _placeholder = "Search...  #tooltip  @mod";

        public void SetPlaceholder(string text) => _placeholder = text;

        public StorageSearchBar()
        {
            BackgroundColor = new Color(35, 43, 79) * 0.9f;
            BorderColor = new Color(89, 116, 213) * 0.7f;
        }

        public void Clear()
        {
            Unfocus();
            if (string.IsNullOrEmpty(SearchText)) return;
            SearchText = "";
            OnTextChanged?.Invoke(SearchText);
        }

        // Unfocus without clearing text (e.g. when the parent window closes).
        public void Unfocus()
        {
            _focused = false;
            _input.Deactivate();
            if (Main.CurrentInputTextTakerOverride == this)
                Main.CurrentInputTextTakerOverride = null;
        }

        public override void LeftClick(UIMouseEvent evt)
        {
            base.LeftClick(evt);
            if (UIClickBlocker.IsConsumed) return;
            if (!_focused) OnFocused?.Invoke();
            _focused = true;
            _input.Reset();
        }

        public override void RightClick(UIMouseEvent evt)
        {
            base.RightClick(evt);
            if (UIClickBlocker.IsConsumed) return;
            if (!string.IsNullOrEmpty(SearchText))
            {
                SearchText = "";
                OnTextChanged?.Invoke(SearchText);
            }
            Unfocus();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (_focused)
            {
                // Check for click outside
                if (Main.mouseLeft && !ContainsPoint(Main.MouseScreen))
                {
                    Unfocus();
                    return;
                }

                // Prevent player from chatting/using items while typing
                Main.chatRelease = false;
                PlayerInput.WritingText = true;
                Main.CurrentInputTextTakerOverride = this;

                string prev = SearchText ?? "";
                string newText = _input.ProcessInput(prev);
                if (newText != prev)
                {
                    SearchText = newText;
                    OnTextChanged?.Invoke(SearchText);
                }

                // Escape to unfocus and clear
                if (Keyboard.GetState().IsKeyDown(Keys.Escape))
                {
                    Unfocus();
                    SearchText = "";
                    OnTextChanged?.Invoke(SearchText);
                }

                _cursorBlink++;
            }
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            // Tint the border based on active search mode
            var (mode, _) = ItemSearchHelper.Parse(SearchText);
            BorderColor = mode switch
            {
                ItemSearchHelper.SearchMode.Tooltip => new Color(220, 160, 60) * 0.9f,  // amber  — #
                ItemSearchHelper.SearchMode.Mod     => new Color(60, 200, 200) * 0.9f,  // teal   — @
                _                                   => new Color(89, 116, 213) * 0.7f,  // default blue
            };

            base.DrawSelf(spriteBatch);

            var dims = GetInnerDimensions();
            string displayText = SearchText;

            if (string.IsNullOrEmpty(displayText) && !_focused)
                displayText = _placeholder;

            Color textColor = string.IsNullOrEmpty(SearchText) && !_focused
                ? Color.Gray
                : Color.White;

            float textHeight = FontAssets.MouseText.Value.MeasureString("A").Y * 0.9f;
            float textY = (dims.Y + (dims.Height - textHeight) / 2f) + 2;

            Utils.DrawBorderString(spriteBatch, displayText, new Vector2(dims.X + 4, textY), textColor, 0.9f);

            // Draw cursor
            if (_focused && _cursorBlink % 40 < 20)
            {
                float textWidth = FontAssets.MouseText.Value.MeasureString(displayText).X * 0.9f;
                Utils.DrawBorderString(spriteBatch, "|", new Vector2(dims.X + 4 + textWidth, textY), Color.White, 0.9f);
            }
        }
    }
}
