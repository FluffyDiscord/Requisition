using Requisition.Common;

namespace Requisition.Content.Items
{
    //Ultimate Storage Disk — holds up to 1024 item stacks.
    public class StorageDiskTier5 : StorageDiskBase
    {
        public override DiskTier Tier => DiskTier.Tier5;
    }
}
