namespace Syracuse;

public record Sale
{
    public int Id { get; set; }
    public DateTime Time { get; set; }
    public SaleType Type { get; set; }
    public Client Client { get; set; }
    public Agenda? Agenda { get; set; }
    public Product? Product { get; set; }
    public WorkoutProgram? WorkoutProgram { get; set; }
    public string? Nutrition { get; set; }
    public bool IsDone { get; set; }
    public string? Key { get; set; } 
}
