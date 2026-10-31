using QuroErp.Contracts.Bc.Companies;
using QuroErp.Contracts.Common.Auth;

namespace QuroErp.Web.Components.Pages.Bc.Models;

/// <summary>The company form (BC-UC-007). The API normalizes and checks every value (BC-BR-302~316).</summary>
public sealed class CompanyModel
{
    public string? Name { get; set; }
    public string? Billto { get; set; }
    public string? Shipto { get; set; }
    public string? Contact { get; set; }
    public string? TelNo { get; set; }
    public string? FaxNo { get; set; }
    public string? RegisterNo { get; set; }
    public string? Represent { get; set; }
    public string? Email { get; set; }
    public string? BusinessType { get; set; }
    public string? BusinessItem { get; set; }
    public string? CountryCode { get; set; }
    public string? BaseCurrencyCode { get; set; }
    public string? DefaultLangCode { get; set; }
    public string? TimeZone { get; set; }
    public string? BillingMode { get; set; }
    public bool HasLogo { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static CompanyModel From(CompanyDto dto) => new()
    {
        Name = dto.Name,
        Billto = dto.Billto,
        Shipto = dto.Shipto,
        Contact = dto.Contact,
        TelNo = dto.TelNo,
        FaxNo = dto.FaxNo,
        RegisterNo = dto.RegisterNo,
        Represent = dto.Represent,
        Email = dto.Email,
        BusinessType = dto.BusinessType,
        BusinessItem = dto.BusinessItem,
        CountryCode = dto.CountryCode,
        BaseCurrencyCode = dto.BaseCurrencyCode,
        DefaultLangCode = dto.DefaultLangCode,
        TimeZone = dto.TimeZone,
        BillingMode = dto.BillingMode,
        HasLogo = dto.HasLogo,
        UpdatedAt = dto.UpdatedAt,
    };

    public UpdateCompanyRequest ToRequest() => new(
        Name, Billto, Shipto, Contact, TelNo, FaxNo, RegisterNo, Represent, Email, BusinessType, BusinessItem, CountryCode, BaseCurrencyCode,
        DefaultLangCode, TimeZone, BillingMode, UpdatedAt);
}

/// <summary>The security settings form (BC-BR-309). Numbers are decimal? for QuroNumber; the API checks the ranges.</summary>
public sealed class SecuritySettingModel
{
    public decimal? MaxFailedAttempts { get; set; }
    public decimal? LockoutMinutes { get; set; }
    public bool ShowRemainingAttempts { get; set; }
    public bool AutoLoginEnabled { get; set; }
    public decimal? AutoLoginValidDays { get; set; }
    public decimal? SessionIdleMinutes { get; set; }
    public bool AllowConcurrentLogin { get; set; }
    public decimal? PasswordMinLength { get; set; }
    public decimal? PasswordRequiredCategories { get; set; }
    public decimal? PasswordExpireDays { get; set; }
    public decimal? PasswordHistoryCount { get; set; }
    public string? PasswordResetMode { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Whether auto login was on when the settings were read; turning it off revokes every token (BC-BR-310).</summary>
    public bool AutoLoginWasEnabled { get; private set; }

    public static SecuritySettingModel From(AuthSettingDto dto) => new()
    {
        MaxFailedAttempts = dto.MaxFailedAttempts,
        LockoutMinutes = dto.LockoutMinutes,
        ShowRemainingAttempts = dto.ShowRemainingAttempts,
        AutoLoginEnabled = dto.AutoLoginEnabled,
        AutoLoginValidDays = dto.AutoLoginValidDays,
        SessionIdleMinutes = dto.SessionIdleMinutes,
        AllowConcurrentLogin = dto.AllowConcurrentLogin,
        PasswordMinLength = dto.PasswordMinLength,
        PasswordRequiredCategories = dto.PasswordRequiredCategories,
        PasswordExpireDays = dto.PasswordExpireDays,
        PasswordHistoryCount = dto.PasswordHistoryCount,
        PasswordResetMode = dto.PasswordResetMode,
        UpdatedAt = dto.UpdatedAt,
        AutoLoginWasEnabled = dto.AutoLoginEnabled,
    };

    // A blank or fractional number goes as -1 or truncated; the API rejects what is out of range (BC-E-309)
    public AuthSettingDto ToRequest() => new(
        Whole(MaxFailedAttempts), Whole(LockoutMinutes), ShowRemainingAttempts, AutoLoginEnabled, Whole(AutoLoginValidDays),
        Whole(SessionIdleMinutes), AllowConcurrentLogin, Whole(PasswordMinLength), Whole(PasswordRequiredCategories), Whole(PasswordExpireDays),
        Whole(PasswordHistoryCount), PasswordResetMode ?? "", UpdatedAt);

    private static int Whole(decimal? value) => value is { } v && v >= int.MinValue && v <= int.MaxValue ? (int)v : -1;
}
