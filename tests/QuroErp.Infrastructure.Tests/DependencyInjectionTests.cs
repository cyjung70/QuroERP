using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace QuroErp.Infrastructure.Tests;

public class DependencyInjectionTests
{
    [Fact, Trait("Id", "CORE-002-T01")]
    public void AddInfrastructure_NoConnectionString_Throws()
    {
        var configuration = new ConfigurationBuilder().Build();

        var act = () => new ServiceCollection().AddInfrastructure(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings:QuroErp*");
    }
}
