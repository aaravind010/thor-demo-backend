using FluentAssertions;
using Thor.Authorizer.Core.Tenancy;

namespace Thor.Authorizer.Test.Tenancy;

public class CognitoIssuerParserTests
{
    [Theory]
    [InlineData("https://cognito-idp.us-east-1.amazonaws.com/us-east-1_AbC123", "us-east-1", "us-east-1_AbC123")]
    [InlineData("https://cognito-idp.ap-southeast-2.amazonaws.com/ap-southeast-2_Xyz", "ap-southeast-2", "ap-southeast-2_Xyz")]
    [InlineData("https://cognito-idp.us-gov-west-1.amazonaws.com/us-gov-west-1_Pool9", "us-gov-west-1", "us-gov-west-1_Pool9")]
    public void TryParse_ValidCognitoIssuer_ReturnsRegionAndPoolId(string issuer, string region, string poolId)
    {
        CognitoIssuerParser.TryParse(issuer).Should().Be(new CognitoIssuer(region, poolId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("thor-task-api")]
    [InlineData("http://cognito-idp.us-east-1.amazonaws.com/us-east-1_AbC")]
    [InlineData("https://cognito-idp.us-east-1.amazonaws.com.evil.com/us-east-1_AbC")]
    [InlineData("https://evil.com/cognito-idp.us-east-1.amazonaws.com/us-east-1_AbC")]
    [InlineData("https://cognito-idp.us-east-1.amazonaws.com/")]
    [InlineData("https://cognito-idp.us-east-1.amazonaws.com/us-east-1_AbC/")]
    [InlineData("https://cognito-idp.us-east-1.amazonaws.com/us-east-1_AbC/extra")]
    [InlineData("https://cognito-idp.us-east-1.amazonaws.com/us-east-1_")]
    [InlineData("https://cognito-idp.us-east-1.amazonaws.com/eu-west-1_AbC")]
    [InlineData("https://cognito-idp.us-east-1.amazonaws.com/us-east-1_A b")]
    [InlineData("https://COGNITO-IDP.us-east-1.amazonaws.com/us-east-1_AbC")]
    [InlineData("https://cognito-idp.us-east-1.amazonaws.com/us-east-1_AbC\n")]
    public void TryParse_AnythingElse_ReturnsNull(string? issuer)
    {
        CognitoIssuerParser.TryParse(issuer).Should().BeNull();
    }
}
