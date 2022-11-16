using System.Text.Json.Serialization;

namespace Syracuse;

[Serializable]
public record TildaOrder
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("email")] public string Email { get; set; }
    [JsonPropertyName("phone")] public string Phone { get; set; }
    [JsonPropertyName("payment")] public Payment Payment { get; set; }
    [JsonPropertyName("token")] public string Token { get; set; } = default!;
    [JsonPropertyName("test")] public string? Test { get; set; }
}

[Serializable]
public record Payment
{
    [JsonPropertyName("orderid")] public string OrderId { get; set; }
    [JsonPropertyName("products")] public List<CartProduct> Products { get; set; }
    [JsonPropertyName("amount")] public string Amount { get; set; }
}

[Serializable]
public record CartProduct
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("externalid")] public string ExternalId { get; set; }
    [JsonPropertyName("price")] public string Price { get; set; }
    [JsonPropertyName("options")] public List<ProductOption> Options { get; set; }
}

[Serializable]
public record ProductOption
{
    [JsonPropertyName("option")] public string Option { get; set; }
    [JsonPropertyName("variant")] public string Variant { get; set; }
}