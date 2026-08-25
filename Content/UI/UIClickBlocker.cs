namespace TerraStorage.Content.UI
{
    // Per-frame flag that keeps a single click from being acted on twice: the first handler to
    // consume it wins, and everything that runs afterwards sees IsConsumed and stands down. This
    // arbitrates between two elements of one panel, and -- because RequisitionUISystem updates
    // windows from the top of the z-order down -- between overlapping windows as well.
    //
    // The frame counter is our own rather than Main's. Main.uCount is reset to zero every second by
    // the FPS counter, so a stamp compared against it comes back "consumed" on whichever frame the
    // counter next wraps onto the stored value -- one dead frame per second, every second, with the
    // click landing on it silently dropped. Main.GameUpdateCount does not have that problem but
    // stops advancing under autopause. RequisitionUISystem advances this one once per frame, before
    // any window updates and so before anything can consume.
    internal static class UIClickBlocker
    {
        private static long _frame;
        private static long _consumedFrame = -1;

        public static long Frame => _frame;

        // The button state as the player actually left it, captured before any suppression.
        //
        // Suppressing a window means zeroing Main.mouse* around its update, which hides the click
        // from it -- but a window that remembers the button between frames must not remember the
        // zero. If it latches `_prev = Main.mouseLeft` while suppressed it records "released" for a
        // button still being held, and the first unsuppressed frame then looks like a fresh press: a
        // click the player never made. Latch from these instead; keep *acting* on Main.mouse*, which
        // is what suppression is supposed to hide.
        //
        // Valid only inside the UI update phase (ModSystem.UpdateUI). These are captured before
        // PlayerInput.UpdateInput() refreshes the real buttons later in the same frame, so code in
        // the player/world update -- tile interaction, item use -- must read Main.mouse* directly.
        // See TerminalUIState.SetTerminal.
        public static bool RealMouseLeft { get; private set; }
        public static bool RealMouseRight { get; private set; }
        public static bool RealMouseMiddle { get; private set; }

        public static void BeginFrame(bool mouseLeft, bool mouseRight, bool mouseMiddle)
        {
            _frame++;
            RealMouseLeft = mouseLeft;
            RealMouseRight = mouseRight;
            RealMouseMiddle = mouseMiddle;
        }

        // The _frame > 0 term means a Consume() that somehow lands before the first BeginFrame
        // cannot latch IsConsumed true forever and kill every click in the mod.
        public static bool IsConsumed => _frame > 0 && _consumedFrame == _frame;

        //Mark the current frame's click as consumed.
        public static void Consume() => _consumedFrame = _frame;

        // Claims this frame's click for the hovered window, whichever button it was.
        //
        // A window must claim a right- or middle-click just as it claims a left one. Claiming only
        // the left button is what let a right-click in an overlapping region be handled by every
        // window under the cursor at once -- in the Terminal that withdraws a stack from storage
        // while the window beneath it acts on the same press.
        public static void ClaimIfPressed(bool hovered, bool left, bool right, bool middle)
        {
            if (hovered && !IsConsumed && (left || right || middle))
                Consume();
        }

        private static long _gestureFrame = -1;

        // True while any window is mid-gesture -- dragging, resizing, panning -- so the arbiter can
        // leave the z-order alone until the player lets go. Marked by the gesture itself rather than
        // owned by any one element, because the windows run several hand-rolled gestures that share
        // no base class (TSWindowElement drag/resize, the Crafting Tree's pan and minimap drag, the
        // Favorites button's middle-drag).
        //
        // Read before the windows update, so it reflects the previous frame: the press that starts a
        // gesture still raises its window, while a gesture already under way does not.
        public static bool GestureActive => _gestureFrame >= 0 && _frame - _gestureFrame <= 1;

        public static void MarkGesture() => _gestureFrame = _frame;

        //Resets the clock so unit tests start from a known frame.
        public static void ResetForTests()
        {
            _frame = 0;
            _consumedFrame = -1;
            _gestureFrame = -1;
            RealMouseLeft = false;
            RealMouseRight = false;
            RealMouseMiddle = false;
        }
    }
}
