using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using SDL2;

namespace TerraStorage.Content.UI.Elements
{
    /// <summary>
    /// Locale-aware text input using FNA's TextInputEXT (fires OS-resolved Unicode chars,
    /// including Cyrillic and IME input). Backspace repeat uses raw keyboard state.
    /// </summary>
    public class TextInputHelper
    {
        private readonly Queue<char> _pendingChars = new();
        private bool _subscribed;
        private KeyboardState _prevKeyState;
        private int _backspaceTimer;

        /// <summary>
        /// Subscribe to TextInputEXT and start OS text input mode.
        /// Safe to call repeatedly — guarded against double-subscription.
        /// </summary>
        public void Activate()
        {
            if (_subscribed) return;
            _subscribed = true;
            TextInputEXT.TextInput += OnTextInput;
            TextInputEXT.StartTextInput();
        }

        /// <summary>
        /// Unsubscribe from TextInputEXT and stop OS text input mode.
        /// </summary>
        public void Deactivate()
        {
            if (!_subscribed) return;
            _subscribed = false;
            TextInputEXT.TextInput -= OnTextInput;
            TextInputEXT.StopTextInput();
            _pendingChars.Clear();
        }

        private void OnTextInput(char c) => _pendingChars.Enqueue(c);

        /// <summary>
        /// Reads buffered text input this frame and returns the updated string.
        /// Must be called every game tick while the field is focused.
        /// </summary>
        public string ProcessInput(string currentText, int maxLength = 0, bool digitsOnly = false)
        {
            var keyState = Keyboard.GetState();
            string result = currentText ?? "";

            // Handle backspace with repeat (physical key, layout-independent)
            if (keyState.IsKeyDown(Keys.Back))
            {
                if (!_prevKeyState.IsKeyDown(Keys.Back))
                {
                    if (result.Length > 0)
                        result = result[..^1];
                    _backspaceTimer = 0;
                }
                else
                {
                    _backspaceTimer++;
                    if (_backspaceTimer > 20 && _backspaceTimer % 3 == 0 && result.Length > 0)
                        result = result[..^1];
                }
            }
            else
            {
                _backspaceTimer = 0;
            }

            // Clipboard shortcuts (Ctrl+C/X/V)
            bool ctrl = keyState.IsKeyDown(Keys.LeftControl) || keyState.IsKeyDown(Keys.RightControl);
            if (ctrl)
            {
                if (keyState.IsKeyDown(Keys.V) && !_prevKeyState.IsKeyDown(Keys.V))
                {
                    try
                    {
                        string clip = SDL.SDL_GetClipboardText();
                        if (!string.IsNullOrEmpty(clip))
                        {
                            foreach (char c in clip)
                            {
                                if (char.IsControl(c)) continue;
                                if (digitsOnly && !char.IsDigit(c)) continue;
                                if (maxLength > 0 && result.Length >= maxLength) break;
                                result += c;
                            }
                        }
                    }
                    catch { }
                }
                else if (keyState.IsKeyDown(Keys.C) && !_prevKeyState.IsKeyDown(Keys.C))
                {
                    try { SDL.SDL_SetClipboardText(result); } catch { }
                }
                else if (keyState.IsKeyDown(Keys.X) && !_prevKeyState.IsKeyDown(Keys.X))
                {
                    try { SDL.SDL_SetClipboardText(result); } catch { }
                    result = "";
                }
            }

            // Drain OS-resolved characters (locale-aware, includes Cyrillic)
            while (_pendingChars.Count > 0)
            {
                char c = _pendingChars.Dequeue();
                if (char.IsControl(c)) continue;
                if (digitsOnly && !char.IsDigit(c)) continue;
                if (maxLength > 0 && result.Length >= maxLength) continue;
                result += c;
            }

            _prevKeyState = keyState;
            return result;
        }

        /// <summary>
        /// Snapshots keyboard state and activates text input. Call when the field gains focus.
        /// </summary>
        public void Reset()
        {
            _prevKeyState = Keyboard.GetState();
            _backspaceTimer = 0;
            Activate();
        }
    }
}
