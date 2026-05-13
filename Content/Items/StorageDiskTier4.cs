using Requisition.Common;

namespace Requisition.Content.Items
{
    //Elite Storage Disk — holds up to 512 item stacks.
    public class StorageDiskTier4 : StorageDiskBase
    {
        public override DiskTier Tier => DiskTier.Tier4;
    }
}
