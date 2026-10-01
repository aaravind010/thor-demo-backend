using Thor.Workflows.Ingestion.AttributeMapping;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.AttributeMapping;

public class AttributeMapProviderTests
{
    [Fact]
    public void GetMap_LoadsShippedAdAccountMap()
    {
        var provider = new AttributeMapProvider();

        var map = provider.GetMap("ActiveDirectory", "Account");

        Assert.Equal("ActiveDirectory", map.Connector);
        Assert.Equal("Account", map.EntityKind);
        Assert.Equal("displayName", map.ColumnMappings["display_name"]);
        Assert.Equal("sAMAccountName", map.ColumnMappings["sam_account_name"]);
        Assert.Equal("userPrincipalName", map.ColumnMappings["upn"]);
        Assert.Equal("mail", map.ColumnMappings["email"]);
        Assert.Equal("objectSid", map.ColumnMappings["native_account_id"]);
        Assert.Contains("edge_refs", map.HashFields);
    }

    [Fact]
    public void GetMap_LoadsShippedAdGroupMap()
    {
        var provider = new AttributeMapProvider();

        var map = provider.GetMap("ActiveDirectory", "Group");

        Assert.Equal("ActiveDirectory", map.Connector);
        Assert.Equal("Group", map.EntityKind);
        Assert.Equal("displayName", map.ColumnMappings["display_name"]);
        Assert.Equal("mail", map.ColumnMappings["email"]);
        Assert.Contains("edge_refs", map.HashFields);
    }

    [Fact]
    public void GetMap_LoadsShippedCyberArkAccountMap()
    {
        var provider = new AttributeMapProvider();

        var map = provider.GetMap("CyberArk", "Account");

        Assert.Equal("CyberArk", map.Connector);
        Assert.Equal("Name", map.ColumnMappings["display_name"]);
        Assert.Equal("UserName", map.ColumnMappings["sam_account_name"]);
        Assert.Equal("Address", map.ColumnMappings["domain_name"]);
        Assert.Contains("edge_refs", map.HashFields); // privileged accounts declare a STORED_IN edge to their safe
    }

    [Fact]
    public void GetMap_LoadsShippedCyberArkSafeMap()
    {
        var provider = new AttributeMapProvider();

        var map = provider.GetMap("CyberArk", "Asset");

        Assert.Equal("SafeName", map.ColumnMappings["display_name"]);
        Assert.Contains("asset_type", map.HashFields);
        Assert.Contains("full_path", map.HashFields);
    }

    [Fact]
    public void GetMap_LoadsShippedCyberArkMemberAccountMap()
    {
        var provider = new AttributeMapProvider();

        var map = provider.GetMap("CyberArk", "MemberAccount");

        Assert.Equal("MemberName", map.ColumnMappings["display_name"]);
        Assert.Contains("edge_refs", map.HashFields);
    }

    [Fact]
    public void GetMap_LoadsShippedCyberArkMemberGroupMap()
    {
        var provider = new AttributeMapProvider();

        var map = provider.GetMap("CyberArk", "MemberGroup");

        Assert.Equal("MemberName", map.ColumnMappings["display_name"]);
        Assert.Contains("edge_refs", map.HashFields);
    }

    [Fact]
    public void GetMap_LoadsShippedWindowsAccountMap()
    {
        var provider = new AttributeMapProvider();

        var map = provider.GetMap("Windows", "Account");

        Assert.Equal("Name", map.ColumnMappings["display_name"]);
        Assert.Equal("SamAccountName", map.ColumnMappings["sam_account_name"]);
        Assert.Equal("Sid", map.ColumnMappings["native_account_id"]);
        Assert.Equal("DomainFQDN", map.ColumnMappings["domain_name"]);
    }

    [Fact]
    public void GetMap_LoadsShippedWindowsGroupMap()
    {
        var provider = new AttributeMapProvider();

        var map = provider.GetMap("Windows", "Group");

        Assert.Equal("Name", map.ColumnMappings["display_name"]);
        Assert.Contains("edge_refs", map.HashFields);
    }

    [Fact]
    public void GetMap_ThrowsForUnknownConnectorOrEntityKind()
    {
        var provider = new AttributeMapProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetMap("ActiveDirectory", "Asset"));
        Assert.Throws<InvalidOperationException>(() => provider.GetMap("Unknown", "Account"));
    }

    [Fact]
    public void GetMap_ReadsFromCustomConfigDirectory()
    {
        var configDir = Path.Combine(Path.GetTempPath(), "thor-attrmap-test-" + Guid.NewGuid());
        Directory.CreateDirectory(configDir);
        try
        {
            File.WriteAllText(Path.Combine(configDir, "unix-account.json"), """
                {
                  "connector": "Unix",
                  "entityKind": "Account",
                  "columnMappings": { "filer_name": "filerName" },
                  "hashFields": ["filer_name"]
                }
                """);

            var provider = new AttributeMapProvider(configDir);
            var map = provider.GetMap("Unix", "Account");

            Assert.Equal("filerName", map.ColumnMappings["filer_name"]);
            Assert.Equal(["filer_name"], map.HashFields);
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }
}
