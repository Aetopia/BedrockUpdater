using System;
using System.Security;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Windows.ApplicationModel.Store.Preview.InstallControl;
using static System.Threading.Tasks.TaskContinuationOptions;
using static Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallState;

static class Store
{
    [SuppressUnmanagedCodeSecurity]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("Kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int GetPackagesByPackageFamily(string packageFamilyName, out uint count, nint packageFullNames, out uint bufferLength, nint buffer);

    static Store() => Task.Run(() => { foreach (var item in s_manager.AppInstallItems) if (item.GetCurrentStatus().InstallState is Error) item.Cancel(); });

    static readonly AppInstallManager s_manager = new();

    internal static async Task<Request?> GetAsync(Product product, Action<AppInstallStatus> action)
    {
        GetPackagesByPackageFamily(product.PackageFamilyName, out var count, 0, out _, 0);

        if (count > 0)
        {
            var item = await s_manager.UpdateAppByPackageFamilyNameAsync(product.PackageFamilyName);
            return item is { } ? new(item, action) : null;
        }

        return new(await s_manager.StartAppInstallAsync(product.ProductId, string.Empty, false, false), action);

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

        readonly Action<AppInstallStatus> _action;

        readonly TaskCompletionSource<bool> _source;

        internal Request(AppInstallItem item, Action<AppInstallStatus> action)
        {
            (_item, _action, _source) = (item, action, new());
            _source.Task.ContinueWith((_) => item.Cancel(), OnlyOnFaulted | ExecuteSynchronously);

            item.Completed += (sender, args) =>
            {
                switch (sender.GetCurrentStatus().InstallState)
                {
                    case Completed: _source.TrySetResult(true); break;
                    case Canceled: if (!_source.Task.IsFaulted) _source.TrySetResult(false); break;
                }
            };

            item.StatusChanged += (sender, args) =>
            {
                var status = sender.GetCurrentStatus(); switch (status.InstallState)
                {
                    case Error: _source.TrySetException(status.ErrorCode); break;
                    case Pending or Downloading or Installing: _action(status); break;
                    case Paused or ReadyToDownload: s_manager.MoveToFrontOfDownloadQueue(sender.ProductId, string.Empty); break;
                }
            };

            s_manager.MoveToFrontOfDownloadQueue(item.ProductId, string.Empty);
        }

        internal TaskAwaiter<bool> GetAwaiter() => _source.Task.GetAwaiter();

        internal bool Cancel() { _item.Cancel(); return !_source.Task.IsCompleted; }
    }
}