using PackageCheck.Core;

namespace PackageCheck.Core.Tests;

public class ToolInfoTests
{
    [Fact]
    public void UserAgent_IsExactlyTheGlobalConstraintString()
    {
        Assert.Equal(
            $"umbraco-package-check/{ToolInfo.Version} (+https://github.com/PascalA94/Umbraco.Community.PackageCheck)",
            ToolInfo.UserAgent);
    }

    [Fact]
    public void UserAgent_ParsesAsAnHttpProductAndComment()
    {
        // HttpClient would reject a malformed value on a strict add; prove the string is a valid header value.
        var headers = new HttpRequestMessage().Headers;
        Assert.True(headers.UserAgent.TryParseAdd(ToolInfo.UserAgent));
        Assert.Equal(2, headers.UserAgent.Count);
    }
}
