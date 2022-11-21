namespace Syracuse;

[Serializable]
public record Table
{
    public List<string> Titles { get; set; }
    public List<List<string>> Data { get; set; }
}