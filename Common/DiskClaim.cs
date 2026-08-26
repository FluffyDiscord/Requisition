namespace TerraStorage.Common
{
    // Whether the client that sent a packet may name a particular disk GUID. Disk GUIDs travel to
    // every client, so naming one proves nothing on its own - the question is whether the disk the
    // GUID belongs to is one the sender actually has. The three answers the game has to look up are
    // taken as arguments so the rule itself can be exercised without Terraria.
    public static class DiskClaim
    {
        // An empty GUID belongs to nobody yet: the disk is uninitialised and the server is about to
        // mint one. It has to be its own arm, because the "is this GUID in use" scan deliberately
        // answers true for empty so that disk recovery refuses it.
        public static bool SenderMayClaim(bool diskIdIsEmpty, bool diskGuidInUse, bool senderHoldsDisk)
        {
            if (diskIdIsEmpty)
                return true;

            if (!diskGuidInUse)
                return true;

            return senderHoldsDisk;
        }

        // Whether archived items may be restored onto this GUID. Restoring is how an unarchived disk
        // gets its contents back, and it replaces whatever the GUID held - so it may only ever create
        // a disk, never overwrite one that already exists.
        public static bool MayRestoreArchivedItems(bool worldAlreadyHasDisk)
        {
            return !worldAlreadyHasDisk;
        }
    }
}
