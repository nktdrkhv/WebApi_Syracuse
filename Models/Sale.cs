using System.ComponentModel.DataAnnotations.Schema;

namespace Syracuse;

public class Sale
{
    public int Id { get; set; }
    public DateTime PurchaseTime { get; set; }
    public SaleType Type { get; set; }
    public Client Client { get; set; }

    public List<Product>? Product { get; set; }
    public Agenda? Agenda { get; set; }
    public WorkoutProgram? WorkoutProgram { get; set; }
    public string? Nutrition { get; set; }

    public int OrderId { get; set; }
    public string? Key { get; set; }

    public bool IsSuccessEmailSent { get; set; }
    public bool IsAdminNotified { get; set; }
    public bool? IsErrorHandled { get; set; }
    public bool IsDone { get; set; }

    public DateTime? ScheduledDeliverTime { get; set; }
}
