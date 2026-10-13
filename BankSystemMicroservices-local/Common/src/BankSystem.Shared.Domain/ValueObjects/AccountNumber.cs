using BankSystem.Shared.Domain.Exceptions;

namespace BankSystem.Shared.Domain.ValueObjects;

/// <summary>
/// Represents a bank account number with validation and formatting logic.
/// </summary>
public record AccountNumber
{
    private const int MaximumIbanLength = 34;
    private const int LegacyAccountNumberLength = 10;
    private static readonly IReadOnlyDictionary<string, int> IbanLengths = new Dictionary<string, int>
    {
        ["BE"] = 16,
        ["DE"] = 22,
        ["FR"] = 27,
        ["NL"] = 18,
    };

    public string Value { get; }

    public AccountNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("Account number cannot be null or empty");

        var cleanValue = Normalize(value);

        if (!IsValidFormat(cleanValue))
            throw new DomainException(
                $"Invalid account number format: {value}. Expected a supported IBAN."
            );

        Value = cleanValue;
    }

    /// <summary>
    /// Generates a new unique account number.
    /// </summary>
    /// <returns>A new AccountNumber instance with generated value</returns>
    public static AccountNumber Generate(string countryCode = "BE")
    {
        var country = countryCode.Trim().ToUpperInvariant();
        if (!IbanLengths.TryGetValue(country, out var ibanLength))
            throw new DomainException($"Unsupported IBAN country: {countryCode}");

        var bbanLength = ibanLength - 4;
        var bban = country == "NL"
            ? "BKSV" + RandomDigits(bbanLength - 4)
            : RandomDigits(bbanLength);

        var checksum = CalculateChecksum(country, bban);
        return new AccountNumber($"{country}{checksum:D2}{bban}");
    }

    /// <summary>
    /// Validates the account number format.
    /// </summary>
    /// <param name="value">The account number to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValidFormat(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var cleanValue = Normalize(value);

        // Temporary compatibility for accounts created before IBAN support.
        if (cleanValue.Length == LegacyAccountNumberLength && cleanValue.All(char.IsDigit))
            return true;

        if (cleanValue.Length is < 5 or > MaximumIbanLength || !cleanValue.All(char.IsLetterOrDigit))
            return false;

        var country = cleanValue[..2];
        return IbanLengths.TryGetValue(country, out var expectedLength)
            && cleanValue.Length == expectedLength
            && int.TryParse(cleanValue[2..4], out _)
            && Mod97(cleanValue[4..] + cleanValue[..4]) == 1;
    }

    /// <summary>
    /// Gets a masked version of the account number for display purposes.
    /// </summary>
    /// <returns>Masked account number (e.g., ******5678)</returns>
    public string GetMaskedValue()
    {
        var suffix = Value[^4..];
        return new string('*', Value.Length - 4) + suffix;
    }

    public string ToDisplayString() => string.Join(" ", Enumerable.Range(0, (Value.Length + 3) / 4).Select(index => Value.Substring(index * 4, Math.Min(4, Value.Length - index * 4))));

    public override string ToString() => Value;

    public static implicit operator string(AccountNumber accountNumber) => accountNumber.Value;

    public static explicit operator AccountNumber(string value) => new(value);

    private static string Normalize(string value) => value.Trim().Replace(" ", string.Empty).ToUpperInvariant();

    private static string RandomDigits(int length)
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(length);
        return string.Concat(bytes.Select(value => (char)('0' + value % 10)));
    }

    private static int CalculateChecksum(string country, string bban)
    {
        var remainder = Mod97(bban + country + "00");
        return 98 - remainder;
    }

    private static int Mod97(string value)
    {
        var remainder = 0;
        foreach (var character in value)
        {
            var digits = char.IsLetter(character) ? (character - 'A' + 10).ToString() : character.ToString();
            foreach (var digit in digits)
                remainder = (remainder * 10 + (digit - '0')) % 97;
        }
        return remainder;
    }
}
