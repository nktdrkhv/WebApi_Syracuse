using Microsoft.AspNetCore.WebUtilities;

namespace Syracuse;

public static class UrlHelper
{
    private static HttpClient s_httpClient = new();

    public static async Task<string> Shortener(string url)
    {
        var response = await s_httpClient.GetAsync($"https://clck.ru/--?url={url}");
        return await response.Content.ReadAsStringAsync();
    }

    public static string MakeLink(SaleType saleType, Dictionary<string, string> data) =>
        QueryHelpers.AddQueryString(saleType.AsReinputLink(), data);

    public static string MakeLink(string baseLink, Dictionary<string, string> data) =>
        QueryHelpers.AddQueryString(baseLink, data);

    public static async Task<string> MakeShortLink(SaleType saleType, Dictionary<string, string> data) => await Shortener(MakeLink(saleType, data));

    public static async Task<string> MakeShortLink(string baseLink, Dictionary<string, string> data) => await Shortener(MakeLink(baseLink, data));
}
