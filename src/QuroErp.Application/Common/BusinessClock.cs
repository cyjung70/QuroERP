using Microsoft.Extensions.DependencyInjection;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Application.Common;

/// <summary>
/// Business date and shown times in the company time zone (BC-BR-318, localization.md "시간대"). Business code asks this
/// clock for today instead of DateTime.Today, TimeProvider.GetLocalNow() or TimeZoneInfo.Local.
/// </summary>
public interface IBusinessClock
{
    /// <summary>The company time zone.</summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>Today in the company time zone.</summary>
    DateOnly Today();

    /// <summary>A time (stored and compared in UTC) as the company time zone shows it, e.g. for a message.</summary>
    DateTimeOffset ToCompanyTime(DateTimeOffset at);

    /// <summary>Forgets the cached time zone, after the company was saved; the next call reads it again.</summary>
    void Reset();
}

/// <summary>
/// Wraps TimeProvider and caches the company time zone. The time zone is read once (the API warms it up at start) and
/// again only after <see cref="Reset"/>, so <see cref="Today"/> stays synchronous.
/// </summary>
/// <param name="loadTimeZone">Reads bc_company.time_zone; null when there is no company yet (the US default applies).</param>
public sealed class BusinessClock(TimeProvider time, Func<string?> loadTimeZone) : IBusinessClock
{
    private readonly Lock _lock = new();
    private TimeZoneInfo? _zone;

    public TimeZoneInfo TimeZone
    {
        get
        {
            lock (_lock)
            {
                return _zone ??= TimeZoneInfo.FindSystemTimeZoneById(
                    loadTimeZone() ?? CountryProfiles.Get(CountryProfiles.Us).DefaultTimeZone);
            }
        }
    }

    public DateOnly Today() => DateOnly.FromDateTime(ToCompanyTime(time.GetUtcNow()).DateTime);

    public DateTimeOffset ToCompanyTime(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, TimeZone);

    public void Reset()
    {
        lock (_lock)
        {
            _zone = null;
        }
    }

    /// <summary>The clock of the host: the time zone comes from the company row through a scope of its own.</summary>
    public static BusinessClock Create(IServiceProvider services) =>
        new(services.GetRequiredService<TimeProvider>(), () => ReadTimeZone(services.GetRequiredService<IServiceScopeFactory>()));

    // Read rarely (start and after a company save), so blocking on the async repository here is acceptable
    private static string? ReadTimeZone(IServiceScopeFactory scopes)
    {
        using var scope = scopes.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        var localization = scope.ServiceProvider.GetRequiredService<ILocalizationRepository>();
        return Task.Run(async () =>
        {
            await using var uow = await unitOfWork.BeginAsync(CancellationToken.None);
            var company = await localization.FindCompanyAsync(uow.Transaction, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            return company?.TimeZone;
        }).GetAwaiter().GetResult();
    }
}
