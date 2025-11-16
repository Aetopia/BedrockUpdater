using System;
using System.Windows;
using static System.String;
using System.ComponentModel;
using System.Windows.Controls;
using Windows.Management.Deployment;
using static Windows.Management.Deployment.PackageTypes;
using Windows.ApplicationModel.Store.Preview.InstallControl;
using static Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallState;

sealed class Window : System.Windows.Window
{
    enum Unit { B, KB, MB, GB }

    readonly TextBlock _textBlock1 = new(), _textBlock2 = new() { Text = $"{Pending}..." };

    readonly ProgressBar _progressBar = new() { Width = 359, Height = 23, IsIndeterminate = true };

    readonly string _text;

    Store.Request? _request;

    readonly Product[] _products;

    internal Window(bool value)
    {
        _text = $"Updating {(value ? "Preview" : "Release")}...";
        _products = [Product.GamingServices, value ? Product.MinecraftWindowsBeta : Product.MinecraftUWP];

        Title = "Bedrock Updater";
        Icon = global::Resources.GetImageSource("Application.ico");

        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        ResizeMode = ResizeMode.NoResize;

        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Canvas _canvas = new() { Width = 381, Height = 115 };

        _canvas.Children.Add(_textBlock1);
        _canvas.Children.Add(_textBlock2);
        _canvas.Children.Add(_progressBar);

        Canvas.SetLeft(_textBlock1, 11);
        Canvas.SetTop(_textBlock1, 15);
        Canvas.SetLeft(_textBlock2, 11);
        Canvas.SetTop(_textBlock2, 84);
        Canvas.SetLeft(_progressBar, 11);
        Canvas.SetTop(_progressBar, 46);

        Content = _canvas;
        _textBlock1.Text = _text;
    }

    protected override void OnClosing(CancelEventArgs args)
    {
        base.OnClosing(args);

        _textBlock2.Text = "Cancelling...";

        _progressBar.Value = 0;
        _progressBar.IsIndeterminate = true;

        args.Cancel = _request?.Cancel() ?? false;
    }

    protected override void OnClosed(EventArgs args)
    {
        base.OnClosed(args);
        
        PackageManager manager = new();

        foreach (var package in manager.FindPackagesForUserWithPackageTypes(Empty, Framework))
            _ = manager.RemovePackageAsync(package.Id.FullName);
    }

    protected override async void OnContentRendered(EventArgs args)
    {
        base.OnContentRendered(args);

        foreach (var product in _products)
        {
            _request = await Store.GetAsync(product, Action);
            if (_request is { } @_) if (!await _) Close();

            _request = null;
            _progressBar.Value = 0;
            _progressBar.IsIndeterminate = true;
            _textBlock2.Text = $"{Pending}...";
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
            case Pending:
            case Installing:
                _textBlock2.Text = $"{args.InstallState}...";
                break;

            case Downloading:
                _textBlock2.Text = $"{Downloading}... {Stringify(args.BytesDownloaded)} / {Stringify(args.DownloadSizeInBytes)}";
                break;
        }

        if (_progressBar.Value != args.PercentComplete)
        {
            _progressBar.Value = args.PercentComplete;
            _progressBar.IsIndeterminate = args.InstallState is Pending;
        }
    });
}