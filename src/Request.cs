
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Store.Preview.InstallControl;

sealed class Request : IDisposable
{
    internal Request(AppInstallItem item, Task task)
    {
        (_item, _task) = (item, task);
        _handle = ((IAsyncResult)_task).AsyncWaitHandle;
    }

    readonly Task _task;

    readonly WaitHandle _handle;

    readonly AppInstallItem _item;

    internal TaskAwaiter GetAwaiter() => _task.GetAwaiter();

    internal void Cancel()
    {
        try
        {
            _item.Cancel();
            _handle.WaitOne();
        }
        catch { }
    }

    public void Dispose() => _task.Dispose();

    ~Request() { GC.SuppressFinalize(this); Dispose(); }
}
