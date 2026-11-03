using System.Data;
using FluentAssertions;
using QuroErp.Application.Bc.FieldPolicies;
using QuroErp.Application.Common;
using QuroErp.Application.Tests.Bc.Localization;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.FieldPolicies;
using QuroErp.Domain.Bc.FieldPolicies;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Tests.Bc.FieldPolicies;

/// <summary>The save check, response masking and company overrides (BC-BR-432~434, BC-BR-436) with fake storage and packs.</summary>
public class FieldPolicyServiceTests
{
    private const string Screen = "Sd.Customer";
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateTimeOffset Read = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly CustomerInput Input = new(
        "ACME", "NY", "10001", "JP", "2208162517", 500m, new Dictionary<string, string?> { ["Messenger"] = "acme" });

    private readonly FakeOverrides _overrides = new();
    private readonly FakePacks _packs = new();
    private readonly FakeLocalizationRepository _localization = new() { Company = new("KR", "KRW", "ko", "DOCUMENT", "Asia/Seoul") };
    private readonly FakeUnitOfWorkFactory _uow = new();
    private readonly FieldPolicyService _service;

    public FieldPolicyServiceTests() => _service = new FieldPolicyService(_overrides, _packs, _localization, _uow);

    [Fact, Trait("Id", "BC-UT-430"), Trait("Rule", "BC-BR-430")]
    public async Task Get_UsesThePackOfTheCompanyCountry()
    {
        var kr = await _service.GetAsync(Screen, None);
        _localization.Company = _localization.Company! with { CountryCode = "US" };
        var us = await _service.GetAsync(Screen, None);

        kr.Single(f => f.FieldKey == "Sd.Customer.State").Should().Be(new FieldPolicyDto("Sd.Customer.State", "HIDDEN", null, "Sd.Customer.State", "CountryPack"));
        us.Single(f => f.FieldKey == "Sd.Customer.State").State.Should().Be("REQUIRED");
    }

