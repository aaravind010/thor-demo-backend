using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;

// Serves HTTPS on 8443 only when TLS_CERT_PFX_PATH is set (NLB TLS re-encryption).
var certPath = Environment.GetEnvironmentVariable("TLS_CERT_PFX_PATH");
var certPassword = Environment.GetEnvironmentVariable("TLS_CERT_PFX_PASSWORD");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

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

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapControllers();

// Forwards /task-api/* to the internal Task API (see ReverseProxy config in appsettings).
app.MapReverseProxy();

app.Run();
