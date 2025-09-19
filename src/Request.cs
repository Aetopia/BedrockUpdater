using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Windows.ApplicationModel.Store.Preview.InstallControl;

partial class Store
{
    internal sealed partial class Request
    {
        readonly Task<bool> _task;

        readonly AppInstallItem _item;

        readonly Action<AppInstallStatus> _action;

        readonly TaskCompletionSource<bool> _source;
    }
}

partial class Store
{
    partial class Request
    {
        internal Request(AppInstallItem item, Action<AppInstallStatus> action)
        {
            _item = item;
            _action = action;

            _source = new();
            _task = _source.Task;

            _item.Completed += Completed;
            _item.StatusChanged += StatusChanged;
            _ = _task.ContinueWith(ContinuationAction, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}

partial class Store
{
    partial class Request
    {
        void ContinuationAction(Task task) => _item.Cancel();

        internal bool Cancel()
        {
            if (_task.IsCompleted) return false;
            _item.Cancel(); return true;
        }

        internal TaskAwaiter<bool> GetAwaiter() => _task.GetAwaiter();
    }
}

partial class Store
{
    partial class Request
    {
        void Completed(AppInstallItem sender, object args)
        {
            switch (sender.GetCurrentStatus().InstallState)
            {
                case AppInstallState.Completed:
                    _source.TrySetResult(true);
                    break;

                case AppInstallState.Canceled:
                    if (!_task.IsFaulted)
                        _source.TrySetResult(false);
                    break;
            }
        }

        void StatusChanged(AppInstallItem sender, object args)
        {
            var status = sender.GetCurrentStatus();
            switch (status.InstallState)
            {
                default:
                    _action(status);
                    break;

                case AppInstallState.Error:
                    _source.TrySetException(status.ErrorCode);
                    break;

                case AppInstallState.Canceled:
                case AppInstallState.Completed:
                    break;

                case AppInstallState.Paused:
                case AppInstallState.ReadyToDownload:
                case AppInstallState.PausedLowBattery:
                case AppInstallState.PausedWiFiRequired:
                case AppInstallState.PausedWiFiRecommended:
                    _manager.MoveToFrontOfDownloadQueue(sender.ProductId, string.Empty);
                    break;
            }
        }
    }
}