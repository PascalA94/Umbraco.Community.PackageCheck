using System.Net;
using System.Net.Sockets;
using NuGet.Protocol.Core.Types;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;
using PackageCheck.Sources.NuGet;

namespace PackageCheck.Sources.Tests;

public class SourceErrorClassifierTests
{
    private static HttpRequestException Status(HttpStatusCode code) => new("failed", null, code);

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, UnknownReason.PrivateFeed)]
    [InlineData(HttpStatusCode.Forbidden, UnknownReason.PrivateFeed)]
    [InlineData(HttpStatusCode.NotFound, UnknownReason.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, UnknownReason.SourceError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, UnknownReason.SourceError)]
    [InlineData(HttpStatusCode.TooManyRequests, UnknownReason.SourceError)]
    public void HttpStatus(HttpStatusCode code, UnknownReason expected) =>
        Assert.Equal(expected, SourceErrorClassifier.Classify(Status(code)));

    [Fact]
    public void StatusWrappedByNuGet_IsFoundInTheInnerChain() =>
        Assert.Equal(UnknownReason.PrivateFeed, SourceErrorClassifier.Classify(
            new FatalProtocolException("Unable to load the service index", new HttpRequestException("x", Status(HttpStatusCode.Unauthorized)))));

    [Fact]
    public void ConnectionRefused_IsSourceError() =>
        Assert.Equal(UnknownReason.SourceError, SourceErrorClassifier.Classify(
            new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused))));

    [Fact]
    public void Timeout_IsSourceError() =>
        Assert.Equal(UnknownReason.SourceError, SourceErrorClassifier.Classify(new TimeoutException()));

    [Fact]
    public void AggregateOfDenied_IsPrivateFeed() =>
        Assert.Equal(UnknownReason.PrivateFeed, SourceErrorClassifier.Classify(
            new AggregateException(Status(HttpStatusCode.Forbidden))));

    [Fact]
    public void PackageSourceException_KeepsItsReason() =>
        Assert.Equal(UnknownReason.NotFound, SourceErrorClassifier.Classify(
            new PackageSourceException(UnknownReason.NotFound, "x")));

    [Fact]
    public void AnythingElse_IsSourceError() =>
        Assert.Equal(UnknownReason.SourceError, SourceErrorClassifier.Classify(new InvalidOperationException()));
}
