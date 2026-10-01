using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Thor.Api.Constants;
using Thor.DataConnectionManager;
using Thor.DataConnectionManager.Exceptions;
using Thor.DataLayer.Data;

namespace Thor.Api.Test.Support;

/// <summary>
/// One isolated in-memory tenant database plus an <see cref="ITenantConnectionManager"/> that hands
/// out fresh contexts over it — the same setup each controller test class used to build inline.
/// </summary>
public sealed class InMemoryTenant
{
    private readonly string _databaseName = $"tenant-{Guid.NewGuid()}";

    // The in-memory provider doesn't support real transactions; services that wrap writes in one
    // trigger this warning, which is expected and safe to ignore in tests.
    public TenantDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    public ITenantConnectionManager ConnectionManager()
    {
        var manager = Substitute.For<ITenantConnectionManager>();
        manager.GetTenantDbContextAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(CreateDbContext()));
        return manager;
    }

    /// <summary>A connection manager that fails closed, as it does for a tenant with no routing.</summary>
    public static ITenantConnectionManager UnknownTenantConnectionManager()
    {
        var manager = Substitute.For<ITenantConnectionManager>();
        manager.GetTenantDbContextAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromException<TenantDbContext>(new TenantNotFoundException(call.Arg<Guid>())));
        return manager;
    }

    public void Seed(params object[] entities)
    {
        using var db = CreateDbContext();
        db.AddRange(entities);
        db.SaveChanges();
    }

    /// <summary>
    /// Gives <paramref name="controller"/> an HTTP context carrying the trusted tenant header —
    /// a random guid by default; pass a non-guid string for the "invalid header" path, or
    /// <paramref name="includeTenantHeader"/>: false to omit it.
    /// </summary>
    public static T WithTenantHeader<T>(T controller, bool includeTenantHeader = true, string? tenantHeaderValue = null)
        where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        if (includeTenantHeader)
        {
            controller.Request.Headers[TenantConstants.TenantHeaderName] = tenantHeaderValue ?? Guid.NewGuid().ToString();
        }

        return controller;
    }
}
