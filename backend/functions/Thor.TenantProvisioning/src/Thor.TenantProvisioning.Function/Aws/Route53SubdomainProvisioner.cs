using Amazon.Route53;
using Amazon.Route53.Model;
using Microsoft.Extensions.Logging;
using Thor.TenantProvisioning.Core.Abstractions;

namespace Thor.TenantProvisioning.Function.Aws;

/// <summary>Environment-level Route53 configuration for tenant subdomain records.</summary>
public sealed record Route53Options(string HostedZoneId, string BaseDomain, string DnsTarget);

/// <summary>
/// <see cref="ISubdomainProvisioner"/> over Route53. Creates a CNAME
/// <c>{subdomain}.{baseDomain} → {dnsTarget}</c> via UPSERT, which is idempotent.
/// </summary>
public sealed class Route53SubdomainProvisioner(
    IAmazonRoute53 client,
    Route53Options options,
    ILogger<Route53SubdomainProvisioner> logger)
    : ISubdomainProvisioner
{
    public async Task EnsureSubdomainAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        var fqdn = $"{subdomain}.{options.BaseDomain}";

        await client.ChangeResourceRecordSetsAsync(new ChangeResourceRecordSetsRequest
        {
            HostedZoneId = options.HostedZoneId,
            ChangeBatch = new ChangeBatch
            {
                Changes =
                [
                    new Change
                    {
                        Action = ChangeAction.UPSERT,
                        ResourceRecordSet = new ResourceRecordSet
                        {
                            Name = fqdn,
                            Type = RRType.CNAME,
                            TTL = 300,
                            ResourceRecords = [new ResourceRecord { Value = options.DnsTarget }],
                        },
                    },
                ],
            },
        }, cancellationToken);

        logger.LogInformation("Upserted CNAME {Fqdn} -> {DnsTarget}.", fqdn, options.DnsTarget);
    }
}
