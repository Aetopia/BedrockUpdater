using System;
using System.Linq;
using System.Windows;
using System.Threading;

static class Program
{
    static Program() => AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
    {
        var exception = (Exception)args.ExceptionObject;
        while (exception.InnerException is not null) exception = exception.InnerException;

        MessageBox.Show(exception.Message, "Bedrock Updater: Error", MessageBoxButton.OK, MessageBoxImage.Error);
        Environment.Exit(1);
    };

    [STAThread]
    static void Main(string[] args)
    {
        using Mutex mutex = new(false, "C7B58EAD-356C-40A1-A145-7262C3C04D00", out var value);
        if (!value) return; value = args.Any(_ => _.Equals("/preview", StringComparison.OrdinalIgnoreCase));
        new Application { ShutdownMode = ShutdownMode.OnMainWindowClose }.Run(new Window(value));
    }
}