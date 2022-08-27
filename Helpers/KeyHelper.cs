namespace Syracuse;

public static class KeyHelper
{
    public static readonly string ApiToken = Environment.GetEnvironmentVariable("API_TOKEN");
    public static readonly string UniversalKey = Environment.GetEnvironmentVariable("UNIVERSAL_KEY");
    public static string NewKey() => Guid.NewGuid().ToString()[^12..];
}
