using System;
using System.Net;
using System.Xml;
using System.Linq;
using System.Text;
using Windows.System.UserProfile;
using System.Collections.Generic;
using Windows.Management.Deployment;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using Windows.System;

interface IUpdate { Tuple<string, string> Identity { get; } bool MainPackage { get; } }

interface IProduct { string Title { get; } string AppCategoryId { get; } }

file class Product(string title, string appCategoryId, string architecture) : IProduct
{
    public string Title => title;

    public string AppCategoryId => appCategoryId;

    internal readonly string Architecture = architecture;
}

file class Update(ProcessorArchitecture architecture, string packageFamilyName, string version, bool mainPackage) : IUpdate
{
    Tuple<string, string> identity;

    internal string Id;

    internal DateTime Modified;

    internal readonly ProcessorArchitecture Architecture = architecture;

    internal readonly string PackageFamilyName = packageFamilyName;

    internal readonly string Version = version;

    public Tuple<string, string> Identity { get { return identity; } set { identity = value; } }

    public bool MainPackage => mainPackage;
}

static class Store
{
    internal static readonly PackageManager PackageManager = new();

    static readonly string address = $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{{0}}?market={GlobalizationPreferences.HomeGeographicRegion}&locale=iv&deviceFamily=Windows.Desktop";

    static readonly WebClient client = new() { BaseAddress = "https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx/" };

    static readonly Tuple<string, string> architectures = new(
        RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
        RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x86",
            Architecture.Arm64 => "arm64",
            _ => null
        }
     );

    static string data;

    internal static IEnumerable<IProduct> GetProducts(params string[] productIds)
    {
        ICollection<IProduct> products = [];
        foreach (var productId in productIds)
        {
            var payload = Deserialize(client.DownloadString(string.Format(address, productId)))["Payload"];
            var title = payload?["ShortTitle"]?.InnerText;
            var enumerable = payload["Platforms"].Cast<XmlNode>().Select(node => node.InnerText);

            products.Add(new Product(
                string.IsNullOrEmpty(title) ? payload["Title"].InnerText : title,
                Deserialize(payload.GetElementsByTagName("FulfillmentData")[0].InnerText)["WuCategoryId"].InnerText,
                (enumerable.FirstOrDefault(item => item.Equals(architectures.Item1, StringComparison.OrdinalIgnoreCase)) ??
                enumerable.FirstOrDefault(item => item.Equals(architectures.Item2, StringComparison.OrdinalIgnoreCase)))?.ToLowerInvariant()
            ));
        }

        return products;
    }

    internal static string GetUrl(IUpdate update)
    {
        return UploadString(string.Format(Resources.GetExtendedUpdateInfo2, update.Identity.Item1, update.Identity.Item2), true)
        .GetElementsByTagName("Url")
        .Cast<XmlNode>()
        .First(node => node.InnerText.StartsWith("http://tlu.dl.delivery.mp.microsoft.com", StringComparison.Ordinal)).InnerText;
    }

    internal static ReadOnlyCollection<IUpdate> GetUpdates(IProduct product)
    {
        List<IUpdate> updates = [];
        var obj = (Product)product;
        if (obj.Architecture is null) goto _;

        var result = (XmlElement)UploadString(
            string.Format(
                data ??= Resources.LoadString("SyncUpdates.xml").Replace("_", UploadString(Resources.LoadString("GetCookie.xml")).GetElementsByTagName("EncryptedData")[0].InnerText),
                product.AppCategoryId))
            .GetElementsByTagName("SyncUpdatesResult")[0];

        ProcessorArchitecture architecture;
        Dictionary<string, Update> dictionary = [];
        foreach (XmlNode node in result.GetElementsByTagName("AppxPackageInstallData"))
        {
            var element = (XmlElement)node.ParentNode.ParentNode.ParentNode;
            var file = element.GetElementsByTagName("File")[0];

            var identity = file.Attributes["InstallerSpecificIdentifier"].InnerText.Split('_');
            var neutral = identity[2] == "neutral";
            if (!neutral && identity[2] != architectures.Item1 && identity[2] != architectures.Item2) continue;
            architecture = (neutral ? obj.Architecture : identity[2]) switch
            {
                "x64" => ProcessorArchitecture.X64,
                "arm" => ProcessorArchitecture.Arm,
                "arm64" => ProcessorArchitecture.Arm64,
                _ => ProcessorArchitecture.X86
            };

            var key = $"{identity[0]}_{identity[2]}";
            if (!dictionary.ContainsKey(key)) dictionary.Add(key, new(architecture, $"{identity[0]}_{identity[4]}", identity[1], node.Attributes["MainPackage"].InnerText == "true"));

            var modified = Convert.ToDateTime(file.Attributes["Modified"].InnerText);
            if (dictionary[key].Modified < modified)
            {
                dictionary[key].Id = element["ID"].InnerText;
                dictionary[key].Modified = modified;
            }
        }

        architecture = dictionary.First(item => item.Value.MainPackage).Value.Architecture;
        var items = dictionary.Select(item => item.Value).Where(item => item.Architecture == architecture);

        foreach (XmlNode node in result.GetElementsByTagName("SecuredFragment"))
        {
            var element = (XmlElement)node.ParentNode.ParentNode.ParentNode;
            var update = items.FirstOrDefault(item => item.Id == element["ID"].InnerText);
            if (update is null) continue;

            var package = PackageManager.FindPackagesForUser(string.Empty, update.PackageFamilyName).FirstOrDefault(package => package.Id.Architecture == update.Architecture);

            if (package is null || (!package.IsDevelopmentMode && new Version(update.Version) > new Version(package.Id.Version.Major, package.Id.Version.Minor, package.Id.Version.Build, package.Id.Version.Revision)))
            {
                var attributes = element.GetElementsByTagName("UpdateIdentity")[0].Attributes;
                update.Identity = new(attributes["UpdateID"].InnerText, attributes["RevisionNumber"].InnerText);
                updates.Add(update);
            }
            else if (update.MainPackage) goto _;
        }

    _: updates.Sort((x, y) => x.MainPackage ? 1 : -1); return updates.AsReadOnly();
    }

    static XmlElement Deserialize(string input)
    {
        using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(input), XmlDictionaryReaderQuotas.Max);
        XmlDocument document = new();
        document.Load(reader);
        return document["root"];
    }

    static XmlDocument UploadString(string data, bool secured = false)
    {
        client.Headers["Content-Type"] = "application/soap+xml";
        XmlDocument document = new();
        document.LoadXml(client.UploadString(secured ? "secured" : string.Empty, data).Replace("&lt;", "<").Replace("&gt;", ">"));
        return document;
    }
}