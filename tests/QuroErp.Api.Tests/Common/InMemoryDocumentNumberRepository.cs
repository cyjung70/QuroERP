using System.Data;
using QuroErp.Application.Common.DocumentNumbers;

namespace QuroErp.Api.Tests.Common;

/// <summary>sys_document_number in memory: the last number per (transaction type, period).</summary>
public sealed class InMemoryDocumentNumberRepository : IDocumentNumberRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string, string), string> _last = [];

    public Task<string?> LockLastAsync(string transType, string period, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_last.GetValueOrDefault((transType, period)));
        }
    }

    public Task<bool> TryCreateAsync(
        string transType, string period, string firstNo, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_last.TryAdd((transType, period), firstNo));
        }
    }

    public Task SaveLastAsync(string transType, string period, string lastNo, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _last[(transType, period)] = lastNo;
            return Task.CompletedTask;
        }
    }
}
