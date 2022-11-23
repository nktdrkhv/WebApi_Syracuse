using Newtonsoft.Json;
using SQLite;

namespace Syracuse;

public class Product
{
    public int Id { get; set; }
    [Unique] public string Code { get; set; }
    [JsonIgnore][Column("SaleId")] public List<Sale> PartOf { get; set; }
    [Column("ChildId")] public List<Product>? Childs { get; set; }
    [Column("ParentId")] public List<Product>? Parents { get; set; }

    [Unique] public string Label { get; set; }
    public int Price { get; set; }
    public string? Content { get; set; }
}
