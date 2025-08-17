using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.Store.Preview.InstallControl;
using static PInvoke;

static class Store
{
    static readonly AppInstallManager _manager = new();

    static async Task<AppInstallItem?> FindItemAsync(Product product, IReadOnlyList<AppInstallItem> items)
    {
        var productId = product.ProductId;

        foreach (var item in items)
        {
            await Task.Yield();
            if (productId.Equals(item.ProductId, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        return null;
    }

    static async Task<AppInstallItem?> GetAppAsync(Product product) => await FindItemAsync(product, _manager.AppInstallItems);

    static async Task<AppInstallItem?> GetAppBundleAsync(Product product) => await FindItemAsync(product, _manager.AppInstallItemsWithGroupSupport);

    static async Task GetEntitlementAsync(Product product)
    {
        var storeId = product.ProductId; var tasks = new Task[2];
        tasks[0] = _manager.GetFreeDeviceEntitlementAsync(storeId, string.Empty, string.Empty).AsTask();
        tasks[1] = _manager.GetFreeUserEntitlementAsync(storeId, string.Empty, string.Empty).AsTask();
        await Task.WhenAll(tasks);
    }

    static async Task<AppInstallItem?> GetItemAsync(Product product)
    {
        await GetEntitlementAsync(product);

        Task<AppInstallItem?>[] tasks = [GetAppAsync(product), GetAppBundleAsync(product)];
        await Task.WhenAll(tasks);

        var item = await tasks[0] ?? await tasks[1];
        if (item is not null) return item;

        string productId = product.ProductId;
        GetPackagesByPackageFamily(product.PackageFamilyName, out var count, new(), out _, new());

        if (count > 0) item = await _manager.SearchForUpdatesAsync(productId, string.Empty);
        else
        {
            var items = await _manager.StartProductInstallAsync(productId, string.Empty, string.Empty, string.Empty, null);
            item = await FindItemAsync(product, items);
        }
        return item;
    }

    internal static async Task<Request?> GetAsync(Product product, Action<double> action)
    {
        var item = await GetItemAsync(product);
        if (item is null) return null;

        _manager.MoveToFrontOfDownloadQueue(item.ProductId, string.Empty);

        TaskCompletionSource<bool> source = new();
        var task = source.Task; var complete = 0D;

        item.Completed += (sender, args) =>
        {
            var status = sender.GetCurrentStatus();
            var state = status.InstallState;

            switch (state)
            {
                case AppInstallState.Completed:
                    source.TrySetResult(new());
                    break;

                case AppInstallState.Canceled:
                    if (!task.IsFaulted)
                        source.TrySetException(status.ErrorCode);
                    break;
            }
        };

        item.StatusChanged += (sender, args) =>
        {
            var status = sender.GetCurrentStatus();
            var state = status.InstallState;

            switch (state)
            {
                case AppInstallState.Error:
                    sender.Cancel();
                    source.TrySetException(status.ErrorCode);
                    break;

                case AppInstallState.Paused:
                case AppInstallState.ReadyToDownload:
                case AppInstallState.PausedLowBattery:
                case AppInstallState.PausedWiFiRequired:
                case AppInstallState.PausedWiFiRecommended:
                    _manager.MoveToFrontOfDownloadQueue(sender.ProductId, string.Empty);
                    break;

                default:
                    var value = status.PercentComplete;
                    if (complete != value) action(complete = value);
                    break;
            }
        };

        return new(item, task);
    }
}