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
    internal string Architecture;

    internal string Id;

    internal string AppCategoryId;
}

struct Update
{
    internal string Id;

    internal string RevisionNumber;

    internal bool MainPackage;
}

class Identity
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
    internal static readonly PackageManager PackageManager = new();

    static string data;

    static readonly ulong build = (GetVersion() >> 16) & 0xFFFF;

    static readonly (string String, ProcessorArchitecture Architecture) native = (
        RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
        RuntimeInformation.OSArchitecture switch
        {
            Architecture.X86 => ProcessorArchitecture.X86,
            Architecture.X64 => ProcessorArchitecture.X64,
            Architecture.Arm => ProcessorArchitecture.Arm,
            Architecture.Arm64 => ProcessorArchitecture.Arm64,
            _ => ProcessorArchitecture.Unknown
        });

    static readonly (string String, ProcessorArchitecture Architecture) compatible = (
        RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x86",
            Architecture.Arm64 => "arm",
            _ => null
        }, RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => ProcessorArchitecture.X86,
            Architecture.Arm64 => ProcessorArchitecture.Arm,
            _ => ProcessorArchitecture.Unknown
        });

    static readonly WebClient client = new() { BaseAddress = "https://fe3cr.delivery.mp.microsoft.com/ClientWebService/client.asmx/" };

    internal static string[] Get(params string[] ids) => Products(ids).Updates().Urls();

    static IEnumerable<Product> Products(params string[] ids) => ids.Select(_ =>
    {
        var payload = Deserialize(client.DownloadData($"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{_}?market={GlobalizationPreferences.HomeGeographicRegion}&locale=iv&deviceFamily=Windows.Desktop"))
        .Element("Payload");
        var title = payload.Element("ShortTitle")?.Value;
        var platforms = payload.Element("Platforms").Descendants().Select(_ => _.Value);

        return new Product()
        {
            Architecture = (platforms.FirstOrDefault(_ => _.Equals(native.String, StringComparison.OrdinalIgnoreCase)) ??
                            platforms.FirstOrDefault(_ => _.Equals(compatible.String, StringComparison.OrdinalIgnoreCase)))?.ToLowerInvariant(),
            AppCategoryId = Deserialize(payload.Descendants("FulfillmentData").First().Value).Element("WuCategoryId").Value,
            Id = _,
        };
    });

    static IEnumerable<Update> Updates(this IEnumerable<Product> products)
    {
        List<Update> list = [];
        return products.Where(_ => _.Architecture is not null).SelectMany(_ => { var updates = _.Sync(); return _.Find(updates); }).OrderBy(_ => _.MainPackage);
    }

    static string[] Urls(this IEnumerable<Update> updates) => updates.Select(_ => Post(string.Format(Resources.GetExtendedUpdateInfo2, _.Id, _.RevisionNumber), true)
   .Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}Url")
   .First(_ => _.Value.StartsWith("http://tlu.dl.delivery.mp.microsoft.com", StringComparison.Ordinal)).Value).ToArray();

    static XElement Sync(this Product product) => Post(string.Format(data ??= string.Format(Resources.GetString("SyncUpdates.xml.gz"),
    Post(Resources.GetString("GetCookie.xml.gz")).Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}EncryptedData").First().Value, "{0}"), product.AppCategoryId), false)
    .Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}SyncUpdatesResult").First();

    static List<Update> Find(this Product product, XElement updates)
    {
        Dictionary<string, Identity> dictionary = [];
        foreach (var element in updates.Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}AppxPackageInstallData"))
        {
            var parent = element.Parent.Parent.Parent;
            var file = parent.Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}File").First();
            if (Path.GetExtension(file.Attribute("FileName").Value).StartsWith(".e", StringComparison.OrdinalIgnoreCase)) continue;

            var name = file.Attribute("InstallerSpecificIdentifier").Value;
            var identity = name.Split('_');
            var neutral = identity[2] == "neutral";

            if (!neutral && identity[2] != native.String && identity[2] != compatible.String) continue;

            var key = identity[0] + identity[2];
            if (!dictionary.ContainsKey(key)) dictionary.Add(key, new()
            {
                Architecture = (neutral ? product.Architecture : identity[2]) switch
                {
                    "x86" => ProcessorArchitecture.X86,
                    "x64" => ProcessorArchitecture.X64,
                    "arm" => ProcessorArchitecture.Arm,
                    "arm64" => ProcessorArchitecture.Arm64,
                    _ => ProcessorArchitecture.Unknown
                },
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
        return product.Filter(dictionary).Verify(updates);
    }

    static IEnumerable<Identity> Filter(this Product product, Dictionary<string, Identity> dictionary)
    {
        if (dictionary.Count == 0) return [];

        var values = dictionary.Where(_ => _.Value.MainPackage).Select(_ => _.Value);
        var value = values.FirstOrDefault(_ => _.Architecture == native.Architecture) ?? values.FirstOrDefault(_ => _.Architecture == compatible.Architecture);

        var source = Deserialize(
            client.DownloadData($"https://displaycatalog.mp.microsoft.com/v7.0/products/{product.Id}?languages=iv&market={GlobalizationPreferences.HomeGeographicRegion}"))
            .Descendants("FrameworkDependencies")
            .First(_ => _.Parent.Element("PackageFullName").Value == value.PackageFullName)
            .Descendants("PackageIdentity")
            .Select(_ => _.Value);

        return dictionary.Where(_ => _.Value.Architecture == value.Architecture && (_.Value.MainPackage || source.Contains(_.Value.PackageIdentity[0]))).Select(_ => _.Value);
    }

    static List<Update> Verify(this IEnumerable<Identity> source, XElement updates)
    {
        List<Update> list = [];
        foreach (var element in updates.Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}SecuredFragment"))
        {
            var parent = element.Parent.Parent.Parent;
            var item = source.FirstOrDefault(_ => _.Id == parent.Element("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}ID").Value);
            if (item is null) continue;

            if (!(((ulong.Parse(Deserialize(
                parent.Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}ApplicabilityBlob").First().Value)
                .Descendants("platform.minVersion").First().Value) >> 16) & 0xFFFF) <= build))
                return [];

            var package = PackageManager.FindPackagesForUser(string.Empty, $"{item.PackageIdentity[0]}_{item.PackageIdentity[4]}").FirstOrDefault(_ => _.Id.Architecture == item.Architecture || item.MainPackage);
            if (package is null || (package.SignatureKind == PackageSignatureKind.Store &&
                new Version(item.Version) > new Version(package.Id.Version.Major, package.Id.Version.Minor, package.Id.Version.Build, package.Id.Version.Revision)))
            {
                var identity = parent.Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}UpdateIdentity").First();
                list.Add(new()
                {
                    Id = identity.Attribute("UpdateID").Value,
                    RevisionNumber = identity.Attribute("RevisionNumber").Value,
                    MainPackage = item.MainPackage
                });
            }
            else if (item.MainPackage) return [];
        }
        return list;
    }

    static XElement Deserialize(string value) => Deserialize(Encoding.Unicode.GetBytes(value));

    static XElement Deserialize(byte[] value)
    {
        using var reader = JsonReaderWriterFactory.CreateJsonReader(value, System.Xml.XmlDictionaryReaderQuotas.Max);
        return XElement.Load(reader);
    }

    static XElement Post(string data, bool? _ = null)
    {
        client.Headers["Content-Type"] = "application/soap+xml";
        var value = client.UploadString(_.HasValue && _.Value ? "secured" : string.Empty, data);
        return XElement.Parse(_.HasValue && !_.Value ? WebUtility.HtmlDecode(value) : value);
    }

    [DllImport("Kernel32"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern ulong GetVersion();
}