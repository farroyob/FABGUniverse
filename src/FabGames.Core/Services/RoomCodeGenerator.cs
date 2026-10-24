using System.Security.Cryptography;

namespace FabGames.Core.Services;

/// <summary>Generates short room codes from an alphabet without look-alike characters (no 0/O, 1/I/L).</summary>
public static class RoomCodeGenerator
{
    public const int CodeLength = 6;
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string Generate()
    {
        Span<char> chars = stackalloc char[CodeLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>Normalizes user input (trim, upper-case). Returns an empty string for malformed codes.</summary>
    public static string Normalize(string? input)
    {
        var code = input?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code.Length != CodeLength)
        {
            return string.Empty;
        }

        foreach (var c in code)
        {
            if (!Alphabet.Contains(c))
            {
                return string.Empty;
            }
        }

        return code;
    }
}
