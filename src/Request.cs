using System;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Windows.ApplicationModel.Store.Preview.InstallControl;
using static System.Threading.Tasks.TaskContinuationOptions;
using static Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallState;

partial class Store
{
    internal sealed class Request
    {
        readonly AppInstallItem _item;

        readonly Action<AppInstallStatus> _action;

        readonly TaskCompletionSource<bool> _source;

        internal Request(AppInstallItem item, Action<AppInstallStatus> action)
        {
            _item = item;
            _source = new();
            _action = action;

            _item.Completed += OnCompleted;
            _item.StatusChanged += OnStatusChanged;
            _source.Task.ContinueWith((_) => _item.Cancel(), OnlyOnFaulted | ExecuteSynchronously);

            _manager.MoveToFrontOfDownloadQueue(item.ProductId, string.Empty);
        }

        internal bool Cancel()
        {
            if (_source.Task.IsCompleted) return false;
            _item.Cancel(); return true;
        }

        internal TaskAwaiter<bool> GetAwaiter() => _source.Task.GetAwaiter();

        void OnCompleted(AppInstallItem sender, object args)
        {
            switch (sender.GetCurrentStatus().InstallState)
            {
                case Completed:
                    _source.TrySetResult(true);
                    break;

                case Canceled:
                    if (!_source.Task.IsFaulted) _source.TrySetResult(false);
                    break;
            }
        }

        void OnStatusChanged(AppInstallItem sender, object args)
        {
            var status = sender.GetCurrentStatus(); switch (status.InstallState)
            {
                case Error:
                    _source.TrySetException(status.ErrorCode);
                    break;

                case Pending:
                case Installing:
                case Downloading:
                    _action(status);
                    break;

                case Paused:
                case ReadyToDownload:
                case PausedLowBattery:
                case PausedWiFiRequired:
                case PausedWiFiRecommended:
                    _manager.MoveToFrontOfDownloadQueue(sender.ProductId, string.Empty);
                    break;
            }
        }
    }
}