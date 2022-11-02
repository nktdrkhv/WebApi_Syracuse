namespace Syracuse;

public class Product
{
    public int Id { get; set; }
    public ProductType Type { get; set; }
    public string Code { get; set; }

    public int Price { get; set; }
    public string? Content { get; set; }
    public List<Product>? Сontains { get; set; }
}
