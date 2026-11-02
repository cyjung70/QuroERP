using FluentAssertions;
using QuroErp.Application.Bc.Items;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Application.Tests.Bc.Items;

/// <summary>Alternate item groups (BC-UC-019, MM-BR-810, MM-BR-813, MM-BR-814) with fake storage.</summary>
public class AlternateItemGroupServiceTests
{
    private static readonly DateTimeOffset Read = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeAlternateItemGroupRepository _groups = new();
    private readonly FakeUnitOfWorkFactory _uow = new();
    private readonly AlternateItemGroupService _service;

    public AlternateItemGroupServiceTests()
    {
        _service = new AlternateItemGroupService(_groups, new FakeCodeValidator(), _uow);
        _groups.Rows[1] = new(1, "BOLT", "Bolts", "PART", 2, Read);
        _groups.Rows[2] = new(2, "NUT", "Nuts", "PART", 0, Read);
        _groups.Rows[3] = new(3, "GEAR", "Gears", "TOOL", 0, Read);
    }

    private static SaveAlternateItemGroupsRequest Request(
        AlternateItemGroupInsert[]? added = null, AlternateItemGroupUpdate[]? updated = null, AlternateItemGroupDelete[]? deleted = null) =>
        new("PART", added, updated, deleted);

    [Fact, Trait("Id", "BC-UC-019-T03")]
    public async Task Search_ByItemClassOrKeyword_PassesCleanConditionsAndTheLimit()
    {
        var part = await _service.SearchAsync(new AlternateItemGroupSearchQuery { ItemClass = " PART " }, None);
        var keyword = await _service.SearchAsync(new AlternateItemGroupSearchQuery { Keyword = " gear " }, None);

        part.Select(g => (g.AltItemGroupCd, g.ItemCount)).Should().Equal(("BOLT", 2L), ("NUT", 0L));
        keyword.Select(g => g.AltItemGroupCd).Should().Equal("GEAR");
        _groups.LastSearch.Should().Be((null, "gear", AlternateItemGroupRoutes.MaxListSize));
    }

    [Fact, Trait("Id", "BC-UC-019-T03")]
    public async Task Search_AnUnknownItemClass_IsCommonE002()
    {
        var act = () => _service.SearchAsync(new AlternateItemGroupSearchQuery { ItemClass = "ZZ" }, None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(FakeCodeValidator.InvalidCode);
    }

    [Fact, Trait("Id", "BC-UC-019-T01"), Trait("Rule", "MM-BR-810")]
    public async Task Save_AddChangeAndDelete_InOneTransaction_ReturnsTheGroupsOfTheItemClass()
    {
        var rows = await _service.SaveAsync(
            Request([new(" washer ", " Washers ")], [new(1, "bolt-m6", "M6 bolts", Read)], [new(2, Read)]), "kim", None);

        rows.Select(g => (g.AltItemGroupCd, g.AltItemGroupName, g.ItemClass)).Should().Equal(
            ("BOLT-M6", "M6 bolts", "PART"), ("WASHER", "Washers", "PART"));
        _groups.Locks.Should().Be(1);
        _uow.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "MM-UT-810"), Trait("Rule", "MM-BR-810")]
    public async Task Save_TwoRowsSwapCodes_OrADeletedCodeIsReused_Saves()
    {
        _groups.Rows[1] = _groups.Rows[1] with { ItemCount = 0 };

        var rows = await _service.SaveAsync(
            Request([new("BOLT", "New bolts")], [new(2, "GEAR", "Nuts", Read), new(3, "NUT", "Gears", Read)], [new(1, Read)]), "kim", None);

        rows.Select(g => (g.Id, g.AltItemGroupCd)).Should().Equal((100L, "BOLT"), (2L, "GEAR"));
    }

    [Theory, Trait("Id", "MM-UT-810"), Trait("Id", "BC-UC-019-T02"), Trait("Rule", "MM-BR-810"), Trait("Rule", "MM-BR-813")]
    [InlineData("GEAR", "Gears", "GEAR")]
    [InlineData("nut", "Nuts", "NUT")]
    [InlineData("WASHER", " ", "WASHER")]
    public async Task Save_ATakenCodeOrNoName_IsMmE810_AndWritesNothing(string code, string name, string arg)
    {
        var act = () => _service.SaveAsync(Request([new("SPRING", "Springs"), new(code, name)]), "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(AlternateItemGroupRules.InvalidCode);
        error.Args.Should().Equal(arg);
        _groups.Writes.Should().Be(0);
        _uow.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "MM-UT-814"), Trait("Rule", "MM-BR-814")]
    public async Task Save_DeletingAGroupWithItems_Is409MmE814_AndWritesNothing()
    {
        var act = () => _service.SaveAsync(Request([new("SPRING", "Springs")], deleted: [new(2, Read), new(1, Read)]), "kim", None);

        var error = (await act.Should().ThrowAsync<ConflictException>()).Which;
        error.Code.Should().Be(AlternateItemGroupRules.InUseCode);
        error.Args.Should().Equal("BOLT");
        _groups.Writes.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UC-019-T02")]
    public async Task Save_AStaleRow_IsConcurrency_AndAnUnknownId_IsNotFound()
    {
        var stale = () => _service.SaveAsync(Request(updated: [new(2, "NUT", "Nuts", Read.AddSeconds(-1))]), "kim", None);
        var unknown = () => _service.SaveAsync(Request(deleted: [new(99, Read)]), "kim", None);

        await stale.Should().ThrowAsync<ConcurrencyException>();
        await unknown.Should().ThrowAsync<NotFoundException>();
        _groups.Writes.Should().Be(0);
    }

    [Fact, Trait("Id", "MM-UT-810"), Trait("Rule", "MM-BR-810")]
    public async Task Save_WithoutItemClass_IsMmE802()
    {
        var act = () => _service.SaveAsync(Request() with { ItemClass = " " }, "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(Item.RequiredCode);
    }

    private sealed class FakeCodeValidator : ICodeValidator
    {
        public const string InvalidCode = "Common-E-002";

        public Task EnsureValidAsync(string majorCode, string? value, CancellationToken cancellationToken) =>
            majorCode == CodeMajors.ItemClass && value is not ("PART" or "TOOL")
                ? throw new DomainException(InvalidCode, majorCode, value ?? "")
                : Task.CompletedTask;
    }
}
