using Microsoft.EntityFrameworkCore;
using BehaviorDocumentation.FourFlows.Data;
using BehaviorDocumentation.FourFlows.Models;

namespace BehaviorDocumentation.FourFlows.Services;

/// <summary>
/// Generic service for the four admitted flows. GetByIdAsync carries the exact supported EF slice
/// (AsNoTracking, ordered Include navigation anchors, SingleOrDefaultAsync with an equality
/// predicate). CancelAsync combines an exact lookup with an exact status assignment, a RemoveRange
/// mutation, a SaveChangesAsync persistence, a bool success result, and a conservative source
/// observation. ReserveAsync carries relational-pattern and DateTime comparisons, two ordered
/// CountAsync aggregations, an Add mutation, a loop-backed collection mutation, and a CreatedAtAction
/// controller link. UpdateAsync excludes mismatched identifiers, runs ordered multi-query, and
/// performs the remove/clear/add/save mutation sequence.
/// </summary>
public sealed class WidgetService(WidgetDbContext context) : IWidgetService
{
    public async Task<WidgetResult<Widget>> GetByIdAsync(int id)
    {
        var widget = await context.Widgets
            .AsNoTracking()
            .Include(item => item.Parts)
            .Include(item => item.Category)
            .SingleOrDefaultAsync(item => item.Id == id);

        if (widget is null)
        {
            return WidgetResult<Widget>.NotFound("Widget was not found");
        }

        return WidgetResult<Widget>.Success(widget);
    }

    public async Task<WidgetResult<bool>> CancelAsync(int id, string reason)
    {
        var widget = await context.Widgets
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id);

        if (widget is null)
        {
            return WidgetResult<bool>.NotFound("Widget was not found");
        }

        if (widget.Status == WidgetStatus.Cancelled)
        {
            return WidgetResult<bool>.Conflict("Widget is already cancelled");
        }

        widget.Status = WidgetStatus.Cancelled;
        context.Widgets.RemoveRange(new[] { widget });
        await context.SaveChangesAsync();

        // TODO: notify the warehouse once the cancellation notice channel exists
        return WidgetResult<bool>.Success(true);
    }

    public async Task<WidgetResult<Reservation>> ReserveAsync(int widgetId, int quantity, DateTime forDate)
    {
        var widget = await context.Widgets
            .Include(item => item.Parts)
            .SingleOrDefaultAsync(item => item.Id == widgetId);

        if (widget is null)
        {
            return WidgetResult<Reservation>.NotFound("Widget was not found");
        }

        if (quantity is <= 0)
        {
            return WidgetResult<Reservation>.ValidationError("Quantity must be positive");
        }

        if (forDate < DateTime.UtcNow.Date)
        {
            return WidgetResult<Reservation>.ValidationError("Reservation date is in the past");
        }

        var currentCount = await context.Reservations
            .Where(item => item.WidgetId == widgetId)
            .CountAsync();
        var partCount = await context.Parts
            .Where(item => item.WidgetId == widgetId)
            .CountAsync();

        if (currentCount >= widget.MaxReservations)
        {
            return WidgetResult<Reservation>.Conflict("Capacity reached");
        }

        var reservation = new Reservation();
        reservation.WidgetId = widget.Id;
        reservation.Quantity = quantity;
        reservation.ForDate = forDate;
        reservation.Status = ReservationStatus.Active;
        context.Reservations.Add(reservation);

        // NOTE: each linked part is added inside the loop; the Add mutation fact is recorded once per
        // compiler-proven call site.
        foreach (var part in widget.Parts)
        {
            context.PartLinks.Add(new PartLink { ReservationId = reservation.Id, PartId = part.Id });
        }

        await context.SaveChangesAsync();
        return WidgetResult<Reservation>.Success(reservation);
    }

    public async Task<WidgetResult<Widget>> UpdateAsync(int id, UpdateWidgetCommand command)
    {
        if (command.Id != id)
        {
            return WidgetResult<Widget>.ValidationError("Identifier mismatch");
        }

        var widget = await context.Widgets
            .Include(item => item.Parts)
            .SingleOrDefaultAsync(item => item.Id == id);

        if (widget is null)
        {
            return WidgetResult<Widget>.NotFound("Widget was not found");
        }

        var categoryCount = await context.Categories
            .Where(item => item.Id == command.CategoryId)
            .CountAsync();
        if (categoryCount == 0)
        {
            return WidgetResult<Widget>.ValidationError("Category is unknown");
        }

        var updated = new Widget();
        updated.Id = id;
        updated.Label = command.Label;
        updated.Price = command.Price;
        updated.Status = WidgetStatus.Updated;
        context.Parts.RemoveRange(widget.Parts);
        context.Parts.Local.Clear();
        context.Widgets.Add(updated);
        await context.SaveChangesAsync();
        return WidgetResult<Widget>.Success(updated);
    }
}
