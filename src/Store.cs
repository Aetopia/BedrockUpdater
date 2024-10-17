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
using System.Runtime.Serialization.Json;

sealed class Package
{
    internal string Name;
    internal string Identity;
    internal int Rank;
    internal bool Main;
    internal string Id;
    internal string Revision;
    internal Version Version;
}

static class Store
{
    internal static readonly PackageManager PackageManager = new();

    static (string SyncUpdates, string GetExtendedUpdateInfo2) _ = (default, Resources.Get<string>("GetExtendedUpdateInfo2.xml.gz"));

    static readonly string address = $"https://displaycatalog.mp.microsoft.com/v7.0/products/{{0}}?languages=iv&market={GlobalizationPreferences.HomeGeographicRegion}";

    static readonly WebClient client = new() { BaseAddress = "https://fe3cr.delivery.mp.microsoft.com/ClientWebService/client.asmx/" };

    static Version Version(XElement element)
    {
        using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.Unicode.GetBytes(element.LocalDescendants("ApplicabilityBlob").First().Value), XmlDictionaryReaderQuotas.Max);
        var json = XElement.Load(reader); return new((
            json.Element("content.bundledPackages")?.Elements().Select(_ => _.Value.Split('_')).FirstOrDefault(_ => _[2] is "x64")
            ??
            json.Element("content.packageId").Value.Split('_'))
        [1]);
    }

    internal static IEnumerable<Lazy<Uri>[]> Get(params string[] source) => source.Select<string, (string AppCategoryId, Dictionary<int, HashSet<string>> Frameworks)>(_ =>
    {
        using var stream = client.OpenRead(string.Format(address, _)); using var reader = JsonReaderWriterFactory.CreateJsonReader(stream, XmlDictionaryReaderQuotas.Max); var json = XElement.Load(reader);
        return new()
        {
            AppCategoryId = json.LocalDescendants("WuCategoryId").First().Value,
            Frameworks = json.Descendants("Packages").First().Descendants("FrameworkDependencies")
            .ToDictionary(_ => int.Parse(_.Parent.Element("PackageRank").Value), _ => _.Descendants("PackageIdentity").Select(_ => _.Value).ToHashSet())
        };
    }).Select(_ => _.Get());

    static Lazy<Uri>[] Get(this (string AppCategoryId, Dictionary<int, HashSet<string>> Frameworks) source)
    {
        var root = Post(string.Format(_.SyncUpdates ??= string.Format(Resources.Get<string>("SyncUpdates.xml.gz"), Post(Resources.Get<string>("GetCookie.xml.gz"))
        .LocalDescendants("EncryptedData").First().Value, "{0}"), source.AppCategoryId), decode: true).LocalDescendants("SyncUpdatesResult").First();

        var dictionary = root.LocalDescendants("AppxPackageInstallData").Where(_ =>
        {
            var attribute = _.Attribute("PackageFileName").Value; return attribute[attribute.LastIndexOf('.') + 1] is not 'e';
        }).ToDictionary(_ => _.Parent.Parent.Parent.LocalElement("ID").Value, _ => _.Attribute("MainPackage").Value is "true");

        Dictionary<string, Package> packages = [];
        foreach (var element in root.LocalDescendants("UpdateInfo"))
        {
            if (!dictionary.TryGetValue(element.LocalElement("ID").Value, out var main)) continue;

            var name = element.LocalDescendants("AppxMetadata").First().Attribute("PackageMoniker").Value; var substrings = name.Split('_');
            var @string = substrings[2]; if (@string is not "neutral" && @string is not "x64") continue;
            var identity = element.LocalDescendants("UpdateIdentity").First();
            var id = identity.Attribute("UpdateID").Value; var revision = identity.Attribute("RevisionNumber").Value;
            var rank = int.Parse(element.LocalDescendants("Properties").First().Attribute("PackageRank").Value);

            var key = $"{substrings[0]}_{substrings[4]}";
            if (!packages.TryGetValue(key, out var value)) packages.Add(key, new() { Name = name, Identity = substrings[0], Rank = rank, Main = main, Id = id, Revision = revision, Version = Version(element) });
            else if (value.Rank < rank) { value.Name = name; value.Rank = rank; value.Id = id; value.Revision = revision; value.Version = Version(element); }
        }
        return packages.Get(source.Frameworks);
    }

    static Lazy<Uri>[] Get(this Dictionary<string, Package> source, Dictionary<int, HashSet<string>> frameworks)
    {
        var main = source.FirstOrDefault(_ => _.Value.Main); frameworks.TryGetValue(main.Value.Rank, out var set);

        List<Package> list = [];
        foreach (var item in source.Where(_ => _.Value.Main || (set?.Contains(_.Value.Identity) ?? true)))
        {
            var package = PackageManager.FindPackagesForUser(string.Empty, item.Key).FirstOrDefault(_ => _.Id.Architecture is ProcessorArchitecture.X64 || item.Value.Main);
            if (package is null || (package.SignatureKind is PackageSignatureKind.Store &&
                item.Value.Version > new Version(package.Id.Version.Major, package.Id.Version.Minor, package.Id.Version.Build, package.Id.Version.Revision))) list.Add(item.Value);
            else if (item.Value.Main) return [];
        }
        return list.OrderBy(_ => _.Main).Select(_ => new Lazy<Uri>(() => new(Post(string.Format(Store._.GetExtendedUpdateInfo2, _.Id, _.Revision), true)
        .LocalDescendants("Url").First(_ => _.Value.StartsWith("http://tlu.dl.delivery.mp.microsoft.com", StringComparison.Ordinal)).Value))).ToArray();
    }

    static XElement LocalElement(this XElement source, string name) => source.Elements().Where(_ => _.Name.LocalName == name).FirstOrDefault();

    static IEnumerable<XElement> LocalDescendants(this XElement source, string name) => source.Descendants().Where(_ => _.Name.LocalName == name);

    static XElement Post(string data, bool secured = false, bool decode = false)
    {
        client.Headers["Content-Type"] = "application/soap+xml";
        var value = client.UploadString(secured ? "secured" : string.Empty, data);
        return XElement.Parse(decode ? WebUtility.HtmlDecode(value) : value);
    }
}