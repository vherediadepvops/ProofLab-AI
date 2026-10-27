using System.Net;

namespace ProofLab.Api;

/// <summary>
/// Public-facing diagnostics never contain the provider's raw error body,
/// bearer tokens, request prompts or the user's own account identifiers.
/// </summary>
public static class MetaSafeDiagnostics
{
    public static IResult ProviderFailure(HttpRequestException exception, string model)
    {
        var upstream = exception.StatusCode;
        var category = upstream switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
                "INVALID_PROVIDER_REQUEST",
            HttpStatusCode.Unauthorized => "TOKEN_REJECTED_OR_EXPIRED",
            HttpStatusCode.Forbidden => "MODEL_ACCESS_FORBIDDEN",
            HttpStatusCode.TooManyRequests => "RATE_LIMIT",
            HttpStatusCode.PaymentRequired => "BILLING_OR_CREDIT_ISSUE",
            HttpStatusCode.NotFound => "MODEL_OR_ENDPOINT_NOT_FOUND",
            >= HttpStatusCode.InternalServerError => "PROVIDER_SERVER_ERROR",
            null => "TRANSPORT_OR_NETWORK_ERROR",
            _ => "PROVIDER_REJECTED_REQUEST"
        };

        return Results.Json(new
        {
            error = "META_PROVIDER_TEST_FAILED",
            model,
            providerHttpStatus = upstream is null ? (int?)null : (int)upstream,
            category,
            requestMayHaveBeenBilled = true,
            retryAutomatically = false,
            detail = "Only the upstream HTTP status is disclosed; no provider error body or API key is returned."
        }, statusCode: StatusCodes.Status502BadGateway);
    }
}
