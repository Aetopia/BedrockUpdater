using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Windows.Management.Deployment;
using Windows.System.UserProfile;

interface IProduct
{
    string Title { get; }

    string AppCategoryId { get; }
}

file class Product(string title, string appCategoryId) : IProduct
{
    public string Title => title;

    public string AppCategoryId => appCategoryId;
}

interface IUpdateIdentity { string UpdateId { get; } }

file class UpdateIdentity(string updateId, bool mainPackage) : IUpdateIdentity
{
    public string UpdateId => updateId;

    internal bool MainPackage => mainPackage;
}

file class Update
{
    internal string Id;

    internal DateTime Modified;

    internal bool MainPackage;
}

file struct SynchronizationContextRemover : INotifyCompletion
{
    internal readonly bool IsCompleted => SynchronizationContext.Current == null;

    internal readonly SynchronizationContextRemover GetAwaiter() => this;

    internal readonly void GetResult() { }

    public readonly void OnCompleted(Action continuation)
    {
        var syncContext = SynchronizationContext.Current;
        try { SynchronizationContext.SetSynchronizationContext(null); continuation(); }
        finally { SynchronizationContext.SetSynchronizationContext(syncContext); }
    }
}

class Store
{
    internal static readonly PackageManager PackageManager = new();

    static readonly string architecture = RuntimeInformation.OSArchitecture.ToString().ToLower();

    static readonly string address = $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{{0}}?market={GlobalizationPreferences.HomeGeographicRegion}&locale=iv&deviceFamily=Windows.Desktop";

    static readonly HttpClient client = new() { BaseAddress = new("https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx/") };

    readonly string syncUpdates;

    Store(string syncUpdates) { this.syncUpdates = syncUpdates; }

    internal static async Task<Store> CreateAsync()
    {
        await default(SynchronizationContextRemover);
        return new(Resources.SyncUpdates.Replace("{1}", (await PostAsSoapAsync(Resources.GetCookie)).GetElementsByTagName("EncryptedData")[0].InnerText));
    }

    internal async Task<IEnumerable<IProduct>> GetProductsAsync(params string[] productIds)
    {
        await default(SynchronizationContextRemover);

        List<IProduct> products = [];
        foreach (var productId in productIds)
        {
            using var message = await client.GetAsync(string.Format(address, productId));
            message.EnsureSuccessStatusCode();

            var payload = Deserialize(await message.Content.ReadAsStringAsync())["Payload"];
            var title = payload?["ShortTitle"]?.InnerText;

            products.Add(new Product(
                string.IsNullOrEmpty(title) ? payload["Title"].InnerText : title,
                Deserialize(payload.GetElementsByTagName("FulfillmentData")[0].InnerText)["WuCategoryId"].InnerText));
        }

        return products;
    }

    internal async Task<string> GetUrlAsync(IUpdateIdentity identity)
    {
        await default(SynchronizationContextRemover);
        return (await PostAsSoapAsync(Resources.GetExtendedUpdateInfo2.Replace("{1}", identity.UpdateId), true))
        .GetElementsByTagName("Url")
        .Cast<XmlNode>()
        .First(xmlNode => xmlNode.InnerText.StartsWith("http://tlu.dl.delivery.mp.microsoft.com")).InnerText;
    }

    internal async Task<ReadOnlyCollection<IUpdateIdentity>> SyncUpdatesAsync(IProduct product)
    {
        await default(SynchronizationContextRemover);
        var syncUpdatesResult = (XmlElement)(await PostAsSoapAsync(syncUpdates.Replace("{2}", product.AppCategoryId))).GetElementsByTagName("SyncUpdatesResult")[0];

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

        List<IUpdateIdentity> identities = [];
        foreach (XmlNode xmlNode in syncUpdatesResult.GetElementsByTagName("SecuredFragment"))
        {
            var xmlElement = (XmlElement)xmlNode.ParentNode.ParentNode.ParentNode;
            var update = updates.Values.FirstOrDefault(update => update.Id.Equals(xmlElement["ID"].InnerText));
            if (update == null) continue;

            if (CheckUpdateAvailability(xmlElement.GetElementsByTagName("AppxMetadata")[0].Attributes["PackageMoniker"].InnerText))
                identities.Add(new UpdateIdentity(xmlElement.GetElementsByTagName("UpdateIdentity")[0].Attributes["UpdateID"].InnerText, update.MainPackage));
            else if (update.MainPackage) return new([]);
        }
        identities.Sort((a, b) => ((UpdateIdentity)a).MainPackage ? 1 : -1);

        return identities.AsReadOnly();
    }

    static XmlElement Deserialize(string input)
    {
        using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(input), XmlDictionaryReaderQuotas.Max);
        XmlDocument xml = new();
        xml.Load(reader);
        return xml["root"];
    }

    static async Task<XmlDocument> PostAsSoapAsync(string value, bool secured = false)
    {
        using StringContent content = new(value, Encoding.UTF8, "application/soap+xml");
        using var message = await client.PostAsync(secured ? "secured" : null, content);
        message.EnsureSuccessStatusCode();

        XmlDocument xmlDocument = new();
        xmlDocument.LoadXml((await message.Content.ReadAsStringAsync()).Replace("&lt;", "<").Replace("&gt;", ">"));
        return xmlDocument;
    }

    static bool CheckUpdateAvailability(string packageFullName)
    {
        var packageIdentity = packageFullName.Split('_');
        var package = PackageManager.FindPackagesForUser(string.Empty, $"{packageIdentity.First()}_{packageIdentity.Last()}").FirstOrDefault();

        return
            package == null ||
            (!package.IsDevelopmentMode &&
            new Version(packageIdentity[1]) > new Version(package.Id.Version.Major, package.Id.Version.Minor, package.Id.Version.Build, package.Id.Version.Revision));
    }
}