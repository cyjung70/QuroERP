using System.Data;
using QuroErp.Api.Tests.Bc.Tax;
using QuroErp.Application.Mm.Vendors;
using QuroErp.Domain.Mm.Vendors;

namespace QuroErp.Api.Tests.Mm.Vendors;

/// <summary>
/// Vendors in memory: 701 ACME (balance 70.5, used by a goods receipt) and 702 BETA (KR business number 2208162517).
/// Tax code codes come from the tax code fake's ids.
/// </summary>
public sealed class InMemoryVendorRepository : IVendorRepository
{
    public const long AcmeId = 701;
    public const long BetaId = 702;
    public const string BusinessRegNo = "2208162517";

    public static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<long, string> TaxCodes = new()
    {
        [InMemoryTaxCodeRepository.NoneId] = "NONE",
        [InMemoryTaxCodeRepository.Vat10Id] = "VAT10",
        [InMemoryTaxCodeRepository.Vat0Id] = "VAT0",
    };

    private readonly Lock _lock = new();
    private readonly HashSet<long> _inUse = [AcmeId];
    private long _nextId = 710;

    private readonly Dictionary<long, VendorRow> _rows = new()
    {
        [AcmeId] = Row(AcmeId, "ACME", new(Phone1: "555", ZipCode: "1", ShipZipCode: "1", Terms: "NET_30", ValidFlag: "Y"), 70.5m),
        [BetaId] = Row(BetaId, "BETA", new(Phone1: "556", ZipCode: "2", ShipZipCode: "2", ValidFlag: "Y", BusinessRegNo: BusinessRegNo), 0),
    };

    public Task<VendorRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_rows.GetValueOrDefault(id));
        }
    }

    public Task<VendorRow?> FindAdjacentAsync(long id, bool forward, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var name = _rows[id].VName;
            var ordered = _rows.Values.OrderBy(r => r.VName, StringComparer.Ordinal);
            return Task.FromResult(forward
                ? ordered.FirstOrDefault(r => string.CompareOrdinal(r.VName, name) > 0)
                : ordered.LastOrDefault(r => string.CompareOrdinal(r.VName, name) < 0));
        }
    }

    public Task<(IReadOnlyList<VendorRow> Items, int Total)> SearchAsync(
        VendorSearch search, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var matches = _rows.Values
                .Where(r => search.VName is null || r.VName.Contains(search.VName, StringComparison.OrdinalIgnoreCase))
                .Where(r => search.ValidFlag is null || r.Fields.ValidFlag == search.ValidFlag)
                .OrderBy(r => r.VName, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult<(IReadOnlyList<VendorRow>, int)>((matches.Skip(search.Offset).Take(search.Limit).ToList(), matches.Count));
        }
    }

    public Task<bool> NameExistsAsync(string vName, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_rows.Values.Any(r => r.VName == vName));
        }
    }

    public Task<IReadOnlyList<string>> FindNamesByBusinessRegNoAsync(
        string businessRegNo, long? exceptId, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<string>>(_rows.Values
                .Where(r => r.Fields.BusinessRegNo == businessRegNo && r.Id != exceptId)
                .Select(r => r.VName)
                .Order(StringComparer.Ordinal)
                .ToList());
        }
    }

    public Task<long> InsertAsync(Vendor vendor, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var id = _nextId++;
            _rows[id] = Row(id, vendor.VName, vendor.Fields, 0) with { CreatedBy = createdBy, UpdatedBy = createdBy };
            return Task.FromResult(id);
        }
    }

    public Task<bool> UpdateAsync(
        Vendor vendor, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_rows.TryGetValue(vendor.Id, out var row) || row.UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            _rows[vendor.Id] = Row(vendor.Id, row.VName, vendor.Fields, row.BalAmt) with
            {
                CreatedBy = row.CreatedBy,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = updatedBy,
            };
            return Task.FromResult(true);
        }
    }

    public Task<bool> IsInUseAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_inUse.Contains(id));
        }
    }

    public Task<bool> DeleteAsync(long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_rows.TryGetValue(id, out var row) && row.UpdatedAt == expectedUpdatedAt && _rows.Remove(id));
        }
    }

    private static VendorRow Row(long id, string name, VendorFields fields, decimal balance) => new(
        id, name, fields, balance, fields.TaxCodeId is { } t ? TaxCodes.GetValueOrDefault(t) : null, Created, "seed", Created, "seed");
}
