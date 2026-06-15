using System.Net;

namespace BlazorApp.Api.Services
{
    public interface IClientIpResolver
    {
        string Resolve(HttpContext context);
    }

    public sealed class ClientIpResolver(IConfiguration configuration) : IClientIpResolver
    {
        private const string UnknownIp = "unknown";
        private const string ClientPublicIpHeaderName = "X-Client-Public-IP";
        private static readonly string[] ProxyForwardedIpHeaderNames =
        [
            "CF-Connecting-IP",
            "True-Client-IP",
            "X-Forwarded-For",
            "X-Real-IP",
        ];

        public string Resolve(HttpContext context)
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            var isTrustedProxy = IsTrustedProxy(remoteIp);
            if (isTrustedProxy)
            {
                // 优先记录用户公网 IPv4；代理/Docker 私网地址只用于判断转发头可信，不落库。
                var forwardedIp = GetProxyForwardedIp(context);
                if (!string.IsNullOrWhiteSpace(forwardedIp))
                {
                    return forwardedIp;
                }
            }

            if (IsClientPublicIpHeaderEnabled())
            {
                var clientPublicIp = GetFirstPublicIpv4FromHeader(context, ClientPublicIpHeaderName);
                if (!string.IsNullOrWhiteSpace(clientPublicIp))
                {
                    return clientPublicIp;
                }
            }

            if (isTrustedProxy)
            {
                // 可信代理没有提供有效用户公网 IPv4 时，不能把代理自身公网 IP 误记成用户 IP。
                return UnknownIp;
            }

            return NormalizePublicIpv4(remoteIp?.ToString()) ?? UnknownIp;
        }

        private string? GetProxyForwardedIp(HttpContext context)
        {
            foreach (var headerName in ProxyForwardedIpHeaderNames)
            {
                var publicIp = GetFirstPublicIpv4FromHeader(context, headerName);
                if (!string.IsNullOrWhiteSpace(publicIp))
                {
                    return publicIp;
                }
            }

            return null;
        }

        private static string? GetFirstPublicIpv4FromHeader(
            HttpContext context,
            string headerName
        )
        {
            foreach (var value in context.Request.Headers[headerName])
            {
                foreach (var part in value.Split(','))
                {
                    var candidate = NormalizeIpAddress(part);
                    if (candidate == null)
                    {
                        continue;
                    }

                    // 同公网互免依赖 IPv4，对 IPv4-mapped IPv6 也统一转成 IPv4 字符串。
                    if (candidate.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        && IsPublicIp(candidate))
                    {
                        return candidate.ToString();
                    }
                }
            }

            return null;
        }

        private bool IsClientPublicIpHeaderEnabled()
        {
            var configured = configuration.GetValue<bool?>("ClientIp:AllowClientPublicIpHeader");
            if (configured.HasValue)
            {
                return configured.Value;
            }

            return string.Equals(
                configuration["ASPNETCORE_ENVIRONMENT"],
                "Development",
                StringComparison.OrdinalIgnoreCase
            );
        }

        private bool IsTrustedProxy(IPAddress? remoteIp)
        {
            if (remoteIp == null)
            {
                return false;
            }

            if (IPAddress.IsLoopback(remoteIp))
            {
                return true;
            }

            var trustedProxies = configuration
                .GetSection("ClientIp:TrustedProxies")
                .Get<string[]>() ?? Array.Empty<string>();

            return trustedProxies
                .Select(NormalizeIp)
                .Where(ip => !string.IsNullOrWhiteSpace(ip))
                .Any(ip => string.Equals(ip, NormalizeIp(remoteIp.ToString()), StringComparison.OrdinalIgnoreCase))
                || IsInTrustedNetwork(remoteIp);
        }

        private bool IsInTrustedNetwork(IPAddress remoteIp)
        {
            var trustedNetworks = configuration
                .GetSection("ClientIp:TrustedNetworks")
                .Get<string[]>() ?? Array.Empty<string>();

            return trustedNetworks.Any(network => IsInCidr(remoteIp, network));
        }

