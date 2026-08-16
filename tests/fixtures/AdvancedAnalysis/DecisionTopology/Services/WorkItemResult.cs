namespace AdvancedAnalysis.DecisionTopology.Services;

/// <summary>
/// Frozen accepted contract result shape mirroring the accepted static-factory pattern. Every factory is a static
/// method returning the constructed result so each guard arm contains an exact factory invocation and
/// an exact return in the same controlled block.
/// </summary>
public sealed class WorkItemResult<T>
{
    private WorkItemResult(bool isSuccess, T? data, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Data = data;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public T? Data { get; }

    public string? ErrorMessage { get; }

    public static WorkItemResult<T> Success(T data) => new(true, data, null);

    public static WorkItemResult<T> NotFound(string errorMessage) => new(false, default, errorMessage);

    public static WorkItemResult<T> Conflict(string errorMessage) => new(false, default, errorMessage);
}
