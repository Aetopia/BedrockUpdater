using System;
using static PInvoke;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Collections.Generic;
using static System.StringComparison;
using Windows.ApplicationModel.Store.Preview.InstallControl;

static partial class Store
{
    static readonly AppInstallManager _manager = new();

    static async Task<AppInstallItem?> FindItemAsync(Product product, IEnumerable<AppInstallItem> items)
    {
        await Dispatcher.Yield();

        var productId = product.ProductId; foreach (var item in items)
        {
            if (productId.Equals(item.ProductId, OrdinalIgnoreCase)) return item;
            await Dispatcher.Yield();
        }

        return null;
    }

    static async Task<AppInstallItem?> FindItemAsync(Product product)
    {
        var tasks = new Task<AppInstallItem?>[2];

        tasks[0] = FindItemAsync(product, _manager.AppInstallItems);
        tasks[1] = FindItemAsync(product, _manager.AppInstallItemsWithGroupSupport);

        await Task.WhenAll(tasks);

        return await tasks[0] ?? await tasks[1];
    }

    static async Task GetEntitlementsAsync(Product product)
    {
        var tasks = new Task[2];
        var productId = product.ProductId;

        tasks[0] = _manager.GetFreeUserEntitlementAsync(productId, string.Empty, string.Empty).AsTask();
        tasks[1] = _manager.GetFreeDeviceEntitlementAsync(productId, string.Empty, string.Empty).AsTask();

        await Task.WhenAll(tasks);
    }

    static async Task<AppInstallItem?> GetItemAsync(Product product)
    {
        var productId = product.ProductId;
        var packageFamilyName = product.PackageFamilyName;

        GetPackagesByPackageFamily(packageFamilyName, out var count, new(), out _, new());
        if (count > 0) return await _manager.UpdateAppByPackageFamilyNameAsync(packageFamilyName);

        return await _manager.StartAppInstallAsync(productId, string.Empty, false, false);
    }

    internal static async Task<Request?> GetAsync(Product product, Action<AppInstallStatus> action)
    {
        await GetEntitlementsAsync(product);

        var item = await FindItemAsync(product);
        item ??= await GetItemAsync(product);

        if (item is null) return null;
        return new(item, action);
    }
}