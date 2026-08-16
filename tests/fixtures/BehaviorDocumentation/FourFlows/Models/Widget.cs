namespace BehaviorDocumentation.FourFlows.Models;

public sealed class Widget
{
    public int Id { get; set; }

    public string? Label { get; set; }

    public decimal Price { get; set; }

    public int CategoryId { get; set; }

    public int MaxReservations { get; set; } = 5;

    public WidgetStatus Status { get; set; } = WidgetStatus.Draft;

    public List<Part> Parts { get; set; } = [];

    public Category? Category { get; set; }
}

public sealed class Part
{
    public int Id { get; set; }

    public int WidgetId { get; set; }

    public string? Name { get; set; }
}

public sealed class Category
{
    public int Id { get; set; }

    public string? Name { get; set; }
}

public sealed class Reservation
{
    public int Id { get; set; }

    public int WidgetId { get; set; }

    public int Quantity { get; set; }

    public DateTime ForDate { get; set; }

    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;
}

public sealed class PartLink
{
    public int Id { get; set; }

    public int ReservationId { get; set; }

    public int PartId { get; set; }
}
