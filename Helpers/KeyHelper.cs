namespace Syracuse;

public static class KeyHelper
{
    public static string ApiToken => Environment.GetEnvironmentVariable("API_TOKEN") ?? throw new InvalidOperationException();
    public static string UniversalKey => Environment.GetEnvironmentVariable("UNIVERSAL_KEY") ?? throw new InvalidOperationException();

    public static string NewKey()
    {
        return Guid.NewGuid().ToString()[..7];
    }
}