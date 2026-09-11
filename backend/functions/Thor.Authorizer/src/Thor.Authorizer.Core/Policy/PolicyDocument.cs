namespace Thor.Authorizer.Core.Policy;

/// <summary>
/// Mirrors the shape API Gateway expects from a custom authorizer response, kept as a
/// Core-owned DTO so Core has zero dependency on Amazon.Lambda.APIGatewayEvents. The Function
/// project maps this to the real AWS event type at the boundary.
/// </summary>
public sealed record PolicyDocument(
    string PrincipalId,
    string Effect,
    string Resource,
    IReadOnlyDictionary<string, string> Context
);
