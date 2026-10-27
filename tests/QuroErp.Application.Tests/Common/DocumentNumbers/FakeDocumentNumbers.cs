using System.Data;
using QuroErp.Application.Common.DocumentNumbers;

namespace QuroErp.Application.Tests.Common.DocumentNumbers;

/// <summary>In-memory sys_document_number. Locked records every LockLastAsync call in order.</summary>
public sealed class FakeDocumentNumbers : IDocumentNumberRepository
{
    public Dictionary<(string TransType, string Period), string> Last { get; } = [];

    public List<(string TransType, string Period)> Locked { get; } = [];

    /// <summary>Simulates another transaction creating the counter between LockLastAsync and TryCreateAsync.</summary>
    public string? CreatedByOther { get; set; }

    public Task<string?> LockLastAsync(string transType, string period, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        Locked.Add((transType, period));
        return Task.FromResult(Last.GetValueOrDefault((transType, period)));
    }

    public Task<bool> TryCreateAsync(
        string transType, string period, string firstNo, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (CreatedByOther is not null)
        {
            Last[(transType, period)] = CreatedByOther;
            CreatedByOther = null;
            return Task.FromResult(false);
        }
        return Task.FromResult(Last.TryAdd((transType, period), firstNo));
    }

    public Task SaveLastAsync(string transType, string period, string lastNo, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        Last[(transType, period)] = lastNo;
        return Task.CompletedTask;
    }
}
