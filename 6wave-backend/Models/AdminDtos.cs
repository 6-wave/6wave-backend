namespace SixWaveBackend.Models;

public record AdminLoginRequest(string Email, string Password);

public record AdminUserResponse(string Id, string Name, string Email);
