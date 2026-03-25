using Terraria;

namespace TerraStorage.Content.UI
{
    /// <summary>
    /// Per-frame singleton flag that prevents click events from propagating through
    /// overlapping UI panels. The first panel whose bounds contain the mouse consumes
    /// the click; all panels that update afterwards see <see cref="IsConsumed"/> = true
    /// and skip their click handling.
    /// </summary>
    internal static class UIClickBlocker
    {
        private static ulong _consumedFrame = ulong.MaxValue;

        /// <summary>True if a click has already been consumed this frame.</summary>
        public static bool IsConsumed => _consumedFrame == Main.GameUpdateCount;

        /// <summary>Mark the current frame's click as consumed.</summary>
        public static void Consume() => _consumedFrame = Main.GameUpdateCount;
    }
}
