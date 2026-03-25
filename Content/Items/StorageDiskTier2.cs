using TerraStorage.Common;

namespace TerraStorage.Content.Items
{
    /// <summary>Advanced Storage Disk — holds up to 128 item stacks.</summary>
    public class StorageDiskTier2 : StorageDiskBase
    {
        public override DiskTier Tier => DiskTier.Tier2;
    }
}
