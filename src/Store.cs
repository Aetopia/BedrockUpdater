using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using Windows.System;
using System.Threading;
using System.Xml.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.System.UserProfile;
using System.Collections.Generic;
using Windows.Management.Deployment;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Json;

struct Product
{
    internal string Title;

    internal string Architecture;

    internal string ProductId;

    internal string AppCategoryId;
}

struct UpdateIdentity
{
    internal string UpdateId;

    internal string RevisionNumber;

    internal bool MainPackage;
}

file class Update
{
    internal string Id;

    internal DateTime Modified;

    internal ProcessorArchitecture Architecture;

    internal string PackageFullName;

    internal string[] PackageIdentity;

    internal string Version;

    internal bool MainPackage;
}

file readonly struct _ : INotifyCompletion
{
    internal readonly bool IsCompleted => SynchronizationContext.Current == null;

    internal readonly void GetResult() { }

    internal readonly _ GetAwaiter() { return this; }

    public readonly void OnCompleted(Action _)
    {
        var syncContext = SynchronizationContext.Current;
        try { SynchronizationContext.SetSynchronizationContext(null); _(); }
        finally { SynchronizationContext.SetSynchronizationContext(syncContext); }
    }
}

static class Store
{
    static string _;

    internal static readonly PackageManager PackageManager = new();

    static readonly WebClient client = new()
    {
        BaseAddress = "https://fe3cr.delivery.mp.microsoft.com/ClientWebService/client.asmx/",
        Encoding = Encoding.UTF8
    };

