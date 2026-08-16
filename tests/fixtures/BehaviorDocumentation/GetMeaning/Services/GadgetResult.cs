namespace BehaviorDocumentation.GetMeaning.Services;

public enum GadgetResultStatus
{
    Success,
    NotFound,
}

/// <summary>
/// Generic result-shape type: an instance boolean IsSuccess member plus self-returning static
/// factories. The structural-result projection admits exactly this shape; lookalike result classes
/// without it never project success/data or failure/status meaning.
/// </summary>
public sealed class GadgetResult<T>
{
    private GadgetResult(bool isSuccess, T? data, string? errorMessage, GadgetResultStatus status)
    {
        IsSuccess = isSuccess;
        Data = data;
        ErrorMessage = errorMessage;
        Status = status;
    }

    public bool IsSuccess { get; }

    public T? Data { get; }

    public string? ErrorMessage { get; }

    public GadgetResultStatus Status { get; }

    public static GadgetResult<T> Success(T data) => new(true, data, null, GadgetResultStatus.Success);

    public static GadgetResult<T> NotFound(string message) => new(false, default, message, GadgetResultStatus.NotFound);
}
