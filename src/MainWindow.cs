using System;
using System.IO;
using System.Net;
using System.Windows;
using Windows.Foundation;
using System.Windows.Media;
using System.Windows.Controls;
using Windows.Management.Deployment;
using System.Windows.Forms.Integration;

class MainWindow : Window
{
    enum Unit { B, KB, MB, GB }

    internal MainWindow(bool preview)
    {
        UseLayoutRounding = true;
        Icon = global::Resources.GetImageSource(".ico");
        Title = $"Bedrock Updater ({(preview ? "Preview" : "Release")})";
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Closed += (sender, e) => Environment.Exit(0);

        Grid grid1 = new() { Width = 1000, Height = 600 };
        Content = grid1;

        WindowsFormsHost host = new()
        {
            Child = new System.Windows.Forms.WebBrowser
            {
                ScrollBarsEnabled = false,
                DocumentText = global::Resources.GetString("Document.html.gz")
            },
            IsEnabled = false
        };
        Grid.SetRow(host, 0);
        grid1.RowDefinitions.Add(new());
        grid1.Children.Add(host);

        Grid grid2 = new() { Margin = new(10, 0, 10, 10) };
        Grid.SetRow(grid2, 1);
        grid1.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid1.Children.Add(grid2);

        ProgressBar progressBar = new()
        {
            Height = 32,
            BorderThickness = default,
            IsIndeterminate = true,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 133, 66)),
            Background = new SolidColorBrush(Color.FromRgb(14, 14, 14))
        };
        grid2.Children.Add(progressBar);

        TextBlock textBlock1 = new()
        {
            Text = "Preparing...",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new(16, 0, 0, 1),
            Foreground = Brushes.White,
        };
        grid2.Children.Add(textBlock1);

        TextBlock textBlock2 = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new(0, 0, 16, 1),
            Foreground = Brushes.White
        };
        grid2.Children.Add(textBlock2);

        using WebClient client = new();
        string value = default;

        client.DownloadProgressChanged += (sender, e) =>
        {
            static string _(float _) { var unit = (int)Math.Log(_, 1024); return $"{_ / Math.Pow(1024, unit):0.00} {(Unit)unit}"; }
            if (progressBar.Value != e.ProgressPercentage)
            {
                textBlock1.Text = $"Downloading {_(e.BytesReceived)} / {value ??= _(e.TotalBytesToReceive)}";
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

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            client.CancelAsync();
            while (client.IsBusy) ;
            operation?.Cancel();
            NativeMethods.DeleteFile(packageUri?.AbsolutePath);
            foreach (var package in Store.PackageManager.FindPackagesForUserWithPackageTypes(string.Empty, PackageTypes.Framework))
                _ = Store.PackageManager.RemovePackageAsync(package.Id.FullName);
        };

        ContentRendered += async (sender, e) =>
        {
            foreach (var product in await Store.GetProductsAsync("9WZDNCRD1HKW", preview ? "9P5X4QVLC2XR" : "9NBLGGH2JHXJ"))
            {
                progressBar.IsIndeterminate = true;
                textBlock1.Text = $"Preparing {product.Title}...";
                textBlock2.Text = default;

                var list = await Store.GetUpdates(product);

                if (list.Count != 0) progressBar.IsIndeterminate = false;
                for (int index = 0; index < list.Count; index++)
                {
                    textBlock1.Text = "Downloading...";
                    textBlock2.Text = list.Count != 1 ? $"{index + 1} / {list.Count}" : null;
                    progressBar.Value = 0;

                    try
                    {
                        await client.DownloadFileTaskAsync(await Store.GetUrl(list[index]), (packageUri = new(Path.GetTempFileName())).LocalPath);
                        operation = Store.PackageManager.AddPackageAsync(packageUri, null, DeploymentOptions.ForceApplicationShutdown);
                        operation.Progress += (sender, e) => Dispatcher.Invoke(() => { if (progressBar.Value != e.percentage) progressBar.Value = e.percentage; });
                        await operation;
                    }
                    finally { NativeMethods.DeleteFile(packageUri.LocalPath); }
                }
            }

            NativeMethods.ShellExecute(lpFile: @$"shell:AppsFolder\Microsoft.Minecraft{(preview ? "WindowsBeta" : "UWP")}_8wekyb3d8bbwe!App");
            Close();
        };
    }
}