    static readonly (
        (string String, ProcessorArchitecture Architecture) Native,
        (string String, ProcessorArchitecture Architecture) Compatible
    ) architectures = (
        (
            RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            RuntimeInformation.OSArchitecture switch
            {
                Architecture.X86 => ProcessorArchitecture.X86,
                Architecture.X64 => ProcessorArchitecture.X64,
                Architecture.Arm => ProcessorArchitecture.Arm,
                Architecture.Arm64 => ProcessorArchitecture.Arm64,
                _ => ProcessorArchitecture.Unknown
            }
        ),
        (
            RuntimeInformation.OSArchitecture switch { Architecture.X64 => "x86", Architecture.Arm64 => "arm", _ => null },
            RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => ProcessorArchitecture.X86,
                Architecture.Arm64 => ProcessorArchitecture.Arm,
                _ => ProcessorArchitecture.Unknown
            }
        )
    );

    internal static async Task<Product[]> GetProductsAsync(params string[] _)
    {
        await default(_);

        var products = new Product[_.Length];

        for (int index = 0; index < _.Length; index++)
        {
            var payload = Deserialize(await client.DownloadDataTaskAsync(
                $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{_[index]}?market={GlobalizationPreferences.HomeGeographicRegion}&locale=iv&deviceFamily=Windows.Desktop"))
                .Element("Payload");
            var title = payload.Element("ShortTitle")?.Value;
            var platforms = payload.Element("Platforms").Descendants().Select(node => node.Value);

            products[index] = new()
            {
                Title = string.IsNullOrEmpty(title) ? payload.Element("Title").Value : title,
                Architecture = (
                    platforms.FirstOrDefault(item => item.Equals(architectures.Native.String, StringComparison.OrdinalIgnoreCase)) ??
                    platforms.FirstOrDefault(item => item.Equals(architectures.Compatible.String, StringComparison.OrdinalIgnoreCase))
                )?.ToLowerInvariant(),
                AppCategoryId = Deserialize(Encoding.Unicode.GetBytes(payload.Descendants("FulfillmentData").First().Value)).Element("WuCategoryId").Value,
                ProductId = _[index],
            };
        }
        return products;
    }

    internal static async Task<string> GetUrl(UpdateIdentity update)
    {
        await default(_);

        return (await PostAsync(string.Format(Resources.GetExtendedUpdateInfo2, update.UpdateId, update.RevisionNumber), true))
        .Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}Url")
        .First(node => node.Value.StartsWith("http://tlu.dl.delivery.mp.microsoft.com", StringComparison.Ordinal)).Value;
    }

    internal static async Task<List<UpdateIdentity>> GetUpdates(Product product)
    {
        await default(_);

        if (product.Architecture is null) return [];

        var result = (await PostAsync(string.Format(
            _ ??= string.Format(Resources.GetString("SyncUpdates.xml.gz"),
            (await PostAsync(Resources.GetString("GetCookie.xml.gz"))).Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}EncryptedData").First().Value, "{0}"),
            product.AppCategoryId), false))
            .Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}SyncUpdatesResult").First();

        var elements = result.Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}AppxPackageInstallData");
        if (!elements.Any()) return [];

        ProcessorArchitecture architecture;
        Dictionary<string, Update> dictionary = [];

        foreach (var element in elements)
        {
            var parent = element.Parent.Parent.Parent;
            var file = parent.Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}File").First();
            if (Path.GetExtension(file.Attribute("FileName").Value).StartsWith(".e", StringComparison.OrdinalIgnoreCase)) continue;

            var name = file.Attribute("InstallerSpecificIdentifier").Value;
            var identity = name.Split('_');
            var neutral = identity[2] == "neutral";

            if (!neutral && identity[2] != architectures.Native.String && identity[2] != architectures.Compatible.String) continue;
            architecture = (neutral ? product.Architecture : identity[2]) switch
            {
                "x86" => ProcessorArchitecture.X86,
                "x64" => ProcessorArchitecture.X64,
                "arm" => ProcessorArchitecture.Arm,
                "arm64" => ProcessorArchitecture.Arm64,
                _ => ProcessorArchitecture.Unknown
            };

            var key = identity[0] + identity[2];
            if (!dictionary.ContainsKey(key)) dictionary.Add(key, new()
            {
                Architecture = architecture,
                PackageFullName = name,
                PackageIdentity = identity,
                Version = identity[1],
                MainPackage = element.Attribute("MainPackage").Value == "true"
            });

            var modified = Convert.ToDateTime(file.Attribute("Modified").Value);
            if (dictionary[key].Modified < modified)
            {
                dictionary[key].Id = parent.Element("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}ID").Value;
                dictionary[key].Modified = modified;
            }
        }

        var values = dictionary.Where(_ => _.Value.MainPackage).Select(_ => _.Value);
        var value = values.FirstOrDefault(_ => _.Architecture == architectures.Native.Architecture) ?? values.FirstOrDefault(_ => _.Architecture == architectures.Compatible.Architecture);
        architecture = value.Architecture;

        var enumerable = Deserialize(
            await client.DownloadDataTaskAsync($"https://displaycatalog.mp.microsoft.com/v7.0/products/{product.ProductId}?languages=iv&market={GlobalizationPreferences.HomeGeographicRegion}"))
            .Descendants("FrameworkDependencies")
            .First(_ => _.Parent.Element("PackageFullName").Value == value.PackageFullName)
            .Descendants("PackageIdentity")
            .Select(_ => _.Value);

        var items = dictionary
        .Select(_ => _.Value)
        .Where(_ => _.Architecture == architecture && (_.MainPackage || enumerable.Contains(_.PackageIdentity[0])));

        List<UpdateIdentity> list = [];
        foreach (var element in result.Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}SecuredFragment"))
        {
            var parent = element.Parent.Parent.Parent;
            var item = items.FirstOrDefault(item => item.Id == parent.Element("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}ID").Value);
            if (item is null) continue;

            var package = PackageManager.FindPackagesForUser(string.Empty, $"{item.PackageIdentity[0]}_{item.PackageIdentity[4]}").FirstOrDefault(_ => _.Id.Architecture == item.Architecture || item.MainPackage);
            if (package is null || (package.SignatureKind == PackageSignatureKind.Store &&
                new Version(item.Version) > new Version(package.Id.Version.Major, package.Id.Version.Minor, package.Id.Version.Build, package.Id.Version.Revision)))
            {
                var identity = parent.Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}UpdateIdentity").First();
                list.Add(new()
                {
                    UpdateId = identity.Attribute("UpdateID").Value,
                    RevisionNumber = identity.Attribute("RevisionNumber").Value,
                    MainPackage = item.MainPackage
                });
            }
            else if (item.MainPackage) return [];
        }

        list.Sort((x, y) => x.MainPackage ? 1 : -1);
        return list;
    }

    static XElement Deserialize(byte[] buffer)
    {
        using var _ = JsonReaderWriterFactory.CreateJsonReader(buffer, System.Xml.XmlDictionaryReaderQuotas.Max);
        return XDocument.Load(_).Element("root");
    }

    static async Task<XDocument> PostAsync(string data, bool? _ = null)
    {
        client.Headers["Content-Type"] = "application/soap+xml";
        var value = await client.UploadStringTaskAsync(_.HasValue && _.Value ? "secured" : string.Empty, data);
        return XDocument.Parse(_.HasValue && !_.Value ? WebUtility.HtmlDecode(value) : value);
    }
}