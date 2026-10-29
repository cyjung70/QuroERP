using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Application.Mm.Inventory;

namespace QuroErp.Api.Tests.Mm.Inventory;

/// <summary>Stock document services take InventoryService from DI (TASK-CORE-011).</summary>
public class InventoryServiceRegistrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "CORE-011-T03")]
    public void InventoryService_ResolvesFromTheApiContainer()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<InventoryService>().Should().NotBeNull();
    }
}