        private static string? NormalizeIp(string? value)
        {
            return NormalizeIpAddress(value)?.ToString();
        }

        private static string? NormalizePublicIpv4(string? value)
        {
            var ipAddress = NormalizeIpAddress(value);
            if (ipAddress?.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return null;
            }

            return IsPublicIp(ipAddress) ? ipAddress.ToString() : null;
        }

        public static bool IsKnownPublicIpv4(string? value)
        {
            return NormalizePublicIpv4(value) != null;
        }

        public static string? NormalizeKnownPublicIpv4(string? value)
        {
            return NormalizePublicIpv4(value);
        }

        private static IPAddress? NormalizeIpAddress(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim().Trim('"');
            if (IPAddress.TryParse(trimmed, out var ipAddress))
            {
                if (ipAddress.IsIPv4MappedToIPv6)
                {
                    ipAddress = ipAddress.MapToIPv4();
                }

                return ipAddress;
            }

            return null;
        }

        private static bool IsPublicIp(IPAddress ipAddress)
        {
            if (ipAddress.IsIPv4MappedToIPv6)
            {
                ipAddress = ipAddress.MapToIPv4();
            }

            if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = ipAddress.GetAddressBytes();
                return bytes[0] switch
                {
                    10 => false,
                    127 => false,
                    169 when bytes[1] == 254 => false,
                    172 when bytes[1] >= 16 && bytes[1] <= 31 => false,
                    192 when bytes[1] == 168 => false,
                    192 when bytes[1] == 0 && bytes[2] == 0 => false,
                    192 when bytes[1] == 0 && bytes[2] == 2 => false,
                    192 when bytes[1] == 88 && bytes[2] == 99 => false,
                    198 when bytes[1] == 18 || bytes[1] == 19 => false,
                    198 when bytes[1] == 51 && bytes[2] == 100 => false,
                    203 when bytes[1] == 0 && bytes[2] == 113 => false,
                    100 when bytes[1] >= 64 && bytes[1] <= 127 => false,
                    0 => false,
                    >= 224 => false,
                    _ => true,
                };
            }

            if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                return !IPAddress.IsLoopback(ipAddress)
                    && !ipAddress.IsIPv6LinkLocal
                    && !ipAddress.IsIPv6SiteLocal
                    && !ipAddress.IsIPv6Multicast
                    && !IsUniqueLocalIpv6(ipAddress);
            }

            return false;
        }

        private static bool IsUniqueLocalIpv6(IPAddress ipAddress)
        {
            var bytes = ipAddress.GetAddressBytes();
            return bytes.Length > 0 && (bytes[0] & 0xfe) == 0xfc;
        }

        private static bool IsInCidr(IPAddress ipAddress, string? cidr)
        {
            if (string.IsNullOrWhiteSpace(cidr))
            {
                return false;
            }

            var parts = cidr.Split('/', StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !IPAddress.TryParse(parts[0], out var network)
                || !int.TryParse(parts[1], out var prefixLength))
            {
                return false;
            }

            if (ipAddress.IsIPv4MappedToIPv6)
            {
                ipAddress = ipAddress.MapToIPv4();
            }

            if (network.IsIPv4MappedToIPv6)
            {
                network = network.MapToIPv4();
            }

            var addressBytes = ipAddress.GetAddressBytes();
            var networkBytes = network.GetAddressBytes();
            if (addressBytes.Length != networkBytes.Length)
            {
                return false;
            }

            var maxPrefixLength = addressBytes.Length * 8;
            if (prefixLength < 0 || prefixLength > maxPrefixLength)
            {
                return false;
            }

            var fullBytes = prefixLength / 8;
            var remainingBits = prefixLength % 8;

            for (var index = 0; index < fullBytes; index += 1)
            {
                if (addressBytes[index] != networkBytes[index])
                {
                    return false;
                }
            }

            if (remainingBits == 0)
            {
                return true;
            }

            var mask = (byte)(0xff << (8 - remainingBits));
            return (addressBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
        }
    }
}
