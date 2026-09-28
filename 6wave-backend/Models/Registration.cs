namespace SixWaveBackend.Models;

public enum PaymentStatus
{
    Pending,
    Paid
}

public enum RegistrationStatus
{
    Confirmed,
    Cancelled
}
 
public class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid RegistrationId { get; set; }
    public Registration? Registration { get; set; }
    public required string BackupCode { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

public class Registration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Reference { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public required string PhoneNumber { get; set; }
    public required string OptionId { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public RegistrationStatus Status { get; set; } = RegistrationStatus.Confirmed;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Ticket> Tickets { get; set; } = [];
}
