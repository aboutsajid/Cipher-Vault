using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using CipherVault.Core.Services;
using Xunit;

namespace CipherVault.Tests;

public class BreachCheckServiceTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responseFactory(request));
    }

    [Fact]
    public async Task CheckPasswordAsync_ReturnsCountWhenSuffixMatches()
    {
        string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes("CorrectHorseBatteryStaple!"))).ToUpperInvariant();
        string expectedPrefix = hash[..5];
        string expectedSuffix = hash[5..];

        Uri? requestedUri = null;
        var handler = new StubHttpMessageHandler(req =>
        {
            requestedUri = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{expectedSuffix}:123{Environment.NewLine}ABCDEF1234567890:1")
            };
        });

        var service = new BreachCheckService(new HttpClient(handler));
        Assert.Equal(123, await service.CheckPasswordAsync("CorrectHorseBatteryStaple!"));

        Assert.NotNull(requestedUri);
        Assert.EndsWith($"/range/{expectedPrefix}", requestedUri!.AbsolutePath);
    }

    [Fact]
    public async Task CheckPasswordAsync_ReturnsZeroWhenNoMatch()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF:2")
        });

        var service = new BreachCheckService(new HttpClient(handler));
        Assert.Equal(0, await service.CheckPasswordAsync("Another-Password-123"));
    }
}


