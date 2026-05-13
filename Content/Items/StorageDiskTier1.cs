using TerraStorage.Common;

namespace TerraStorage.Content.Items
{
    //Basic Storage Disk — holds up to 64 item stacks.
    public class StorageDiskTier1 : StorageDiskBase
    {
        public override DiskTier Tier => DiskTier.Tier1;
    }
}
