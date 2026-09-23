using Amazon.S3;
using Asp.Versioning;
using DotNetEnv;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Scalar.AspNetCore;
using Serilog;
using System.Security.Cryptography.X509Certificates;
using Thor.Auth;
using Thor.DataConnectionManager;
using Thor.DataConnectionManager.Caching;
using Thor.DataConnectionManager.Routing;
using Thor.DataConnectionManager.Validation;
using Thor.DataLayer.Auth;
using Thor.DataLayer.Data;
using Thor.TaskApi.Constants;
using Thor.TaskApi.Middleware;
using Thor.Core.Logging;
using Thor.TaskApi.Services;

Env.Load();

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // task-api always terminates TLS on Service Connect, so this is always set.
    var certPath = Environment.GetEnvironmentVariable("TLS_CERT_PFX_PATH");
    var certPassword = Environment.GetEnvironmentVariable("TLS_CERT_PFX_PASSWORD");

    if (!string.IsNullOrEmpty(certPath))
    {
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(8443, listenOptions =>
            {
                listenOptions.UseHttps(new HttpsConnectionAdapterOptions
                {
                    ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile(certPath, certPassword)
                });
            });
        });
    }

    builder.Host.UseThorLogging();

    builder.Services.AddControllers();
    builder.Services.AddHealthChecks();

    // API Versioning
    builder.Services.AddApiVersioning(options => {
        options.DefaultApiVersion = new ApiVersion(1.0);
        options.ReportApiVersions = true;

        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader()
        );
    })
    .AddMvc()
    .AddApiExplorer(options => {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    })
    // Asp.Versioning.OpenApi defaults Info.Title/Description/Version from Assembly.GetEntryAssembly(),
    // which resolves to the GetDocument.Insider build-time tool (not Thor.TaskApi) when this doc is
    // regenerated via Microsoft.Extensions.ApiDescription.Server — set it explicitly instead.
    .AddOpenApi(options =>
    {
        options.Document.AddDocumentTransformer((document, context, _) =>
        {
            document.Info.Title = $"Thor.TaskApi | {options.Description.GroupName}";
            document.Info.Description = null;
            document.Info.Version = options.Description.ApiVersion.ToString();
            return Task.CompletedTask;
        });
    });


    // Master DB connection parameters (ADR §6.2/§6.3) — same THOR_MASTERDB_* env var
    // convention as MasterDbContextDesignTimeFactory. Values are read from .env.
    builder.Services.AddSingleton(new MasterConnectionInfo(
        Host: Environment.GetEnvironmentVariable("THOR_MASTERDB_HOST")!,
        Database: Environment.GetEnvironmentVariable("THOR_MASTERDB_DATABASE")!,
        Username: Environment.GetEnvironmentVariable("THOR_MASTERDB_USER")!,
        Region: Environment.GetEnvironmentVariable("THOR_MASTERDB_REGION")!,
        Port: int.Parse(Environment.GetEnvironmentVariable("THOR_MASTERDB_PORT")!),
        UseSsl: false));

    // RDS IAM auth token minting (ADR §6.2/§6.3) — required by MasterDbContextFactory;
    // there is no password auth path.
    builder.Services.AddSingleton<IRdsIamTokenProvider, RdsIamTokenProvider>();
    builder.Services.AddSingleton<IMasterDbContextFactory, MasterDbContextFactory>();


    builder.Services.AddSingleton<ITenantRoutingResolver, TenantRoutingResolver>();
    builder.Services.AddSingleton<ITenantLogSinkResolver, TenantLogSinkResolver>();

    // Tenant DB connection flow (ADR §6.3): resolve routing, open an IAM-authenticated
    // connection, validate it targets the expected tenant database before use.
    builder.Services.AddSingleton<ITenantConnectionManager, TenantConnectionManager>();
    builder.Services.AddSingleton<ITenantConnectionValidator, TenantConnectionValidator>();
    builder.Services.AddSingleton<TenantConnectionCache>();
    builder.Services.AddSingleton<ITenantDbContextFactory, TenantDbContextFactory>();
    
    builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
    builder.Services.AddSingleton(new UploadOptions(
        BucketName: Environment.GetEnvironmentVariable("THOR_UPLOADS_BUCKET")!));

    // RSA public key (RS256) matching the private key Thor.Api's connector registration flow signs
    // with (ADR §5.2/§8), sourced from Secrets Manager via env var in deployed environments. TaskApi
    // never holds the private key, so it can verify tokens but never forge them.
    builder.Services.AddSingleton<ITokenValidator>(_ => new ThorTokenValidator(
        Environment.GetEnvironmentVariable("THOR_TASKAPI_JWT_PUBLIC_KEY")!));

    builder.Services.AddScoped<UploadService>();

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().WithDocumentPerVersion();
        app.MapScalarApiReference();
    }

    // Headers this service wants attached to its logs, and what to call them in the log
    // context. Header-log-enrichment must wrap request logging (not the other way round) —
    // its LogContext scope needs to still be active when UseSerilogRequestLogging logs the
    // request-completion line after next() returns.
    var headerLogProperties = new Dictionary<string, string>
    {
        [UploadConstants.TenantHeaderName] = "TenantId",
    };

    app.UseMiddleware<HeaderLogEnrichmentMiddleware>(headerLogProperties);
    app.UseSerilogRequestLogging();

    app.MapHealthChecks("/health");

    // Every other endpoint requires the connector JWT from Thor.Api's registration flow; the exempt
    // routes in AuthExemptRoutes (health checks — ALB/ECS target group probes never carry one) are
    // excluded from the gate.
    app.UseWhen(
        context => !AuthExemptRoutes.PathPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix)),
        appBuilder => appBuilder.UseMiddleware<TaskApiAuthMiddleware>());

    app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            var version = context.RequestedApiVersion;
            if (version is not null)
            {
                context.Response.Headers["api-version"] = version.ToString();
            }
            return Task.CompletedTask;
        });

        await next();
    });

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
