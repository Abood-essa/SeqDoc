using Microsoft.EntityFrameworkCore;
using BehaviorDocumentation.GetMeaning.Models;

namespace BehaviorDocumentation.GetMeaning.Data;

/// <summary>
/// Minimal DbContext for the unrelated GetMeaning fixture. It is never composed through AddDbContext;
/// the fixture only proves the exact EF query facts the translation-alpha slice admits.
/// </summary>
public sealed class GadgetDbContext : DbContext
{
    public GadgetDbContext(DbContextOptions<GadgetDbContext> options)
        : base(options)
    {
    }

    public DbSet<Gadget> Gadgets => Set<Gadget>();

    public DbSet<Category> Categories => Set<Category>();
}

public sealed class RelationalProbe(GadgetDbContext context)
{
    public async Task<Gadget?> RunAsync(int id)
    {
        await context.Database.ExecuteSqlRawAsync("UPDATE Gadgets SET Label = 'raw' WHERE Id = {0}", id);

        var result = await context.Gadgets
            .FromSqlRaw("SELECT * FROM Gadgets WHERE Id = {0}", id)
            .SingleOrDefaultAsync(item => item.Id == id);

        return result;
    }
}
