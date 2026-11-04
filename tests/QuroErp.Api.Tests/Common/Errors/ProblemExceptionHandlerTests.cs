using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Api.Common.Errors;
using QuroErp.Application.Common;
using QuroErp.Domain.Common;

namespace QuroErp.Api.Tests.Common.Errors;

/// <summary>Warnings the user must answer are 422 with code, args and confirmField (api.md "경고").</summary>
public class ProblemExceptionHandlerTests
{
    [Fact, Trait("Id", "MM-UT-016")]
    public async Task ConfirmationRequired_Is422WithConfirmField()
    {
        var (status, body) = await HandleAsync(new ConfirmationRequiredException("MM-W-016", "updateVendorPrice", "I1, I2"));

        status.Should().Be(StatusCodes.Status422UnprocessableEntity);
        (body.GetProperty("code").GetString(), body.GetProperty("confirmField").GetString(), body.GetProperty("args")[0].GetString())
            .Should().Be(("MM-W-016", "updateVendorPrice", "I1, I2"));
    }

    [Fact]
    public async Task DomainException_HasNoConfirmField()
    {
        var (status, body) = await HandleAsync(new DomainException("MM-E-011", 1));

        status.Should().Be(StatusCodes.Status422UnprocessableEntity);
        body.TryGetProperty("confirmField", out _).Should().BeFalse();
    }

    private static async Task<(int Status, JsonElement Body)> HandleAsync(Exception exception)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        var handler = new ProblemExceptionHandler(services.GetRequiredService<IProblemDetailsService>());

        (await handler.TryHandleAsync(context, exception, CancellationToken.None)).Should().BeTrue();

        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, (await JsonDocument.ParseAsync(context.Response.Body)).RootElement.Clone());
    }
}
