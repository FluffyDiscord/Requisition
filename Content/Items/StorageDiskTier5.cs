using TerraStorage.Common;

namespace TerraStorage.Content.Items
{
    /// <summary>Ultimate Storage Disk — holds up to 1024 item stacks.</summary>
    public class StorageDiskTier5 : StorageDiskBase
    {
        public override DiskTier Tier => DiskTier.Tier5;
    }
}