    [Fact, Trait("Id", "BC-UT-431")]
    public async Task Get_WithoutAScreen_Is400_AndAnUnknownScreen_Is404()
    {
        (await FluentActions.Awaiting(() => _service.GetAsync(" ", None)).Should().ThrowAsync<BadRequestException>()).Which.Code.Should().Be("Common-E-005");
        (await FluentActions.Awaiting(() => _service.GetAsync("Sd.Nothing", None)).Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be("Common-E-006");
    }

    [Fact, Trait("Id", "BC-UT-432"), Trait("Rule", "BC-BR-432")]
    public async Task Validate_ARequiredFieldEmpty_IsBcE432WithTheLabelKey()
    {
        _overrides.Rows.Add(Override("Sd.Customer.ZipCode", null, null, "Company.Label.PostCode"));
        var policy = await LoadAsync();

        var act = () => FieldPolicyService.Validate(policy, Input with { ZipCode = " " }, null);

        var error = act.Should().Throw<DomainException>().Which;
        (error.Code, error.Args[0]).Should().Be(("BC-E-432", "Company.Label.PostCode"));
    }

    [Fact, Trait("Id", "BC-UT-434"), Trait("Rule", "BC-BR-434")]
    public async Task Validate_NewRecord_HiddenAndReadOnlyInputGetsThePolicyDefault()
    {
        _overrides.Rows.Add(Override("Sd.Customer.Country", "READONLY", null, null));
        _overrides.Rows.Add(Override("Sd.Customer.CreditLimit", "HIDDEN", "1000", null));
        var policy = await LoadAsync();

        var saved = FieldPolicyService.Validate(policy, Input, null);

        (saved.State, saved.Country, saved.CreditLimit, saved.CName).Should().Be((null, "KR", 1000m, "ACME"));
        Input.State.Should().Be("NY", "the input is not changed");
    }

    [Fact, Trait("Id", "BC-UT-434"), Trait("Rule", "BC-BR-434")]
    public async Task Validate_Edit_HiddenInputKeepsTheStoredValue()
    {
        var policy = await LoadAsync();
        var stored = Input with { State = "CA", ExtAttrs = new Dictionary<string, string?> { ["Messenger"] = "stored" } };

        var saved = FieldPolicyService.Validate(policy, Input with { State = "NY" }, stored);

        saved.State.Should().Be("CA");
        saved.ExtAttrs.Should().Equal(new Dictionary<string, string?> { ["Messenger"] = "acme" }, "OPTIONAL ext keys take the input");
    }

    [Fact, Trait("Id", "BC-UT-434"), Trait("Rule", "BC-BR-434")]
    public async Task Mask_HiddenFieldsAreNull_InTheResponse()
    {
        _overrides.Rows.Add(Override("Sd.Customer.ext.Messenger", "HIDDEN", null, null));
        var policy = await LoadAsync();

        var shown = FieldPolicyService.Mask(policy, Input);

        (shown.State, shown.ZipCode, shown.CreditLimit).Should().Be((null, "10001", 500m));
        shown.ExtAttrs.Should().BeEmpty();
        Input.State.Should().Be("NY");
    }

    [Fact, Trait("Id", "BC-UT-436"), Trait("Rule", "BC-BR-436")]
    public async Task Validate_AnUndeclaredExtKey_IsBcE436WithTheFieldKey()
    {
        var policy = await LoadAsync();

        var act = () => FieldPolicyService.Validate(policy, Input with { ExtAttrs = new Dictionary<string, string?> { ["Fax2"] = "1" } }, null);

        var error = act.Should().Throw<DomainException>().Which;
        (error.Code, error.Args[0]).Should().Be(("BC-E-436", "Sd.Customer.ext.Fax2"));
    }

    [Fact, Trait("Id", "BC-UT-436"), Trait("Rule", "BC-BR-436")]
    public async Task Validate_ARequiredExtKeyMissing_IsBcE432()
    {
        _overrides.Rows.Add(Override("Sd.Customer.ext.Messenger", "REQUIRED", null, null));
        var policy = await LoadAsync();

        var act = () => FieldPolicyService.Validate(policy, Input with { ExtAttrs = null }, null);

        act.Should().Throw<DomainException>().Which.Args[0].Should().Be("Sd.Customer.ext.Messenger");
    }

    [Fact, Trait("Id", "BC-UT-433"), Trait("Rule", "BC-BR-433")]
    public async Task Save_SetsAndRemovesOverrides_AndCommits()
    {
        _overrides.Rows.Add(Override("Sd.Customer.Email", "HIDDEN", null, null));

        var after = await _service.SaveAsync(
            new SaveFieldPoliciesRequest([new("Sd.Customer.State", "optional", null, null), new("Sd.Customer.Email", null, " ", null)]), "kim", None);

        after.Should().ContainSingle().Which.Should().Match<FieldPolicyOverrideDto>(o => o.FieldKey == "Sd.Customer.State" && o.State == "OPTIONAL");
        _uow.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UT-433"), Trait("Rule", "BC-BR-433")]
    public async Task Save_OneBadRow_SavesNothing()
    {
        var request = new SaveFieldPoliciesRequest([new("Sd.Customer.State", "OPTIONAL", null, null), new("Sd.Customer.CName", "HIDDEN", null, null)]);

        var error = (await FluentActions.Awaiting(() => _service.SaveAsync(request, "kim", None)).Should().ThrowAsync<DomainException>()).Which;

        (error.Code, error.Args[0]).Should().Be(("BC-E-433", "Sd.Customer.CName"));
        (_overrides.Writes, _uow.Commits).Should().Be((0, 0));
    }

    [Fact, Trait("Id", "BC-UT-433"), Trait("Rule", "BC-BR-433")]
    public async Task Save_TheSameKeyTwice_IsBcE433()
    {
        var request = new SaveFieldPoliciesRequest([new("Sd.Customer.State", "OPTIONAL", null, null), new("Sd.Customer.State ", "REQUIRED", null, null)]);

        var error = (await FluentActions.Awaiting(() => _service.SaveAsync(request, "kim", None)).Should().ThrowAsync<DomainException>()).Which;

        error.Args[0].Should().Be("Sd.Customer.State");
        _overrides.Writes.Should().Be(0);
    }

    private Task<FieldPolicy> LoadAsync() => _service.LoadAsync(Screen, null!, None);

    private static FieldPolicyOverrideDto Override(string key, string? state, string? defaultValue, string? label) =>
        new(1, key, state, defaultValue, label, Read, "kim");

    /// <summary>A customer save DTO as the pilot screen would have it.</summary>
    public sealed record CustomerInput(
        string? CName, string? State, string? ZipCode, string? Country, string? BusinessRegNo, decimal CreditLimit,
        IReadOnlyDictionary<string, string?>? ExtAttrs);

    private sealed class FakePacks : ICountryPackProvider
    {
        private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> NoLabels = [];

        public IReadOnlyList<CountryPack> All { get; } =
        [
            new("KR",
                new Dictionary<string, FieldPolicyEntry>
                {
                    ["Sd.Customer.State"] = new("HIDDEN", null, null),
                    ["Sd.Customer.ZipCode"] = new("REQUIRED", null, null),
                    ["Sd.Customer.ext.Messenger"] = new("OPTIONAL", null, null),
                },
                new Dictionary<string, string> { ["Sd.Customer.Country"] = "KR" },
                NoLabels),
            new("US", new Dictionary<string, FieldPolicyEntry> { ["Sd.Customer.State"] = new("REQUIRED", null, null) }, new Dictionary<string, string>(), NoLabels),
        ];

        public CountryPack? Find(string? countryCode) => All.FirstOrDefault(p => p.CountryCode == countryCode);
    }

    private sealed class FakeOverrides : IFieldPolicyRepository
    {
        public List<FieldPolicyOverrideDto> Rows { get; } = [];

        public int Writes { get; private set; }

        public Task<IReadOnlyList<FieldPolicyOverrideDto>> FindAllAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FieldPolicyOverrideDto>>([.. Rows.OrderBy(r => r.FieldKey, StringComparer.Ordinal)]);

        public Task UpsertAsync(string fieldKey, FieldPolicyEntry entry, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            Rows.RemoveAll(r => r.FieldKey == fieldKey);
            Rows.Add(new(Rows.Count + 1, fieldKey, entry.State, entry.DefaultValue, entry.LabelKey, Read, updatedBy));
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IReadOnlyCollection<string> fieldKeys, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (fieldKeys.Count > 0)
            {
                Writes++;
                Rows.RemoveAll(r => fieldKeys.Contains(r.FieldKey));
            }
            return Task.CompletedTask;
        }
    }
}
