using Requisition.Common;

namespace Requisition.Content.Items
{
    //Terra Storage Disk — holds up to 2048 item stacks.
    public class StorageDiskTier6 : StorageDiskBase
    {
        public override DiskTier Tier => DiskTier.Tier6;
    }
}
