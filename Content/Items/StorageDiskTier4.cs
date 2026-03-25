using TerraStorage.Common;

namespace TerraStorage.Content.Items
{
    /// <summary>Elite Storage Disk — holds up to 512 item stacks.</summary>
    public class StorageDiskTier4 : StorageDiskBase
    {
        public override DiskTier Tier => DiskTier.Tier4;
    }
}
