namespace TerraStorage.Content.UI
{
    // Decides whether the item on the cursor is deposited into storage on this click.
    //
    // Deliberately free of Terraria types so Tests.csproj can compile it directly; TerminalUIState
    // reads the live game state and passes it in.
    public static class DepositGate
    {
        // occupiedSlotHovered excludes clicks on a filled grid slot: those deposit through
        // OnItemClicked instead, and counting them here would deposit the stack twice.
        public static bool ShouldDeposit(
            bool pressEdge,
            bool sawPressSinceOpen,
            bool storageTabActive,
            bool cursorHasItem,
            bool itemAnimationActive,
            bool insideGridRect,
            bool occupiedSlotHovered)
            => pressEdge
               && sawPressSinceOpen
               && storageTabActive
               && cursorHasItem
               && !itemAnimationActive
               && insideGridRect
               && !occupiedSlotHovered;
    }
}
