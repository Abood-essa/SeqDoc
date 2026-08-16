using Microsoft.EntityFrameworkCore;
using AdvancedAnalysis.DecisionTopology.Models;

namespace AdvancedAnalysis.DecisionTopology.Data;

/// <summary>
/// Minimal DbContext for the frozen accepted contract DecisionTopology fixture. It is never composed through
/// AddDbContext; the fixture only proves the exact EF query and mutation facts the admitted slice
/// understands.
/// </summary>
public sealed class WorkDbContext : DbContext
{
    public WorkDbContext(DbContextOptions<WorkDbContext> options)
        : base(options)
    {
    }

    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
}
