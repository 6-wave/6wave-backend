namespace SixWaveBackend.Models;

public enum PurchaseKind
{
    Ticket,
    Group,
    Table
}

public record PurchaseOption(string Id, PurchaseKind Kind, string Label, int Admits);

/// <summary>A sale period. It runs through <see cref="EndsOn"/> (a Lagos calendar date); null means open-ended.</summary>
public record SaleWave(string Id, string Label, DateOnly? EndsOn, IReadOnlyDictionary<string, int> Prices);

public static class Catalog
{
    public static readonly PurchaseOption[] Options =
    [
        new("regular", PurchaseKind.Ticket, "Regular", 1),
        new("vip", PurchaseKind.Ticket, "VIP", 1),
        new("regular-group", PurchaseKind.Group, "Regular group", 5),
        new("vip-group", PurchaseKind.Group, "VIP group", 5),
        new("table-100k", PurchaseKind.Table, "Table ₦100k", 1),
        new("table-150k", PurchaseKind.Table, "Table ₦150k", 1),
        new("table-200k", PurchaseKind.Table, "Table ₦200k", 1),
        new("table-250k", PurchaseKind.Table, "Table ₦250k", 1),
        new("table-300k", PurchaseKind.Table, "Table ₦300k", 1),
    ];

    // Tables cost the same in every wave.
    private static Dictionary<string, int> Prices(int regular, int vip, int regularGroup, int vipGroup) => new()
    {
        ["regular"] = regular,
        ["vip"] = vip,
        ["regular-group"] = regularGroup,
        ["vip-group"] = vipGroup,
        ["table-100k"] = 100000,
        ["table-150k"] = 150000,
        ["table-200k"] = 200000,
        ["table-250k"] = 250000,
        ["table-300k"] = 300000,
    };

    /// <summary>In date order. The last wave has no end, so a price always resolves.</summary>
    public static readonly SaleWave[] Waves =
    [
        new("wave-1", "Wave 1", new DateOnly(2026, 10, 20), Prices(5000, 15000, 22500, 67500)),
        new("wave-2", "Wave 2", new DateOnly(2026, 10, 30), Prices(7000, 18000, 32500, 81000)),
        new("d-day", "D Day", null, Prices(10000, 20000, 45000, 90000)),
    ];

    /// <summary>Lagos is UTC+1 all year (no daylight saving).</summary>
    public static DateOnly LagosToday(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(1)).DateTime);

    public static SaleWave CurrentWave(DateTimeOffset now)
    {
        var today = LagosToday(now);
        return Waves.First(w => w.EndsOn is null || today <= w.EndsOn);
    }

    public static int PriceFor(string optionId, DateTimeOffset now) => CurrentWave(now).Prices[optionId];

    /// <summary>
    /// What a registration costs. Paid ones keep the price they were paid at; unpaid ones owe the price of the
    /// wave on sale today, so missing a wave's deadline means paying the next wave's price.
    /// </summary>
    public static int AmountDue(Registration r, DateTimeOffset now) =>
        r.PaymentStatus == PaymentStatus.Paid ? r.PriceNaira : PriceFor(r.OptionId, now);
}
