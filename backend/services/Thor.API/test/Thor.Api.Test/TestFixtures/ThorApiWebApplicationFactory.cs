using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Thor.Api.Test.TestFixtures;

/// <summary>
/// Boots Thor.Api in-process for endpoint tests. Program.cs resolves its Master DB, token-signing and
/// Intelligence Engine settings through <c>RequiredEnvironment.GetVariable</c>, which throws rather
/// than falling back to a default (ADR §5/§6), so the host cannot start with any of them unset —
/// a plain <see cref="WebApplicationFactory{TEntryPoint}"/> fails in its constructor.
///
/// The values below are placeholders. The tests that use this factory exercise routing, API
/// versioning and the OpenAPI document only; none of them opens a database, gRPC or signing
/// operation, so the settings need to be present and well-formed rather than real.
/// </summary>
public sealed class ThorApiWebApplicationFactory : WebApplicationFactory<Program>
{
    public ThorApiWebApplicationFactory()
    {
        // Generated per run rather than checked in: ConnectorSecurityOptions holds the PEM for
        // ThorTokenIssuer to import lazily, so it has to parse, and committing key-shaped text
        // trips secret scanning for no benefit.
        using var signingKey = RSA.Create(2048);

        var startup = new Dictionary<string, string>
        {
            ["THOR_MASTERDB_HOST"] = "localhost",
            ["THOR_MASTERDB_DATABASE"] = "thor_masterdb_test",
            ["THOR_MASTERDB_USER"] = "thor_app",
            ["THOR_MASTERDB_REGION"] = "us-east-1",
            ["THOR_MASTERDB_PORT"] = "5432",
            ["THOR_TASKAPI_JWT_PRIVATE_KEY"] = signingKey.ExportPkcs8PrivateKeyPem(),
            ["THOR_API_KEY_PEPPER"] = "test-pepper",
            ["THOR_INTELLIGENCE_ENGINE_GRPC_ADDRESS"] = "http://localhost:50051",
        };

        foreach (var (name, value) in startup)
        {
            // Leave a real value alone, so the suite can also be pointed at a configured environment.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
