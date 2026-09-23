using Amazon.SecretsManager;
using Asp.Versioning;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using DotNetEnv;
using Scalar.AspNetCore;
using Serilog;
using Thor.Api.Constants;
using Thor.Api.Middleware;
using Thor.Api.Services;
using Thor.Api.Utils;
using Thor.Auth;
using Thor.Core.Logging;
using Thor.DataConnectionManager;
using Thor.DataConnectionManager.Caching;
using Thor.DataConnectionManager.Routing;
using Thor.DataConnectionManager.Validation;
using Thor.DataLayer.Auth;
using Thor.DataLayer.Data;
using Thor.IntelligenceEngine.Grpc.V1;

Env.Load();

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();


try
{

    var builder = WebApplication.CreateBuilder(args);

    // Serves HTTPS on 8443 only when TLS_CERT_PFX_PATH is set (NLB TLS re-encryption).
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
    builder.Services.AddReverseProxy()
        .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));


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
    // which resolves to the GetDocument.Insider build-time tool (not Thor.Api) when this doc is
    // regenerated via Microsoft.Extensions.ApiDescription.Server — set it explicitly instead.
    .AddOpenApi(options =>
    {
        options.Document.AddDocumentTransformer((document, context, _) =>
        {
            document.Info.Title = $"Thor.Api | {options.Description.GroupName}";
            document.Info.Description = null;
            document.Info.Version = options.Description.ApiVersion.ToString();
            return Task.CompletedTask;
        });
    });

    // Secrets Manager is real AWS infra with no local emulator in this repo; in Development,
    // swap in an in-memory writer instead.
    if (builder.Environment.IsDevelopment())
    {
        builder.Services.AddSingleton<IAuthenticationSecretWriter, LocalAuthenticationSecretWriter>();
    }
    else
    {
        builder.Services.AddSingleton<IAmazonSecretsManager>(_ => new AmazonSecretsManagerClient());
        builder.Services.AddSingleton<IAuthenticationSecretWriter, AuthenticationSecretWriter>();
    }


    // Master DB connection parameters (ADR §6.2/§6.3) — same THOR_MASTERDB_* env var
    // convention as MasterDbContextDesignTimeFactory.
    builder.Services.AddSingleton(new MasterConnectionInfo(
        Host: RequiredEnvironment.GetVariable("THOR_MASTERDB_HOST"),
        Database: RequiredEnvironment.GetVariable("THOR_MASTERDB_DATABASE"),
        Username: RequiredEnvironment.GetVariable("THOR_MASTERDB_USER"),
        Region: RequiredEnvironment.GetVariable("THOR_MASTERDB_REGION"),
        Port: int.Parse(RequiredEnvironment.GetVariable("THOR_MASTERDB_PORT")),
        UseSsl: true));

    // RDS IAM auth token minting (ADR §6.2/§6.3) — shared by the Master DB factory and the
    // per-tenant connection manager; there is no password auth path.
    builder.Services.AddSingleton<IRdsIamTokenProvider, RdsIamTokenProvider>();
    builder.Services.AddSingleton<IMasterDbContextFactory, MasterDbContextFactory>();
    builder.Services.AddSingleton<ITenantLogSinkResolver, TenantLogSinkResolver>();

    // RSA private key (RS256) for signing access tokens + API-key/refresh-token hashing pepper
    // (ADR §5.2/§8), sourced from Secrets Manager via env var in deployed environments. Only
    // Thor.Api holds the private key; Thor.TaskApi validates with the matching public key only.
    builder.Services.AddSingleton(new ConnectorSecurityOptions(
        JwtPrivateKeyPem: RequiredEnvironment.GetVariable("THOR_TASKAPI_JWT_PRIVATE_KEY"),
        SecretPepper: RequiredEnvironment.GetVariable("THOR_API_KEY_PEPPER")));

    builder.Services.AddSingleton<ITokenIssuer, ThorTokenIssuer>();
    builder.Services.AddScoped<IAuthService, ConnectorAuthService>();
    builder.Services.AddScoped<AuthenticationMethodService>();
    builder.Services.AddScoped<ScanConfigService>();
    builder.Services.AddScoped<ScanService>();
    builder.Services.AddScoped<ConnectorApiKeyService>();
    builder.Services.AddScoped<SourceService>();
    builder.Services.AddScoped<AccountTypeService>();

    // Cognito user-token validation (see CLAUDE.md/ADR §5.1) — tenant's user pool is resolved from
    // the Host subdomain via ITenantRoutingResolver, same Master DB tenant_routing row the
    // Lambda authorizer resolves from, so both sides agree on which pool a token must verify against.
    builder.Services.AddHttpClient("CognitoJwks");
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton(new JwksProviderOptions { Ttl = TimeSpan.FromMinutes(10) });
    builder.Services.AddSingleton<IJwksProvider, CognitoJwksProvider>();
    builder.Services.AddSingleton<ICognitoValidator, CognitoJwtValidator>();
    builder.Services.AddSingleton<ITenantRoutingResolver, TenantRoutingResolver>();

    // Intelligence Engine gRPC client (see backend/services/Thor.IntelligenceEngine, shared
    // contract at proto/thor/intelligence_engine/v1). The Engine is only reachable from Thor.Api
    // over the private network (ADR §4) - no app-layer auth, tenant_id travels as a plain field.
    // Required for the local-dev address (plaintext HTTP/2, no TLS) - Grpc.Net.Client otherwise
    // refuses to negotiate HTTP/2 over a cleartext connection. No-op against the deployed HTTPS
    // address.
    AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    builder.Services
        .AddGrpcClient<IntelligenceEngineService.IntelligenceEngineServiceClient>(o =>
            o.Address = new Uri(RequiredEnvironment.GetVariable("THOR_INTELLIGENCE_ENGINE_GRPC_ADDRESS")))
        .ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler();
            // Infra bakes a self-signed cert into the Intelligence Engine container (openssl step
            // in its Dockerfile, see infra/src/modules/ecs/services.tf) in every environment - the
            // same tradeoff infra's own `curl -k` ECS healthcheck already accepts for this service.
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            return handler;
        });
    builder.Services.AddScoped<IIntelligenceEngineClient, IntelligenceEngineClient>();
    // Tenant DB connection flow (ADR §6.3): resolve routing, open an IAM-authenticated
    // connection, validate it targets the expected tenant database before use.
    builder.Services.AddSingleton<ITenantConnectionValidator, TenantConnectionValidator>();
    builder.Services.AddSingleton<TenantConnectionCache>();
    builder.Services.AddSingleton<ITenantDbContextFactory, TenantDbContextFactory>();
    builder.Services.AddSingleton<ITenantConnectionManager, TenantConnectionManager>();
    
    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().WithDocumentPerVersion();
        app.MapScalarApiReference();
    }

    

    // Cognito auth gate: everything except the exempt routes in AuthExemptRoutes (health checks,
    // the connector API-key registration flow — RegisterController, a different credential type,
    // see ADR §5.2 — and the reverse-proxied TaskAPI routes, which validate their own Thor-issued
    // connector JWT independently).
    
    // COMMENTING OUT THE TOKEN VALIDATOR FOR DEV TESTING, SHOULD NOT BE MOVED TO QA or PROD ENVS
    // app.UseWhen(
    //     context =>
    //     {
    //         // A special check for register endpoint is needed as it has the version prefix `/v<version>/register`. Rest of the exempted endpoints are absolute
    //         var path = context.Request.Path;
    //         var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
    //         var isRegisterRoute = segments.Length >= 2 && segments[1].Equals(AuthExemptRoutes.RegisterSegment, StringComparison.OrdinalIgnoreCase);

    //         return !AuthExemptRoutes.PathPrefixes.Any(prefix => path.StartsWithSegments(prefix))
    //             && !isRegisterRoute;
    //     },
    //     appBuilder => appBuilder.UseMiddleware<CognitoAuthMiddleware>());


    // Headers this service wants attached to its logs, and what to call them in the log
    // context. Header-log-enrichment must wrap request logging (not the other way round) —
    // its LogContext scope needs to still be active when UseSerilogRequestLogging logs the
    // request-completion line after next() returns.
    var headerLogProperties = new Dictionary<string, string>
    {
        ["X-THOR-TENANT-ID"] = "TenantId",
    };

    app.UseMiddleware<HeaderLogEnrichmentMiddleware>(headerLogProperties);
    app.UseSerilogRequestLogging();

    app.MapHealthChecks("/health");

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

    // Forwards /task-api/* to the internal Task API (see ReverseProxy config in appsettings).
    app.MapReverseProxy();

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
