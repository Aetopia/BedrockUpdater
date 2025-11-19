using System;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Windows.ApplicationModel.Store.Preview.InstallControl;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

static class Store
{
    [SuppressUnmanagedCodeSecurity]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("Kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int GetPackagesByPackageFamily(string packageFamilyName, out uint count, nint packageFullNames, out uint bufferLength, nint buffer);

    static readonly AppInstallManager s_manager = new();

    internal static async Task<Request?> GetAsync(Product product, Action<AppInstallStatus> action)
    {
        var item = s_manager.AppInstallItems.FirstOrDefault(_ => _.ProductId.Equals(product.ProductId, StringComparison.OrdinalIgnoreCase));

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

            item.Completed += Completed; item.StatusChanged += StatusChanged;
            s_manager.MoveToFrontOfDownloadQueue(item.ProductId, string.Empty);

            _source.Task.ContinueWith((_) => item.Cancel(), TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        internal TaskAwaiter<bool> GetAwaiter() => _source.Task.GetAwaiter();

        internal bool Cancel()
        {
            if (_source.Task.IsCompleted) return false;
            _item.Cancel(); return true;
        }

        void Completed(AppInstallItem sender, object args)
        {
            switch (sender.GetCurrentStatus().InstallState)
            {
                case AppInstallState.Completed:
                    _source.TrySetResult(true);
                    break;

                case AppInstallState.Canceled:
                    if (!_source.Task.IsFaulted) _source.TrySetResult(false);
                    break;
            }
        }

        void StatusChanged(AppInstallItem sender, object args)
        {
            var status = sender.GetCurrentStatus(); switch (status.InstallState)
            {
                case AppInstallState.Error:
                    _source.TrySetException(status.ErrorCode);
                    break;

                case AppInstallState.Pending:
                case AppInstallState.Installing:
                case AppInstallState.Downloading:
                    _action(status);
                    break;

                case AppInstallState.Paused:
                case AppInstallState.ReadyToDownload:
                case AppInstallState.PausedLowBattery:
                case AppInstallState.PausedWiFiRequired:
                case AppInstallState.PausedWiFiRecommended:
                    s_manager.MoveToFrontOfDownloadQueue(sender.ProductId, string.Empty);
                    break;
            }
        }
    }
}