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

sealed class Package
{
    internal string FullName;
    internal string Name;
    internal int Rank;
    internal bool Main;
    internal string Id;
    internal string Revision;
    internal Version Version;
}

static class Store
{
    internal static readonly PackageManager Manager = new();

    static (string SyncUpdates, string GetExtendedUpdateInfo2) _ = (default, Resources.Get<string>("GetExtendedUpdateInfo2.xml.gz"));

    static readonly (string String, ProcessorArchitecture Architecture) platform = (
        RuntimeInformation.OSArchitecture == Architecture.X64 ? "x64" : default,
        RuntimeInformation.OSArchitecture == Architecture.X64 ? ProcessorArchitecture.X64 : ProcessorArchitecture.Unknown
    );

    static readonly ulong build = (GetVersion() >> 16) & 0xFFFF;

    static readonly string storeedgefd = $"https://storeedgefd.dsx.mp.microsoft.com/v7.0/products/{{0}}?market={GlobalizationPreferences.HomeGeographicRegion}&locale=iv&deviceFamily=Windows.Desktop";

    static readonly string displaycatalog = $"https://displaycatalog.mp.microsoft.com/v6.0/products/{{0}}?languages=iv&market={GlobalizationPreferences.HomeGeographicRegion}";

    static readonly WebClient client = new() { BaseAddress = "https://fe3cr.delivery.mp.microsoft.com/ClientWebService/client.asmx/" };

    internal static IEnumerable<string[]> Products(params string[] ids) => ids.Select<string, (string AppCategoryId, string Id)>(_ =>
    {
        var payload = Get(string.Format(storeedgefd, _)).Element("Payload");
        var platforms = payload.Element("Platforms").Descendants().Select(_ => _.Value);
        return platforms.Any(_ => _ == platform.String) ? new()
        {
            AppCategoryId = Parse(payload.LocalDescendant("FulfillmentData").Value).Element("WuCategoryId").Value,
            Id = _,
        } : default;
    }).Where(_ => _.AppCategoryId is not null).Select(_ => _.Get());

    static string[] Get(this (string AppCategoryId, string Id) source)
    {
        var root = Post(string.Format(_.SyncUpdates ??= string.Format(Resources.Get<string>("SyncUpdates.xml.gz"), Post(Resources.Get<string>("GetCookie.xml.gz"))
        .LocalDescendant("EncryptedData").Value, "{0}"), source.AppCategoryId), decode: true)
        .LocalDescendant("SyncUpdatesResult");

        var dictionary = root.LocalDescendants("AppxPackageInstallData").Where(_ =>
        {
            var attribute = _.Attribute("PackageFileName").Value;
            return attribute[attribute.LastIndexOf('.') + 1] != 'e';
        }).ToDictionary(_ => _.Parent.Parent.Parent.LocalElement("ID").Value, _ => _.Attribute("MainPackage").Value == "true");

        Dictionary<string, Package> packages = [];

        foreach (var element in root.LocalDescendants("UpdateInfo"))
        {
            if (!dictionary.TryGetValue(element.LocalElement("ID").Value, out var main)) continue;

            var moniker = element.LocalDescendant("AppxMetadata").Attribute("PackageMoniker").Value;
            var substrings = moniker.Split('_');

            var @string = substrings[2];
            if (@string != "neutral" && @string != platform.String) continue;

            var identity = element.LocalDescendant("UpdateIdentity");
            var id = identity.Attribute("UpdateID").Value;
            var revision = identity.Attribute("RevisionNumber").Value;
            var rank = int.Parse(element.LocalDescendant("Properties").Attribute("PackageRank").Value);

            var blob = Parse(element.LocalDescendant("ApplicabilityBlob").Value);
            if (((ulong.Parse(blob.LocalDescendant("platform.minVersion").Value) >> 16) & 0xFFFF) > build) continue;
            Version _() => new((
                blob.Element("content.bundledPackages")?.Elements().Select(_ => _.Value.Split('_')).FirstOrDefault(_ => _[2] == platform.String)
                ??
                blob.Element("content.packageId").Value.Split('_')
            )[1]);

            var key = $"{substrings[0]}_{substrings[4]}";
            if (!packages.TryGetValue(key, out var value)) packages.Add(key, new()
            {
                FullName = moniker,
                Name = substrings[0],
                Rank = rank,
                Main = main,
                Id = id,
                Revision = revision,
                Version = _()
            });
            else if (value.Rank < rank)
            {
                value.FullName = moniker;
                value.Rank = rank;
                value.Id = id;
                value.Revision = revision;
                value.Version = _();
            }
        }

        return packages.Get(source.Id);
    }

    static string[] Get(this Dictionary<string, Package> source, string id)
    {
        var main = source.FirstOrDefault(_ => _.Value.Main); if (main.Value is null) return [];
        var set = Get(string.Format(displaycatalog, id))
        .Descendants("FrameworkDependencies")
        .FirstOrDefault(_ => _.Parent.Element("PackageFullName").Value == main.Value.FullName)?
        .Descendants("PackageIdentity")
        .Select(_ => _.Value).ToHashSet();

        List<Package> list = [];

        foreach (var item in source.Where(_ => _.Value.Main || (set?.Contains(_.Value.Name) ?? true)))
        {
            var package = Manager.FindPackagesForUser(string.Empty, item.Key).FirstOrDefault(_ => _.Id.Architecture == platform.Architecture || item.Value.Main);
            if (package is null || (package.SignatureKind == PackageSignatureKind.Store &&
                item.Value.Version > new Version(package.Id.Version.Major, package.Id.Version.Minor, package.Id.Version.Build, package.Id.Version.Revision))) list.Add(item.Value);
            else if (item.Value.Main) return [];
        }

        list.Sort((x, y) => x.Main ? 1 : -1); return list
        .Select(_ => Post(string.Format(Store._.GetExtendedUpdateInfo2, _.Id, _.Revision), true)
        .LocalDescendants("Url")
        .First(_ => _.Value.StartsWith("http://tlu.dl.delivery.mp.microsoft.com", StringComparison.Ordinal)).Value)
        .ToArray();
    }

    static XElement LocalElement(this XElement source, string name) => source.Elements().Where(_ => _.Name.LocalName == name).FirstOrDefault();

    static IEnumerable<XElement> LocalDescendants(this XElement source, string name) => source.Descendants().Where(_ => _.Name.LocalName == name);

    static XElement LocalDescendant(this XElement source, string name) => source.LocalDescendants(name).FirstOrDefault();

    static XElement Parse(string value)
    {
        using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.Unicode.GetBytes(value), XmlDictionaryReaderQuotas.Max);
        return XElement.Load(reader);
    }

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