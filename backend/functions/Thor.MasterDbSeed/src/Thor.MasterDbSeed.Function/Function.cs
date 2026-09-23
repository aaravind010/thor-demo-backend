using System.Reflection;
using System.Text.Json;
using Amazon.Lambda.Core;
using Npgsql;
using Thor.DataLayer.Auth;
using Thor.DataLayer.Data;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Thor.MasterDbSeed.Function;

/// <summary>
/// Runs every .sql script embedded from Thor.DataLayer/Scripts/DeploymentSeed against the
/// Master DB, in filename order, inside one transaction. Connects through the RDS Proxy as the
/// least-privilege thor_master_seed IAM role (see db-roles.sql) — no password anywhere. Scripts
/// must be idempotent (safe to re-run), since Terraform re-invokes this whenever the Lambda's
/// build output changes (a script add/edit changes the embedded resource bytes, hence the hash).
/// Invoked by Terraform (<c>aws_lambda_invocation</c>) at apply.
/// </summary>
public sealed class Function
{
    private const string ResourcePrefix = "DeploymentSeed.";

    private readonly IRdsIamTokenProvider _tokenProvider;

    public Function() : this(new RdsIamTokenProvider())
    {
    }

    internal Function(IRdsIamTokenProvider tokenProvider) => _tokenProvider = tokenProvider;

    public async Task<string> FunctionHandler(JsonElement input, ILambdaContext context)
    {
        var scripts = ReadEmbeddedScripts();
        if (scripts.Count == 0)
        {
            context.Logger.LogInformation("No deployment seed scripts found; nothing to run.");
            return "ok";
        }

        var connectionInfo = new MasterConnectionInfo(
            Host: RequireEnv("THOR_MASTERDB_HOST"),
            Database: RequireEnv("THOR_MASTERDB_DATABASE"),
            Username: RequireEnv("THOR_MASTERDB_USER"),
            Region: RequireEnv("THOR_MASTERDB_REGION"),
            Port: int.Parse(Environment.GetEnvironmentVariable("THOR_MASTERDB_PORT") ?? "5432"));

        var token = _tokenProvider.GenerateToken(
            connectionInfo.Host, connectionInfo.Port, connectionInfo.Username, connectionInfo.Region);

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = connectionInfo.Host,
            Port = connectionInfo.Port,
            Database = connectionInfo.Database,
            Username = connectionInfo.Username,
            Password = token,
            SslMode = SslMode.Require,
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        foreach (var (name, sql) in scripts)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
            context.Logger.LogInformation("Applied deployment seed script {Script}.", name);
        }

        await transaction.CommitAsync();
        context.Logger.LogInformation(
            "Applied {Count} deployment seed script(s) to {Database}.", scripts.Count, connectionInfo.Database);
        return "ok";
    }

    // Ordered ordinally by resource name so "NNN_description.sql" filenames determine execution order.
    private static List<(string Name, string Sql)> ReadEmbeddedScripts()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException($"Embedded resource '{name}' was not found.");
                using var reader = new StreamReader(stream);
                return (name, reader.ReadToEnd());
            })
            .ToList();
    }

    private static string RequireEnv(string name) =>
        Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
}
