using System;
using System.Windows;
using System.Windows.Media;
using static System.String;
using System.ComponentModel;
using System.Windows.Controls;
using Windows.Management.Deployment;
using static Windows.Management.Deployment.PackageTypes;
using Windows.ApplicationModel.Store.Preview.InstallControl;
using static Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallState;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation;
using System.Windows.Documents;
using Windows.ApplicationModel;

sealed class Window : System.Windows.Window
{
    enum Unit { B, KB, MB, GB }

    readonly TextBlock _textBlock1 = new()
    {
        Foreground = Brushes.White
    };

    readonly TextBlock _textBlock2 = new()
    {
        Text = "Preparing...",
        Foreground = Brushes.White
    };

    readonly ProgressBar _progressBar = new()
    {
        Width = 359,
        Height = 23,
        IsIndeterminate = true,
        BorderThickness = new(),
        Foreground = new SolidColorBrush(Color.FromRgb(0, 133, 66)),
        Background = new SolidColorBrush(Color.FromRgb(14, 14, 14))
    };

    readonly string _text;

    Store.Request? _request;

    readonly Product[] _products;

    internal Window(bool value)
    {
        _text = value ? "Updating Preview..." : "Updating Release...";
        _products = [value ? Product.GamingServices : Product.XboxIdentityProvider, value ? Product.MinecraftWindowsBeta : Product.MinecraftUWP];

        Title = "Bedrock Updater";
        Icon = global::Resources.GetImageSource("Application.ico");

        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        ResizeMode = ResizeMode.NoResize;

        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));

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
        Package[] packages = [.. manager.FindPackagesForUserWithPackageTypes(Empty, Framework)];

        var tasks = new Task[packages.Length]; for (var index = 0; index < packages.Length; index++)
        {
            TaskCompletionSource<bool> source = new();
          
            tasks[index] = source.Task;
            var operation = manager.RemovePackageAsync(packages[index].Id.FullName);
          
            operation.Completed += delegate { source.TrySetResult(true); };
        }

        Task.WaitAll(tasks);
    }

    protected override async void OnContentRendered(EventArgs args)
    {
        base.OnContentRendered(args);

        for (var index = 0; index < _products.Length; index++)
        {
            _textBlock1.Text = $"{_text} {index + 1} / {_products.Length}";

            _request = await Store.GetAsync(_products[index], Action);
            if (_request is { } @_) if (!await _) Close();

            _request = null;
            _progressBar.Value = 0;
            _progressBar.IsIndeterminate = true;
            _textBlock2.Text = $"Preparing...";
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
        if (_progressBar.Value != args.PercentComplete)
        {
            _progressBar.IsIndeterminate = false;
            _progressBar.Value = args.PercentComplete;
        }

        _textBlock2.Text = args.InstallState switch
        {
            Installing or RestoringData => "Installing...",
            Downloading => $"Downloading... {Stringify(args.BytesDownloaded)} / {Stringify(args.DownloadSizeInBytes)}",
            _ => "Preparing...",
        };
    });
}