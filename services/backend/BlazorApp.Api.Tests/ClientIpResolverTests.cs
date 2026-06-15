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
        public void Resolve_WhenPrivateRemoteProvidesForwardedChainWithoutTrust_ReturnsUnknown()
        {
            var context = CreateContext("172.19.0.1");
            context.Request.Headers["X-Forwarded-For"] = "10.0.0.5, ::ffff:8.8.8.8, 2001:db8::1";

            var resolver = new ClientIpResolver(new ConfigurationBuilder().Build());

            var result = resolver.Resolve(context);

            Assert.Equal("unknown", result);
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
            var context = CreateContext("8.8.4.4");
            context.Request.Headers["X-Real-IP"] = "::ffff:8.8.8.42";
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ClientIp:TrustedProxies:0"] = "8.8.4.4",
                })
                .Build();

            var resolver = new ClientIpResolver(configuration);

            var result = resolver.Resolve(context);

            Assert.Equal("8.8.8.42", result);
        }

        [Fact]
        public void Resolve_WhenTrustedProxyDoesNotProvideValidUserIp_ReturnsUnknown()
        {
            var context = CreateContext("8.8.4.4");
            context.Request.Headers["X-Forwarded-For"] = "10.0.0.5, 203.0.113.8";
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ClientIp:TrustedProxies:0"] = "8.8.4.4",
                })
                .Build();

            var resolver = new ClientIpResolver(configuration);

            var result = resolver.Resolve(context);

            Assert.Equal("unknown", result);
        }

        [Fact]
        public void Resolve_WhenConfiguredTrustedNetworkProvidesForwardedChain_ReturnsFirstPublicIpv4()
        {
            var context = CreateContext("172.19.0.1");
            context.Request.Headers["X-Forwarded-For"] = "10.0.0.5, ::ffff:8.8.8.9";
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ClientIp:TrustedNetworks:0"] = "172.19.0.0/16",
                })
                .Build();

            var resolver = new ClientIpResolver(configuration);

            var result = resolver.Resolve(context);

            Assert.Equal("8.8.8.9", result);
        }

        [Fact]
        public void Resolve_WhenDevelopmentClientProvidesPublicIpv4Header_ReturnsClientPublicIpv4()
        {
            var context = CreateContext("172.19.0.1");
            context.Request.Headers["X-Client-Public-IP"] = "8.8.8.77";
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                })
                .Build();

            var resolver = new ClientIpResolver(configuration);

            var result = resolver.Resolve(context);

            Assert.Equal("8.8.8.77", result);
        }

        [Fact]
        public void Resolve_WhenProductionClientProvidesPublicIpv4HeaderWithoutOptIn_ReturnsUnknown()
        {
            var context = CreateContext("172.19.0.1");
            context.Request.Headers["X-Client-Public-IP"] = "8.8.8.77";
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Production",
                })
                .Build();

            var resolver = new ClientIpResolver(configuration);

            var result = resolver.Resolve(context);

            Assert.Equal("unknown", result);
        }

        [Fact]
        public void Resolve_WhenProductionClientPublicIpv4HeaderOptedIn_ReturnsClientPublicIpv4()
        {
            var context = CreateContext("172.19.0.1");
            context.Request.Headers["X-Client-Public-IP"] = "8.8.8.77";
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Production",
                    ["ClientIp:AllowClientPublicIpHeader"] = "true",
                })
                .Build();

            var resolver = new ClientIpResolver(configuration);

            var result = resolver.Resolve(context);

            Assert.Equal("8.8.8.77", result);
        }

        [Fact]
        public void Resolve_WhenDirectRemoteIsPublicIpv4_ReturnsRemoteIpv4()
        {
            var context = CreateContext("8.8.8.99");
            var resolver = new ClientIpResolver(new ConfigurationBuilder().Build());

            var result = resolver.Resolve(context);

            Assert.Equal("8.8.8.99", result);
        }

        [Theory]
        [InlineData("192.0.2.10")]
        [InlineData("198.51.100.10")]
        [InlineData("203.0.113.10")]
        [InlineData("198.18.0.10")]
        public void Resolve_WhenDirectRemoteIsReservedIpv4_ReturnsUnknown(string remoteIp)
        {
            var context = CreateContext(remoteIp);
            var resolver = new ClientIpResolver(new ConfigurationBuilder().Build());

            var result = resolver.Resolve(context);

            Assert.Equal("unknown", result);
        }

        private static DefaultHttpContext CreateContext(string remoteIp)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            return context;
        }
    }
}
