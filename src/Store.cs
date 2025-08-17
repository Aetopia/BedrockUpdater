using System;
using static PInvoke;
using System.Threading.Tasks;
using System.Collections.Generic;
using static System.Threading.Tasks.TaskContinuationOptions;
using Windows.ApplicationModel.Store.Preview.InstallControl;
using static Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallState;

static class Store
{
    static readonly AppInstallManager _manager = new();

    static async Task<AppInstallItem?> FindItemAsync(string productId, IReadOnlyList<AppInstallItem> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            if (productId.Equals(item.ProductId, StringComparison.OrdinalIgnoreCase))
                return item;
        }
        return null;
    }

    static async Task GetEntitlementsAsync(string storeId)
    {
        var tasks = new Task[2];
        tasks[0] = _manager.GetFreeDeviceEntitlementAsync(storeId, string.Empty, string.Empty).AsTask();
        tasks[1] = _manager.GetFreeUserEntitlementAsync(storeId, string.Empty, string.Empty).AsTask();
        await Task.WhenAll(tasks);
    }

    static async Task<AppInstallItem?> GetItemAsync(Product product)
    {
        string productId = product.ProductId;
        await GetEntitlementsAsync(productId);

        var tasks = new Task<AppInstallItem?>[2];
        tasks[0] = FindItemAsync(productId, _manager.AppInstallItems);
        tasks[1] = FindItemAsync(productId, _manager.AppInstallItemsWithGroupSupport);
        await Task.WhenAll(tasks);

        var item = await tasks[0] ?? await tasks[1];
        if (item is not null) return item;

        var packageFamilyName = product.PackageFamilyName;
        GetPackagesByPackageFamily(packageFamilyName, out var count, new(), out _, new());

        if (count > 0) item = await _manager.UpdateAppByPackageFamilyNameAsync(packageFamilyName);
        else item = await _manager.StartAppInstallAsync(productId, string.Empty, false, false);

        return item;
    }

    internal static async Task<Request?> GetAsync(Product product, Action<double> action)
    {
        var item = await GetItemAsync(product);
        if (item is null) return null;

        _manager.MoveToFrontOfDownloadQueue(item.ProductId, string.Empty);

        TaskCompletionSource<bool> source = new();

        var task = source.Task;
        _ = task.ContinueWith(_ => item.Cancel(), OnlyOnFaulted | ExecuteSynchronously);

        item.Completed += (sender, args) =>
        {
            var status = sender.GetCurrentStatus();
            var state = status.InstallState;

            switch (state)
            {
                case Completed:
                    source.TrySetResult(new());
                    break;

                case Canceled:
                    if (!task.IsFaulted) source.TrySetCanceled();
                    break;
            }
        };

        item.StatusChanged += (sender, args) =>
        {
            var status = sender.GetCurrentStatus();
            var state = status.InstallState;

            switch (state)
            {
                default:
                    action(status.PercentComplete);
                    break;

                case Error:
                    source.TrySetException(status.ErrorCode);
                    break;

                case Canceled:
                case Completed:
                    break;

                case Paused:
                case ReadyToDownload:
                case PausedLowBattery:
                case PausedWiFiRequired:
                case PausedWiFiRecommended:
                    _manager.MoveToFrontOfDownloadQueue(sender.ProductId, string.Empty);
                    break;
            }
        };

        return new(item, task);
    }
}