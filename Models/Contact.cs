namespace Syracuse;

public record Contact
{
    public int Id { get; set; }
    public ContactType Type { get; set; }
    public string Info { get; set; }
    public Worker Worker { get; set; }
}