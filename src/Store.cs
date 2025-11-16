using System;
using static PInvoke;
using static System.String;
using System.Threading.Tasks;
using static System.StringComparison;
using Windows.ApplicationModel.Store.Preview.InstallControl;

static partial class Store
{
    static readonly AppInstallManager _manager = new();

    static async Task<AppInstallItem?> FindItemAsync(Product product) => await Task.Run(() =>
    {
        foreach (var item in _manager.AppInstallItems)
            if (product.ProductId.Equals(item.ProductId, OrdinalIgnoreCase))
                return item;
        return null;
    });

    static async Task GetEntitlementsAsync(Product product)
    {
        var tasks = new Task[2];
        tasks[0] = _manager.GetFreeUserEntitlementAsync(product.ProductId, Empty, Empty).AsTask();
        tasks[1] = _manager.GetFreeDeviceEntitlementAsync(product.ProductId, Empty, Empty).AsTask();
        await Task.WhenAll(tasks);
    }

    static async Task<AppInstallItem?> GetItemAsync(Product product)
    {
        GetPackagesByPackageFamily(product.PackageFamilyName, out var count, 0, out _, 0);
        if (count > 0) return await _manager.UpdateAppByPackageFamilyNameAsync(product.PackageFamilyName);
        return await _manager.StartAppInstallAsync(product.ProductId, Empty, false, false);
    }

    internal static async Task<Request?> GetAsync(Product product, Action<AppInstallStatus> action)
    {
        await GetEntitlementsAsync(product);

        var item = await FindItemAsync(product);
        item ??= await GetItemAsync(product);

        return item is {} ? new(item, action) : null;
    }
}