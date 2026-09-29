namespace SixWaveBackend.Models;

public enum PaymentMethod
{
    Paystack,
    Pos,
    Cash,
    Bank_Transfer
}

public enum PaymentTransactionStatus
{
    Pending,
    Success,
    Failed,
    Abandoned
}

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Reference { get; set; }
    public required Guid RegistrationId { get; set; }
    public Registration? Registration { get; set; }
    public required int AmountNaira { get; set; }
    public required PaymentMethod Method { get; set; }
    public PaymentTransactionStatus Status { get; set; } = PaymentTransactionStatus.Success;
    public string? RecordedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
