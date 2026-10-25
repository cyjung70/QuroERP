using System.Net.Http.Headers;
using System.Text.Json;
using QuroErp.Contracts.Common;

namespace QuroErp.Web.Common;

/// <summary>API가 실패로 응답했다. Problem.Code를 ProblemMessages로 사용자 언어 메시지로 바꿔 보여준다.</summary>
public sealed class ApiProblemException(ApiProblem problem)
    : Exception($"API 오류 {problem.Status} {problem.Code}")
{
    public ApiProblem Problem { get; } = problem;
}

/// <summary>실패 응답 본문(ProblemDetails)에서 code·args를 읽는다.</summary>
public static class ApiProblemReader
{
    public static async Task<ApiProblem> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        if (!IsJson(response.Content.Headers.ContentType))
        {
            return new ApiProblem(status, null, []);
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ApiProblem(status, null, []);
            }
            var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var args = root.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().Select(ArgText).ToList()
                : [];
            return new ApiProblem(status, code, args);
        }
        catch (JsonException)
        {
            return new ApiProblem(status, null, []);
        }
    }

    // application/json, application/problem+json
    private static bool IsJson(MediaTypeHeaderValue? contentType) =>
        contentType?.MediaType is { } mediaType
        && (mediaType == "application/json" || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

    private static string ArgText(JsonElement arg) =>
        arg.ValueKind == JsonValueKind.String ? arg.GetString() ?? "" : arg.GetRawText();
}
