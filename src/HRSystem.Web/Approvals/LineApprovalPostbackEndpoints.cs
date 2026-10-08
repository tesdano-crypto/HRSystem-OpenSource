using HRSystem.Application.Approvals;

namespace HRSystem.Web.Approvals;

public static class LineApprovalPostbackEndpoints
{
    public static IEndpointRouteBuilder MapLineApprovalPostbackEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/integrations/line/approval-postback",
            async (HttpRequest request,
                LineApprovalPostbackPayload payload,
                ILineApprovalBridgeRequestVerifier verifier,
                IApprovalService approvals,
                CancellationToken cancellationToken) =>
            {
                var key = request.Headers["X-HRSystem-Bridge-Key"].FirstOrDefault();
                if (!verifier.IsAuthorized(key)) return Results.Unauthorized();
                var result = await approvals.HandleLinePostbackAsync(
                    new(payload.LineUserId, payload.Token, payload.Reason),
                    cancellationToken);
                return result.Succeeded
                    ? Results.Ok(new { result.SafeMessage, result.ApprovalId, result.Status })
                    : Results.BadRequest(new { result.SafeMessage, result.ApprovalId, result.Status });
            })
            .AllowAnonymous()
            .DisableAntiforgery();
        return endpoints;
    }
}

public sealed record LineApprovalPostbackPayload(
    string LineUserId,
    string Token,
    string? Reason);
