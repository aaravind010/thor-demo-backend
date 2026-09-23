using System.Text;
using System.Text.Json.Nodes;
using Thor.Workflows.Ingestion.Extraction;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Extraction;

/// <summary>
/// Net-new tests directly encoding §4.3's two rules: multi-document merge and
/// dn=distinguishedName aliasing. There is no POC test file covering extraction.
/// </summary>
public class AdExtractorTests
{
    private static string AccountsPathJson(string accountsJson) =>
        $"{{\"Domain\":\"test.com\",\"Accounts\":[{accountsJson}]}}";

    private static byte[] SingleDocument(string accountJson)
    {
        var accountsPath = AccountsPathJson(accountJson).Replace("\"", "\\\"");
        var doc = $"{{\"Domains\":[{{\"ServerId\":1,\"AccountsPaths\":[\"{accountsPath}\"]}}]}}";
        return Encoding.UTF8.GetBytes(doc);
    }

    private static JsonObject GetFirstAccount(Thor.Workflows.Ingestion.Models.ExtractedAdExport export, int domainIndex = 0)
    {
        var domain = (JsonObject)export.Domains[domainIndex]!;
        var accountsPaths = (JsonArray)domain["AccountsPaths"]!;
        var pathJson = accountsPaths[0]!.GetValue<string>();
        var inner = (JsonObject)JsonNode.Parse(pathJson)!;
        var accounts = (JsonArray)inner["Accounts"]!;
        return (JsonObject)accounts[0]!;
    }

    [Fact]
    public void Extract_MergesMultipleConcatenatedTopLevelDocuments()
    {
        var doc1 = Encoding.UTF8.GetString(SingleDocument("{\"objectGUID\":[\"guid-1\"]}"));
        var doc2 = Encoding.UTF8.GetString(SingleDocument("{\"objectGUID\":[\"guid-2\"]}"));
        var bytes = Encoding.UTF8.GetBytes(doc1 + doc2);

        var export = AdExtractor.Extract(bytes);

        Assert.Equal(2, export.Domains.Count);
        Assert.Equal((short)308, export.ConnectorType);
    }

    [Fact]
    public void Extract_StripsLeadingUtf8Bom()
    {
        var withoutBom = SingleDocument("{\"objectGUID\":[\"guid-1\"]}");
        var withBom = Encoding.UTF8.GetPreamble().Concat(withoutBom).ToArray();

        var export = AdExtractor.Extract(withBom);

        Assert.Single(export.Domains);
    }

    [Fact]
    public void Extract_AliasesDistinguishedNameOntoDn_WhenDnAbsent()
    {
        var bytes = SingleDocument("{\"objectGUID\":[\"guid-1\"],\"distinguishedName\":[\"CN=User1,DC=test\"]}");

        var export = AdExtractor.Extract(bytes);
        var account = GetFirstAccount(export);

        Assert.True(account.ContainsKey("dn"));
        Assert.Equal("CN=User1,DC=test", account["dn"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Extract_DoesNotOverwriteExistingDn()
    {
        var bytes = SingleDocument(
            "{\"objectGUID\":[\"guid-1\"],\"dn\":[\"CN=Existing,DC=test\"],\"distinguishedName\":[\"CN=Other,DC=test\"]}");

        var export = AdExtractor.Extract(bytes);
        var account = GetFirstAccount(export);

        Assert.Equal("CN=Existing,DC=test", account["dn"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Extract_LeavesAccountWithNeitherDnNorDistinguishedNameUnchanged()
    {
        var bytes = SingleDocument("{\"objectGUID\":[\"guid-1\"]}");

        var export = AdExtractor.Extract(bytes);
        var account = GetFirstAccount(export);

        Assert.False(account.ContainsKey("dn"));
    }

    [Fact]
    public void Extract_RecoversTruncatedTopLevelDocument_AlongsideAValidOne()
    {
        var valid = Encoding.UTF8.GetString(SingleDocument("{\"objectGUID\":[\"guid-1\"]}"));
        var truncated = "{\"Domains\":[{\"ServerId\":2,\"AccountsPaths\":[";
        var bytes = Encoding.UTF8.GetBytes(valid + truncated);

        var export = AdExtractor.Extract(bytes);

        Assert.Equal(2, export.Domains.Count);
        Assert.True(export.RepairedCount > 0);
        Assert.Equal(0, export.SkippedCount);
    }

    [Fact]
    public void Extract_DropsUnrecoverableTopLevelDocument_ButKeepsValidOnes()
    {
        var valid1 = Encoding.UTF8.GetString(SingleDocument("{\"objectGUID\":[\"guid-1\"]}"));
        var garbage = "{\"a\":undefined}";
        var valid2 = Encoding.UTF8.GetString(SingleDocument("{\"objectGUID\":[\"guid-2\"]}"));
        var bytes = Encoding.UTF8.GetBytes(valid1 + garbage + valid2);

        var export = AdExtractor.Extract(bytes);

        Assert.Equal(2, export.Domains.Count);
        Assert.Equal(1, export.SkippedCount);
    }

    [Fact]
    public void Extract_TreatsDocumentWithNoDomainsWrapper_AsOneBareDomain()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "{\"ServerId\":1,\"AccountsPaths\":[\"{\\\"Domain\\\":\\\"test.com\\\",\\\"Accounts\\\":[{\\\"objectGUID\\\":[\\\"guid-1\\\"]}]}\"]}");

        var export = AdExtractor.Extract(bytes);

        Assert.Single(export.Domains);
        var account = GetFirstAccount(export);
        Assert.Equal("guid-1", account["objectGUID"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Extract_TreatsSingleDomainObject_NotWrappedInArray_AsOneDomain()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "{\"Domains\":{\"ServerId\":1,\"AccountsPaths\":[\"{\\\"Domain\\\":\\\"test.com\\\",\\\"Accounts\\\":[{\\\"objectGUID\\\":[\\\"guid-1\\\"]}]}\"]}}");

        var export = AdExtractor.Extract(bytes);

        Assert.Single(export.Domains);
    }

    [Fact]
    public void Extract_AcceptsAccountsPathGivenAsNestedObject_InsteadOfDoubleEncodedString()
    {
        var domain = new JsonObject
        {
            ["ServerId"] = 1,
            ["AccountsPaths"] = new JsonArray(new JsonObject
            {
                ["Domain"] = "test.com",
                ["Accounts"] = new JsonArray(new JsonObject { ["objectGUID"] = new JsonArray("guid-1") }),
            }),
        };
        var document = new JsonObject { ["Domains"] = new JsonArray(domain) };
        var bytes = Encoding.UTF8.GetBytes(document.ToJsonString());

        var export = AdExtractor.Extract(bytes);

        Assert.Single(export.Domains);
        var account = GetFirstAccount(export);
        Assert.Equal("guid-1", account["objectGUID"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Extract_DropsUnrecoverableAccountsPathEntry_ButKeepsValidSiblingEntries()
    {
        var domain = new JsonObject
        {
            ["ServerId"] = 1,
            ["AccountsPaths"] = new JsonArray(
                JsonValue.Create("not json at all"),
                JsonValue.Create(AccountsPathJson("{\"objectGUID\":[\"guid-1\"]}"))),
        };
        var document = new JsonObject { ["Domains"] = new JsonArray(domain) };
        var bytes = Encoding.UTF8.GetBytes(document.ToJsonString());

        var export = AdExtractor.Extract(bytes);

        Assert.Equal(1, export.SkippedCount);
        var accountsPaths = (JsonArray)((JsonObject)export.Domains[0]!)["AccountsPaths"]!;
        Assert.Single(accountsPaths);
    }
}
