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
        readonly Task<bool> _task;

        readonly AppInstallItem _item;

        readonly Action<AppInstallStatus> _action;

        readonly TaskCompletionSource<bool> _source;

        internal Request(AppInstallItem item, Action<AppInstallStatus> action)
        {
            _manager.MoveToFrontOfDownloadQueue(item.ProductId, string.Empty);

            _item = item; _action = action;
            _source = new(); _task = _source.Task;

            _item.Completed += OnCompleted; _item.StatusChanged += OnStatusChanged;
            _ = _task.ContinueWith(delegate{ _item.Cancel(); }, OnlyOnFaulted | ExecuteSynchronously);
        }

        internal bool Cancel()
        {
            if (_task.IsCompleted) return false;
            _item.Cancel(); return true;
        }

        internal TaskAwaiter<bool> GetAwaiter() => _task.GetAwaiter();

        void OnCompleted(AppInstallItem sender, object args)
        {
            switch (sender.GetCurrentStatus().InstallState)
            {
                case Completed:
                    _source.TrySetResult(true);
                    break;

                case Canceled:
                    if (!_task.IsFaulted) _source.TrySetResult(false);
                    break;
            }
        }

        void OnStatusChanged(AppInstallItem sender, object args)
        {
            var status = sender.GetCurrentStatus();
            switch (status.InstallState)
            {
                default:
                    _action(status);
                    break;

                case Completed:
                    _source.TrySetResult(true);
                    break;

                case Error:
                    _source.TrySetException(status.ErrorCode);
                    break;

                case Canceled:
                    if (!_task.IsFaulted) _source.TrySetResult(false);
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