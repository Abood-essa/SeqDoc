using System;

namespace PredicateFacts;

public enum OrderStatus
{
    Pending,
    Cancelled,
    Shipped,
}

/// <summary>
/// CR-1 generic predicate fixture. Admitted methods return one material boolean decision whose
/// compiler operations must normalize into the typed predicate tree; unsupported methods must
/// produce no predicate fact and a stable diagnostic when the decision is material.
/// </summary>
public static class PredicateFactShapes
{
    // Admitted: null check.
    public static bool IsNull(object? value)
    {
        if (value is null)
        {
            return true;
        }

        return false;
    }

    // Admitted: equality null check retains both the tested operand and converted null operand.
    public static bool IsEqualNull(string? value)
    {
        if (value == null)
        {
            return true;
        }

        return false;
    }

    // Admitted: boolean truth of a parameter.
    public static bool IsTrue(bool flag)
    {
        if (flag)
        {
            return true;
        }

        return false;
    }

    // Admitted: six closed comparison operators over value operands.
    public static bool IsEqual(int left, int right)
    {
        if (left == right)
        {
            return true;
        }

        return false;
    }

    public static bool IsDifferent(int left, int right) => left != right;

    public static bool IsBelow(int left, int right) => left < right;

    public static bool IsAffordable(int price, int budget) => price <= budget;

    public static bool IsQualified(int score, int minimum) => score >= minimum;

    public static bool IsAbove(int value, int limit) => value > limit;

    // Admitted: enum constant operand.
    public static bool IsCancelled(OrderStatus status)
    {
        if (status == OrderStatus.Cancelled)
        {
            return true;
        }

        return false;
    }

    // Admitted: relational pattern normalized into the comparison tree.
    public static bool IsLarge(int value)
    {
        if (value is > 10)
        {
            return true;
        }

        return false;
    }

    // Admitted: logical and/or over boolean operands.
    public static bool CanProceed(bool ready, bool enabled)
    {
        if (ready && enabled)
        {
            return true;
        }

        return false;
    }

    public static bool MayFallback(bool ready, bool enabled) => ready || enabled;

    // Admitted: explicit structural negation, never swapped text.
    public static bool IsNotReady(bool ready)
    {
        if (!ready)
        {
            return true;
        }

        return false;
    }

    public static bool IsNotEqualPair(int left, int right) => !(left == right);

    // Admitted: stable opaque current-time operand; the value is never evaluated.
    public static bool IsExpired(DateTime deadline)
    {
        if (deadline < DateTime.UtcNow)
        {
            return true;
        }

        return false;
    }

    // Admitted: supported binary arithmetic as a value operand.
    public static bool IsSumAbove(int left, int right, int threshold)
    {
        if (left + right > threshold)
        {
            return true;
        }

        return false;
    }

    // Admitted: explicit grouping preserved by the tree, not flattened by precedence.
    public static bool IsGroupedDecision(bool first, bool second, bool enabled)
    {
        if ((first || second) && enabled)
        {
            return true;
        }

        return false;
    }

    // Admitted: member receiver identity must not collapse same-named members.
    public static bool HasMoreItems(CountSource left, CountSource right)
    {
        if (left.Count > right.Count)
        {
            return true;
        }

        return false;
    }

    // Unsupported: an invocation receiver is not a stable member operand.
    public static bool HasReturnedOrderItems()
    {
        if (GetOrder().Count > 0)
        {
            return true;
        }

        return false;
    }

    private static CountSource GetOrder() => new();

    // Unsupported: arbitrary property evaluation, including a nested property receiver.
    public static bool IsReadyProperty(PropertySource source) => source.IsReady;

    public static bool HasNestedPropertyCount(PropertySource source) => source.Child.Count > 0;

    // Admitted: exact built-in string and character equality partitions.
    public static bool IsReadyString(string value)
    {
        if (value == "ready")
        {
            return true;
        }

        return false;
    }

    public static bool IsNotEmptyString(string value)
    {
        if (value != "")
        {
            return true;
        }

        return false;
    }

    public static bool IsReadyCharacter(char value)
    {
        if (value == 'r')
        {
            return true;
        }

        return false;
    }

    // Ownership coverage: source predicates nested in statements, loops, and a conditional expression.
    public static bool IsNestedStatement(bool outer, bool inner)
    {
        if (outer)
        {
            if (inner)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsWhileCondition(bool ready)
    {
        while (ready)
        {
            return true;
        }

        return false;
    }

    public static bool IsForCondition(bool ready)
    {
        var found = false;
        for (var index = 0; ready && index < 1; index++)
        {
            found = true;
        }

        return found;
    }

    public static bool IsConditionalExpression(bool flag)
    {
        var selected = flag ? 1 : 0;
        return selected > 0;
    }

    // Admitted: nested and grouped logical-not nodes retain source structure.
    public static bool IsDoubleNegated(bool ready)
    {
        if (!!ready)
        {
            return true;
        }

        return false;
    }

    public static bool IsNeitherReadyNorEnabled(bool ready, bool enabled)
    {
        if (!(ready || enabled))
        {
            return true;
        }

        return false;
    }

    public static bool IsParenthesizedNotReady(bool ready)
    {
        if ((!ready))
        {
            return true;
        }

        return false;
    }

    // Unsupported: user-defined overloaded equality.
    public static bool IsSamePrice(Money left, Money right)
    {
        if (left == right)
        {
            return true;
        }

        return false;
    }

    // Unsupported: ambiguous lifted nullable comparison.
    public static bool AreEqualNullable(int? left, int? right)
    {
        if (left == right)
        {
            return true;
        }

        return false;
    }

    // Unsupported: dynamic operands.
    public static bool IsDynamicEqual(dynamic left, dynamic right)
    {
        if (left == right)
        {
            return true;
        }

        return false;
    }

    // Unsupported: side-effecting increment inside the predicate.
    public static bool IsAfterIncrement(int value)
    {
        if (value++ > 0)
        {
            return true;
        }

        return false;
    }

    // Unsupported: invocation whose result is not a stable opaque value.
    public static bool IsBlank(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        return false;
    }

    // Unsupported: interpolated operand.
    public static bool HasInterpolatedPrefix(string value, string prefix)
    {
        if (value == $"{prefix}:")
        {
            return true;
        }

        return false;
    }
}

public sealed class CountSource
{
    public int Count;
}

public sealed class PropertySource
{
    private int _reads;

    public bool IsReady
    {
        get
        {
            _reads++;
            return _reads > 0;
        }
    }

    public CountSource Child
    {
        get
        {
            _reads++;
            return new CountSource { Count = _reads };
        }
    }
}

/// <summary>Read-only value type with a user-defined equality operator for the unsupported partition.</summary>
public readonly struct Money
{
    public Money(decimal amount)
    {
        Amount = amount;
    }

    public decimal Amount { get; }

    public static bool operator ==(Money left, Money right) => left.Amount == right.Amount;

    public static bool operator !=(Money left, Money right) => !(left == right);

    public override bool Equals(object? obj) => obj is Money other && other.Amount == Amount;

    public override int GetHashCode() => Amount.GetHashCode();
}
