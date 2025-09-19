using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using static System.Threading.Tasks.TaskContinuationOptions;
using Windows.ApplicationModel.Store.Preview.InstallControl;
using static Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallState;

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

            _item.Completed += OnCompleted;
            _item.StatusChanged += OnStatusChanged;
            _ = _task.ContinueWith(ContinuationAction, OnlyOnFaulted | ExecuteSynchronously);
        }
    }
}

partial class Store
{
    partial class Request
    {
        void ContinuationAction(Task task) => _item.Cancel();

        internal void Cancel() { if (!_task.IsCompleted) _item.Cancel(); }

        internal TaskAwaiter<bool> GetAwaiter() => _task.GetAwaiter();
    }
}

partial class Store
{
    partial class Request
    {
        void OnCompleted(AppInstallItem sender, object args)
        {
            switch (sender.GetCurrentStatus().InstallState)
            {
                case Completed:
                    _source.TrySetResult(true);
                    break;

                case Canceled:
                    if (!_task.IsFaulted)
                        _source.TrySetResult(false);
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

                case Error:
                    _source.TrySetException(status.ErrorCode);
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
        }
    }
}