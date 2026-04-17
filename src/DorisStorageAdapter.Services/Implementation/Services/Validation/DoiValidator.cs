using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DorisStorageAdapter.Services.Implementation.Services.Validation;

internal static partial class DoiValidator
{
    [GeneratedRegex(
        @"^https:\/\/doi\.org\/10\.\d{4,9}\/[-._;()\/:A-Z0-9]+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DoiRegex();

    public static void ThrowIfInvalid(
        string value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (!IsValid(value))
        {
            throw new ValidationException([new(
                Target: paramName,
                Message: "Invalid DOI.")]);
        }
    }

    private static bool IsValid(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        DoiRegex().IsMatch(value);
}