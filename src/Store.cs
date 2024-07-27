using System;
using System.IO;
using System.Net;
using System.Xml;
using System.Linq;
using System.Text;
using Windows.System;
using Windows.System.UserProfile;
using System.Collections.Generic;
using Windows.Management.Deployment;
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
    internal string UpdateID;

    internal string RevisionNumber;

    internal bool MainPackage;
}

class Update
{
    internal string ID;

    internal DateTime Modified;

    internal ProcessorArchitecture Architecture;

    internal string PackageFamilyName;

    internal string Version;

    internal bool MainPackage;
}

static class Store
{
    static string data;

    internal static readonly PackageManager PackageManager = new();

    static readonly string address = $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{{0}}?market={GlobalizationPreferences.HomeGeographicRegion}&locale=iv&deviceFamily=Windows.Desktop";

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

    internal static Product[] GetProducts(params string[] productIds)
    {
        var products = new Product[productIds.Length];

        for (int index = 0; index < productIds.Length; index++)
        {
            var payload = Deserialize(client.DownloadData(string.Format(address, productIds[index])))["Payload"];
            var title = payload?["ShortTitle"]?.InnerText;
            var platforms = payload["Platforms"].Cast<XmlNode>().Select(node => node.InnerText);

            products[index] = new()
            {
                Title = string.IsNullOrEmpty(title) ? payload["Title"].InnerText : title,
                AppCategoryId = Deserialize(Encoding.Unicode.GetBytes(payload.GetElementsByTagName("FulfillmentData")[0].InnerText))["WuCategoryId"].InnerText,
                Architecture = (
                    platforms.FirstOrDefault(item => item.Equals(architectures.Native.String, StringComparison.OrdinalIgnoreCase)) ??
                    platforms.FirstOrDefault(item => item.Equals(architectures.Compatible.String, StringComparison.OrdinalIgnoreCase))
                )?.ToLowerInvariant()
            };
        }

        return products;
    }

    internal static string GetUrl(UpdateIdentity update) =>
    UploadString(string.Format(Resources.GetExtendedUpdateInfo2, update.UpdateID, update.RevisionNumber), true)
    .GetElementsByTagName("Url")
    .Cast<XmlNode>()
    .First(node => node.InnerText.StartsWith("http://tlu.dl.delivery.mp.microsoft.com", StringComparison.Ordinal)).InnerText;

    internal static List<UpdateIdentity> GetUpdates(Product product)
    {
        if (product.Architecture is null) return [];
        var result = (XmlElement)UploadString(
            string.Format(
                data ??= string.Format(Resources.GetString("SyncUpdates.xml.gz"), UploadString(Resources.GetString("GetCookie.xml.gz")).GetElementsByTagName("EncryptedData")[0].InnerText, "{0}"),
                product.AppCategoryId), false)
            .GetElementsByTagName("SyncUpdatesResult")[0];

        var nodes = result.GetElementsByTagName("AppxPackageInstallData");
        if (nodes.Count == 0) return [];

        ProcessorArchitecture architecture;
        Dictionary<string, Update> dictionary = [];

        foreach (XmlNode node in nodes)
        {
            var element = (XmlElement)node.ParentNode.ParentNode.ParentNode;
            var file = element.GetElementsByTagName("File")[0];
            if (Path.GetExtension(file.Attributes["FileName"].InnerText).StartsWith(".e", StringComparison.OrdinalIgnoreCase)) continue;

            var identity = file.Attributes["InstallerSpecificIdentifier"].InnerText.Split('_');
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
                PackageFamilyName = $"{identity[0]}_{identity[4]}",
                Version = identity[1],
                MainPackage = node.Attributes["MainPackage"].InnerText == "true"
            });

            var modified = Convert.ToDateTime(file.Attributes["Modified"].InnerText);
            if (dictionary[key].Modified < modified)
            {
                dictionary[key].ID = element["ID"].InnerText;
                dictionary[key].Modified = modified;
            }
        }

        var values = dictionary.Where(item => item.Value.MainPackage).Select(item => item.Value);
        architecture = (
            values.FirstOrDefault(value => value.Architecture == architectures.Native.Architecture) ??
            values.FirstOrDefault(value => value.Architecture == architectures.Compatible.Architecture)
        ).Architecture;
        var items = dictionary.Select(item => item.Value).Where(item => item.Architecture == architecture);

        List<UpdateIdentity> list = [];

        foreach (XmlNode node in result.GetElementsByTagName("SecuredFragment"))
        {
            var element = (XmlElement)node.ParentNode.ParentNode.ParentNode;
            var item = items.FirstOrDefault(item => item.ID == element["ID"].InnerText);
            if (item is null) continue;

            var packages = PackageManager.FindPackagesForUser(string.Empty, item.PackageFamilyName);
            var package = item.MainPackage ? packages.SingleOrDefault() : packages.FirstOrDefault(package => package.Id.Architecture == item.Architecture);
            if (package is null || (!package.IsDevelopmentMode &&
                new Version(item.Version) > new Version(package.Id.Version.Major, package.Id.Version.Minor, package.Id.Version.Build, package.Id.Version.Revision)))
            {
                var attributes = element.GetElementsByTagName("UpdateIdentity")[0].Attributes;
                list.Add(new()
                {
                    UpdateID = attributes["UpdateID"].InnerText,
                    RevisionNumber = attributes["RevisionNumber"].InnerText,
                    MainPackage = item.MainPackage
                });
            }
            else if (item.MainPackage) return [];
        }

        list.Sort((x, y) => x.MainPackage ? 1 : -1);
        return list;
    }

    static XmlElement Deserialize(byte[] buffer)
    {
        using var reader = JsonReaderWriterFactory.CreateJsonReader(buffer, XmlDictionaryReaderQuotas.Max);
        XmlDocument document = new();
        document.Load(reader);
        return document["root"];
    }

    static XmlDocument UploadString(string data, bool? _ = null)
    {
        client.Headers["Content-Type"] = "application/soap+xml";
        var value = client.UploadString(_.HasValue && _.Value ? "secured" : string.Empty, data);

        XmlDocument document = new();
        document.LoadXml(_.HasValue && !_.Value ? WebUtility.HtmlDecode(value) : value);
        return document;
    }
}