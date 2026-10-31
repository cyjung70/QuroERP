using QuroErp.Application.Common;

namespace QuroErp.Api.Common;

/// <summary>
/// Reads the company time zone when the API starts, so the first request does not wait for it (BC-BR-318).
/// A database that is not reachable yet only delays the read to the first use.
/// </summary>
public sealed partial class BusinessClockWarmUp(IBusinessClock clock, ILogger<BusinessClockWarmUp> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = clock.TimeZone;
        }
        catch (Exception e)
        {
            clock.Reset();
            LogNotRead(logger, e);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The company time zone could not be read at start; it is read on first use.")]
    private static partial void LogNotRead(ILogger logger, Exception exception);
}
