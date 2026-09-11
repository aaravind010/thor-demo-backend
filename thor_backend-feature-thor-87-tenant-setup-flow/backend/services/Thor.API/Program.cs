using Asp.Versioning;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

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

// Forwards /task-api/* to the internal Task API (see ReverseProxy config in appsettings).
app.MapReverseProxy();

app.Run();
