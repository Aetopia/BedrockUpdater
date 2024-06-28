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

interface IUpdate { string UpdateId { get; } }

interface IProduct { string Title { get; } string AppCategoryId { get; } }

file class Product(string title, string appCategoryId) : IProduct
{
    public string Title => title;

    public string AppCategoryId => appCategoryId;
}

file class Update : IUpdate
{
    string updateId;

    internal string Id;

    internal DateTime Modified;

    internal bool MainPackage;

    public string UpdateId { get { return updateId; } set { updateId = value; } }
}

static class Store
{
    internal static readonly PackageManager PackageManager = new();

    static readonly string architecture = RuntimeInformation.OSArchitecture.ToString();

    static readonly string address = $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{{0}}?market={GlobalizationPreferences.HomeGeographicRegion}&locale=iv&deviceFamily=Windows.Desktop";

    static readonly WebClient client = new() { BaseAddress = "https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx/" };

    static string data;

    internal static IEnumerable<IProduct> GetProducts(params string[] productIds)
    {
        ICollection<IProduct> products = [];
        foreach (var productId in productIds)
        {
            var payload = Deserialize(client.DownloadString(string.Format(address, productId)))["Payload"];
            var title = payload?["ShortTitle"]?.InnerText;

            products.Add(new Product(
                string.IsNullOrEmpty(title) ? payload["Title"].InnerText : title,
                Deserialize(payload.GetElementsByTagName("FulfillmentData")[0].InnerText)["WuCategoryId"].InnerText));
        }

        return products;
    }

    internal static string GetUrl(IUpdate update)
    {
        return UploadString(Resources.GetExtendedUpdateInfo2.Replace("{1}", update.UpdateId), true)
        .GetElementsByTagName("Url")
        .Cast<XmlNode>()
        .First(node => node.InnerText.StartsWith("http://tlu.dl.delivery.mp.microsoft.com", StringComparison.Ordinal)).InnerText;
    }

    internal static ReadOnlyCollection<IUpdate> GetUpdates(IProduct product)
    {
        var result = (XmlElement)UploadString(
            (data ??= Resources.SyncUpdates.Replace("{1}", UploadString(Resources.GetCookie).GetElementsByTagName("EncryptedData")[0].InnerText))
            .Replace("{2}", product.AppCategoryId))
            .GetElementsByTagName("SyncUpdatesResult")[0];

        Dictionary<string, Update> updates = [];
        foreach (XmlNode node in result.GetElementsByTagName("AppxPackageInstallData"))
        {
            var element = (XmlElement)node.ParentNode.ParentNode.ParentNode;
            var file = element.GetElementsByTagName("File")[0];

            var identity = file.Attributes["InstallerSpecificIdentifier"].InnerText.Split('_');
            if (!identity[2].Equals(architecture, StringComparison.OrdinalIgnoreCase) && !identity[2].Equals("neutral")) continue;
            if (!updates.ContainsKey(identity[0])) updates.Add(identity[0], new());

            var modified = Convert.ToDateTime(file.Attributes["Modified"].InnerText);
            if (updates[identity[0]].Modified < modified)
            {
                updates[identity[0]].Id = element["ID"].InnerText;
                updates[identity[0]].Modified = modified;
                updates[identity[0]].MainPackage = node.Attributes["MainPackage"].InnerText.Equals("true");
            }
        }

        foreach (XmlNode node in result.GetElementsByTagName("SecuredFragment"))
        {
            var element = (XmlElement)node.ParentNode.ParentNode.ParentNode;
            var update = updates.FirstOrDefault(update => update.Value.Id.Equals(element["ID"].InnerText));
            if (update.Value == null) continue;

            var identity = element.GetElementsByTagName("AppxMetadata")[0].Attributes["PackageMoniker"].InnerText.Split('_');
            var package = PackageManager.FindPackagesForUser(string.Empty, $"{identity[0]}_{identity[4]}").FirstOrDefault();

            if (package == null || (!package.IsDevelopmentMode && new Version(identity[1]) > new Version(package.Id.Version.Major, package.Id.Version.Minor, package.Id.Version.Build, package.Id.Version.Revision)))
                updates[update.Key].UpdateId = element.GetElementsByTagName("UpdateIdentity")[0].Attributes["UpdateID"].InnerText;
            else if (update.Value.MainPackage) return new([]);
        }

        return updates
        .Select(update => update.Value)
        .Where(update => update.UpdateId != null)
        .OrderBy(update => update.MainPackage)
        .Cast<IUpdate>()
        .ToList()
        .AsReadOnly();
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