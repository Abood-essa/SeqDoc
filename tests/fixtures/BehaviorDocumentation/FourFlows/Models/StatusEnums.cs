namespace BehaviorDocumentation.FourFlows.Models;

/// <summary>Compiler-proven status vocabulary of the generic widget result shape.</summary>
public enum WidgetResultStatus
{
    Success,
    NotFound,
    Conflict,
    ValidationError,
}

/// <summary>Compiler-proven lifecycle status of a widget.</summary>
public enum WidgetStatus
{
    Draft,
    Active,
    Updated,
    Cancelled,
}

/// <summary>Compiler-proven lifecycle status of a reservation.</summary>
public enum ReservationStatus
{
    Pending,
    Active,
    Completed,
    Cancelled,
}
