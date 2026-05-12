using Terraria;

namespace TerraStorage.Content.UI
{
    // Per-frame singleton flag that prevents click events from propagating through
    // overlapping UI panels. The first panel whose bounds contain the mouse consumes
    // the click; all panels that update afterwards see <see cref="IsConsumed"/> = true
    // and skip their click handling.
    //
    // Uses Main.uCount instead of Main.GameUpdateCount because GameUpdateCount does
    // not increment during autopause (its increment is past the autopause early return),
    // which would permanently latch IsConsumed = true for all autopause frames.
    // Main.uCount increments just before UpdateUIStates, so it advances every frame.
    internal static class UIClickBlocker
    {
        private static int _consumedCount = -1;

        //True if a click has already been consumed this frame.
        public static bool IsConsumed => _consumedCount == Main.uCount;

        //Mark the current frame's click as consumed.
        public static void Consume() => _consumedCount = Main.uCount;
    }
}
