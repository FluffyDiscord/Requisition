using Requisition.Common;

namespace Requisition.Content.Items
{
    //Advanced Storage Disk — holds up to 128 item stacks.
    public class StorageDiskTier2 : StorageDiskBase
    {
        public override DiskTier Tier => DiskTier.Tier2;
    }
}
