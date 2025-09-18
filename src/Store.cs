using System;
using static PInvoke;
using System.Threading.Tasks;
using System.Collections.Generic;
using static System.StringComparison;
using Windows.ApplicationModel.Store.Preview.InstallControl;

static partial class Store
{
    static readonly AppInstallManager _manager = new();

    static async Task<AppInstallItem?> FindItemForProductIdAsync(string productId, IReadOnlyList<AppInstallItem> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            if (productId.Equals(item.ProductId, OrdinalIgnoreCase))
                return item;
        }
        return null;
    }

    static async Task<AppInstallItem?> FindItemForProductIdAsync(string productId)
    {
        var tasks = new Task<AppInstallItem?>[2];
        tasks[0] = FindItemForProductIdAsync(productId, _manager.AppInstallItems);
        tasks[1] = FindItemForProductIdAsync(productId, _manager.AppInstallItemsWithGroupSupport);

        await Task.WhenAll(tasks);
        return await tasks[0] ?? await tasks[1];
    }

    static async Task GetEntitlementsForProductIdAsync(string productId)
    {
        var tasks = new Task[2];
        tasks[0] = _manager.GetFreeDeviceEntitlementAsync(productId, string.Empty, string.Empty).AsTask();
        tasks[1] = _manager.GetFreeUserEntitlementAsync(productId, string.Empty, string.Empty).AsTask();
        await Task.WhenAll(tasks);
    }

    internal static async Task<Request?> GetAsync(Product product, Action<AppInstallStatus> action)
    {
        var productId = product.ProductId;
        var packageFamilyName = product.PackageFamilyName;

        await GetEntitlementsForProductIdAsync(productId);
        var item = await FindItemForProductIdAsync(productId);
        if (item is not null) return null;

        GetPackagesByPackageFamily(packageFamilyName, out var count, new(), out _, new());
        if (count > 0) item = await _manager.UpdateAppByPackageFamilyNameAsync(packageFamilyName);
        else item = await _manager.StartAppInstallAsync(productId, string.Empty, false, false);

        return item is null ? null : new(item, action);
    }
}