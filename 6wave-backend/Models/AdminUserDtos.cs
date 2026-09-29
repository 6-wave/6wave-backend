namespace SixWaveBackend.Models;

public record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);

public record AdminPurchaseOption(string Id, string Kind, string Label, int PriceNaira, int Admits);

public record AdminUserRow(
    Guid Id, string Reference, string FullName, string Phone, string Email,
    string OptionId, string PaymentStatus, string Status, DateTimeOffset CreatedAt,
    AdminPurchaseOption Option, int Amount, int PassesUsed, int PassesTotal);

public record AdminPass(
    Guid Token, Guid UserId, int GuestIndex, string Status,
    DateTimeOffset? UsedAt, string? ScannedBy);

public record AdminTransaction(
    string Reference, Guid UserId, int Amount, string Method, string Status,
    DateTimeOffset CreatedAt, string? RecordedBy, string? FailureReason);

public record AdminUserDetail(AdminUserRow User, List<AdminPass> Passes, List<AdminTransaction> Transactions);

public record RecordPaymentRequest(string Method);

public static class AdminMapper
{
    public static AdminUserRow ToAdminUserRow(this Registration r)
    {
        var option = Catalog.Options.First(o => o.Id == r.OptionId);
        return new AdminUserRow(
            r.Id, r.Reference, r.FullName, r.PhoneNumber, r.Email,
            r.OptionId, r.PaymentStatus.ToString().ToUpperInvariant(), r.Status.ToString().ToUpperInvariant(), r.CreatedAt,
            new AdminPurchaseOption(option.Id, option.Kind.ToString().ToUpperInvariant(), option.Label, option.PriceNaira, option.Admits),
            option.PriceNaira,
            r.Tickets.Count(t => t.Status == TicketStatus.Used),
            r.Tickets.Count);
    }

    public static AdminPass ToAdminPass(this Ticket t) => new(
        t.Id, t.RegistrationId, t.GuestIndex, t.Status.ToString().ToUpperInvariant(), t.UsedAt, null);

    public static AdminTransaction ToAdminTransaction(this Payment p) => new(
        p.Reference, p.RegistrationId, p.AmountNaira, p.Method.ToString().ToUpperInvariant(),
        p.Status.ToString().ToUpperInvariant(), p.CreatedAt, p.RecordedBy, null);
}
