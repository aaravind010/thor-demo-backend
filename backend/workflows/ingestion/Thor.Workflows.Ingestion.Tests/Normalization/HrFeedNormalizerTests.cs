using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Thor.Workflows.Ingestion.AttributeMapping;
using Thor.Workflows.Ingestion.Normalization;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Normalization;

/// <summary>Covers <see cref="HrFeedNormalizer"/>'s <c>UserSpecs</c> → identity mapping, based on the connector's sample envelope.</summary>
public class HrFeedNormalizerTests
{
    private static readonly HrFeedNormalizer Normalizer = new(NullLogger<HrFeedNormalizer>.Instance, new AttributeMapProvider());

    private const string JaneSpec = """
        {
          "FirstName": "Jane", "LastName": "Doe", "FullName": "Jane Doe",
          "SupervisorEmployeeID": "E10023", "SupervisorName": "John Smith", "SupervisorID": null,
          "BusinessUnit": " Marketing and Sales", "Department": "Accounts Payable", "City": "Austin", "State": null,
          "Email": "Jane.Doe@Example.com", "EmployeeID": "E20481", "JobDescription": "Clerk",
          "SamAccountName": "EXAMPLE\\jdoe", "UserName": null, "UserEmail": null,
          "ADFieldToMatch": "SamAccountName", "FieldToMatchValue": "jdoe",
          "FeedID": null, "Delete": false
        }
        """;

    private static byte[] Envelope(params string[] userSpecs) =>
        Encoding.UTF8.GetBytes($$"""{"UserSpecs":[{{string.Join(",", userSpecs)}}],"Targets":null,"TaskId":-1}""");

    [Fact]
    public void Normalize_MapsUserSpecFieldsOntoIdentity()
    {
        var sourceId = Guid.NewGuid();

        var batch = Normalizer.Normalize(Envelope(JaneSpec), sourceId);

        var identity = Assert.Single(batch.Identities);
        Assert.Equal(sourceId, identity.SourceId);
        Assert.Equal("E20481", identity.HrEmployeeId);
        Assert.Equal("Jane Doe", identity.DisplayName);
        Assert.Equal("Jane", identity.GivenName);
        Assert.Equal("Doe", identity.Surname);
        Assert.Equal("jane.doe@example.com", identity.Email);
        Assert.Equal("Accounts Payable", identity.Department);
        Assert.Equal("Clerk", identity.Title);
        Assert.Equal("Marketing and Sales", identity.BusinessUnit);
        Assert.Equal(@"EXAMPLE\jdoe", identity.SamAccountName);
        Assert.Equal("E10023", identity.ManagerEmployeeId);
        Assert.Equal("John Smith", identity.ManagerName);
        Assert.Equal("SamAccountName", identity.AdMatchField);
        Assert.Equal("jdoe", identity.AdMatchValue);
        Assert.False(identity.MarkedToRetire);
        Assert.NotEmpty(identity.ContentHash);

        Assert.Empty(batch.Accounts);
        Assert.Empty(batch.Groups);
    }

    [Fact]
    public void Normalize_UnmappedFieldsLandInRawAttributes()
    {
        var identity = Assert.Single(Normalizer.Normalize(Envelope(JaneSpec), Guid.NewGuid()).Identities);

        var raw = JsonSerializer.Serialize(identity.RawAttributes);
        Assert.Contains("\"City\":\"Austin\"", raw);
        Assert.DoesNotContain("\"EmployeeID\"", raw);
        Assert.DoesNotContain("\"Delete\"", raw);
    }

    [Fact]
    public void Normalize_DeleteFlag_SetsMarkedToRetire()
    {
        var retiring = JaneSpec.Replace("\"Delete\": false", "\"Delete\": true");

        var identity = Assert.Single(Normalizer.Normalize(Envelope(retiring), Guid.NewGuid()).Identities);

        Assert.True(identity.MarkedToRetire);
    }

    [Fact]
    public void Normalize_BlankEmployeeId_IsDroppedAndCounted()
    {
        var noId = JaneSpec.Replace("\"EmployeeID\": \"E20481\"", "\"EmployeeID\": \"  \"");

        var batch = Normalizer.Normalize(Envelope(noId), Guid.NewGuid());

        Assert.Empty(batch.Identities);
        Assert.Equal(1, batch.SkippedCount);
    }

    [Fact]
    public void Normalize_RepeatedEmployeeId_LastEntryWins()
    {
        var later = JaneSpec.Replace("\"Department\": \"Accounts Payable\"", "\"Department\": \"Treasury\"");

        var batch = Normalizer.Normalize(Envelope(JaneSpec, later), Guid.NewGuid());

        var identity = Assert.Single(batch.Identities);
        Assert.Equal("Treasury", identity.Department);
        Assert.Equal(1, batch.SkippedCount);
    }

    [Fact]
    public void Normalize_PostDataFile_ProducesEmptyBatch()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"AppUsers":null,"RawFeedData":"a,b\n1,2","TaskId":-1}""");

        var batch = Normalizer.Normalize(bytes, Guid.NewGuid());

        Assert.Empty(batch.Identities);
        Assert.Equal(0, batch.SkippedCount);
    }

    [Fact]
    public void Normalize_ContentHash_StableForSameInputAndChangesWithAField()
    {
        var sourceId = Guid.NewGuid();
        var first = Assert.Single(Normalizer.Normalize(Envelope(JaneSpec), sourceId).Identities);
        var again = Assert.Single(Normalizer.Normalize(Envelope(JaneSpec), sourceId).Identities);
        var moved = Assert.Single(Normalizer.Normalize(Envelope(JaneSpec.Replace("Accounts Payable", "Treasury")), sourceId).Identities);

        Assert.Equal(first.ContentHash, again.ContentHash);
        Assert.NotEqual(first.ContentHash, moved.ContentHash);
    }
}
