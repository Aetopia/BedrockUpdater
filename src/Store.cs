using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;
using Windows.Management.Deployment;
using Windows.System.UserProfile;

interface IProduct
{
    string Title { get; }

    string AppCategoryId { get; }
}

file struct Product(string title, string appCategoryId) : IProduct
{
    public readonly string Title => title;

    public readonly string AppCategoryId => appCategoryId;
}

file struct UpdateIdentity
{
    internal string UpdateId;

    internal bool MainPackage;
}

file class Update
{
    internal string Id;

    internal DateTime Modified;

    internal bool MainPackage;
}

file static class Resources
{
    static readonly Assembly assembly = Assembly.GetExecutingAssembly();

    internal readonly static string GetCookie = ToString("GetCookie.xml");

    internal readonly static string GetExtendedUpdateInfo2 = ToString("GetExtendedUpdateInfo2.xml");

    internal readonly static string SyncUpdates = ToString("SyncUpdates.xml");

    static string ToString(string name)
    {
        using StreamReader stream = new(assembly.GetManifestResourceStream(name));
        return stream.ReadToEnd();
    }
}

file struct SynchronizationContextRemover : INotifyCompletion
{
    internal readonly bool IsCompleted => SynchronizationContext.Current == null;

    internal readonly SynchronizationContextRemover GetAwaiter() => this;

    internal readonly void GetResult() { }

    public readonly void OnCompleted(Action continuation)
    {
        var syncContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            continuation();
        }
        finally { SynchronizationContext.SetSynchronizationContext(syncContext); }
    }
}

class Store
{
    readonly string syncUpdates;

    static readonly JavaScriptSerializer javaScriptSerializer = new();

    static readonly string requestUri = $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{{0}}?market={GlobalizationPreferences.HomeGeographicRegion}&locale=iv&deviceFamily=Windows.Desktop";

    static readonly HttpClient httpClient = new() { BaseAddress = new("https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx/") };

    static readonly PackageManager packageManager = new();

    static readonly string architecture = RuntimeInformation.OSArchitecture.ToString().ToLower();

    internal async Task<IEnumerable<IProduct>> GetProductsAsync(params string[] productIds)
    {
        await default(SynchronizationContextRemover);

        List<IProduct> products = [];
        foreach (var productId in productIds)
        {
            using var response = await httpClient.GetAsync(string.Format(requestUri, productId));
            response.EnsureSuccessStatusCode();

            var payload = (Dictionary<string, object>)javaScriptSerializer.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync())["Payload"];
            payload.TryGetValue("ShortTitle", out object value);

            products.Add(
                new Product((string)(value ?? payload["Title"]),
                javaScriptSerializer.Deserialize<Dictionary<string, string>>(
                    (string)((Dictionary<string, object>)((ArrayList)payload["Skus"])[0])["FulfillmentData"])["WuCategoryId"]));
        }

        return products;
    }

    static async Task<XmlDocument> PostAsSoapAsync(string content, bool secured = false)
    {
        using var response = await httpClient.PostAsync(secured ? "secured" : null, new StringContent(content, Encoding.UTF8, "application/soap+xml"));
        response.EnsureSuccessStatusCode();

        XmlDocument xmlDocument = new();
        xmlDocument.LoadXml((await response.Content.ReadAsStringAsync()).Replace("&lt;", "<").Replace("&gt;", ">"));
        return xmlDocument;
    }

    Store(string encryptedData) { syncUpdates = Resources.SyncUpdates.Replace("{1}", encryptedData); }

    internal static async Task<Store> CreateAsync()
    {
        await default(SynchronizationContextRemover);

        return new((await PostAsSoapAsync(Resources.GetCookie)).GetElementsByTagName("EncryptedData")[0].InnerText);
    }

    internal async Task<string> GetUrlAsync(string updateId)
    {
        await default(SynchronizationContextRemover);

        return (await PostAsSoapAsync(Resources.GetExtendedUpdateInfo2.Replace("{1}", updateId), true)).GetElementsByTagName("Url").Cast<XmlNode>().First(
            xmlNode => xmlNode.InnerText.StartsWith("http://tlu.dl.delivery.mp.microsoft.com")).InnerText;
    }

    static bool CheckUpdateAvailability(string packageFullName)
    {
        var packageIdentity = packageFullName.Split('_');
        var package = packageManager.FindPackagesForUser(string.Empty, $"{packageIdentity.First()}_{packageIdentity.Last()}").FirstOrDefault();

        return
            package == null ||
            (!package.IsDevelopmentMode &&
            new Version(packageIdentity[1]) > new Version(package.Id.Version.Major, package.Id.Version.Minor, package.Id.Version.Build, package.Id.Version.Revision));
    }

    internal async Task<IEnumerable<string>> SyncUpdates(IProduct product)
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

        List<UpdateIdentity> updateIdentities = [];
        foreach (XmlNode xmlNode in syncUpdatesResult.GetElementsByTagName("SecuredFragment"))
        {
            var xmlElement = (XmlElement)xmlNode.ParentNode.ParentNode.ParentNode;
            var update = updates.Values.FirstOrDefault(update => update.Id.Equals(xmlElement["ID"].InnerText));
            if (update == null) continue;

            if (CheckUpdateAvailability(xmlElement.GetElementsByTagName("AppxMetadata")[0].Attributes["PackageMoniker"].InnerText))
                updateIdentities.Add(new()
                {
                    UpdateId = xmlElement.GetElementsByTagName("UpdateIdentity")[0].Attributes["UpdateID"].InnerText,
                    MainPackage = update.MainPackage
                });
            else if (update.MainPackage) return [];
        }

        return updateIdentities.OrderBy(updateIdentity => updateIdentity.MainPackage).Select(updateIdentity => updateIdentity.UpdateId);
    }
}