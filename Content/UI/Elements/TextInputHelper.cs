using Microsoft.Xna.Framework.Input;

namespace TerraStorage.Content.UI.Elements
{
    /// <summary>
    /// Platform-independent text input using direct keyboard state reading.
    /// Bypasses Main.GetInputText which doesn't work reliably on Linux/FNA.
    /// </summary>
    public class TextInputHelper
    {
        private KeyboardState _prevKeyState;
        private int _backspaceTimer;

        /// <summary>
        /// Reads newly pressed keys this frame and returns the updated text string.
        /// Must be called every game tick while the field is focused.
        /// </summary>
        /// <param name="currentText">The text before this frame's input.</param>
        /// <param name="maxLength">Maximum character count; 0 = unlimited.</param>
        /// <param name="digitsOnly">When true, non-digit characters are ignored.</param>
        public string ProcessInput(string currentText, int maxLength = 0, bool digitsOnly = false)
        {
            var keyState = Keyboard.GetState();
            bool shift = keyState.IsKeyDown(Keys.LeftShift) || keyState.IsKeyDown(Keys.RightShift);
            string result = currentText ?? "";

            // Handle backspace with repeat
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
                    // Start repeating after 20 frames, then every 3 frames
                    if (_backspaceTimer > 20 && _backspaceTimer % 3 == 0 && result.Length > 0)
                        result = result[..^1];
                }
            }
            else
            {
                _backspaceTimer = 0;
            }

            // Process character keys
            var pressedKeys = keyState.GetPressedKeys();
            foreach (var key in pressedKeys)
            {
                if (_prevKeyState.IsKeyDown(key))
                    continue; // Only process new key presses

                char? c = KeyToChar(key, shift);
                if (c == null)
                    continue;

                if (digitsOnly && !char.IsDigit(c.Value))
                    continue;

                if (maxLength > 0 && result.Length >= maxLength)
                    continue;

                result += c.Value;
            }

            _prevKeyState = keyState;
            return result;
        }

        /// <summary>
        /// Snapshots the current keyboard state so the next <see cref="ProcessInput"/>
        /// call won't treat already-held keys as newly pressed. Call this when the
        /// field first gains focus.
        /// </summary>
        public void Reset()
        {
            _prevKeyState = Keyboard.GetState();
            _backspaceTimer = 0;
        }

        private static char? KeyToChar(Keys key, bool shift)
        {
            // XNA Keys.A–Keys.Z are contiguous integers, so arithmetic offset maps to 'a'–'z'.
            if (key >= Keys.A && key <= Keys.Z)
            {
                char c = (char)('a' + (key - Keys.A));
                return shift ? char.ToUpper(c) : c;
            }

            // Number row
            if (key >= Keys.D0 && key <= Keys.D9)
            {
                if (!shift)
                    return (char)('0' + (key - Keys.D0));

                return key switch
                {
                    Keys.D1 => '!',
                    Keys.D2 => '@',
                    Keys.D3 => '#',
                    Keys.D4 => '$',
                    Keys.D5 => '%',
                    Keys.D6 => '^',
                    Keys.D7 => '&',
                    Keys.D8 => '*',
                    Keys.D9 => '(',
                    Keys.D0 => ')',
                    _ => null
                };
            }

            // Numpad
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
                return (char)('0' + (key - Keys.NumPad0));

            // Common symbols
            return key switch
            {
                Keys.Space => ' ',
                Keys.OemMinus => shift ? '_' : '-',
                Keys.OemPlus => shift ? '+' : '=',
                Keys.OemOpenBrackets => shift ? '{' : '[',
                Keys.OemCloseBrackets => shift ? '}' : ']',
                Keys.OemPipe => shift ? '|' : '\\',
                Keys.OemSemicolon => shift ? ':' : ';',
                Keys.OemQuotes => shift ? '"' : '\'',
                Keys.OemComma => shift ? '<' : ',',
                Keys.OemPeriod => shift ? '>' : '.',
                Keys.OemQuestion => shift ? '?' : '/',
                Keys.OemTilde => shift ? '~' : '`',
                _ => null
            };
        }
    }
}
