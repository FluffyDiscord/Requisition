using TerraStorage.Common;

namespace TerraStorage.Content.Items
{
    /// <summary>Basic Storage Disk — holds up to 64 item stacks.</summary>
    public class StorageDiskTier1 : StorageDiskBase
    {
        public override DiskTier Tier => DiskTier.Tier1;
    }
}
