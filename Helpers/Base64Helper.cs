namespace Syracuse;

public static class Base64Helper
{
    public static async Task DecodeToPdf(string base64, string path)
    {
        var bytes = Convert.FromBase64String(base64);
        await File.WriteAllBytesAsync(path, bytes);
    }
}