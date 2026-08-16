using System.Text.RegularExpressions;

namespace GeneratedAndPartialSource;

[Obsolete("fixture")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Fixture", "SEQ001")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Fixture", "SEQ001")]
public sealed partial class ReservationMatcher : IReservationMatcher
{
    private readonly string normalizedPattern = string.Concat("reservation", "-[0-9]+");

    public event EventHandler? Matched;

    public string Pattern => normalizedPattern;

    public bool IsMatch(string value)
    {
        var matched = ReservationRegex().IsMatch(value);
        if (matched)
        {
            Matched?.Invoke(this, EventArgs.Empty);
        }

        return matched;
    }

    public static ReservationMatcher Create() => new();

    public static bool MatchThroughContract(IReservationMatcher matcher, string value) => matcher.IsMatch(value);

    [GeneratedRegex("reservation-[0-9]+")]
    private static partial Regex ReservationRegex();
}
