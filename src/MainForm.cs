using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Windows.Foundation;
using Windows.Management.Deployment;
using Windows.System;

class MainForm : Form
{
    [DllImport("Kernel32", CharSet = CharSet.Auto, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern bool DeleteFile(string lpFileName);

    [DllImport("Shell32", CharSet = CharSet.Auto, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int ShellMessageBox(IntPtr hAppInst = default, IntPtr hWnd = default, string lpcText = default, string lpcTitle = "Error", int fuStyle = 0x00000010);

    internal MainForm(IEnumerable<string> args)
    {
        Application.ThreadException += (sender, e) =>
        {
            var exception = e.Exception;
            while (exception.InnerException != null) exception = exception.InnerException;
            ShellMessageBox(lpcText: exception.Message);
            Close();
        };

        Font = new("MS Shell Dlg 2", 8);
        Text = "Bedrock Updater";
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = LogicalToDeviceUnits(new System.Drawing.Size(380, 115));
        CenterToScreen();

        Label label1 = new()
        {
            Text = "Connecting...",
            AutoSize = true,
            Location = new(LogicalToDeviceUnits(9), LogicalToDeviceUnits(23)),
            Margin = default
        };
        Controls.Add(label1);

        ProgressBar progressBar = new()
        {
            Width = LogicalToDeviceUnits(359),
            Height = LogicalToDeviceUnits(23),
            Location = new(LogicalToDeviceUnits(11), LogicalToDeviceUnits(46)),
            Margin = default,
            MarqueeAnimationSpeed = 30,
            Style = ProgressBarStyle.Marquee
        };
        Controls.Add(progressBar);

        Label label2 = new()
        {
            Text = "Checking...",
            AutoSize = true,
            Location = new(label1.Location.X, LogicalToDeviceUnits(80)),
            Margin = default
        };
        Controls.Add(label2);

        Button button = new()
        {
            Text = "Cancel",
            Width = LogicalToDeviceUnits(75),
            Height = LogicalToDeviceUnits(23),
            Location = new(LogicalToDeviceUnits(294), LogicalToDeviceUnits(81)),
            Margin = default
        };
        button.Click += (sender, e) => Close();
        Controls.Add(button);

        using WebClient webClient = new();
        webClient.DownloadProgressChanged += (sender, e) => progressBar.Value = e.ProgressPercentage;
        webClient.DownloadFileCompleted += (sender, e) =>
        {
            label2.Text = "Installing...";
            progressBar.Value = 0;
        };
        IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> deploymentOperation = default;
        Uri packageUri = default;
        PackageManager packageManager = new();

        Application.ThreadExit += (sender, e) =>
        {
            webClient.CancelAsync();
            deploymentOperation?.Cancel();
            DeleteFile(packageUri?.AbsolutePath);
            foreach (var packageFullName in packageManager.FindPackagesForUserWithPackageTypes(string.Empty, PackageTypes.Framework).Select(package => package.Id.FullName))
                packageManager.RemovePackageAsync(packageFullName).GetResults();
        };

        Shown += async (sender, e) =>
        {
            var store = await Store.CreateAsync();
            var preview = args.FirstOrDefault()?.ToLowerInvariant().Equals("/preview") ?? false;

            foreach (var product in await store.GetProductsAsync("9wzdncrd1hkw", preview ? "9p5x4qvlc2xr" : "9nblggh2jhxj"))
            {
                label1.Text = $"Updating {product.Title}...";
                label2.Text = "Checking...";
                progressBar.Style = ProgressBarStyle.Marquee;

                var updateIds = await store.SyncUpdates(product);
                if (!updateIds.Any()) continue;

                var count = updateIds.Count();
                progressBar.Style = ProgressBarStyle.Blocks;

                for (int i = 0; i < count; i++)
                {
                    label1.Text = $"Updating {product.Title}... ({i + 1} of {count})";
                    label2.Text = "Downloading...";
                    progressBar.Value = 0;

                    packageUri = new(Path.GetTempFileName());
                    try
                    {
                        await webClient.DownloadFileTaskAsync(await store.GetUrlAsync(updateIds.ElementAt(i)), packageUri.AbsolutePath);
                        deploymentOperation = packageManager.AddPackageAsync(packageUri, null, DeploymentOptions.ForceApplicationShutdown);
                        deploymentOperation.Progress += (sender, e) => progressBar.Value = (int)e.percentage;
                        await deploymentOperation;
                    }
                    finally { DeleteFile(packageUri.AbsolutePath); }
                }
            }

            await Launcher.LaunchUriAsync(new(preview ? "minecraft-preview://" : "minecraft://"));
            Close();
        };
    }
}