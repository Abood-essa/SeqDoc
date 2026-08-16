namespace BehaviorDocumentation.GetMeaning.Models;

public sealed class Gadget
{
    public int Id { get; set; }

    public Guid Token { get; set; }

    public string? Label { get; set; }

    public List<Part> Parts { get; set; } = [];

    public Category? Category { get; set; }
}

public sealed class Part
{
    public int Id { get; set; }
}

public sealed class Category
{
    public int Id { get; set; }
}
