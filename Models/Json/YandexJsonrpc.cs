namespace Syracuse;
using System.Text.Json.Serialization;

[Serializable]
public record YandexJsonRpc
{
    [JsonPropertyName("jsonrpc")] public string Jsonrpc { get; set; }
    [JsonPropertyName("method")] public string Method { get; set; }
    [JsonPropertyName("params")] public Dictionary<string, string> Params { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; }
}