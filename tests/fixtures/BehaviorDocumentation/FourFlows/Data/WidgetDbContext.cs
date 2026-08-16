using Microsoft.EntityFrameworkCore;
using BehaviorDocumentation.FourFlows.Models;

namespace BehaviorDocumentation.FourFlows.Data;

/// <summary>
/// Minimal DbContext for the generic FourFlows fixture. It is never composed through AddDbContext;
/// the fixture only proves the exact EF query and mutation facts the translation-alpha slice admits.
/// </summary>
public sealed class WidgetDbContext : DbContext
{
    public WidgetDbContext(DbContextOptions<WidgetDbContext> options)
        : base(options)
    {
    }

    public DbSet<Widget> Widgets => Set<Widget>();

    public DbSet<Part> Parts => Set<Part>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<PartLink> PartLinks => Set<PartLink>();
}
