namespace RelocatableIdentity;

public sealed class ReservationService
{
    public string Confirm(int reservationId) => Formatter.Format(reservationId);
}

internal static class Formatter
{
    public static string Format(int reservationId) => $"reservation-{reservationId}";
}
