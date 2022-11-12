namespace Syracuse;

public class Product
{
    public int Id { get; set; }
    public string Code { get; set; }
    public List<Sale>? PartOf { get; set; }

    public int Price { get; set; }
    public string Label { get; set; }
    public string? Content { get; set; }
}
