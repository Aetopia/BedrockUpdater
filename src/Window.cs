using System;
using System.Windows;
using System.Reflection;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Windows.ApplicationModel.Store.Preview.InstallControl;
using static Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallState;

sealed class Window : System.Windows.Window
{
    enum Unit { B, KB, MB, GB }

    readonly TextBlock _textBlock1 = new(), _textBlock2 = new() { Text = $"{Pending}..." };

    readonly ProgressBar _progressBar = new() { Width = 359, Height = 23, IsIndeterminate = true };

    Store.Request? _request;

    readonly Store.Product[] _products;

    internal Window(bool value)
    {
        _textBlock1.Text = $"Updating {(value ? "Preview" : "Release")}...";
        _products = [Store.Product.GamingServices, value ? Store.Product.MinecraftWindowsBeta : Store.Product.MinecraftUWP];

        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Application.ico"))
            Icon = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        Title = "Bedrock Updater";
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        ResizeMode = ResizeMode.NoResize;

        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = new Canvas { Width = 381, Height = 115 };
        ((Canvas)Content).Children.Add(_textBlock1);
        ((Canvas)Content).Children.Add(_textBlock2);
        ((Canvas)Content).Children.Add(_progressBar);

        Canvas.SetLeft(_textBlock1, 11);
        Canvas.SetTop(_textBlock1, 15);
        Canvas.SetLeft(_textBlock2, 11);
        Canvas.SetTop(_textBlock2, 84);
        Canvas.SetLeft(_progressBar, 11);
        Canvas.SetTop(_progressBar, 46);
    }

    protected override void OnClosing(CancelEventArgs args)
    {
        base.OnClosing(args);
        args.Cancel = _request?.Cancel() ?? false;
    }

    protected override async void OnContentRendered(EventArgs args)
    {
        base.OnContentRendered(args);

        foreach (var product in _products)
        {
            _request = await Store.GetAsync(product, Action);
            if (_request is { } @_) if (!await _) break;

            _request = null; _textBlock2.Text = $"{Pending}...";
            _progressBar.Value = 0; _progressBar.IsIndeterminate = true;
        }

        Close();
    }

    static string Stringify(double value)
    {
        var x = Math.Abs(value);
        var y = (int)Math.Log(Math.Max(x, 1), 1024);
        return $"{x / Math.Pow(1024, y):0.##} {(Unit)y}";
    }

    void Action(AppInstallStatus args) => Dispatcher.Invoke(() =>
    {
        switch (args.InstallState)
        {
            case Pending or Installing: _textBlock2.Text = $"{args.InstallState}..."; break;
            case Downloading: _textBlock2.Text = $"{args.InstallState}... {Stringify(args.BytesDownloaded)} / {Stringify(args.DownloadSizeInBytes)}"; break;
        }

        if (_progressBar.Value != args.PercentComplete)
        {
            _progressBar.IsIndeterminate = args.InstallState is AppInstallState.Pending;
            _progressBar.Value = _progressBar.IsIndeterminate ? 0 : args.PercentComplete;
        }
    });
}