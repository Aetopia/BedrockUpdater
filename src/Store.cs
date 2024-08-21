using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using Windows.System;
using System.Xml.Linq;
using Windows.ApplicationModel;
using Windows.System.UserProfile;
using System.Collections.Generic;
using Windows.Management.Deployment;
using System.Runtime.InteropServices;
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

static class Store
{
    [DllImport("Kernel32"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern ulong GetVersion();

    internal static readonly PackageManager PackageManager = new();

    static (string SyncUpdates, ulong Build) _ = (null, (GetVersion() >> 16) & 0xFFFF);

    static readonly WebClient client = new() { BaseAddress = "https://fe3cr.delivery.mp.microsoft.com/ClientWebService/client.asmx/" };

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

    internal static Product[] GetProducts(params string[] _)
    {
        var products = new Product[_.Length];

        for (int index = 0; index < _.Length; index++)
        {
            var payload = Deserialize(client.DownloadData(
                $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{_[index]}?market={GlobalizationPreferences.HomeGeographicRegion}&locale=iv&deviceFamily=Windows.Desktop"))
                .Element("Payload");
            var title = payload.Element("ShortTitle")?.Value;
            var platforms = payload.Element("Platforms").Descendants().Select(_ => _.Value);

            products[index] = new()
            {
                Title = (string.IsNullOrEmpty(title) ? payload.Element("Title").Value : title).Trim(),
                Architecture = (platforms.FirstOrDefault(_ => _.Equals(architectures.Native.String, StringComparison.OrdinalIgnoreCase)) ??
                                platforms.FirstOrDefault(_ => _.Equals(architectures.Compatible.String, StringComparison.OrdinalIgnoreCase)))?.ToLowerInvariant(),
                AppCategoryId = Deserialize(payload.Descendants("FulfillmentData").First().Value).Element("WuCategoryId").Value,
                ProductId = _[index],
            };
        }
        return products;
    }

    internal static string GetUrl(UpdateIdentity _)
    {
        return Post(string.Format(Resources.GetExtendedUpdateInfo2, _.UpdateId, _.RevisionNumber), true)
        .Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}Url")
        .First(_ => _.Value.StartsWith("http://tlu.dl.delivery.mp.microsoft.com", StringComparison.Ordinal)).Value;
    }

    internal static List<UpdateIdentity> GetUpdates(Product _)
    {

        if (_.Architecture is null) return [];

        var result = Post(string.Format(Store._.SyncUpdates ??= string.Format(Resources.GetString("SyncUpdates.xml.gz"),
            Post(Resources.GetString("GetCookie.xml.gz")).Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}EncryptedData").First().Value, "{0}"), _.AppCategoryId), false)
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
            architecture = (neutral ? _.Architecture : identity[2]) switch
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
             client.DownloadData($"https://displaycatalog.mp.microsoft.com/v7.0/products/{_.ProductId}?languages=iv&market={GlobalizationPreferences.HomeGeographicRegion}"))
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

            if (!(((ulong.Parse(Deserialize(
                parent.Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}ApplicabilityBlob").First().Value)
                .Descendants("platform.minVersion").First().Value) >> 16) & 0xFFFF) <= Store._.Build))
                return [];

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

    static XElement Deserialize(string _) => Deserialize(Encoding.Unicode.GetBytes(_));

    static XElement Deserialize(byte[] _)
    {
        using var reader = JsonReaderWriterFactory.CreateJsonReader(_, System.Xml.XmlDictionaryReaderQuotas.Max);
        return XElement.Load(reader);
    }

    static XElement Post(string data, bool? _ = null)
    {
        client.Headers["Content-Type"] = "application/soap+xml";
        var value = client.UploadString(_.HasValue && _.Value ? "secured" : string.Empty, data);
        return XElement.Parse(_.HasValue && !_.Value ? WebUtility.HtmlDecode(value) : value);
    }
}