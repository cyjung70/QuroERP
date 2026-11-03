using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.FieldPolicies;
using QuroErp.Contracts.Bc.FieldPolicies;

namespace QuroErp.Api.Bc.FieldPolicies;

/// <summary>
/// Field policies (field-policy.md, BC-BR-431, BC-BR-433, BC-BR-435). Any signed-in user can read a screen's merged policies;
/// company overrides need Full Access on the localization menu, checked by the admin role until the menu policy is applied.
/// </summary>
[ApiController]
public sealed class FieldPolicyController(FieldPolicyService policies) : ControllerBase
{
    /// <summary>The screen's merged policies (system default, company country pack, company override).</summary>
    /// <param name="screen">The screen code, e.g. Sd.Customer. 400 Common-E-005 when missing, 404 Common-E-006 when unknown.</param>
    [HttpGet(FieldPolicyRoutes.Base)]
    public Task<IReadOnlyList<FieldPolicyDto>> Get([FromQuery] string? screen, CancellationToken cancellationToken) =>
        policies.GetAsync(screen, cancellationToken);

    /// <summary>
    /// 200 with every company override after the save. Nothing is saved when a row fails: 422 BC-E-433 with the field key (unknown
    /// key, invalid state or default, HIDDEN on a required business field, the same key twice).
    /// </summary>
    [HttpPut(FieldPolicyRoutes.Base), Authorize(Roles = "admin")]
    public Task<IReadOnlyList<FieldPolicyOverrideDto>> Save(SaveFieldPoliciesRequest request, CancellationToken cancellationToken) =>
        policies.SaveAsync(request, User.Identity!.Name!, cancellationToken);
}
