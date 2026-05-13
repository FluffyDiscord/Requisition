using Requisition.Common;

namespace Requisition.Content.Items
{
    //Superior Storage Disk — holds up to 256 item stacks.
    public class StorageDiskTier3 : StorageDiskBase
    {
        public override DiskTier Tier => DiskTier.Tier3;
    }
}
