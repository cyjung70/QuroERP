using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace QuroERP.Web.Features.MM.PurchaseOrder;

public partial class PoEntry
{
    [Parameter] public string? PoNo { get; set; }

    [Inject] private IPurchaseOrderApi Api { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    // TODO(config): read from company/MM configuration. Single company, single plant.
    private const string LocalCurrency = "KRW";
    private static readonly string[] Currencies = ["KRW", "USD", "EUR", "JPY", "CNY"];

    private PoDocument Doc { get; set; } = NewDocument();
    private PoLine? SelectedLine { get; set; }
    private CurrencyPrecision Precision { get; set; } = new(LocalCurrency, Qty: 2, Price: 4, Amount: 0);
    private PoTotals Totals { get; set; } = new(0, 0, 0, 0, 0, 0);
    private bool IsDirty { get; set; }
    private bool Busy { get; set; }

    private bool ReadOnly => !PoStatusRules.IsEditable(Doc);

    private string? LockMessage =>
        Doc.Header.Status is PoStatus.Received or PoStatus.Closed or PoStatus.Cancelled ? L["Msg.Finished"]
        : PoStatusRules.HasReceipt(Doc) ? L["Msg.ReceivedLocked"]
        : null;

    // ───────────────────────── lifecycle ─────────────────────────
    protected override async Task OnParametersSetAsync()
    {
        if (!string.IsNullOrEmpty(PoNo)) await LoadAsync(PoNo);
        else await ApplyPrecisionAsync();
    }

    private static PoDocument NewDocument() => new()
    {
        Header = new PoHeader { Status = PoStatus.Draft, PoDate = DateOnly.FromDateTime(DateTime.Today), Currency = LocalCurrency, ExchangeRate = 1m }
    };

    private async Task LoadAsync(string poNo)
    {
        var doc = await Api.GetAsync(poNo);
        if (doc is null) { Snackbar.Add(L["Msg.NotFound", poNo], Severity.Warning); return; }
        Bind(doc);
        await ApplyPrecisionAsync();
    }

    /// Bind a server document; snapshot each line so Undo Row can restore it.
    private void Bind(PoDocument doc)
    {
        foreach (var l in doc.Lines) { l.State = RowState.Unchanged; l.Original = l.Clone(); }
        Doc = doc;
        SelectedLine = null;
        IsDirty = false;
        Recalc();
    }

    private async Task ApplyPrecisionAsync()
    {
        Precision = await Api.GetPrecisionAsync(Doc.Header.Currency);
        Recalc();
    }

    private void Recalc()
    {
        var localPlaces = LocalCurrency == "KRW" ? 0 : 2; // TODO(config): local currency precision
        Totals = PoCalculator.CalcTotals(Doc.Lines, Doc.Header.ExchangeRate, Precision, localPlaces);
    }

    private void MarkDirty() => IsDirty = true;

    // ───────────────────────── File commands ─────────────────────────
    private async Task<bool> ConfirmDiscardAsync() =>
        !IsDirty || await Dialogs.ShowMessageBox(L["Confirm.Title"], L["Confirm.Discard"], yesText: L["Confirm.Discard.Yes"], cancelText: L["Confirm.Cancel"]) == true;

    private async Task OpenQueryAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var r = await (await Dialogs.ShowAsync<PoQueryDialog>(L["Cmd.Query"])).Result;
        if (r is { Canceled: false, Data: string poNo }) Nav.NavigateTo($"/mm/purchase-orders/entry/{poNo}");
    }

