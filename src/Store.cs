using System;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Windows.ApplicationModel.Store.Preview.InstallControl;
using static System.Threading.Tasks.TaskContinuationOptions;
using static Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallState;
using Windows.Management.Deployment;

static class Store
{
    static readonly PackageManager s_package = new();

    static readonly AppInstallManager s_app = new();

    internal static async Task<Request?> GetAsync(Product product, Action<AppInstallStatus> action)
    {
        var item = await Task.Run(() =>
        {
            AppInstallItem? _ = null; foreach (var item in s_app.AppInstallItems)
            {
                if (item.GetCurrentStatus().InstallState is Error) item.Cancel();
                else if (item.ProductId.Equals(product.ProductId, StringComparison.OrdinalIgnoreCase)) _ = item;
            }
            return _;
        });

        if (item is null)
        {
            if (s_package.FindPackagesForUser(string.Empty, product.PackageFamilyName).Any())
                item = await s_app.UpdateAppByPackageFamilyNameAsync(product.PackageFamilyName);
            else
                item = await s_app.StartAppInstallAsync(product.ProductId, string.Empty, false, false);
        }

        return item is { } ? new(item, action) : null;

    }

    internal sealed class Product
    {
        internal readonly string ProductId, PackageFamilyName;
        Product(string productId, string packageFamilyName) => (ProductId, PackageFamilyName) = (productId, packageFamilyName);

        internal static readonly Product MinecraftUWP = new("9NBLGGH2JHXJ", "Microsoft.MinecraftUWP_8wekyb3d8bbwe");
        internal static readonly Product GamingServices = new("9MWPM2CQNLHN", "Microsoft.GamingServices_8wekyb3d8bbwe");
        internal static readonly Product MinecraftWindowsBeta = new("9P5X4QVLC2XR", "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe");
    }

    internal sealed class Request
    {
        readonly AppInstallItem _item;
        readonly TaskCompletionSource<bool> _source = new();

        internal Request(AppInstallItem item, Action<AppInstallStatus> action)
        {
            s_app.MoveToFrontOfDownloadQueue(item.ProductId, string.Empty);
            _source.Task.ContinueWith(_ => item.Cancel(), OnlyOnFaulted | ExecuteSynchronously);

            item.Completed += (sender, args) =>
            {
                switch (sender.GetCurrentStatus().InstallState)
                {
                    case Completed: _source.TrySetResult(true); break;
                    case Canceled: if (!_source.Task.IsFaulted) _source.TrySetResult(false); break;
                }
            };

            (_item = item).StatusChanged += (sender, args) =>
            {
                var status = sender.GetCurrentStatus(); switch (status.InstallState)
                {
                    case Paused: _source.TrySetResult(false); break;
                    case Error: _source.TrySetException(status.ErrorCode); break;
                    case Pending or Downloading or Installing: action(status); break;
                }
            };
        }

        internal TaskAwaiter<bool> GetAwaiter() => _source.Task.GetAwaiter();

        internal bool Cancel()
        {
            if (_source.Task.IsCompleted)
                return false;

            if (s_package.FindPackagesForUser(string.Empty, _item.PackageFamilyName).Any())
                _item.Pause();
            else
                _item.Cancel();

            return true;
        }
    }
}