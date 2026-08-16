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
}
