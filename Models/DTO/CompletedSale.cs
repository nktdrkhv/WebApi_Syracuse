using CsvHelper.Configuration.Attributes;

namespace Syracuse;

public record CompletedSale
{
    [Index(0)] [Name("ID")] public int SaleId { get; set; }
    [Index(1)] [Name("ID в банке")] public int OrderId { get; set; }
    [Index(2)] [Name("Время покупки UTC")] public string DateOfPurchase { get; set; }
    [Index(3)] [Name("Почта")] public string Email { get; set; }
    [Index(4)] [Name("Телефон")] public string Phone { get; set; }
    [Index(5)] [Name("Имя")] public string Name { get; set; }
    [Index(6)] [Name("Товар")] public string Products { get; set; }
}