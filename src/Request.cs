using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Windows.ApplicationModel.Store.Preview.InstallControl;

sealed class Request : IDisposable
{
    bool _disposed;

    readonly Task _task;

    readonly WaitHandle _handle;

    readonly AppInstallItem _item;

    internal Request(AppInstallItem item, Task task)
    {
        _item = item;
        _task = task;

        var result = (IAsyncResult)_task;
        _handle = result.AsyncWaitHandle;
    }

    internal TaskAwaiter GetAwaiter()
    {
        if (_disposed)
            throw new ObjectDisposedException(null);

        return _task.GetAwaiter();
    }

    internal void Cancel()
    {
        if (_disposed)
            throw new ObjectDisposedException(null);

        if (_task.IsCompleted)
            return;

        _item.Cancel();
        _handle.WaitOne();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _disposed = true;
        _handle.Dispose();
    }

    ~Request() => Dispose();
}