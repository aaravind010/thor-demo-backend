using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Microsoft.Extensions.Logging;
using Thor.TenantProvisioning.Core.Abstractions;

namespace Thor.TenantProvisioning.Function.Aws;

/// <summary>
/// <see cref="ICognitoProvisioner"/> over the AWS Cognito Identity Provider SDK. Each
/// operation resolves-or-creates so Step Functions retries never duplicate a pool,
/// group, or user.
/// </summary>
public sealed class CognitoProvisioner(
    IAmazonCognitoIdentityProvider client,
    ILogger<CognitoProvisioner> logger)
    : ICognitoProvisioner
{
    public async Task<CognitoPool> EnsureUserPoolAsync(
        string poolName, string appClientName, CancellationToken cancellationToken = default)
    {
        var userPoolId = await FindUserPoolByNameAsync(poolName, cancellationToken);
        if (userPoolId is null)
        {
            var created = await client.CreateUserPoolAsync(
                new CreateUserPoolRequest { PoolName = poolName }, cancellationToken);
            userPoolId = created.UserPool.Id;
            logger.LogInformation("Created Cognito user pool {UserPoolId} ({PoolName}).", userPoolId, poolName);
        }

        var appClientId = await EnsureAppClientAsync(userPoolId, appClientName, cancellationToken);
        return new CognitoPool(userPoolId, appClientId);
    }

    public async Task EnsureAdminGroupAsync(
        string userPoolId, string groupName, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.CreateGroupAsync(
                new CreateGroupRequest { UserPoolId = userPoolId, GroupName = groupName }, cancellationToken);
            logger.LogInformation("Created group {GroupName} in pool {UserPoolId}.", groupName, userPoolId);
        }
        catch (GroupExistsException)
        {
            logger.LogInformation("Group {GroupName} already exists in pool {UserPoolId}.", groupName, userPoolId);
        }
    }

    public async Task EnsureAdminUserAsync(
        string userPoolId, string groupName, string email, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.AdminCreateUserAsync(new AdminCreateUserRequest
            {
                UserPoolId = userPoolId,
                Username = email,
                UserAttributes =
                [
                    new AttributeType { Name = "email", Value = email },
                    new AttributeType { Name = "email_verified", Value = "true" },
                ],
                DesiredDeliveryMediums = [DeliveryMediumType.EMAIL],
            }, cancellationToken);
            logger.LogInformation("Created admin user in pool {UserPoolId}.", userPoolId);
        }
        catch (UsernameExistsException)
        {
            logger.LogInformation("Admin user already exists in pool {UserPoolId}.", userPoolId);
        }

        // Idempotent: re-adding a user already in the group is a no-op.
        await client.AdminAddUserToGroupAsync(new AdminAddUserToGroupRequest
        {
            UserPoolId = userPoolId,
            Username = email,
            GroupName = groupName,
        }, cancellationToken);
    }

    private async Task<string?> FindUserPoolByNameAsync(string poolName, CancellationToken cancellationToken)
    {
        string? nextToken = null;
        do
        {
            var response = await client.ListUserPoolsAsync(
                new ListUserPoolsRequest { MaxResults = 60, NextToken = nextToken }, cancellationToken);

            var match = response.UserPools.FirstOrDefault(pool => pool.Name == poolName);
            if (match is not null)
            {
                return match.Id;
            }

            nextToken = response.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

        return null;
    }

    private async Task<string> EnsureAppClientAsync(
        string userPoolId, string appClientName, CancellationToken cancellationToken)
    {
        string? nextToken = null;
        do
        {
            var response = await client.ListUserPoolClientsAsync(
                new ListUserPoolClientsRequest { UserPoolId = userPoolId, MaxResults = 60, NextToken = nextToken },
                cancellationToken);

            var match = response.UserPoolClients.FirstOrDefault(appClient => appClient.ClientName == appClientName);
            if (match is not null)
            {
                return match.ClientId;
            }

            nextToken = response.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

        var created = await client.CreateUserPoolClientAsync(new CreateUserPoolClientRequest
        {
            UserPoolId = userPoolId,
            ClientName = appClientName,
            GenerateSecret = false,
        }, cancellationToken);

        logger.LogInformation("Created app client {AppClientId} ({AppClientName}) in pool {UserPoolId}.",
            created.UserPoolClient.ClientId, appClientName, userPoolId);
        return created.UserPoolClient.ClientId;
    }
}
