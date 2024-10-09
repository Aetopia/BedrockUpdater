using System;
using System.IO;
using System.Net;
using System.Windows;
using Windows.Foundation;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Windows.Controls;
using Windows.Management.Deployment;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.ComponentModel;

sealed class MainWindow : Window
{
    enum Unit { B, KB, MB, GB }

    [DllImport("Shell32", CharSet = CharSet.Auto, SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int ShellMessageBox(nint hAppInst = default, nint hWnd = default, string lpcText = default, string lpcTitle = "Bedrock Updater", int fuStyle = 0x00000010);

    [DllImport("Kernel32", CharSet = CharSet.Auto, SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern bool DeleteFile(string lpFileName);

    public MainWindow(bool preview)
    {
        Icon = global::Resources.Get<ImageSource>(".ico");
        UseLayoutRounding = true;
        Title = "Bedrock Updater";
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        var text = preview ? "Updating Preview..." : "Updating Release...";

        Canvas canvas = new() { Width = 381, Height = 115 };
        Content = canvas;

        TextBlock block1 = new() { Text = text, Foreground = Brushes.White };
        canvas.Children.Add(block1); Canvas.SetLeft(block1, 11); Canvas.SetTop(block1, 15);

        TextBlock block2 = new() { Text = "Preparing...", Foreground = Brushes.White };
        canvas.Children.Add(block2); Canvas.SetLeft(block2, 11); Canvas.SetTop(block2, 84);

        ProgressBar bar = new()
        {
            Width = 359,
            Height = 23,
            BorderThickness = default,
            IsIndeterminate = true,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 133, 66)),
            Background = new SolidColorBrush(Color.FromRgb(14, 14, 14))
        };
        canvas.Children.Add(bar); Canvas.SetLeft(bar, 11); Canvas.SetTop(bar, 46);

        using WebClient client = new();
        string value = default;

        client.DownloadProgressChanged += (_, e) => Dispatcher.Invoke(() =>
        {
            static string Value(double value) { var unit = (int)Math.Log(value, 1024); return $"{value / Math.Pow(1024, value):0.00} {(Unit)unit}"; }
            if (bar.Value != e.ProgressPercentage) { block2.Text = $"Downloading... {Value(e.BytesReceived)} / {value ??= Value(e.TotalBytesToReceive)}"; bar.Value = e.ProgressPercentage; }
        });

        client.DownloadFileCompleted += (sender, e) => Dispatcher.Invoke(() => { value = default; bar.Value = 0; block2.Text = "Installing..."; });

        Uri uri = default;
        IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> operation = default;

        Application.Current.Exit += (_, e) =>
        {
            client.CancelAsync(); while (client.IsBusy) ;
            if (operation is not null) { operation.Cancel(); while (operation.Status == AsyncStatus.Started) ; }
            DeleteFile(uri?.AbsolutePath);
            foreach (var package in Store.PackageManager.FindPackagesForUserWithPackageTypes(string.Empty, PackageTypes.Framework)) _ = Store.PackageManager.RemovePackageAsync(package.Id.FullName);
        };

        Application.Current.Dispatcher.UnhandledException += (_, e) =>
        {
            e.Handled = true; var exception = e.Exception;
            while (exception.InnerException != null) exception = exception.InnerException;
            ShellMessageBox(hWnd: new WindowInteropHelper(this).Handle, lpcText: exception.Message);
            Application.Current.Shutdown();
        };

        ContentRendered += async (sender, e) => await Task.Run(() =>
        {
            Progress<DeploymentProgress> progress = new(_ => Dispatcher.Invoke(() => { if (bar.Value != _.percentage) block2.Text = $"Installing... {bar.Value = _.percentage}%"; }));
            foreach (var array in Store.Get("9WZDNCRD1HKW", preview ? "9P5X4QVLC2XR" : "9NBLGGH2JHXJ"))
            {
                Dispatcher.Invoke(() => bar.IsIndeterminate = array.Length == 0);
                for (int index = 0; index < array.Length; index++)
                {
                    Dispatcher.Invoke(() =>
                    {
                        block1.Text = array.Length != 1 ? $"{text} {index + 1} / {array.Length}" : text;
                        block2.Text = "Downloading...";
                        bar.Value = 0;
                    });
                    try
                    {
                        client.DownloadFileTaskAsync(array[index], (uri = new(Path.GetTempFileName())).LocalPath).Wait();
                        (operation = Store.PackageManager.AddPackageAsync(uri, null, DeploymentOptions.ForceApplicationShutdown)).AsTask(progress).Wait();
                    }
                    finally { DeleteFile(uri.LocalPath); }
                }
                Dispatcher.Invoke(() => { block1.Text = text; block2.Text = "Preparing..."; bar.Value = 0; ; bar.IsIndeterminate = true; });
            }
            Dispatcher.Invoke(Close);
        });
    }
}