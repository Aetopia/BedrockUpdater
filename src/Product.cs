sealed class Product
{
    internal readonly string ProductId, PackageFamilyName;

    internal Product(string productId, string packageFamilyName) => (ProductId, PackageFamilyName) = (productId, packageFamilyName);

    internal static readonly Product Xbox = new("9WZDNCRD1HKW", "Microsoft.XboxIdentityProvider_8wekyb3d8bbwe");

    internal static readonly Product Release = new("9NBLGGH2JHXJ", "Microsoft.MinecraftUWP_8wekyb3d8bbwe");

    internal static readonly Product Preview = new("9P5X4QVLC2XR", "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe");
}