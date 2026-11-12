using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace QuroErp.Api.Common.OpenApi;

/// <summary>
/// Adds the JWT bearer scheme to the OpenAPI document so Swagger UI shows Authorize: sign in with POST /api/auth/login and paste
/// its accessToken. Every endpoint needs it except the anonymous ones (security.md).
/// </summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeName = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "accessToken from POST /api/auth/login",
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SchemeName, document)] = [] });
        return Task.CompletedTask;
    }
}
