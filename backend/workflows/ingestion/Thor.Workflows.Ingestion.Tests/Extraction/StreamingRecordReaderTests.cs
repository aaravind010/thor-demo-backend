using System.Text;
using System.Text.Json.Nodes;
using Thor.Workflows.Ingestion.Extraction;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Extraction;

public class StreamingRecordReaderTests
{
    private static (List<JsonObject> Records, RecordTally Tally) Read(string json, params string[] path)
    {
        var tally = new RecordTally();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return (StreamingRecordReader.Read(stream, path, tally).ToList(), tally);
    }

    [Fact]
    public void Read_YieldsEachObjectOfTopLevelArray()
    {
        var (records, tally) = Read("""{"Safes":[{"n":1},{"n":2}],"Other":[{"n":9}]}""", "Safes");

        Assert.Equal([1, 2], records.Select(r => (int)r["n"]!));
        Assert.Equal((0, 0), (tally.Repaired, tally.Skipped));
    }

    [Fact]
    public void Read_FollowsNestedPath()
    {
        var (records, _) = Read("""{"Accounts":{"Count":1,"AccountDetails":[{"id":"a"},{"id":"b"}]},"Safes":[{"id":"x"}]}""", "Accounts", "AccountDetails");

        Assert.Equal(["a", "b"], records.Select(r => (string)r["id"]!));
    }

    [Fact]
    public void Read_IgnoresArraysWithSameNameAtOtherDepths()
    {
        var (records, _) = Read("""{"Meta":{"Safes":[{"id":"wrong"}]},"Safes":[{"id":"right"}]}""", "Safes");

        Assert.Equal(["right"], records.Select(r => (string)r["id"]!));
    }

    [Fact]
    public void Read_StripsLeadingBom()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("""{"Safes":[{"id":1}]}""")).ToArray();
        var tally = new RecordTally();
        using var stream = new MemoryStream(bytes);

        var records = StreamingRecordReader.Read(stream, ["Safes"], tally).ToList();

        Assert.Single(records);
    }

    [Fact]
    public void Read_BracketsAndQuotesInsideStringsDoNotAffectBoundaries()
    {
        var (records, tally) = Read("""{"Safes":[{"name":"a}]\"{[,","b":2},{"name":"c"}]}""", "Safes");

        Assert.Equal(2, records.Count);
        Assert.Equal("a}]\"{[,", (string)records[0]["name"]!);
        Assert.Equal(0, tally.Skipped);
    }

    [Fact]
    public void Read_EmptyArray_YieldsNothingAndIsNotMissing()
    {
        var (records, tally) = Read("""{"Safes":[]}""", "Safes");

        Assert.Empty(records);
        Assert.Equal(0, tally.ArraysMissing);
    }

    [Fact]
    public void Read_MissingArray_IsCountedAsMissing()
    {
        var (records, tally) = Read("""{"Other":[{"n":1}]}""", "Safes");

        Assert.Empty(records);
        Assert.Equal(1, tally.ArraysMissing);
    }

    [Fact]
    public void Read_RootNotAnObject_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Read("""[{"n":1}]""", "Safes"));
    }

    [Fact]
    public void Read_TrailingCommasInsideAndBetweenRecords_AreRepaired()
    {
        var (records, tally) = Read("""{"Safes":[{"n":1,},{"n":2},]}""", "Safes");

        Assert.Equal(2, records.Count);
        Assert.Equal(1, tally.Repaired);
        Assert.Equal(0, tally.Skipped);
    }

    [Fact]
    public void Read_CommentsBetweenRecordsAndSections_AreSkipped()
    {
        var json = "{ // header { [\n \"Other\": 1, /* ] } */ \"Safes\": [ // first\n {\"n\":1}, /* {\"n\":99} */ {\"n\":2} // done\n ] }";

        var (records, tally) = Read(json, "Safes");

        Assert.Equal([1, 2], records.Select(r => (int)r["n"]!));
        Assert.Equal(0, tally.Skipped);
    }

    [Fact]
    public void Read_CommentContainingBracketsInsideRecord_DoesNotBreakBoundaries()
    {
        var (records, _) = Read("{\"Safes\":[{\"n\":1, /* } */ \"m\":2},{\"n\":3}]}", "Safes");

        Assert.Equal(2, records.Count);
        Assert.Equal(2, (int)records[0]["m"]!);
    }

    [Fact]
    public void Read_InvalidTokenInOneRecord_SkipsOnlyThatRecord()
    {
        var (records, tally) = Read("""{"Safes":[{"n":1},{"n":undefined},{"n":3}]}""", "Safes");

        Assert.Equal([1, 3], records.Select(r => (int)r["n"]!));
        Assert.Equal(1, tally.Skipped);
    }

    [Fact]
    public void Read_NonObjectElements_AreSkipped()
    {
        var (records, tally) = Read("""{"Safes":[{"n":1},5,"text",[1],null,{"n":2}]}""", "Safes");

        Assert.Equal(2, records.Count);
        Assert.Equal(4, tally.Skipped);
    }

    [Fact]
    public void Read_TruncatedMidRecord_KeepsEarlierRecordsAndRepairsTheLastOnce()
    {
        var (records, tally) = Read("""{"Safes":[{"n":1},{"n":2,"name":"par""", "Safes");

        Assert.Equal(2, records.Count);
        Assert.Equal(1, (int)records[0]["n"]!);
        Assert.Equal(2, (int)records[1]["n"]!);
        Assert.Equal(1, tally.Repaired);
    }

    [Fact]
    public void Read_TruncatedBetweenRecords_CountsOneRepair()
    {
        var (records, tally) = Read("""{"Safes":[{"n":1},{"n":2},""", "Safes");

        Assert.Equal(2, records.Count);
        Assert.Equal(1, tally.Repaired);
    }

    [Fact]
    public void Read_TruncatedBeforeTheArray_YieldsNothing()
    {
        var (records, tally) = Read("""{"Other":{"a":""", "Safes");

        Assert.Empty(records);
        Assert.Equal(1, tally.ArraysMissing);
    }

    [Fact]
    public void Read_StrayClosingBracketInRecord_SkipsThatRecordButNotTheNext()
    {
        var (records, tally) = Read("""{"Safes":[{"n":1],"x":2},{"n":3}]}""", "Safes");

        Assert.Equal([3], records.Select(r => (int)r["n"]!));
        Assert.Equal(1, tally.Skipped);
    }

    [Fact]
    public void Read_RecordLargerThanBuffers_IsReadWhole()
    {
        var big = new string('x', 300_000);

        var (records, _) = Read($$"""{"Safes":[{"big":"{{big}}"},{"n":2}]}""", "Safes");

        Assert.Equal(2, records.Count);
        Assert.Equal(300_000, ((string)records[0]["big"]!).Length);
    }

    [Fact]
    public void Read_DrainReportsOnlyNewCounts()
    {
        var tally = new RecordTally();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"Safes":[{"n":undefined},{"n":2}]}"""));

        _ = StreamingRecordReader.Read(stream, ["Safes"], tally).ToList();
        var first = tally.Drain();
        var second = tally.Drain();

        Assert.Equal((0, 1), first);
        Assert.Equal((0, 0), second);
    }
}
