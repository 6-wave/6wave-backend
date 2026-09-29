namespace SixWaveBackend.Models;

public record CreateRegistrationRequest(string OptionId, string FullName, string Phone, string Email);

public record RegistrationQrResponse(Guid RegistrationId, List<string> Tokens);

public record RegistrationResponse(
    Guid Id,
    string Reference,
    string DisplayName,
    string OptionId,
    string PaymentStatus,
    string Status,
    DateTimeOffset CreatedAt);

public static class RegistrationMapper
{
    public static RegistrationResponse ToResponse(this Registration registration) => new(
        registration.Id, registration.Reference, Codes.ShortenName(registration.FullName),
        registration.OptionId, registration.PaymentStatus.ToString().ToUpperInvariant(),
        registration.Status.ToString().ToUpperInvariant(), registration.CreatedAt);
}
