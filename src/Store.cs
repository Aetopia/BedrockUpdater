using System;
using System.Net;
using System.Xml;
using System.Linq;
using System.Text;
using Windows.System;
using Windows.System.UserProfile;
using System.Collections.Generic;
using Windows.Management.Deployment;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;

struct Product
{
    internal string Title;

    internal string AppCategoryId;

    internal string Architecture;
}

struct UpdateIdentity
{
    internal string UpdateId;

    internal string RevisionNumber;

    internal bool MainPackage;
}

class Update
{
    internal string Id;

    internal DateTime Modified;

    internal ProcessorArchitecture Architecture;

    internal string PackageFamilyName;

    internal string Version;

    internal bool MainPackage;
}

static class Store
{
    internal static readonly PackageManager PackageManager = new();

    static readonly string address = $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{{0}}?market={GlobalizationPreferences.HomeGeographicRegion}&locale=iv&deviceFamily=Windows.Desktop";

    static readonly WebClient client = new() { BaseAddress = "https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx/" };

    static readonly (string, string) runtime = (
        RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
        RuntimeInformation.OSArchitecture switch { Architecture.X64 => "x86", Architecture.Arm64 => "arm", _ => null }
    );

    static readonly (ProcessorArchitecture, ProcessorArchitecture) processor = (
        GetArchitecture(runtime.Item1),
        GetArchitecture(runtime.Item2)
    );

    static string data;

    internal static IEnumerable<Product> GetProducts(params string[] productIds)
    {
        ICollection<Product> products = [];

        foreach (var productId in productIds)
        {
            var payload = Deserialize(client.DownloadString(string.Format(address, productId)))["Payload"];
            var title = payload?["ShortTitle"]?.InnerText;
            var enumerable = payload["Platforms"].Cast<XmlNode>().Select(node => node.InnerText);

            products.Add(new Product()
            {
                Title = string.IsNullOrEmpty(title) ? payload["Title"].InnerText : title,
                AppCategoryId = Deserialize(payload.GetElementsByTagName("FulfillmentData")[0].InnerText)["WuCategoryId"].InnerText,
                Architecture = (enumerable.FirstOrDefault(item => item.Equals(runtime.Item1, StringComparison.OrdinalIgnoreCase)) ??
                enumerable.FirstOrDefault(item => item.Equals(runtime.Item2, StringComparison.OrdinalIgnoreCase)))?.ToLowerInvariant()
            });
        }

        return products;
    }

    internal static string GetUrl(UpdateIdentity update)
    {
        return UploadString(string.Format(Resources.GetExtendedUpdateInfo2, update.UpdateId, update.RevisionNumber), true)
        .GetElementsByTagName("Url")
        .Cast<XmlNode>()
        .First(node => node.InnerText.StartsWith("http://tlu.dl.delivery.mp.microsoft.com", StringComparison.Ordinal)).InnerText;
    }

    internal static ReadOnlyCollection<UpdateIdentity> GetUpdates(Product product)
    {
        if (product.Architecture is null) return null;
        var result = (XmlElement)UploadString(
            string.Format(
                data ??= string.Format(Resources.LoadString("SyncUpdates.xml"), UploadString(Resources.LoadString("GetCookie.xml")).GetElementsByTagName("EncryptedData")[0].InnerText, "{0}"),
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
            if (!neutral && identity[2] != runtime.Item1 && identity[2] != runtime.Item2) continue;
            if ((architecture = (neutral ? product.Architecture : identity[2]) switch
            {
                "x86" => ProcessorArchitecture.X86,
                "x64" => ProcessorArchitecture.X64,
                "arm" => ProcessorArchitecture.Arm,
                "arm64" => ProcessorArchitecture.Arm64,
                _ => ProcessorArchitecture.Unknown
            }) == ProcessorArchitecture.Unknown) return null;

            var key = $"{identity[0]}_{identity[2]}";
            if (!dictionary.ContainsKey(key)) dictionary.Add(key, new()
            {
                Architecture = architecture,
                PackageFamilyName = $"{identity[0]}_{identity[4]}",
                Version = identity[1],
                MainPackage = node.Attributes["MainPackage"].InnerText == "true"
            });

            var modified = Convert.ToDateTime(file.Attributes["Modified"].InnerText);
            if (dictionary[key].Modified < modified)
            {
                dictionary[key].Id = element["ID"].InnerText;
                dictionary[key].Modified = modified;
            }
        }

        var packages = dictionary.Where(item => item.Value.MainPackage).Select(item => item.Value);
        architecture = (packages.FirstOrDefault(item => item.Architecture == processor.Item1) ?? packages.FirstOrDefault(item => item.Architecture == processor.Item2)).Architecture;
        var items = dictionary.Select(item => item.Value).Where(item => item.Architecture == architecture);
        List<UpdateIdentity> updates = [];

        foreach (XmlNode node in result.GetElementsByTagName("SecuredFragment"))
        {
            var element = (XmlElement)node.ParentNode.ParentNode.ParentNode;
            var item = items.FirstOrDefault(item => item.Id == element["ID"].InnerText);
            if (item is null) continue;

            var package = PackageManager.FindPackagesForUser(string.Empty, item.PackageFamilyName).FirstOrDefault(package => package.Id.Architecture == item.Architecture);
            if (package is null || (!package.IsDevelopmentMode && new Version(item.Version) > new Version(package.Id.Version.Major, package.Id.Version.Minor, package.Id.Version.Build, package.Id.Version.Revision)))
            {
                var attributes = element.GetElementsByTagName("UpdateIdentity")[0].Attributes;
                updates.Add(new() { UpdateId = attributes["UpdateID"].InnerText, RevisionNumber = attributes["RevisionNumber"].InnerText, MainPackage = item.MainPackage });
            }
            else if (item.MainPackage) return null;
        }

        updates.Sort((x, y) => x.MainPackage ? 1 : -1);
        return updates.AsReadOnly();
    }

    static ProcessorArchitecture GetArchitecture(string architecture) => architecture switch
    {
        "x86" => ProcessorArchitecture.X86,
        "x64" => ProcessorArchitecture.X64,
        "arm" => ProcessorArchitecture.Arm,
        "arm64" => ProcessorArchitecture.Arm64,
        _ => ProcessorArchitecture.Unknown
    };

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