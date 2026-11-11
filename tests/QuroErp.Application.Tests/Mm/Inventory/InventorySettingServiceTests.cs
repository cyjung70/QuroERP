using System.Data;
using FluentAssertions;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Mm.Inventory;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Application.Tests.Mm.Inventory;

public class InventorySettingServiceTests
{
    private static readonly DateTimeOffset Saved = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeUnitOfWorkFactory _unitOfWork = new();
    private readonly FakeInventorySettingRepository _settings = new(new InventorySettingRow("DENY", "DENY", false, Saved, "system"));

    private InventorySettingService Service => new(_settings, _unitOfWork);

    [Fact, Trait("Id", "MM-SET-T10")]
    public async Task GetAsync_ReturnsStoredSetting()
    {
        var dto = await Service.GetAsync(CancellationToken.None);

        dto.Should().Be(new InventorySettingDto("DENY", "DENY", false, Saved, "system"));
    }

    [Fact, Trait("Id", "MM-SET-T11")]
    public async Task UpdateAsync_CurrentUpdatedAt_SavesAndReturnsNewSetting()
    {
        var dto = await Service.UpdateAsync(new UpdateInventorySettingRequest("ALLOW_IN_MONTH", "WARN", true, Saved), "kim", CancellationToken.None);

        dto.NegativeStockMode.Should().Be("ALLOW_IN_MONTH");
        dto.OverReceiptMode.Should().Be("WARN");
        dto.AllowCostAdjustmentEdit.Should().BeTrue();
        dto.UpdatedBy.Should().Be("kim");
        dto.UpdatedAt.Should().BeAfter(Saved);
        _unitOfWork.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "MM-SET-T12")]
    public async Task UpdateAsync_StaleUpdatedAt_ThrowsConcurrencyAndKeepsSetting()
    {
        var act = () => Service.UpdateAsync(new UpdateInventorySettingRequest("ALLOW_IN_MONTH", "DENY", false, Saved.AddSeconds(-1)), "kim", CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        _settings.Row.NegativeStockMode.Should().Be("DENY");
        _unitOfWork.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "MM-SET-T13")]
    public async Task UpdateAsync_RowChangedBetweenReadAndWrite_ThrowsConcurrency()
    {
        _settings.FailNextUpdate = true;

        var act = () => Service.UpdateAsync(new UpdateInventorySettingRequest("ALLOW_IN_MONTH", "DENY", false, Saved), "kim", CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        _unitOfWork.Commits.Should().Be(0);
    }

    [Theory, Trait("Id", "MM-SET-T14")]
    [InlineData("MAYBE")]
    [InlineData("deny")]
    [InlineData("ALLOW")]
    public async Task UpdateAsync_UnknownMode_ThrowsCommonE002(string mode)
    {
        var act = () => Service.UpdateAsync(new UpdateInventorySettingRequest(mode, "DENY", false, Saved), "kim", CancellationToken.None);

        var e = (await act.Should().ThrowAsync<DomainException>()).Which;
        e.Code.Should().Be(CodeMessages.InvalidCode);
        e.Args.Should().Equal(InventorySettingService.ModeName, mode);
    }

    [Fact, Trait("Id", "MM-SET-T15")]
    public void ContractValues_MatchDomainCodes()
    {
        NegativeStockModes.All.Should().Equal(Enum.GetValues<NegativeStockMode>().Select(m => m.ToCode()));
        OverReceiptModes.All.Should().Equal(QuroErp.Domain.Mm.GoodsReceipts.OverReceiptModes.All);
    }

    [Theory, Trait("Id", "MM-SET-T16"), Trait("Rule", "MM-BR-102")]
    [InlineData("ALLOW")]
    [InlineData("warn")]
    public async Task UpdateAsync_UnknownOverReceiptMode_ThrowsCommonE002(string mode)
    {
        var act = () => Service.UpdateAsync(new UpdateInventorySettingRequest("DENY", mode, false, Saved), "kim", CancellationToken.None);

        var e = (await act.Should().ThrowAsync<DomainException>()).Which;
        e.Code.Should().Be(CodeMessages.InvalidCode);
        e.Args.Should().Equal(InventorySettingService.OverReceiptModeName, mode);
    }

    private sealed class FakeInventorySettingRepository(InventorySettingRow row) : IInventorySettingRepository
    {
        public InventorySettingRow Row { get; private set; } = row;

        public bool FailNextUpdate { get; set; }

        public Task<InventorySettingRow?> GetAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<InventorySettingRow?>(Row);

        public Task<InventorySettingRow?> UpdateAsync(
            string negativeStockMode, string overReceiptMode, bool allowCostAdjustmentEdit, DateTimeOffset expectedUpdatedAt, string updatedBy,
            IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (FailNextUpdate || expectedUpdatedAt != Row.UpdatedAt)
            {
                return Task.FromResult<InventorySettingRow?>(null);
            }
            Row = new InventorySettingRow(negativeStockMode, overReceiptMode, allowCostAdjustmentEdit, Row.UpdatedAt.AddMinutes(1), updatedBy);
            return Task.FromResult<InventorySettingRow?>(Row);
        }
    }
}
