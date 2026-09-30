namespace SixWaveBackend.Models;

public record ScanRequest(string Code);

public record ScanResponse(
    string Decision, string? Reference, string? FullName, string? OptionId, string? PaymentStatus, int? AmountNaira);
