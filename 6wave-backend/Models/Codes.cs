using System.Security.Cryptography;

namespace SixWaveBackend.Models;

public static class Codes
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string GenerateReference()
    {
        Span<char> chars = stackalloc char[5];
        for (var i = 0; i < 5; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return $"WAVE-{new string(chars)}";
    }

    public static string GenerateBackupCode()
    {
        Span<char> chars = stackalloc char[9];
        for (var i = 0; i < 9; i++)
            chars[i] = i == 4 ? '-' : Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    public static string ShortenName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 1 ? fullName : $"{parts[0]} {parts[^1][0]}.";
    }
}
