namespace SixWaveBackend.Models;

public record PurchaseOption(string Id, int PriceNaira, int Admits);

public static class Catalog
{
    public static readonly PurchaseOption[] Options =
    [
        new("regular", 7000, 1),
        new("vip", 10000, 1),
        new("regular-group", 35000, 5),
        new("vip-group", 50000, 5),
        new("table-100k", 100000, 1),
        new("table-150k", 150000, 1),
        new("table-200k", 200000, 1),
        new("table-250k", 250000, 1),
        new("table-300k", 300000, 1),
    ];
}
