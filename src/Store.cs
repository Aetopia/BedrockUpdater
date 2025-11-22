using System;
using System.Security;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Windows.ApplicationModel.Store.Preview.InstallControl;
using static Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallState;

static class Store
{
    [SuppressUnmanagedCodeSecurity]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("Kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int GetPackagesByPackageFamily(string packageFamilyName, out uint count, nint packageFullNames, out uint bufferLength, nint buffer);

    static readonly AppInstallManager s_manager = new();

    internal static async Task<Request?> GetAsync(Product product, Action<AppInstallStatus> action)
    {
        var item = await Task.Run(() =>
        {
            AppInstallItem? _ = null; foreach (var item in s_manager.AppInstallItems)
            {
                if (item.GetCurrentStatus().InstallState is Error) item.Cancel();
                else if (item.ProductId.Equals(product.ProductId, StringComparison.OrdinalIgnoreCase)) _ = item;
            }
            return _;
        });

        if (item is null)
        {
            GetPackagesByPackageFamily(product.PackageFamilyName, out var count, 0, out _, 0);
            if (count > 0) item = await s_manager.UpdateAppByPackageFamilyNameAsync(product.PackageFamilyName);
            else item = await s_manager.StartAppInstallAsync(product.ProductId, string.Empty, false, false);
        }

        return item is { } ? new(item, action) : null;

    }

    internal sealed class Product
    {
        internal readonly string ProductId, PackageFamilyName;
        internal static readonly Product MinecraftUWP = new("9NBLGGH2JHXJ", "Microsoft.MinecraftUWP_8wekyb3d8bbwe");
        internal static readonly Product GamingServices = new("9MWPM2CQNLHN", "Microsoft.GamingServices_8wekyb3d8bbwe");
        internal static readonly Product MinecraftWindowsBeta = new("9P5X4QVLC2XR", "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe");

        Product(string productId, string packageFamilyName) => (ProductId, PackageFamilyName) = (productId, packageFamilyName);
    }

    internal sealed class Request
    {
        readonly AppInstallItem _item;
        readonly TaskCompletionSource<bool> _source = new();

        internal Request(AppInstallItem item, Action<AppInstallStatus> action)
        {
            s_manager.MoveToFrontOfDownloadQueue(item.ProductId, string.Empty);

            item.Completed += (sender, args) =>
            {
                switch (sender.GetCurrentStatus().InstallState)
                {
                    case Completed: _source.TrySetResult(true); break;
                    case Canceled: _source.TrySetResult(false); break;
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

        internal bool Pause()
        {
            if (!_source.Task.IsCompleted) _item.Pause();
            return !_source.Task.IsCompleted;
        }
    }
}