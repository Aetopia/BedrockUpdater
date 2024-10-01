using System;
using System.Net;
using System.Xml;
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

    internal string Platform;

    internal string PackageFullName;

    internal string[] PackageIdentity;

    internal bool MainPackage;
}

static class Store
{
    internal static readonly PackageManager PackageManager = new();

    static string data;

    static readonly ulong build = (Unmanaged.GetVersion() >> 16) & 0xFFFF;

    static readonly string storeedgefd = $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{{0}}?market={GlobalizationPreferences.HomeGeographicRegion}&locale=iv&deviceFamily=Windows.Desktop";

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

    internal static IEnumerable<string[]> Products(params string[] ids) => ids.Select(_ =>
    {
        var payload = Get(string.Format(storeedgefd, _)).Element("Payload");
        var platforms = payload.Element("Platforms").Descendants().Select(_ => _.Value);

        return new Product()
        {
            Architecture = (platforms.FirstOrDefault(_ => _.Equals(native.String, StringComparison.OrdinalIgnoreCase)) ??
                            platforms.FirstOrDefault(_ => _.Equals(compatible.String, StringComparison.OrdinalIgnoreCase)))?.ToLowerInvariant(),
            AppCategoryId = Parse(payload.Descendants("FulfillmentData").First().Value).Element("WuCategoryId").Value,
            Id = _,
        };
    }).Where(_ => _.Architecture is not null).Select(_ => _.Get());

    static string[] Urls(this IEnumerable<Update> updates) => updates.Select(_ => Post(string.Format(Resources.GetExtendedUpdateInfo2, _.Id, _.RevisionNumber), true)
   .Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}Url")
   .First(_ => _.Value.StartsWith("http://tlu.dl.delivery.mp.microsoft.com", StringComparison.Ordinal)).Value).ToArray();

    static XElement Sync(this Product product) => Post(string.Format(data ??= string.Format(Resources.GetString("SyncUpdates.xml.gz"),
    Post(Resources.GetString("GetCookie.xml.gz")).Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}EncryptedData").First().Value, "{0}"), product.AppCategoryId), decode: true)
    .Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}SyncUpdatesResult").First();

    static string[] Get(this Product product)
    {
        var updates = product.Sync();
        var elements = updates.Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}AppxPackageInstallData");
        if (!elements.Any()) return [];

        Dictionary<string, Identity> dictionary = [];

        foreach (var element in elements)
        {
            var parent = element.Parent.Parent.Parent;
            var file = parent.Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}File").First();
            var attribute = file.Attribute("FileName").Value;

            if (attribute[attribute.LastIndexOf('.') + 1] == 'e') continue;

            var name = file.Attribute("InstallerSpecificIdentifier").Value;
            var identity = name.Split('_');
            var neutral = identity[2] == "neutral";

            if (!neutral && identity[2] != native.String && identity[2] != compatible.String) continue;

            var key = identity[0] + identity[2];
            var platform = neutral ? product.Architecture : identity[2];
            if (!dictionary.ContainsKey(key)) dictionary.Add(key, new()
            {
                Architecture = platform switch
                {
                    "x86" => ProcessorArchitecture.X86,
                    "x64" => ProcessorArchitecture.X64,
                    "arm" => ProcessorArchitecture.Arm,
                    "arm64" => ProcessorArchitecture.Arm64,
                    _ => ProcessorArchitecture.Unknown
                },
                Platform = platform,
                PackageFullName = name,
                PackageIdentity = identity,
                MainPackage = element.Attribute("MainPackage").Value == "true"
            });

            var modified = Convert.ToDateTime(file.Attribute("Modified").Value);
            if (dictionary[key].Modified < modified)
            {
                dictionary[key].Id = parent.Element("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}ID").Value;
                dictionary[key].Modified = modified;
            }
        }

        if (dictionary.Count == 0) return [];
        var values = dictionary.Where(_ => _.Value.MainPackage).Select(_ => _.Value);
        var architecture = (values.FirstOrDefault(_ => _.Architecture == native.Architecture) ?? values.FirstOrDefault(_ => _.Architecture == compatible.Architecture)).Architecture;
        return dictionary.Where(_ => _.Value.Architecture == architecture).Select(_ => _.Value).Verify(updates).Urls();
    }

    static List<Update> Verify(this IEnumerable<Identity> source, XElement updates)
    {
        List<Update> list = [];

        foreach (var element in updates.Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}SecuredFragment"))
        {
            var parent = element.Parent.Parent.Parent;
            var item = source.FirstOrDefault(_ => _.Id == parent.Element("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}ID").Value);
            if (item is null) continue;

            var blob = Parse(parent.Descendants("{http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService}ApplicabilityBlob").First().Value);
            if (item.MainPackage && ((ulong.Parse(blob.Descendants("platform.minVersion").First().Value) >> 16) & 0xFFFF) > build) return [];

            var package = PackageManager.FindPackagesForUser(string.Empty, $"{item.PackageIdentity[0]}_{item.PackageIdentity[4]}").FirstOrDefault(_ => _.Id.Architecture == item.Architecture || item.MainPackage);
            if (package is null || (package.SignatureKind == PackageSignatureKind.Store &&
                new Version((blob.Element("content.bundledPackages")?.Elements().Select(_ => _.Value.Split('_')).FirstOrDefault(_ => _[2] == item.Platform) ?? blob.Element("content.packageId").Value.Split('_'))[1])
                > new Version(package.Id.Version.Major, package.Id.Version.Minor, package.Id.Version.Build, package.Id.Version.Revision)))
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

        list.Sort((x, y) => x.MainPackage ? 1 : -1); return list;
    }

    static XElement Parse(string value) { using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.Unicode.GetBytes(value), XmlDictionaryReaderQuotas.Max); return XElement.Load(reader); }

    static XElement Get(string address)
    {
        using var stream = client.OpenRead(address);
        using var reader = JsonReaderWriterFactory.CreateJsonReader(stream, XmlDictionaryReaderQuotas.Max);
        return XElement.Load(reader);
    }

    static XElement Post(string data, bool secured = false, bool decode = false)
    {
        client.Headers["Content-Type"] = "application/soap+xml";
        var value = client.UploadString(secured ? "secured" : string.Empty, data);
        return XElement.Parse(decode ? WebUtility.HtmlDecode(value) : value);
    }
}