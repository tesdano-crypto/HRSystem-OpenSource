using HRSystem.Application.Approvals;

namespace HRSystem.Web.Approvals;

public static class LinePairingEndpoints
{
    public static IEndpointRouteBuilder MapLinePairingEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/integrations/line/private-pairing",
            async (HttpRequest request,
                LinePairingCallbackPayload payload,
                ILineApprovalBridgeRequestVerifier verifier,
                ILineUserBindingService bindings,
                CancellationToken cancellationToken) =>
            {
                var key = request.Headers["X-HRSystem-Bridge-Key"].FirstOrDefault();
                if (!verifier.IsAuthorized(key)) return Results.Unauthorized();
                var result = await bindings.CompletePrivatePairingAsync(
                    new(payload.PairingToken, payload.LineUserId, payload.SourceType),
                    cancellationToken);
                return result.Succeeded
                    ? Results.Ok(new { result.SafeMessage })
                    : Results.BadRequest(new { result.SafeMessage });
            })
            .AllowAnonymous()
            .DisableAntiforgery();
        return endpoints;
    }
}

public sealed record LinePairingCallbackPayload(
    string PairingToken,
    string LineUserId,
    string SourceType);
