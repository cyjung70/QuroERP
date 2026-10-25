namespace QuroErp.Application.Common.Codes;

/// <summary>Validates code values before they are saved to business tables (codes.md).</summary>
public interface ICodeValidator
{
    /// <summary>
    /// Throws DomainException(Common-E-002) when value is not an active Minor code of majorCode.
    /// A null or blank value passes; required fields are checked separately.
    /// </summary>
    Task EnsureValidAsync(string majorCode, string? value, CancellationToken cancellationToken);
}