    private async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        Nav.NavigateTo("/mm/purchase-orders/entry");
        Bind(NewDocument());
        await ApplyPrecisionAsync();
    }

    private async Task SaveAsync()
    {
        var errors = Validate();
        if (errors.Count > 0) { foreach (var e in errors) Snackbar.Add(e, Severity.Error); return; }

        await RunAsync(async () =>
        {
            var saved = await Api.SaveAsync(Doc);
            Bind(saved);
            Snackbar.Add(L["Msg.Saved", saved.Header.PoNo!], Severity.Success);
            if (PoNo != saved.Header.PoNo) Nav.NavigateTo($"/mm/purchase-orders/entry/{saved.Header.PoNo}", replace: true);
        });
    }

    private List<string> Validate()
    {
        var e = new List<string>();
        var h = Doc.Header;
        if (string.IsNullOrWhiteSpace(h.VendorCode)) e.Add(L["Val.VendorRequired"]);
        if (h.DueDate is null) e.Add(L["Val.DueDateRequired"]);
        else if (h.DueDate < h.PoDate) e.Add(L["Val.DueDateBeforePoDate"]);
        var live = Doc.Lines.Where(l => l.State != RowState.MarkedForDelete).ToList();
        if (live.Count == 0) e.Add(L["Val.NoLines"]);
        foreach (var l in live)
        {
            if (string.IsNullOrWhiteSpace(l.ItemCode)) e.Add(L["Val.ItemRequired", l.Seq]);
            if (l.Qty <= 0) e.Add(L["Val.QtyPositive", l.Seq]);
            if (l.UnitPrice < 0) e.Add(L["Val.PriceNotNegative", l.Seq]);
            if (l.DiscountRate is < 0 or > 100) e.Add(L["Val.DiscountRange", l.Seq]);
        }
        return e;
    }

    private async Task DeleteAsync()
    {
        var ok = await Dialogs.ShowMessageBox(L["Confirm.Title"], L["Confirm.Delete", Doc.Header.PoNo!], yesText: L["Cmd.Delete"], cancelText: L["Confirm.Cancel"]);
        if (ok != true) return;
        await RunAsync(async () =>
        {
            await Api.DeleteAsync(Doc.Header.PoNo!, Doc.Header.RowVersion);
            Snackbar.Add(L["Msg.Deleted", Doc.Header.PoNo!], Severity.Success);
            Nav.NavigateTo("/mm/purchase-orders/entry");
            Bind(NewDocument());
        });
    }

    // ───────────────────────── Status commands ─────────────────────────
    private async Task ChangeStatusAsync(PoStatusCommand cmd)
    {
        var ok = await Dialogs.ShowMessageBox(L["Confirm.Title"], L[$"ConfirmStatus.{cmd}", Doc.Header.PoNo!], yesText: L[$"Cmd.{cmd}"], cancelText: L["Confirm.Cancel"]);
        if (ok != true) return;
        await RunAsync(async () =>
        {
            var doc = await Api.ChangeStatusAsync(Doc.Header.PoNo!, cmd, Doc.Header.RowVersion);
            Bind(doc);
            Snackbar.Add(L[$"Msg.{cmd}.Done", doc.Header.PoNo!], Severity.Success);
        });
    }

    // ───────────────────────── Row commands ─────────────────────────
    private void AddRow()
    {
        var line = new PoLine
        {
            Seq = Doc.Lines.Count == 0 ? 1 : Doc.Lines.Max(l => l.Seq) + 1,
            WarehouseCode = Doc.Header.WarehouseCode,
            TaxCode = Doc.Header.TaxCode,
            DueDate = Doc.Header.DueDate,
            State = RowState.Added
        };
        Doc.Lines.Add(line);
        SelectedLine = line;
        MarkDirty();
    }

    /// Delete Mark: toggles. A never-saved row is removed at once; a saved row is struck through and deleted on Save.
    private void ToggleMarkDelete()
    {
        if (SelectedLine is null) return;
        if (SelectedLine.State == RowState.Added) { Doc.Lines.Remove(SelectedLine); SelectedLine = null; }
        else SelectedLine.State = SelectedLine.State == RowState.MarkedForDelete
            ? (IsSameAsOriginal(SelectedLine) ? RowState.Unchanged : RowState.Modified)
            : RowState.MarkedForDelete;
        MarkDirty();
        Recalc();
    }

    /// Undo Row: restore the selected row to the values it had when loaded. A new row is removed.
    private void UndoRow()
    {
        var l = SelectedLine;
        if (l is null) return;
        if (l.Original is null) { Doc.Lines.Remove(l); SelectedLine = null; }
        else
        {
            var restored = l.Original.Clone();
            restored.Original = l.Original;
            restored.State = RowState.Unchanged;
            Doc.Lines[Doc.Lines.IndexOf(l)] = restored;
            SelectedLine = restored;
        }
        IsDirty = Doc.Lines.Any(x => x.State != RowState.Unchanged);
        Recalc();
    }

    private static bool IsSameAsOriginal(PoLine l) => l.Original is { } o &&
        o.ItemCode == l.ItemCode && o.WarehouseCode == l.WarehouseCode && o.Qty == l.Qty && o.UnitPrice == l.UnitPrice &&
        o.DiscountRate == l.DiscountRate && o.TaxCode == l.TaxCode && o.DueDate == l.DueDate;

    private string RowClass(PoLine l) => l.State == RowState.MarkedForDelete ? "qg-row-del" : l == SelectedLine ? "qg-row-sel" : "";

    // ───────────────────────── Cell edits & lookups ─────────────────────────
    private async Task OnCellCommittedAsync(QuroCellCommit<PoLine> c)
    {
        var l = c.Item;
        switch (c.Field)
        {
            case nameof(PoLine.ItemCode):
                var hit = (await Api.SearchItemsAsync(l.ItemCode, null, null)).FirstOrDefault(i => i.Code.Equals(l.ItemCode, StringComparison.OrdinalIgnoreCase));
                if (hit is null) { await OpenItemSearchAsync(l); return; }
                await ApplyItemAsync(l, hit);
                break;
            case nameof(PoLine.TaxCode):
                l.TaxRate = string.IsNullOrEmpty(l.TaxCode) ? 0 : await Api.GetTaxRateAsync(l.TaxCode);
                break;
        }
        if (l.State == RowState.Unchanged) l.State = RowState.Modified;
        if (l.State == RowState.Modified && IsSameAsOriginal(l)) l.State = RowState.Unchanged;
        MarkDirty();
        Recalc();
    }

    private async Task OpenItemSearchAsync(PoLine l)
    {
        var p = new DialogParameters<ItemSearchDialog> { { x => x.InitialCode, l.ItemCode } };
        var r = await (await Dialogs.ShowAsync<ItemSearchDialog>(L["ItemSearch"], p, new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true })).Result;
        if (r is { Canceled: false, Data: ItemLookup item })
        {
            await ApplyItemAsync(l, item);
            if (l.State == RowState.Unchanged) l.State = RowState.Modified;
            MarkDirty();
            Recalc();
        }
    }

    private async Task ApplyItemAsync(PoLine l, ItemLookup item)
    {
        l.ItemCode = item.Code;
        l.ItemName = item.Name;
        l.Unit = item.Unit;
        if (l.UnitPrice == 0) l.UnitPrice = item.LastPrice; // default from last purchase price; user may overwrite
        l.TaxCode ??= item.DefaultTaxCode;
        l.TaxRate = string.IsNullOrEmpty(l.TaxCode) ? 0 : await Api.GetTaxRateAsync(l.TaxCode);
    }

    private async Task OnVendorCodeChangedAsync(string code)
    {
        Doc.Header.VendorCode = code;
        var hit = (await Api.SearchVendorsAsync(code, null)).FirstOrDefault(v => v.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (hit is null) { await OpenVendorSearchAsync(); return; }
        await ApplyVendorAsync(hit);
    }

    private async Task OpenVendorSearchAsync()
    {
        var r = await (await Dialogs.ShowAsync<VendorSearchDialog>(L["VendorSearch"])).Result;
        if (r is { Canceled: false, Data: VendorLookup v }) await ApplyVendorAsync(v);
    }

    private async Task ApplyVendorAsync(VendorLookup v)
    {
        Doc.Header.VendorCode = v.Code;
        Doc.Header.VendorName = v.Name;
        Doc.Header.PaymentTerms ??= v.PaymentTerms;
        Doc.Header.Incoterms ??= v.Incoterms;
        if (v.Currency is not null && v.Currency != Doc.Header.Currency && Doc.Lines.Count == 0) await OnCurrencyChangedAsync(v.Currency);
        MarkDirty();
    }

    private async Task OnCurrencyChangedAsync(string currency)
    {
        Doc.Header.Currency = currency;
        if (currency == LocalCurrency) Doc.Header.ExchangeRate = 1m;
        MarkDirty();
        await ApplyPrecisionAsync();
    }

    private Task OnWarehouseCodeChangedAsync(string code) { Doc.Header.WarehouseCode = code; MarkDirty(); return Task.CompletedTask; } // TODO: resolve name via warehouse API
    private Task OpenWarehouseSearchAsync() => Task.CompletedTask;                                                                    // TODO: WarehouseSearchDialog (same pattern as Vendor)

    // ───────────────────────── Output ─────────────────────────
    private async Task DownloadPdfAsync()
    {
        await RunAsync(async () =>
        {
            var bytes = await Api.GetPdfAsync(Doc.Header.PoNo!);
            await Js.InvokeVoidAsync("quro.downloadFile", $"{Doc.Header.PoNo}.pdf", "application/pdf", Convert.ToBase64String(bytes));
        });
    }

    // ───────────────────────── helpers ─────────────────────────
    private async Task RunAsync(Func<Task> work)
    {
        Busy = true;
        try { await work(); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            Snackbar.Add(L["Msg.Conflict"], Severity.Warning); // someone else changed or received this PO
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            Snackbar.Add(L["Msg.RuleViolation", ex.Message], Severity.Error);
        }
        finally { Busy = false; }
    }

    private string FmtAmt(decimal v) => v.ToString("N" + Precision.Amount, CultureInfo.CurrentCulture);
    private static DateTime? ToDateTime(DateOnly? d) => d?.ToDateTime(TimeOnly.MinValue);
}
