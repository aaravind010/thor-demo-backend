using FluentAssertions;
using Thor.Authorizer.Core.Tenancy;

namespace Thor.Authorizer.Test.Tenancy;

public class HostParserTests
{
    [Fact]
    public void ExtractSubdomain_WithFourLabelHost_ReturnsLeftmostLabel()
    {
        HostParser.ExtractSubdomain("acme.api.thor.example.com").Should().Be("acme");
    }

    [Fact]
    public void ExtractSubdomain_WithThreeLabelHost_ReturnsLeftmostLabel()
    {
        HostParser.ExtractSubdomain("acme.thor.example").Should().Be("acme");
    }

    [Fact]
    public void ExtractSubdomain_WithBareApexDomain_ReturnsNull()
    {
        HostParser.ExtractSubdomain("thor.example").Should().BeNull();
    }

    [Fact]
    public void ExtractSubdomain_WithPort_StripsPortBeforeParsing()
    {
        HostParser.ExtractSubdomain("acme.api.thor.example.com:443").Should().Be("acme");
    }

    [Fact]
    public void ExtractSubdomain_IsCaseInsensitive_ReturnsLowercased()
    {
        HostParser.ExtractSubdomain("ACME.API.THOR.example.com").Should().Be("acme");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractSubdomain_WithNullOrEmptyHost_ReturnsNull(string? host)
    {
        HostParser.ExtractSubdomain(host).Should().BeNull();
    }
}
