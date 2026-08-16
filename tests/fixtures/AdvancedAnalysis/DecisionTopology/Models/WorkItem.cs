namespace AdvancedAnalysis.DecisionTopology.Models;

public sealed class WorkItem
{
    public int Id { get; set; }

    public bool IsLocked { get; set; }

    public WorkItemStatus Status { get; set; } = WorkItemStatus.Pending;
}

public enum WorkItemStatus
{
    Pending,
    Processed,
}
