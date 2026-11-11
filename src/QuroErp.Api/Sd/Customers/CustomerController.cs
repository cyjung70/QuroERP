using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Sd.Customers;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Sd.Customers;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Sd.Customers;

/// <summary>
/// The customer master screen (SD-UC-001). Any signed-in user can read, since sales documents look customers up; changes need
/// Full Access on the screen, checked by the screen path policy (BC-BR-214). That policy is the server check of SD-BR-306: a user
/// who may only read cannot change terms, discount status or freight.
/// </summary>
[ApiController, Route(CustomerRoutes.Base)]
public sealed class CustomerController(CustomerService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.Customer;

    [HttpGet]
    public Task<CustomerListDto> Search([FromQuery] CustomerSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    /// <summary>The customer with its balance (SD-BR-308). 404 Common-E-006.</summary>
    [HttpGet("{id:long}")]
    public Task<CustomerDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>201 with the saved customer and its warnings. 422 SD-E-301~335, BC-E-415, BC-E-521~523 or Common-E-002.</summary>
    [HttpPost, Authorize(Policy = Screen)]
    public async Task<ActionResult<CustomerDto>> Create(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var dto = await service.CreateAsync(request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>200 with the saved customer and its warnings. 409 Common-E-004 when updatedAt is stale.</summary>
    [HttpPut("{id:long}"), Authorize(Policy = Screen)]
    public Task<CustomerDto> Update(long id, UpdateCustomerRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(id, request, User.Identity!.Name!, cancellationToken);

    /// <summary>204. 422 SD-E-307 when the customer has documents; 409 Common-E-004 when updatedAt is stale.</summary>
    [HttpDelete("{id:long}"), Authorize(Policy = Screen)]
    public async Task<IActionResult> Delete(long id, [FromQuery] DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, updatedAt, cancellationToken);
        return NoContent();
    }

    /// <summary>The US exemption certificates (BC-BR-524). 404 for a KR company.</summary>
    [HttpGet("{id:long}/" + CustomerRoutes.TaxExemptions)]
    public Task<IReadOnlyList<CustomerTaxExemptionDto>> GetTaxExemptions(long id, CancellationToken cancellationToken) =>
        service.GetTaxExemptionsAsync(id, cancellationToken);

    /// <summary>
    /// Saves the grid's rows in one transaction and returns all certificates. 422 BC-E-524, SD-E-314, SD-E-333 or Common-E-002;
    /// 409 Common-E-004 when a row's updatedAt is stale; 404 for a KR company.
    /// </summary>
    [HttpPut("{id:long}/" + CustomerRoutes.TaxExemptions), Authorize(Policy = Screen)]
    public Task<IReadOnlyList<CustomerTaxExemptionDto>> SaveTaxExemptions(
        long id, BatchRequest<CustomerTaxExemptionSaveRow, CustomerTaxExemptionKey> request, CancellationToken cancellationToken) =>
        service.SaveTaxExemptionsAsync(id, request, User.Identity!.Name!, cancellationToken);
}
