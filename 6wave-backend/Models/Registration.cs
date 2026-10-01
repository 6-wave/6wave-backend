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
 
public enum TicketStatus
{
    Unused,
    Used,
    Void
}

public class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid RegistrationId { get; set; }
    public Registration? Registration { get; set; }
    public required string BackupCode { get; set; }
    public int GuestIndex { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Unused;
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
    /// <summary>The price in force when this was booked. Wave changes never touch it.</summary>
    public int PriceNaira { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public RegistrationStatus Status { get; set; } = RegistrationStatus.Confirmed;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Ticket> Tickets { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}
