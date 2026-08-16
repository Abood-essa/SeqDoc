using System.Linq.Expressions;
using BehaviorDocumentation.GetMeaning.Models;

namespace BehaviorDocumentation.GetMeaning.Services;

/// <summary>
/// Lookalike EF helper: an extension method with the same terminal shape as SingleOrDefaultAsync but
/// on a user type in the fixture assembly. Exact framework-symbol admission must never accept it.
/// </summary>
public static class QueryableLookalikes
{
    public static Task<Gadget?> LookalikeSingleOrDefaultAsync(
        this IQueryable<Gadget> source,
        Expression<Func<Gadget, bool>> predicate)
        => Task.FromResult<Gadget?>(null);
}

/// <summary>
/// Lookalike result shapes: an IsSuccess member without a self-returning factory and a factory that
/// does not return its containing type. Neither shape may project success/data or failure/status
/// meaning.
/// </summary>
public sealed class LookalikeOutcome
{
    public bool IsSuccess { get; set; }

    public static string Success(string value) => value;
}

public static class PlainFactory
{
    public static int Success(int value) => value;
}

public enum OppositeResultStatus
{
    Success,
    NotFound,
}

/// <summary>
/// Opposite-polarity fully-shaped lookalike: a result-shaped type whose Success factory constructs
/// IsSuccess as false. Polarity is proven from the construction, so this never projects meaning.
/// </summary>
public sealed class OppositePolarityResult
{
    public OppositePolarityResult(bool isSuccess, OppositeResultStatus status)
    {
        IsSuccess = isSuccess;
        Status = status;
    }

    public bool IsSuccess { get; }

    public OppositeResultStatus Status { get; }

    public static OppositePolarityResult Success() => new(false, OppositeResultStatus.Success);

    public static OppositePolarityResult NotFound() => new(false, OppositeResultStatus.NotFound);
}

/// <summary>
/// Fully-shaped opposite-polarity lookalike: passes the result-shape gate (instance boolean IsSuccess
/// plus self-returning static factories) but every factory returns the opposite polarity from its
/// name and the type also exposes a second Boolean member. Neither the factories nor a branch on the
/// non-IsSuccess Boolean member may project success/data or failure/status meaning; only the exact
/// IsSuccess property and compiler-proven factory returned state are admissible.
/// </summary>
public sealed class OppositePolarityResult<T>
{
    private readonly bool _isSuccess;
    private readonly T? _data;

    private OppositePolarityResult(bool isSuccess, T? data)
    {
        _isSuccess = isSuccess;
        _data = data;
    }

    public bool IsSuccess => _isSuccess;

    public bool HasError => !_isSuccess;

    public static OppositePolarityResult<T> Success(T data) => new(false, data);

    public static OppositePolarityResult<T> NotFound(string message) => new(true, default);
}
