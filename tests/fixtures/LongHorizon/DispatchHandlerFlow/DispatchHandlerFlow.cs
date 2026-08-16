using System.Linq;

namespace LongHorizon.DispatchHandlerFlow;

public sealed class DispatchHandler
{
    public Dto Handle(Request request)
    {
        var aggregate = Aggregate.Create(request);
        foreach (var item in request.Items)
        {
            aggregate.Add(item);
        }

        var dto = Dto.FromDomain(aggregate);
        _ = new[] { dto }.Select(lookalike => lookalike);
        return dto;
    }
}

public sealed record Request(IReadOnlyList<Item> Items);
public sealed record Item(string Value);

public sealed class Aggregate
{
    public static Aggregate Create(Request request) => new();
    public void Add(Item item) { }
    public int Total() => 0;
}

public sealed class Dto
{
    public static Dto FromDomain(Aggregate aggregate)
    {
        _ = aggregate.Total();
        return new();
    }
}
