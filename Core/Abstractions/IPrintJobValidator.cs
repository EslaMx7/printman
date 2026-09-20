using OhMyPrinter.Core.Models;

namespace OhMyPrinter.Core.Abstractions;

public record ValidationResult(bool IsValid, string? ErrorMessage = null)
{
    public static ValidationResult Ok() => new(true);
    public static ValidationResult Fail(string error) => new(false, error);
}

public interface IPrintJobValidator
{
    /// <summary>
    /// Validates a print job request before execution.
    /// </summary>
    Task<ValidationResult> ValidateAsync(PrintJobRequest request);
}
