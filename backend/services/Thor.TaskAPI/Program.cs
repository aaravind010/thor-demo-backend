using Amazon.S3;
using DotNetEnv;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Thor.DataConnectionManager.Routing;
using Thor.DataLayer.Data;
using Thor.TaskApi.Services;

Env.Load();

// task-api always terminates TLS on Service Connect, so this is always set.
var certPath = Environment.GetEnvironmentVariable("TLS_CERT_PFX_PATH");
var certPassword = Environment.GetEnvironmentVariable("TLS_CERT_PFX_PASSWORD");

var builder = WebApplication.CreateBuilder(args);

if (!string.IsNullOrEmpty(certPath))
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(8443, listenOptions =>
        {
            listenOptions.UseHttps(new HttpsConnectionAdapterOptions
            {
                ServerCertificate = new X509Certificate2(certPath, certPassword)
            });
        });
    });
}

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// Master DB connection parameters (ADR §6.2/§6.3) — same THOR_MASTERDB_* env var
// convention as MasterDbContextDesignTimeFactory. Values are read from .env.
builder.Services.AddSingleton(new MasterConnectionInfo(
    Host: Environment.GetEnvironmentVariable("THOR_MASTERDB_HOST")!,
    Database: Environment.GetEnvironmentVariable("THOR_MASTERDB_DATABASE")!,
    Username: Environment.GetEnvironmentVariable("THOR_MASTERDB_USER")!,
    Password: Environment.GetEnvironmentVariable("THOR_MASTERDB_PASSWORD")!,
    Port: int.Parse(Environment.GetEnvironmentVariable("THOR_MASTERDB_PORT")!),
    UseSsl: false));

builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
builder.Services.AddSingleton(new UploadOptions(
    BucketName: Environment.GetEnvironmentVariable("THOR_UPLOADS_BUCKET")!));

builder.Services.AddSingleton<IMasterDbContextFactory, MasterDbContextFactory>();
builder.Services.AddSingleton<ITenantRoutingResolver, TenantRoutingResolver>();

builder.Services.AddScoped<UploadService>();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
