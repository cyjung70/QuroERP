using System.Data;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Contracts.Sd.Assemblies;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Lots;
using QuroErp.Domain.Sd.Assemblies;

namespace QuroErp.Application.Sd.Assemblies;

/// <summary>
/// Set assembly and disassembly (SD-UC-055). A save locks the Set and component item rows first, as every stock save does
/// (inventory.md step 0), so the FIFO allocation it reads cannot change before its movements are posted.
/// </summary>
public sealed class AssemblyService(
    IAssemblyRepository assemblies,
    IInventoryRepository stock,
    ISetComponentRepository sets,
    ILocalizationRepository localization,
    InventoryService inventory,
    DocumentNumberService numbers,
    IBusinessClock clock,
    IUnitOfWorkFactory unitOfWork)
{
    public const string RequiredCode = "Common-E-005";
    public const string InvalidCode = "Common-E-002";
    public const string NotFoundCode = "Common-E-006";

    /// <summary>Assemblies and disassemblies, newest first, at most <see cref="AssemblyRoutes.MaxListSize"/> rows.</summary>
    /// <exception cref="DomainException">422 Common-E-002 for an unknown kind.</exception>
    public async Task<IReadOnlyList<AssemblySummaryDto>> SearchAsync(AssemblySearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var kind = query.Kind?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(kind) && !AssemblyRules.IsKind(kind))
        {
            throw new DomainException(InvalidCode, "kind", query.Kind!);
        }
        var filter = new AssemblyFilter(query.From, query.To, query.SetItemId, string.IsNullOrEmpty(kind) ? null : kind, AssemblyRoutes.MaxListSize);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await assemblies.SearchAsync(filter, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return [.. rows.Select(r => r.ToSummary())];
    }

    /// <summary>One assembly or disassembly with its component lines.</summary>
    /// <exception cref="NotFoundException">404 Common-E-006.</exception>
    public async Task<AssemblyDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await assemblies.FindAsync(id, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        var lines = await assemblies.FindLinesAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return new AssemblyDto(row.ToSummary(), row.Remarks, lines);
    }

    /// <summary>
    /// SD-BR-518: the components of qty Sets and their FIFO allocation in the warehouse on the date, at the moving average cost now.
    /// Nothing is locked or saved; a short component shows its ShortQty instead of an error.
    /// </summary>
    /// <exception cref="DomainException">422 Common-E-005 / Common-E-002 for a missing or wrong condition, SD-E-509 a Set without components.</exception>
    public async Task<AssemblyPreviewDto> PreviewAsync(AssemblyPreviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (setItemId, qty, warehouseId) = Required(query.SetItemId, query.Qty, query.WarehouseId);
        var dt = query.AssemblyDt ?? clock.Today();
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        await EnsureWarehouseAsync(warehouseId, tx, cancellationToken);
        var (set, components) = await ReadSetAsync(setItemId, tx, cancellationToken);
        var items = (await assemblies.FindItemsAsync([.. components.Select(c => c.ComponentItemId)], tx, cancellationToken)).ToDictionary(i => i.Id);
        var rows = new List<AssemblyPreviewComponentDto>();
        foreach (var component in components)
        {
            var item = items[component.ComponentItemId];
            var needed = qty * component.ComponentQty;
            var balances = await stock.FindKeyBalancesAsync(item.Id, warehouseId, dt, tx, cancellationToken);
            var picks = StockSuggestion.Fifo(balances, needed);
            rows.Add(new AssemblyPreviewComponentDto(
                item.Id, item.ItemCd, item.ItemName, component.ComponentQty, needed, StockSuggestion.ShortQty(picks, needed),
                [.. picks.Select(p => new AssemblyPreviewPickDto(
                    p.LocationId, p.LocationCode, p.LotId, p.LotNo, p.Qty, item.MovingAverageCost, p.Qty * item.MovingAverageCost))]));
        }
        await uow.CommitAsync(cancellationToken);
        return new AssemblyPreviewDto(set.Id, qty, warehouseId, rows, rows.Sum(r => r.Picks.Sum(p => p.Amt)));
    }

    /// <summary>
    /// SD-BR-514, SD-BR-515, SD-BR-518, MM-BR-368: issues every component (BI) from its FIFO Locations and Lots and receives the
    /// Set (BP) at their issue amount, in one transaction. A Lot-managed Set gets a new Lot linked to the component Lots.
    /// </summary>
    /// <exception cref="DomainException">
    /// 422 Common-E-005 / Common-E-002 for a missing or wrong value, SD-E-509 a Set without components, SD-E-514 a component
    /// short in any negative stock mode, and the stock errors of <see cref="InventoryService.PostAsync"/> (closed month, a later
    /// or month-end balance below zero, a Location the Set cannot use).
    /// </exception>
    /// <exception cref="ConcurrencyException">409 when the Set's components changed while it was saved.</exception>
    public async Task<AssemblyCreatedDto> AssembleAsync(SaveAssemblyRequest request, string user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (setItemId, qty, warehouseId) = Required(request.SetItemId, request.Qty, request.WarehouseId);
        var dt = request.AssemblyDt ?? clock.Today();
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var warehouseCode = await EnsureWarehouseAsync(warehouseId, tx, cancellationToken);
        var location = await LocationAsync(warehouseId, request.LocationId, tx, cancellationToken);
        var (set, read) = await ReadSetAsync(setItemId, tx, cancellationToken);
        var policy = await DecimalsAsync(tx, cancellationToken);
        policy.Policy.EnsureQty(DecimalModule.Mm, qty, "qty");

        // inventory.md step 0: every item row in one pass in id order; then the components must still be the ones read
        var locked = (await stock.LockItemsAsync([.. read.Select(c => c.ComponentItemId).Append(set.Id).Distinct()], tx, cancellationToken))
            .ToDictionary(i => i.ItemId);
        var components = await sets.GetComponentsAsync([set.Id], tx, cancellationToken);
        if (!Same(read, components))
        {
            throw new ConcurrencyException();
        }

        var issues = new List<StockMovementRequest>();
        var picks = new List<(long ItemId, StockPick Pick)>();
        foreach (var component in components)
        {
            var item = locked[component.ComponentItemId];
            var balances = await stock.FindKeyBalancesAsync(item.ItemId, warehouseId, dt, tx, cancellationToken);
            foreach (var pick in AssemblyRules.Allocate(set.ItemCd, item.ItemCd, warehouseCode, balances, qty * component.ComponentQty))
            {
                picks.Add((item.ItemId, pick));
            }
        }

        var assemblyNo = await numbers.NextAsync(DocumentNumberFormat.Assembly, dt, user, tx, cancellationToken);
        var id = await assemblies.InsertAsync(
            new NewAssembly(assemblyNo, AssemblyRules.Assemble, dt, set.Id, qty, warehouseId, location.Id, null, null, Trim(request.Remarks)),
            user, tx, cancellationToken);
        for (var i = 0; i < picks.Count; i++)
        {
            var (itemId, pick) = picks[i];
            issues.Add(new StockMovementRequest(
                MovementType.AssemblyIssue, dt, itemId, warehouseId, -pick.Qty, RefKey: assemblyNo, RefSeq: i + 1,
                LotId: pick.LotId, LocationId: pick.LocationId));
        }
        var issued = await inventory.PostAsync(issues, user, tx, cancellationToken);
        var issueAmount = -issued.Sum(m => m.Amount);

        // MM-BR-353, MM-BR-365: a Lot-managed Set gets a new automatic Lot on the assembly date; another Set uses Lot '*'
        LotRef? setLot = null;
        if (locked[set.Id].LotManaged)
        {
            var lotNo = LotRules.AutoNumber(dt, await stock.GetLastAutoLotSeqAsync(set.Id, LotRules.AutoNumberPrefix(dt), tx, cancellationToken));
            setLot = await stock.EnsureLotAsync(set.Id, lotNo, dt, user, tx, cancellationToken);
        }
        var unitPrice = AssemblyRules.UnitPrice(issueAmount, qty, policy.PriceDecimals, policy.Policy.SettingOf(DecimalModule.Mm).RoundingMode);
        var received = await inventory.PostAsync(
            [new StockMovementRequest(MovementType.SetAssembly, dt, set.Id, warehouseId, qty, unitPrice, assemblyNo,
                LotId: setLot?.Id, Amount: issueAmount, LocationId: location.Id)],
            user, tx, cancellationToken);
        var setLotId = received[0].LotId!.Value;
        await assemblies.SetSetLotAsync(id, setLotId, setLot is not null, user, tx, cancellationToken);

        await assemblies.InsertLinesAsync(id, [.. issued.Select((m, i) => new NewAssemblyLine(
            i + 1, m.ItemId, -m.Qty, warehouseId, m.LocationId!.Value, m.LotId!.Value, m.Prc, -m.Amount))], user, tx, cancellationToken);
        if (setLot is not null)
        {
            // MM-BR-368: the component Lots that went into the Set Lot; plain components have no Lot to trace
            var links = picks.Where(p => p.Pick.LotNo != StockKeyRules.System)
                .GroupBy(p => p.Pick.LotId)
                .Select(g => new NewLotLink(setLot.Id, g.Key, g.Sum(p => p.Pick.Qty)))
                .ToList();
            await assemblies.InsertLotLinksAsync(id, links, user, tx, cancellationToken);
        }
        await uow.CommitAsync(cancellationToken);
        return new AssemblyCreatedDto(id, assemblyNo, setLot?.LotNo ?? StockKeyRules.System);
    }

    /// <summary>
    /// SD-BR-516: issues qty of the assembly's Set Lot (DA) from its Locations in FIFO order and gives the components back (DR) to
    /// the Lots and at the prices the assembly used, the last used first. Location-managed components are received at the
    /// Location asked, others at '*'.
    /// </summary>
    /// <exception cref="NotFoundException">404 Common-E-006.</exception>
    /// <exception cref="DomainException">
    /// 422 Common-E-005 / Common-E-002 for a missing or wrong value, SD-E-516 when the assembly is voided, is a disassembly or has
    /// less left, and the stock errors of <see cref="InventoryService.PostAsync"/> (MM-E-356 / MM-E-001 when the Set is short).
    /// </exception>
    public async Task<AssemblyCreatedDto> DisassembleAsync(long id, DisassembleRequest request, string user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var qty = request.Qty ?? throw new DomainException(RequiredCode, "qty");
        if (qty <= 0)
        {
            throw new DomainException(InvalidCode, "qty", qty);
        }
        var dt = request.DisassemblyDt ?? clock.Today();
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var source = await assemblies.LockAsync(id, tx, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        if (source.Kind != AssemblyRules.Assemble || source.Status != AssemblyRules.Open)
        {
            throw new DomainException(AssemblyRules.DisassemblyCode, source.AssemblyNo, 0m);
        }
        AssemblyRules.EnsureCanDisassemble(source.AssemblyNo, qty, source.QtyLeft);
        var policy = await DecimalsAsync(tx, cancellationToken);
        policy.Policy.EnsureQty(DecimalModule.Mm, qty, "qty");
        var location = await LocationAsync(source.WarehouseId, request.LocationId, tx, cancellationToken);

        var used = (await assemblies.FindLinesAsync(id, tx, cancellationToken))
            .Select(l => new AssemblyPart(l.Seq, l.ItemId, l.LotId, l.Qty, l.Prc)).ToList();
        var locked = (await stock.LockItemsAsync([.. used.Select(p => p.ItemId).Append(source.SetItemId).Distinct()], tx, cancellationToken))
            .ToDictionary(i => i.ItemId);
        var returned = await assemblies.FindReturnedQtyAsync(id, tx, cancellationToken);

        // The Set Lot from its Locations in FIFO order; a short part stays on Location '*' for the stock checks to refuse
        var setLotId = source.SetLotId!.Value;
        var balances = (await stock.FindKeyBalancesAsync(source.SetItemId, source.WarehouseId, dt, tx, cancellationToken))
            .Where(b => b.LotId == setLotId);
        var setPicks = StockSuggestion.Fifo(balances, qty);
        var shortQty = StockSuggestion.ShortQty(setPicks, qty);

        var number = await numbers.NextAsync(DocumentNumberFormat.Assembly, dt, user, tx, cancellationToken);
        var newId = await assemblies.InsertAsync(
            new NewAssembly(number, AssemblyRules.Disassemble, dt, source.SetItemId, qty, source.WarehouseId, location.Id, setLotId, id, null),
            user, tx, cancellationToken);
        var issues = setPicks
            .Select(p => new StockMovementRequest(MovementType.DisassemblyIssue, dt, source.SetItemId, source.WarehouseId, -p.Qty,
                RefKey: number, LotId: setLotId, LocationId: p.LocationId))
            .ToList();
        if (shortQty > 0)
        {
            issues.Add(new StockMovementRequest(MovementType.DisassemblyIssue, dt, source.SetItemId, source.WarehouseId, -shortQty,
                RefKey: number, LotId: setLotId));
        }
        await inventory.PostAsync(issues, user, tx, cancellationToken);

        var parts = used.GroupBy(p => p.ItemId)
            .SelectMany(g => AssemblyRules.Return([.. g.OrderBy(p => p.Seq)], source.Qty, returned.GetValueOrDefault(g.Key), qty))
            .ToList();
        var receipts = parts.Select((p, i) => new StockMovementRequest(
            MovementType.DisassemblyReceipt, dt, p.ItemId, source.WarehouseId, p.Qty, p.Prc, number, i + 1, LotId: p.LotId,
            LocationId: locked[p.ItemId].LocationManaged ? location.Id : null)).ToList();
        var received = await inventory.PostAsync(receipts, user, tx, cancellationToken);
        await assemblies.InsertLinesAsync(newId, [.. received.Select((m, i) => new NewAssemblyLine(
            i + 1, m.ItemId, m.Qty, source.WarehouseId, m.LocationId!.Value, m.LotId!.Value, m.Prc, m.Amount))], user, tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return new AssemblyCreatedDto(newId, number, source.SetLotNo ?? StockKeyRules.System);
    }

    /// <summary>
    /// MM-BR-601, SD-BR-516: reverses the movements of an assembly or a disassembly, dated today or its own date when later. An
    /// assembly with an open disassembly cannot be voided.
    /// </summary>
    /// <exception cref="NotFoundException">404 Common-E-006.</exception>
    /// <exception cref="ConcurrencyException">409 Common-E-004 when it changed or was voided after it was read.</exception>
    /// <exception cref="ConflictException">409 SD-E-516 when the assembly has an open disassembly.</exception>
    public async Task<AssemblySummaryDto> VoidAsync(long id, VoidAssemblyRequest request, string user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var row = await assemblies.LockAsync(id, tx, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        if (row.UpdatedAt != request.UpdatedAt || row.Status != AssemblyRules.Open)
        {
            throw new ConcurrencyException();
        }
        if (row.Kind == AssemblyRules.Assemble && row.QtyLeft != row.Qty)
        {
            throw new ConflictException(AssemblyRules.DisassemblyCode, row.AssemblyNo, row.QtyLeft);
        }
        var today = clock.Today();
        var movementIds = await assemblies.FindOpenMovementIdsAsync(row.AssemblyNo, tx, cancellationToken);
        if (movementIds.Count > 0)
        {
            await inventory.ReverseAsync(movementIds, today < row.AssemblyDt ? row.AssemblyDt : today, user, tx, cancellationToken);
        }
        await assemblies.VoidAsync(id, user, tx, cancellationToken);
        var voided = await assemblies.FindAsync(id, tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return voided!.ToSummary();
    }

    private static (long SetItemId, decimal Qty, long WarehouseId) Required(long? setItemId, decimal? qty, long? warehouseId)
    {
        var set = setItemId ?? throw new DomainException(RequiredCode, "setItemId");
        var value = qty ?? throw new DomainException(RequiredCode, "qty");
        var warehouse = warehouseId ?? throw new DomainException(RequiredCode, "warehouseId");
        if (value <= 0)
        {
            throw new DomainException(InvalidCode, "qty", value);
        }
        return (set, value, warehouse);
    }

    private async Task<string> EnsureWarehouseAsync(long warehouseId, IDbTransaction tx, CancellationToken cancellationToken) =>
        await assemblies.FindWarehouseCodeAsync(warehouseId, tx, cancellationToken)
            ?? throw new DomainException(InvalidCode, "warehouseId", warehouseId);

    // The Location asked, null being '*'; one of another warehouse or not in use is refused here (MM-BR-713 checks the rest)
    private async Task<StockLocation> LocationAsync(long warehouseId, long? locationId, IDbTransaction tx, CancellationToken cancellationToken)
    {
        var location = await stock.FindLocationAsync(warehouseId, locationId, tx, cancellationToken);
        if (location is null || location.WarehouseId != warehouseId || !location.InUse)
        {
            throw new DomainException(InvalidCode, "locationId", locationId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? StockKeyRules.System);
        }
        return location;
    }

    // SD-BR-511, SD-BR-509: a Set item in use with at least one component
    private async Task<(AssemblyItem Set, IReadOnlyList<SetComponent> Components)> ReadSetAsync(
        long setItemId, IDbTransaction tx, CancellationToken cancellationToken)
    {
        var set = (await assemblies.FindItemsAsync([setItemId], tx, cancellationToken)).SingleOrDefault();
        if (set is null || set.Type != ItemStock.Set || !set.InUse)
        {
            throw new DomainException(InvalidCode, "setItemId", setItemId);
        }
        var components = await sets.GetComponentsAsync([setItemId], tx, cancellationToken);
        if (components.Count == 0)
        {
            throw new DomainException(SetComponent.NoComponents, set.ItemCd);
        }
        return (set, components);
    }

    // BC-BR-423, BC-BR-424: the MM unit price decimals of the base currency's country and the MM rounding mode
    private async Task<(DecimalPolicy Policy, int PriceDecimals)> DecimalsAsync(IDbTransaction tx, CancellationToken cancellationToken)
    {
        var company = await localization.FindCompanyAsync(tx, cancellationToken);
        var baseCurrency = company?.BaseCurrencyCode ?? CountryProfiles.Get(CountryProfiles.Us).BaseCurrencyCode;
        var currencies = await localization.FindCurrenciesAsync(tx, cancellationToken);
        var policy = new DecimalPolicy(
            company?.CountryCode ?? CountryProfiles.Us,
            (await localization.FindDecimalSettingsAsync(tx, cancellationToken))
                .Select(d => new DecimalSetting(d.Module, d.QtyDecimals, Rounding.ParseMode(d.RoundingMode))),
            (await localization.FindCountryDecimalsAsync(tx, cancellationToken))
                .Select(d => new CountryDecimal(d.CountryCode, d.Module, d.UnitPriceDecimals)),
            currencies.ToDictionary(c => c.CurrencyCode, c => c.CountryCode));
        return (policy, policy.UnitPriceDecimalsOf(DecimalModule.Mm, baseCurrency));
    }

    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static bool Same(IEnumerable<SetComponent> read, IEnumerable<SetComponent> locked) =>
        read.OrderBy(c => c.ComponentItemId).SequenceEqual(locked.OrderBy(c => c.ComponentItemId));
}
