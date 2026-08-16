using BehaviorDocumentation.FourFlows.Models;

namespace BehaviorDocumentation.FourFlows.Services;

/// <summary>
/// Generic result shape mirroring the accepted ServiceResult&lt;T&gt; static-factory pattern with an
/// added status enum so the controller can switch over a compiler-proven status member. Every factory
/// is a static method returning the constructed result; the structural-result companion projection
/// links success/data and failure/status factories to their exact return provenance.
/// </summary>
public sealed class WidgetResult<T>
{
    private WidgetResult(bool isSuccess, WidgetResultStatus status, T? data, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Status = status;
        Data = data;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public WidgetResultStatus Status { get; }

    public T? Data { get; }

    public string? ErrorMessage { get; }

    public static WidgetResult<T> Success(T data) => new(true, WidgetResultStatus.Success, data, null);

    public static WidgetResult<T> NotFound(string errorMessage) => new(false, WidgetResultStatus.NotFound, default, errorMessage);

    public static WidgetResult<T> Conflict(string errorMessage) => new(false, WidgetResultStatus.Conflict, default, errorMessage);

    public static WidgetResult<T> ValidationError(string errorMessage) => new(false, WidgetResultStatus.ValidationError, default, errorMessage);
}
