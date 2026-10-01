using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Thor.Api.Controllers.V1;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.Api.Test.Controllers.V1;

public class AuthenticationMethodsControllerTests
{
    private const short ConnectorTypeId = 1;

    private readonly string _masterDbName = $"master-{Guid.NewGuid()}";

    private MasterDbContext CreateMasterDbContext() =>
        new(new DbContextOptionsBuilder<MasterDbContext>().UseInMemoryDatabase(_masterDbName).Options);

    private void SeedMasterDb(Action<MasterDbContext> seed)
    {
        using var context = CreateMasterDbContext();
        seed(context);
        context.SaveChanges();
    }

    private Guid SeedAuthenticationType(string name, params short[] connectorTypes)
    {
        var id = Guid.NewGuid();
        SeedMasterDb(db =>
        {
            db.AuthenticationTypes.Add(new AuthenticationType { Id = id, Name = name });
            db.AuthenticationTypeConnectorTypes.AddRange(connectorTypes.Select(c =>
                new AuthenticationTypeConnectorType { AuthenticationTypeId = id, ConnectorTypeId = c }));
        });
        return id;
    }

    private AuthenticationMethodsController CreateController()
    {
        var masterFactory = Substitute.For<IMasterDbContextFactory>();
        masterFactory.Create(Arg.Any<MasterConnectionInfo>()).Returns(_ => CreateMasterDbContext());

        var masterConnectionInfo = new MasterConnectionInfo("host", "db", "user", "password");
        var service = new AuthenticationMethodService(
            Substitute.For<ITenantConnectionManager>(),
            masterFactory,
            masterConnectionInfo,
            Substitute.For<IAuthenticationSecretWriter>(),
            NullLogger<AuthenticationMethodService>.Instance);

        return new AuthenticationMethodsController(service, NullLogger<AuthenticationMethodsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    /// <summary>Returns every authentication type in the Master metadata DB, ordered by name.</summary>
    [Fact]
    public async Task ListTypes_ReturnsAllAuthenticationTypesOrderedByName()
    {
        SeedMasterDb(db => db.ConnectorTypes.Add(new ConnectorType { Id = ConnectorTypeId, Name = "Active Directory" }));
        var oauthId = SeedAuthenticationType("OAuth", ConnectorTypeId);
        var apiKeyId = SeedAuthenticationType("API Key");
        var controller = CreateController();

        var result = await controller.ListTypes(null, CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IReadOnlyList<AuthenticationTypeResponse>>().Subject;
        response.Should().Equal(
            new AuthenticationTypeResponse(apiKeyId, "API Key"),
            new AuthenticationTypeResponse(oauthId, "OAuth"));
    }

    /// <summary>With ?connectorType=, only the types mapped to that connector type are returned.</summary>
    [Fact]
    public async Task ListTypes_WithConnectorType_ReturnsOnlyMappedTypes()
    {
        const short otherConnectorTypeId = ConnectorTypeId + 1;
        SeedMasterDb(db => db.ConnectorTypes.AddRange(
            new ConnectorType { Id = ConnectorTypeId, Name = "Active Directory" },
            new ConnectorType { Id = otherConnectorTypeId, Name = "SailPoint" }));
        var passwordId = SeedAuthenticationType("Local Username and Password", ConnectorTypeId, otherConnectorTypeId);
        var ldapId = SeedAuthenticationType("LDAP Authentication", ConnectorTypeId);
        SeedAuthenticationType("API Key", otherConnectorTypeId);
        SeedAuthenticationType("SSH Key");
        var controller = CreateController();

        var result = await controller.ListTypes(ConnectorTypeId, CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IReadOnlyList<AuthenticationTypeResponse>>().Subject;
        response.Should().Equal(
            new AuthenticationTypeResponse(ldapId, "LDAP Authentication"),
            new AuthenticationTypeResponse(passwordId, "Local Username and Password"));
    }

    /// <summary>A connector type with no mapped auth types returns an empty list, not an error.</summary>
    [Fact]
    public async Task ListTypes_WithConnectorTypeWithNoMappings_ReturnsEmptyList()
    {
        SeedAuthenticationType("API Key");
        var controller = CreateController();

        var result = await controller.ListTypes(ConnectorTypeId, CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IReadOnlyList<AuthenticationTypeResponse>>().Subject;
        response.Should().BeEmpty();
    }

    /// <summary>An empty Master DB returns an empty list, not an error.</summary>
    [Fact]
    public async Task ListTypes_NoAuthenticationTypes_ReturnsEmptyList()
    {
        var controller = CreateController();

        var result = await controller.ListTypes(null, CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IReadOnlyList<AuthenticationTypeResponse>>().Subject;
        response.Should().BeEmpty();
    }

    /// <summary>Returns only the given type's fields, with their details, ordered by name.</summary>
    [Fact]
    public async Task ListFields_ReturnsFieldsForTypeOrderedByName()
    {
        var typeId = SeedAuthenticationType("Local Username and Password");
        var otherTypeId = SeedAuthenticationType("API Key");
        var usernameId = Guid.NewGuid();
        var passwordId = Guid.NewGuid();
        SeedMasterDb(db => db.AuthenticationFields.AddRange(
            new AuthenticationField { Id = usernameId, TypeId = typeId, Name = "username", DisplayName = "Username", InputType = "text", Description = "Login name" },
            new AuthenticationField { Id = passwordId, TypeId = typeId, Name = "password", DisplayName = "Password", InputType = "password" },
            new AuthenticationField { Id = Guid.NewGuid(), TypeId = otherTypeId, Name = "api_key", DisplayName = "API Key", InputType = "password" }));
        var controller = CreateController();

        var result = await controller.ListFields(typeId, CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IReadOnlyList<AuthenticationFieldResponse>>().Subject;
        response.Should().Equal(
            new AuthenticationFieldResponse(passwordId, "password", "Password", "password", null),
            new AuthenticationFieldResponse(usernameId, "username", "Username", "text", "Login name"));
    }

    /// <summary>A known type with no fields returns an empty list, not an error.</summary>
    [Fact]
    public async Task ListFields_TypeWithNoFields_ReturnsEmptyList()
    {
        var typeId = SeedAuthenticationType("API Key");
        var controller = CreateController();

        var result = await controller.ListFields(typeId, CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IReadOnlyList<AuthenticationFieldResponse>>().Subject;
        response.Should().BeEmpty();
    }

    /// <summary>An unknown type returns 404.</summary>
    [Fact]
    public async Task ListFields_UnknownType_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = await controller.ListFields(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }
}
