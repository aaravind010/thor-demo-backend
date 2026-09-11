using Amazon.S3;
using DotNetEnv;
using Thor.DataConnectionManager.Routing;
using Thor.DataLayer.Auth;
using Thor.DataLayer.Data;
using Thor.TaskApi.Services;
using Asp.Versioning;

Env.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// API Versioning
builder.Services.AddApiVersioning(options => {
    options.DefaultApiVersion = new ApiVersion(1.0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;

    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader()
    );
})
.AddApiExplorer(options => {
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// Master DB connection parameters (ADR §6.2/§6.3). Auth is always RDS IAM — the task role
// connects with a short-lived IAM token (no password). Values are read from the environment.
builder.Services.AddSingleton(new MasterConnectionInfo(
    Host: Environment.GetEnvironmentVariable("THOR_MASTERDB_HOST")!,
    Database: Environment.GetEnvironmentVariable("THOR_MASTERDB_DATABASE")!,
    Username: Environment.GetEnvironmentVariable("THOR_MASTERDB_USER")!,
    Region: Environment.GetEnvironmentVariable("THOR_MASTERDB_REGION")!,
    Port: int.Parse(Environment.GetEnvironmentVariable("THOR_MASTERDB_PORT")!),
    UseSsl: true));

builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
builder.Services.AddSingleton(new UploadOptions(
    BucketName: Environment.GetEnvironmentVariable("THOR_UPLOADS_BUCKET")!));

// Master DB stays in password mode here (credentials come from a managed secret, injected
// securely by ECS at runtime). The token provider is registered because MasterDbContextFactory
// depends on it; it is only invoked when a MasterConnectionInfo opts into IAM auth.
builder.Services.AddSingleton<IRdsIamTokenProvider, RdsIamTokenProvider>();
builder.Services.AddSingleton<IMasterDbContextFactory, MasterDbContextFactory>();
builder.Services.AddSingleton<ITenantRoutingResolver, TenantRoutingResolver>();

builder.Services.AddScoped<UploadService>();

var app = builder.Build();

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

app.Run();
