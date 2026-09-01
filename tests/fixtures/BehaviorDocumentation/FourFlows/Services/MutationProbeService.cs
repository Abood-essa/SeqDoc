using BehaviorDocumentation.FourFlows.Data;
using BehaviorDocumentation.FourFlows.Models;

namespace BehaviorDocumentation.FourFlows.Services;

/// <summary>
/// Negative probe only: never admitted as a flow. The probe clears an unrelated local collection,
/// calls the unsupported DbSet Remove, and calls AddRange; none of these shapes is an exact admitted
/// EF mutation, so the collector must emit no mutation fact for this method.
/// </summary>
public sealed class MutationProbeService(WidgetDbContext context)
{
    public void UnsupportedAndUnrelatedProbe()
    {
        var scratch = new List<string>();
        scratch.Clear();

        var widget = new Widget();
        context.Widgets.Remove(widget);
        context.Widgets.AddRange([widget]);
    }

    public void AssignmentLookalikeProbe()
    {
        var local = new StatusCarrier();
        local.Status = "local";

        var localValue = "initial";
        localValue = "changed";
        _ = localValue;

        var dto = new WidgetDto();
        dto.Status = "dto";

        GetStatusCarrier().Status = GetComputedStatus();
    }

    private static StatusCarrier GetStatusCarrier() => new();

    private static string GetComputedStatus() => "computed";

    private sealed class StatusCarrier
    {
        public string? Status { get; set; }
    }

    private sealed class WidgetDto
    {
        public string? Status { get; set; }
    }
}
