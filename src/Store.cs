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

sealed class Package
{
    internal string Name;
    internal int Rank;
    internal bool Main;
    internal string[] Identity;
    internal (string String, ProcessorArchitecture Architecture) Platform;
    internal string Id;
    internal string Revision;
    internal string Blob;
}

static class Store
{
    internal static readonly PackageManager Manager = new();

    static string data;

    static readonly ulong build = (GetVersion() >> 16) & 0xFFFF;

    static readonly string storeedgefd = $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{{0}}?market={GlobalizationPreferences.HomeGeographicRegion}&locale=iv&deviceFamily=Windows.Desktop";

    static readonly string displaycatalog = $"https://displaycatalog.mp.microsoft.com/v7.0/products/{{0}}?languages=iv&market={GlobalizationPreferences.HomeGeographicRegion}";

    static readonly (string String, ProcessorArchitecture Architecture) native = (
    RuntimeInformation.OSArchitecture switch
    {
        Architecture.X86 => "x86",
        Architecture.X64 => "x64",
        _ => default
    },
    RuntimeInformation.OSArchitecture switch
    {
        Architecture.X86 => ProcessorArchitecture.X86,
        Architecture.X64 => ProcessorArchitecture.X64,
        _ => ProcessorArchitecture.Unknown
    });

    static readonly (string String, ProcessorArchitecture Architecture) compatible = (
    RuntimeInformation.OSArchitecture == Architecture.X64 ? "x86" : default,
    RuntimeInformation.OSArchitecture == Architecture.X64 ? ProcessorArchitecture.X86 : ProcessorArchitecture.Unknown
    );

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

    static string[] Urls(this IEnumerable<Package> source) => source.Select(_ => Post(string.Format(Resources.GetExtendedUpdateInfo2, _.Id, _.Revision), true)
    .LocalDescendants("Url").First(_ => _.Value.StartsWith("http://tlu.dl.delivery.mp.microsoft.com", StringComparison.Ordinal)).Value).ToArray();

    static XElement Sync(this Product source) => Post(string.Format(data ??= string.Format(Resources.GetString("SyncUpdates.xml.gz"),
    Post(Resources.GetString("GetCookie.xml.gz")).LocalDescendant("EncryptedData").Value, "{0}"), source.AppCategoryId), decode: true)
    .LocalDescendant("SyncUpdatesResult");

    static string[] Get(this Product source)
    {
        var root = source.Sync();
        var dictionary = root.LocalDescendants("AppxPackageInstallData").Where(_ =>
        {
            var attribute = _.Attribute("PackageFileName").Value;
            return attribute[attribute.LastIndexOf('.') + 1] != 'e';
        }).ToDictionary(_ => _.Parent.Parent.Parent.LocalElement("ID").Value, _ => _.Attribute("MainPackage").Value == "true");

        Dictionary<string, Package> packages = [];

        foreach (var element in root.LocalDescendants("UpdateInfo"))
        {
            if (!dictionary.TryGetValue(element.LocalElement("ID").Value, out var main)) continue;

            var name = element.LocalDescendant("AppxMetadata").Attribute("PackageMoniker").Value;
            var identity = name.Split('_');

            var architecture = identity[2];
            var neutral = architecture == "neutral";
            if (!neutral && architecture != native.String && architecture != compatible.String) continue;

            var update = element.LocalDescendant("UpdateIdentity");
            var id = update.Attribute("UpdateID").Value;
            var revision = update.Attribute("RevisionNumber").Value;
            var rank = int.Parse(element.LocalDescendant("Properties").Attribute("PackageRank").Value);
            var blob = element.LocalDescendant("ApplicabilityBlob").Value;

            var key = identity[0] + architecture;
            if (!packages.ContainsKey(key))
            {
                packages.Add(key, new()
                {
                    Name = name,
                    Identity = identity,
                    Rank = rank,
                    Main = main,
                    Id = id,
                    Revision = revision,
                    Platform = (neutral ? source.Architecture : architecture) == native.String ? native : compatible,
                    Blob = blob
                });
                continue;
            }

            var package = packages[key];
            if (package.Rank < rank)
            {
                package.Name = name;
                package.Identity = identity;
                package.Rank = rank;
                package.Id = id;
                package.Revision = revision;
                package.Blob = blob;
            }
        }

        return packages.Values.Filter(source.Id).Urls();
    }

    static IEnumerable<Package> Filter(this IEnumerable<Package> source, string id)
    {
        var items = source.Where(_ => _.Main);
        var main = items.FirstOrDefault(_ => _.Platform.Architecture == native.Architecture) ?? items.FirstOrDefault(_ => _.Platform.Architecture == compatible.Architecture);
        var architecture = main.Platform.Architecture;

        var set = Get(string.Format(displaycatalog, id))
        .Descendants("FrameworkDependencies")
        .FirstOrDefault(_ => _.Parent.Element("PackageFullName").Value == main.Name)?
        .Descendants("PackageIdentity")
        .Select(_ => _.Value).ToHashSet();

        List<Package> list = [];

        foreach (var item in source.Where(_ => _.Platform.Architecture == architecture && (_.Main || (set?.Contains(_.Identity[0]) ?? true))))
        {
            var blob = Parse(item.Blob);

            if (item.Main && ((ulong.Parse(blob.Descendants("platform.minVersion").First().Value) >> 16) & 0xFFFF) > build) return [];

            var package = Manager.FindPackagesForUser(string.Empty, $"{item.Identity[0]}_{item.Identity[4]}").FirstOrDefault(_ => _.Id.Architecture == item.Platform.Architecture || item.Main);
            if (package is null || (package.SignatureKind == PackageSignatureKind.Store &&
                new Version((
                    blob.Element("content.bundledPackages")?.Elements().Select(_ => _.Value.Split('_')).FirstOrDefault(_ => _[2] == item.Platform.String)
                    ??
                    blob.Element("content.packageId").Value.Split('_')
                )[1])
                > new Version(package.Id.Version.Major, package.Id.Version.Minor, package.Id.Version.Build, package.Id.Version.Revision))) list.Add(item);
            else if (item.Main) return [];
        }

        list.Sort((x, y) => x.Main ? 1 : -1); return list;
    }

    static XElement LocalElement(this XElement source, string name) => source.Elements().Where(_ => _.Name.LocalName == name).FirstOrDefault();

    static IEnumerable<XElement> LocalDescendants(this XElement source, string name) => source.Descendants().Where(_ => _.Name.LocalName == name);

    static XElement LocalDescendant(this XElement source, string name) => source.LocalDescendants(name).FirstOrDefault();

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

    [DllImport("Kernel32"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern ulong GetVersion();
}