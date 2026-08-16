using BehaviorDocumentation.FourFlows.Models;

namespace BehaviorDocumentation.FourFlows.Services;

public interface IWidgetService
{
    Task<WidgetResult<Widget>> GetByIdAsync(int id);

    Task<WidgetResult<bool>> CancelAsync(int id, string reason);

    Task<WidgetResult<Reservation>> ReserveAsync(int widgetId, int quantity, DateTime forDate);

    Task<WidgetResult<Widget>> UpdateAsync(int id, UpdateWidgetCommand command);
}

/// <summary>
/// Generic command payload for the Update flow. The service excludes a mismatched identifier through
/// an exact inequality comparison before admitting any mutation.
/// </summary>
public sealed record UpdateWidgetCommand(int Id, int CategoryId, string Label, decimal Price);
