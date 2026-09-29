namespace SixWaveBackend.Models;

public record AdminTransactionRow(
    string Reference, Guid UserId, int Amount, string Method, string Status,
    DateTimeOffset CreatedAt, string? RecordedBy, string? FailureReason,
    string UserName, string UserReference, string OptionLabel);

public record AdminTransactionsPage(List<AdminTransactionRow> Items, int Total, int Page, int PageSize, int SuccessAmount, int SuccessCount);

public record AdminPassRow(
    Guid Token, Guid UserId, int GuestIndex, string Status, DateTimeOffset? UsedAt, string? ScannedBy,
    string UserName, string UserReference, string OptionLabel, int PassesTotal);

public record AdminPassCounts(int Total, int Used, int Unused, int Void);

public record AdminPassesPage(List<AdminPassRow> Items, int Total, int Page, int PageSize, AdminPassCounts Counts);

public record AdminKindStat(string Kind, int Count, int Revenue);
public record AdminMethodStat(string Method, int Amount);
public record AdminDayStat(string Day, int Count);
public record AdminRegistrationStats(int Total, int Paid, int Pending, int Cancelled);
public record AdminCheckedInStats(int Used, int Total);

public record AdminDashboard(
    AdminRegistrationStats Registrations, int Revenue, int Outstanding, AdminCheckedInStats CheckedIn,
    List<AdminKindStat> ByKind, List<AdminMethodStat> ByMethod, List<AdminDayStat> PerDay, List<AdminTransactionRow> Recent);

public static class AdminReportMapper
{
    public static AdminTransactionRow ToAdminTransactionRow(this Payment p, Registration r)
    {
        var option = Catalog.Options.First(o => o.Id == r.OptionId);
        return new AdminTransactionRow(
            p.Reference, p.RegistrationId, p.AmountNaira, p.Method.ToString().ToUpperInvariant(),
            p.Status.ToString().ToUpperInvariant(), p.CreatedAt, p.RecordedBy, null,
            r.FullName, r.Reference, option.Label);
    }
}
