using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using Windows.Foundation;
using Windows.Management.Deployment;

class MainWindow : Window
{
    [DllImport("Kernel32", CharSet = CharSet.Auto, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern bool DeleteFile(string lpFileName);

    [DllImport("Shell32", CharSet = CharSet.Auto, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int ShellMessageBox(IntPtr hAppInst = default, IntPtr hWnd = default, string lpcText = default, string lpcTitle = "Bedrock Updater", int fuStyle = 0x00000010);

    static readonly IEnumerable<string> Units = ["B", "KB", "MB", "GB"];

    static string Format(float bytes)
    {
        int index = 0;
        while (bytes >= 1024) { bytes /= 1024; ++index; }
        return string.Format($"{bytes:0.00} {Units.ElementAt(index)}");
    }

    internal MainWindow(IEnumerable<string> args)
    {
        var preview = args.FirstOrDefault()?.Equals("/preview", StringComparison.OrdinalIgnoreCase) ?? false;
        Application.Current.DispatcherUnhandledException += (sender, e) =>
        {
            e.Handled = true;
            var exception = e.Exception;
            while (exception.InnerException != null) exception = exception.InnerException;
            ShellMessageBox(lpcText: exception.Message);
            Close();
        };

        UseLayoutRounding = true;
        Icon = global::Resources.Icon;
        Title = preview ? "Bedrock Updater Preview" : "Bedrock Updater";
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E1E"));
        Content = new Grid { Width = 1000, Height = 600 };
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;

        WindowsFormsHost host = new()
        {
            Child = new System.Windows.Forms.WebBrowser
            {
                ScrollBarsEnabled = false,
                DocumentText = $@"<head><meta http-equiv=""X-UA-Compatible"" content=""IE=9""/></head><body style=""background-color:#1E1E1E""><div style=""width:100%;height:100%;position:absolute;left:50%;top:50%;transform:translate(-50%, -50%)"">{(global::Resources.Minecraft)}</div></body>"
            },
            IsEnabled = false
        };

        Grid.SetRow(host, 0);
        ((Grid)Content).RowDefinitions.Add(new());
        ((Grid)Content).Children.Add(host);

        Grid grid = new() { Margin = new(10, 0, 10, 10) };
        grid.RowDefinitions.Add(new());

        Grid.SetRow(grid, 1);
        ((Grid)Content).RowDefinitions.Add(new() { Height = GridLength.Auto });
        ((Grid)Content).Children.Add(grid);

        ProgressBar progressBar = new()
        {
            Height = 32,
            BorderThickness = default,
            IsIndeterminate = true,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#008542")),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0E0E0E"))
        };

        Grid.SetRow(progressBar, 0);
        grid.Children.Add(progressBar);

        TextBlock textBlock1 = new()
        {
            Text = "Connecting...",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new(16, 0, 0, 1),
            Foreground = Brushes.White
        };

        Grid.SetRow(textBlock1, 0);
        grid.Children.Add(textBlock1);

        TextBlock textBlock2 = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new(0, 0, 16, 1),
            Foreground = Brushes.White
        };

        Grid.SetRow(textBlock2, 0);
        grid.Children.Add(textBlock2);

        using WebClient client = new();
        string value = default;

        client.DownloadProgressChanged += (sender, e) =>
        {
            if (progressBar.Value != e.ProgressPercentage)
            {
                textBlock1.Text = $"Downloading {Format(e.BytesReceived)} / {value ??= Format(e.TotalBytesToReceive)}";
                progressBar.Value = e.ProgressPercentage;
            }
        };

        client.DownloadFileCompleted += (sender, e) =>
        {
            value = default;
            progressBar.Value = 0;
            textBlock1.Text = "Installing...";
        };

        Uri packageUri = default;
        IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> operation = default;

        Application.Current.Exit += (sender, e) =>
        {
            client.CancelAsync();
            while (client.IsBusy) ;
            operation?.Cancel();
            DeleteFile(packageUri?.AbsolutePath);
            foreach (var package in Store.PackageManager.FindPackagesForUserWithPackageTypes(string.Empty, PackageTypes.Framework))
                _ = Store.PackageManager.RemovePackageAsync(package.Id.FullName);
        };

        ContentRendered += async (sender, e) =>
        {
            var store = await Store.CreateAsync();
            foreach (var product in await store.GetProductsAsync("9WZDNCRD1HKW", preview ? "9P5X4QVLC2XR" : "9NBLGGH2JHXJ"))
            {
                progressBar.IsIndeterminate = true;
                textBlock1.Text = $"Updating {product.Title}...";
                textBlock2.Text = null;
                var identities = await store.SyncUpdatesAsync(product);

                if (identities.Count != 0) progressBar.IsIndeterminate = false;
                for (int i = 0; i < identities.Count; i++)
                {
                    textBlock1.Text = "Downloading...";
                    textBlock2.Text = $"{i + 1} of {identities.Count}";
                    progressBar.Value = 0;

                    try
                    {
                        await client.DownloadFileTaskAsync(await store.GetUrlAsync(identities[i]), (packageUri = new(Path.GetTempFileName())).AbsolutePath);
                        operation = Store.PackageManager.AddPackageAsync(packageUri, null, DeploymentOptions.ForceApplicationShutdown);
                        operation.Progress += (sender, e) => Dispatcher.Invoke(() => { if (progressBar.Value != e.percentage) textBlock1.Text = $"Installing {progressBar.Value = e.percentage}%"; });
                        await operation;
                    }
                    finally { DeleteFile(packageUri.AbsolutePath); }
                }
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                Arguments = preview ? "minecraft-preview://" : "minecraft://",
                UseShellExecute = false
            }).Dispose();
            Close();
        };
    }
}