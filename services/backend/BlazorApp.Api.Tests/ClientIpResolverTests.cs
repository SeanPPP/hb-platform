using System.Net;
using BlazorApp.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public sealed class ClientIpResolverTests
    {
        [Fact]
        public void Resolve_WhenPrivateProxyProvidesForwardedChain_ReturnsFirstPublicIpv4()
        {
            var context = CreateContext("172.19.0.1");
            context.Request.Headers["X-Forwarded-For"] = "10.0.0.5, ::ffff:203.0.113.8, 2001:db8::1";

            var resolver = new ClientIpResolver(new ConfigurationBuilder().Build());

            var result = resolver.Resolve(context);

            Assert.Equal("203.0.113.8", result);
        }

        [Fact]
        public void Resolve_WhenForwardedHeadersOnlyContainPrivateOrIpv6_ReturnsUnknown()
        {
            var context = CreateContext("172.19.0.1");
            context.Request.Headers["X-Forwarded-For"] = "192.168.1.10, 2001:db8::1";

            var resolver = new ClientIpResolver(new ConfigurationBuilder().Build());

            var result = resolver.Resolve(context);

            Assert.Equal("unknown", result);
        }

        [Fact]
        public void Resolve_WhenConfiguredProxyProvidesMappedIpv4_ReturnsIpv4()
        {
            var context = CreateContext("198.51.100.1");
            context.Request.Headers["X-Real-IP"] = "::ffff:198.51.100.42";
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ClientIp:TrustedProxies:0"] = "198.51.100.1",
                })
                .Build();

            var resolver = new ClientIpResolver(configuration);

            var result = resolver.Resolve(context);

            Assert.Equal("198.51.100.42", result);
        }

        [Fact]
        public void Resolve_WhenDevelopmentClientProvidesPublicIpv4Header_ReturnsClientPublicIpv4()
        {
            var context = CreateContext("172.19.0.1");
            context.Request.Headers["X-Client-Public-IP"] = "198.51.100.77";
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                })
                .Build();

            var resolver = new ClientIpResolver(configuration);

            var result = resolver.Resolve(context);

            Assert.Equal("198.51.100.77", result);
        }

        [Fact]
        public void Resolve_WhenDirectRemoteIsPublicIpv4_ReturnsRemoteIpv4()
        {
            var context = CreateContext("203.0.113.99");
            var resolver = new ClientIpResolver(new ConfigurationBuilder().Build());

            var result = resolver.Resolve(context);

            Assert.Equal("203.0.113.99", result);
        }

        private static DefaultHttpContext CreateContext(string remoteIp)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            return context;
        }
    }
}
