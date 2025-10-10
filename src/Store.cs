using System;
using static PInvoke;
using System.Threading.Tasks;
using System.Collections.Generic;
using static System.StringComparison;
using Windows.ApplicationModel.Store.Preview.InstallControl;

static partial class Store
{
    static readonly AppInstallManager _manager = new();

    static async Task<AppInstallItem?> FindItemAsync(Product product, IEnumerable<AppInstallItem> items)
    {
        await Task.Yield();

        var productId = product.ProductId; foreach (var item in items)
        {
            await Task.Yield();
            if (productId.Equals(item.ProductId, OrdinalIgnoreCase)) return item;
        }

        return null;
    }

    static async Task<AppInstallItem?> FindItemAsync(Product product)
    {
        await Task.Yield();

        var tasks = new Task<AppInstallItem?>[2];

        tasks[0] = FindItemAsync(product, _manager.AppInstallItems);
        tasks[1] = FindItemAsync(product, _manager.AppInstallItemsWithGroupSupport);

        await Task.WhenAll(tasks);

        return await tasks[0] ?? await tasks[1];
    }

    static async Task GetEntitlementsAsync(Product product)
    {
        await Task.Yield();

        var tasks = new Task[2];
        var productId = product.ProductId;

        tasks[0] = _manager.GetFreeUserEntitlementAsync(productId, string.Empty, string.Empty).AsTask();
        tasks[1] = _manager.GetFreeDeviceEntitlementAsync(productId, string.Empty, string.Empty).AsTask();

        await Task.WhenAll(tasks);
    }

    static async Task<AppInstallItem?> GetItemAsync(Product product)
    {
        await Task.Yield();

        var productId = product.ProductId;
        var packageFamilyName = product.PackageFamilyName;
        GetPackagesByPackageFamily(packageFamilyName, out var count, new(), out _, new());

        if (count > 0) return await _manager.UpdateAppByPackageFamilyNameAsync(packageFamilyName);
        return await _manager.StartAppInstallAsync(productId, string.Empty, false, false);
    }

    internal static async Task<Request?> GetAsync(Product product, Action<AppInstallStatus> action)
    {
        await Task.Yield(); await GetEntitlementsAsync(product);
        var item = await FindItemAsync(product) ?? await GetItemAsync(product);
        return item is null ? null : new(item, action);
    }
}