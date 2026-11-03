using FluentAssertions;
using QuroErp.Application.Bc.Items;
using QuroErp.Application.Common;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Tests.Bc.Items;

/// <summary>Alternate items (BC-UC-012, MM-BR-811~813) with fake storage.</summary>
public class AlternateItemServiceTests
{
    private static readonly DateTimeOffset Read = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeAlternateItemRepository _repository = new();
    private readonly FakeUnitOfWorkFactory _uow = new();
    private readonly AlternateItemService _service;

    public AlternateItemServiceTests()
    {
        _service = new AlternateItemService(_repository, _uow);
        _repository.Groups[1] = new(1, "G1", "Bolts", Read);
        _repository.Groups[2] = new(2, "G2", "Nuts", Read);
        foreach (var item in new AlternateItemCandidate[]
        {
            new(10, "A10", "INVENTORY", "Y", 1, "G1"), new(11, "A11", "INVENTORY", "Y", 1, "G1"),
            new(20, "B20", "INVENTORY", "Y", null, null), new(21, "B21", "INVENTORY", "Y", 2, "G2"),
            new(22, "S22", "SERVICE", "Y", null, null),
        })
        {
            _repository.Items[item.ItemCd] = item;
        }
    }

    private static SaveAlternateItemsRequest Request(
        string?[]? added = null, UpdatedAlternateItemRow[]? updated = null, long[]? deleted = null, bool moveConfirmed = false,
        DateTimeOffset? updatedAt = null) =>
        new(1, added, updated, deleted, moveConfirmed, updatedAt ?? Read);

    [Fact, Trait("Id", "BC-UC-012-T03")]
    public async Task Get_ByTrimmedCode_ReturnsTheGroupWithItsItemsInCodeOrder()
    {
        var dto = await _service.GetAsync(new AlternateItemQuery { GroupCode = " G1 " }, None);

        dto.Should().BeEquivalentTo(new { Id = 1L, AltItemGroupCd = "G1", AltItemGroupName = "Bolts", UpdatedAt = Read });
        dto.Items.Select(i => i.ItemCd).Should().Equal("A10", "A11");
    }

    [Fact, Trait("Id", "BC-UC-012-T03")]
    public async Task Get_WithoutCode_IsMmE802_AndAnUnknownCode_IsNotFound()
    {
        var blank = () => _service.GetAsync(new AlternateItemQuery { GroupCode = " " }, None);
        var unknown = () => _service.GetAsync(new AlternateItemQuery { GroupCode = "ZZ" }, None);

        (await blank.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(Item.RequiredCode);
        (await unknown.Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be(AlternateItemService.NotFoundCode);
    }

    [Fact, Trait("Id", "BC-UC-012-T01"), Trait("Rule", "MM-BR-811")]
    public async Task Save_AddChangeAndDelete_WritesOnceInOneTransaction_AndReturnsTheNewGroup()
    {
        // Row of A10 deleted, row of A11 changed to B21 (moved from G2), B20 added
        var dto = await _service.SaveAsync(Request(["b20"], [new(11, "B21")], [10], moveConfirmed: true), "kim", None);

        var save = _repository.Saves.Should().ContainSingle().Which;
        save.JoinIds.Should().Equal(20, 21);
        save.LeaveIds.Should().Equal(10, 11);
        save.UpdatedBy.Should().Be("kim");
        dto.Items.Select(i => i.ItemCd).Should().Equal("B20", "B21");
        dto.UpdatedAt.Should().Be(Read.AddSeconds(1));
        _repository.GroupLocks.First().Should().BeTrue();
        _uow.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UC-012-T02"), Trait("Rule", "MM-BR-813")]
    public async Task Save_ABadRow_WritesNothing()
    {
        var act = () => _service.SaveAsync(Request(["B20"], [new(11, "S22")], [10]), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("S22");
        _repository.Saves.Should().BeEmpty();
        _uow.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "MM-UT-812"), Trait("Rule", "MM-BR-812")]
    public async Task Save_AnItemOfAnotherGroup_Is409MmE812_UntilTheMoveIsConfirmed()
    {
        var unconfirmed = () => _service.SaveAsync(Request(["B21"]), "kim", None);

        var error = (await unconfirmed.Should().ThrowAsync<ConflictException>()).Which;
        error.Code.Should().Be(AlternateItemRules.InvalidItemCode);
        error.Args.Should().Equal("B21", "G2");
        _repository.Saves.Should().BeEmpty();

        var dto = await _service.SaveAsync(Request(["B21"], moveConfirmed: true), "kim", None);

        dto.Items.Select(i => i.ItemCd).Should().Equal("A10", "A11", "B21");
    }

    [Fact, Trait("Id", "BC-UC-012-T01")]
    public async Task Save_AStaleScreen_OrARemovedItemNoLongerInTheGroup_IsConcurrency()
    {
        var stale = () => _service.SaveAsync(Request(["B20"], updatedAt: Read.AddSeconds(-1)), "kim", None);
        var notMember = () => _service.SaveAsync(Request(deleted: [20]), "kim", None);

        await stale.Should().ThrowAsync<ConcurrencyException>();
        await notMember.Should().ThrowAsync<ConcurrencyException>();
        _repository.Saves.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-012-T01")]
    public async Task Save_AnUnknownGroup_IsNotFound()
    {
        var act = () => _service.SaveAsync(Request() with { GroupId = 99 }, "kim", None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
