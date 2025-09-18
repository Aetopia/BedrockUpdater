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
        bool _disposed;

        readonly Task _task;

        readonly WaitHandle _handle;

        readonly AppInstallItem _item;

        readonly Action<AppInstallStatus> _action;

        readonly TaskCompletionSource<bool> _source;
    }
}

partial class Store
{
    partial class Request : IDisposable
    {
        internal Request(AppInstallItem item, Action<AppInstallStatus> action)
        {
            _item = item;
            _source = new();
            _action = action;
            _task = _source.Task;

            var result = (IAsyncResult)_task;
            _handle = result.AsyncWaitHandle;

            _item.Completed += OnCompleted;
            _item.StatusChanged += OnStatusChanged;
            _ = _task.ContinueWith(OnFaulted, OnlyOnFaulted | ExecuteSynchronously);
        }

        public void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);

            _handle.Dispose();
            _item.Completed -= OnCompleted;
            _item.StatusChanged -= OnStatusChanged;
        }

        void OnFaulted(Task args) => _item.Cancel();

        ~Request() => Dispose();
    }
}

partial class Store
{
    partial class Request
    {
        internal void Cancel()
        {
            if (_disposed)
                throw new ObjectDisposedException(null);

            if (_task.IsCompleted)
                return;

            _item.Cancel();
            _handle.WaitOne();
        }

        internal TaskAwaiter GetAwaiter()
        {
            if (_disposed)
                throw new ObjectDisposedException(null);

            return _task.GetAwaiter();
        }
    }
}

partial class Store
{
    partial class Request
    {
        void OnCompleted(AppInstallItem sender, object args)
        {
            var status = sender.GetCurrentStatus();
            var state = status.InstallState;

            switch (state)
            {
                case Completed:
                    _source.TrySetResult(new());
                    break;

                case Canceled:
                    if (!_task.IsFaulted)
                        _source.TrySetCanceled();
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