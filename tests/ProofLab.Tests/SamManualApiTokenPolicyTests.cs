using ProofLab.Api;

namespace ProofLab.Tests;

public sealed class SamManualApiTokenPolicyTests
{
    [Theory]
    [InlineData("LLM_1234567890123456_AABBCCDD0123456789")]
    [InlineData("LLM|1234567890123456|AABBCCDD0123456789")]
    public void AcceptsOnlyPossibleMetaBearerKeys(string token)
    {
        Assert.True(SamManualApiTokenPolicy.Accepts(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short-key")]
    [InlineData("LLM_fake")]
    [InlineData("LLM_1234567890123456_XXXXXXXXXX\r\nAuthorization: Bearer EVIL")]
    [InlineData("LLM_1234567890123456_ABC DEF GHI")]
    public void RejectsMalformedCredentialsWithoutEcho(string? token)
    {
        Assert.False(SamManualApiTokenPolicy.Accepts(token));
    }
}
