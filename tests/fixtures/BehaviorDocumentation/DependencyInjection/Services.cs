namespace BehaviorDocumentation.DependencyInjection.Services;

public interface IGadgetStore
{
}

public sealed class GadgetStore : IGadgetStore
{
}

public sealed class MemoryGadgetStore : IGadgetStore
{
}

public interface IClock
{
}

public sealed class SystemClock : IClock
{
}

public interface IGadgetRepository
{
}

public sealed class GadgetRepository : IGadgetRepository
{
}

public sealed class GadgetStoreCollection : List<IGadgetStore>
{
}

public sealed class Gadget
{
    public Guid Id { get; set; }

    public string? Label { get; set; }
}

/// <summary>
/// Unrelated class with a same-service constructor that is not an admitted ASP.NET controller. Its
/// parameter type exactly matches an admitted registration but the type never binds because the
/// constructor-candidate boundary admits only exact ApiController/ControllerBase controllers.
/// </summary>
public sealed class GadgetReporter
{
    public GadgetReporter(IGadgetStore store)
    {
    }
}

/// <summary>
/// Demonstrates the fail-closed collection-injection boundary: a constructor parameter typed as
/// <see cref="IEnumerable{T}"/> never binds to a single registration and no binding is invented.
/// </summary>
public sealed class GadgetCollectionConsumer
{
    public GadgetCollectionConsumer(IEnumerable<IGadgetStore> stores)
    {
    }
}
