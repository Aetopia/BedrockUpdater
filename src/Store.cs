using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using Windows.Management.Deployment;
using Windows.System.UserProfile;

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

    static readonly string architecture = RuntimeInformation.OSArchitecture.ToString().ToLower();

    static readonly string address = $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{{0}}?market={GlobalizationPreferences.HomeGeographicRegion}&locale=iv&deviceFamily=Windows.Desktop";

    static readonly WebClient client = new() { BaseAddress = "https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx/" };

    static string syncUpdates;

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
        .First(xmlNode => xmlNode.InnerText.StartsWith("http://tlu.dl.delivery.mp.microsoft.com")).InnerText;
    }

    internal static ReadOnlyCollection<IUpdate> GetUpdates(IProduct product)
    {
        var syncUpdatesResult = (XmlElement)UploadString(
            (syncUpdates ??= Resources.SyncUpdates.Replace("{1}", UploadString(Resources.GetCookie).GetElementsByTagName("EncryptedData")[0].InnerText))
            .Replace("{2}", product.AppCategoryId))
            .GetElementsByTagName("SyncUpdatesResult")[0];

        Dictionary<string, Update> updates = [];
        foreach (XmlNode xmlNode in syncUpdatesResult.GetElementsByTagName("AppxPackageInstallData"))
        {
            var xmlElement = (XmlElement)xmlNode.ParentNode.ParentNode.ParentNode;
            var file = xmlElement.GetElementsByTagName("File")[0];

            var packageIdentity = file.Attributes["InstallerSpecificIdentifier"].InnerText.Split('_');
            if (!packageIdentity[2].Equals(architecture) && !packageIdentity[2].Equals("neutral")) continue;
            if (!updates.ContainsKey(packageIdentity[0])) updates.Add(packageIdentity[0], new());

            var modified = Convert.ToDateTime(file.Attributes["Modified"].InnerText);
            if (updates[packageIdentity[0]].Modified < modified)
            {
                updates[packageIdentity[0]].Id = xmlElement["ID"].InnerText;
                updates[packageIdentity[0]].Modified = modified;
                updates[packageIdentity[0]].MainPackage = xmlNode.Attributes["MainPackage"].InnerText == "true";
            }
        }

        foreach (XmlNode xmlNode in syncUpdatesResult.GetElementsByTagName("SecuredFragment"))
        {
            var xmlElement = (XmlElement)xmlNode.ParentNode.ParentNode.ParentNode;
            var update = updates.FirstOrDefault(update => update.Value.Id.Equals(xmlElement["ID"].InnerText));
            if (update.Value == null) continue;

            var packageIdentity = xmlElement.GetElementsByTagName("AppxMetadata")[0].Attributes["PackageMoniker"].InnerText.Split('_');
            var package = PackageManager.FindPackagesForUser(string.Empty, $"{packageIdentity.First()}_{packageIdentity.Last()}").FirstOrDefault();

            if (package == null || (!package.IsDevelopmentMode && new Version(packageIdentity[1]) >
                new Version(package.Id.Version.Major, package.Id.Version.Minor, package.Id.Version.Build, package.Id.Version.Revision)))
                updates[update.Key].UpdateId = xmlElement.GetElementsByTagName("UpdateIdentity")[0].Attributes["UpdateID"].InnerText;
            else if (update.Value.MainPackage) return new([]);
            else updates.Remove(update.Key);
        }

        return updates
        .Select(update => update.Value)
        .OrderBy(update => update.MainPackage)
        .Cast<IUpdate>()
        .ToList()
        .AsReadOnly();
    }

    static XmlElement Deserialize(string input)
    {
        using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(input), XmlDictionaryReaderQuotas.Max);
        XmlDocument xml = new();
        xml.Load(reader);
        return xml["root"];
    }

    static XmlDocument UploadString(string data, bool secured = false)
    {
        client.Headers["Content-Type"] = "application/soap+xml";
        XmlDocument xmlDocument = new();
        xmlDocument.LoadXml(client.UploadString(secured ? "secured" : string.Empty, data).Replace("&lt;", "<").Replace("&gt;", ">"));
        return xmlDocument;
    }
}