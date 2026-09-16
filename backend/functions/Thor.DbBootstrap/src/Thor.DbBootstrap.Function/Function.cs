using System.Reflection;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Thor.DataLayer.Data;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Thor.DbBootstrap.Function;

/// <summary>
/// One-time (re-runnable) cluster bootstrap: applies the Master DB migrations, then creates the
/// least-privilege DB roles the platform assumes at runtime. Runs in-VPC as the Aurora master
/// user — the only place master credentials are ever used — over a direct connection to the
/// writer. Migrations run first so the role script's table grants (guarded on the tables
/// existing) land in the same invocation on a brand-new cluster. Both halves are idempotent.
/// Invoked by Terraform (<c>aws_lambda_invocation</c>) at apply.
/// </summary>
public sealed class Function
{
    private const string SqlResourceName = "db-roles.sql";

    private readonly IAmazonSecretsManager _secrets;

    public Function() : this(new AmazonSecretsManagerClient())
    {
    }

    internal Function(IAmazonSecretsManager secrets) => _secrets = secrets;

    public async Task<string> FunctionHandler(JsonElement input, ILambdaContext context)
    {
        var secretArn = RequireEnv("THOR_BOOTSTRAP_SECRET_ARN");
        var host = RequireEnv("THOR_BOOTSTRAP_HOST");
        var database = RequireEnv("THOR_BOOTSTRAP_DATABASE");
        var port = int.Parse(Environment.GetEnvironmentVariable("THOR_BOOTSTRAP_PORT") ?? "5432");

        var (username, password) = await GetMasterCredentialsAsync(secretArn);

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password,
            SslMode = SslMode.Require,
        }.ConnectionString;

        await ApplyMasterMigrationsAsync(connectionString);
        context.Logger.LogInformation("Master DB migrations applied to {Database}.", database);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Npgsql's SQL parser understands dollar-quoting, so the whole script (DO $$…$$ blocks and
        // all) executes as one batch — no manual statement splitting.
        var sql = ReadEmbeddedSql();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();

        context.Logger.LogInformation("DB role bootstrap applied to {Database}.", database);
        return "ok";
    }

    // MigrateAsync only applies migrations missing from the history table, so re-invocation is a
    // no-op once the schema is current. The history table lives in the master schema alongside
    // the rest of the platform metadata rather than in public.
    private static async Task ApplyMasterMigrationsAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MasterDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "master"))
            .Options;

        await using var db = new MasterDbContext(options);
        await db.Database.MigrateAsync();
    }

    private async Task<(string Username, string Password)> GetMasterCredentialsAsync(string secretArn)
    {
        var response = await _secrets.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretArn });
        using var document = JsonDocument.Parse(response.SecretString);
        var root = document.RootElement;
        return (root.GetProperty("username").GetString()!, root.GetProperty("password").GetString()!);
    }

    private static string ReadEmbeddedSql()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(SqlResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{SqlResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string RequireEnv(string name) =>
        Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
}
