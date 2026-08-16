using Microsoft.EntityFrameworkCore;
using AdvancedAnalysis.DecisionTopology.Data;
using AdvancedAnalysis.DecisionTopology.Models;

namespace AdvancedAnalysis.DecisionTopology.Services;

/// <summary>
/// Frozen accepted contract guard/terminal service. Semantic order: query one work item by identifier; when the
/// item is absent return Not Found and terminate; when the item is locked return Conflict and
/// terminate; otherwise assign a processed state, save changes, and return success. Each terminal arm
/// keeps its result-factory invocation and its return in the same controlled block so the
/// first-node-only control-dependence defect is observable in the projected Method Flow.
/// </summary>
public sealed class WorkItemService(WorkDbContext context)
{
    public async Task<WorkItemResult<WorkItem>> ProcessAsync(int id)
    {
        var item = await context.WorkItems
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id);

        if (item is null)
        {
            return WorkItemResult<WorkItem>.NotFound("Work item was not found");
        }

        if (item.IsLocked)
        {
            return WorkItemResult<WorkItem>.Conflict("Work item is locked");
        }

        item.Status = WorkItemStatus.Processed;
        await context.SaveChangesAsync();
        return WorkItemResult<WorkItem>.Success(item);
    }
}
