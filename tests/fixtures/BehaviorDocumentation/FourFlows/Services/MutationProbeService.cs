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
}
