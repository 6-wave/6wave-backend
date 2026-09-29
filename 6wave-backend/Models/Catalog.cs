namespace SixWaveBackend.Models;

public enum PurchaseKind
{
    Ticket,
    Group,
    Table
}

public record PurchaseOption(string Id, PurchaseKind Kind, string Label, int PriceNaira, int Admits);

public static class Catalog
{
    public static readonly PurchaseOption[] Options =
    [
        new("regular", PurchaseKind.Ticket, "Regular", 7000, 1),
        new("vip", PurchaseKind.Ticket, "VIP", 10000, 1),
        new("regular-group", PurchaseKind.Group, "Regular group", 35000, 5),
        new("vip-group", PurchaseKind.Group, "VIP group", 50000, 5),
        new("table-100k", PurchaseKind.Table, "Table ₦100k", 100000, 1),
        new("table-150k", PurchaseKind.Table, "Table ₦150k", 150000, 1),
        new("table-200k", PurchaseKind.Table, "Table ₦200k", 200000, 1),
        new("table-250k", PurchaseKind.Table, "Table ₦250k", 250000, 1),
        new("table-300k", PurchaseKind.Table, "Table ₦300k", 300000, 1),
    ];
}
