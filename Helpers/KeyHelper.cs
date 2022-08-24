namespace Syracuse;

public static class KeyHelper
{
    public static readonly string ApiToken = Environment.GetEnvironmentVariable("API_TOKEN") ?? "1522171a-b029-4079-9996-dafa51be9404";
    public static readonly string UniversalKey = Environment.GetEnvironmentVariable("UNIVERSAL_KEY") ?? "5d7ca0a5-3740-45ba-8062-2086abc30a4c";
    public static string NewKey() => Guid.NewGuid().ToString()[^12..];
}
