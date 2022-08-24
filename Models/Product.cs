namespace Syracuse;

public record Product
{
    public int Id { get; set; }
    public ProductType Type { get; set; }
    public string Code { get; set; }
    public string? Link { get; set; }
    public List<Product>? Сontains { get; set; }
}
