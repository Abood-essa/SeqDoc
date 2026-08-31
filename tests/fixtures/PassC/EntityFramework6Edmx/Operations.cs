using System.Data.Entity;
using System.Linq;

namespace InitialRedTest;

public sealed class Record
{
    public int Id { get; set; }
}

public sealed class RecordsContext : DbContext
{
    public DbSet<Record> Records { get; set; } = null!;
}

// These types deliberately resemble the supported shapes but are not EF6 symbols.
public sealed class ForeignSet<T> : EnumerableQuery<T>
{
    public ForeignSet() : base(Enumerable.Empty<T>()) { }
    public void Add(T item) { }
}

public sealed class ForeignContext
{
    private readonly int _marker;
    public ForeignContext() => _marker = 0;
    public ForeignSet<Record> Records { get; } = new();
    public int SaveChanges() => _marker;
}

public sealed class NonEfContext
{
    private readonly int _marker;
    public NonEfContext() => _marker = 0;
    public IQueryable<Record> Records => _marker == 0 ? Enumerable.Empty<Record>().AsQueryable() : Enumerable.Empty<Record>().AsQueryable();
}

public sealed class NonContextDbSetOwner
{
    public DbSet<Record> Records { get; } = null!;
}

public sealed class Operations
{
    public void Execute(RecordsContext context, int id)
    {
        _ = this;
        _ = context.Records.FirstOrDefault(record => record.Id == id);
        _ = context.Records.Count();
        context.Records.Add(new Record { Id = id });
        context.SaveChanges();
    }

    public void Lookalikes(ForeignContext foreign, NonEfContext queryable)
    {
        _ = this;
        _ = foreign.Records.FirstOrDefault(record => record.Id == 1);
        _ = queryable.Records.Count();
        foreign.Records.Add(new Record());
        foreign.SaveChanges();
    }

    public void ReassignedTransactions(RecordsContext context, int id)
    {
        _ = this;
        var transactions = context.Records.Where(record => record.Id == id);
        transactions = context.Records.Where(record => record.Id != id);
        _ = transactions.Count();
    }

    public void LocalWhereCount(RecordsContext context, int id)
    {
        var records = context.Records.Where(record => record.Id == id);
        _ = records.Count();
    }

    public void ConditionalLocalWhereCount(RecordsContext context, int id)
    {
        if (id > 0)
        {
            var records = context.Records.Where(record => record.Id == id);
            _ = records.Count();
        }
    }

    public void LoopLocalWhereCount(RecordsContext context, int id)
    {
        while (id > 0)
        {
            var records = context.Records.Where(record => record.Id == id);
            _ = records.Count();
            break;
        }
    }

    public void ForeignLocalWhereCount(NonEfContext context, int id)
    {
        var records = context.Records.Where(record => record.Id == id);
        _ = records.Count();
    }

    public void UnsupportedLocalWhereCount(RecordsContext context, int id)
    {
        var records = context.Records.Select(record => record);
        _ = records.Count();
    }

    public void NonContextDbSetDirect(NonContextDbSetOwner owner) => _ = owner.Records.Count();

    public void NonContextDbSetRecovered(NonContextDbSetOwner owner)
    {
        var records = owner.Records;
        _ = records.Count();
    }

    public void NonContextDbSetLocal(NonContextDbSetOwner owner)
    {
        var records = owner.Records;
        _ = records.Count();
    }

    public void NonContextDbSetAdd(NonContextDbSetOwner owner, int id)
        => owner.Records.Add(new Record { Id = id });

    public void CapturedLambdaLocalWhereCount(RecordsContext context, int id)
    {
        Func<int> count = () =>
        {
            var records = context.Records.Where(record => record.Id == id);
            return records.Count();
        };
        _ = count();
    }

    public void CapturedLocalFunctionWhereCount(RecordsContext context, int id)
    {
        int Count()
        {
            var records = context.Records.Where(record => record.Id == id);
            return records.Count();
        }
        _ = Count();
    }

    public void MultipleWhereCount(RecordsContext context, int id)
    {
        _ = context.Records
            .Where(record => record.Id > 0)
            .Where(record => record.Id == id)
            .Count();
    }
}
