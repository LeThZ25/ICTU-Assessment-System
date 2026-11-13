using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace backend_api.Services
{
    public static class LcmsNetworkHelper
    {
        public static IPAddress? GetEffectiveClientIp(HttpContext context, IConfiguration config)
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            if (remoteIp == null) return null;

            // Map IPv4-mapped IPv6 (e.g. ::ffff:192.168.10.5) to pure IPv4
            if (remoteIp.IsIPv4MappedToIPv6)
            {
                remoteIp = remoteIp.MapToIPv4();
            }

            var trustedProxies = (config.GetSection("ExamRoom:TrustedProxies").Get<string[]>() ?? Array.Empty<string>())
                .Select(value => value.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (trustedProxies.Count > 0 && context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedValues))
            {
                // Walk backwards from the TCP peer through trusted proxies. The
                // first X-Forwarded-For entry is attacker-controlled if the
                // client supplied its own header before the proxy appended to it.
                var currentHop = remoteIp;
                var forwardedIps = forwardedValues.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
                for (var index = forwardedIps.Length - 1; index >= 0; index--)
                {
                    if (!trustedProxies.Contains(currentHop.ToString()))
                        break;

                    if (!IPAddress.TryParse(forwardedIps[index].Trim(), out var previousHop))
                        return remoteIp;
                    currentHop = previousHop.IsIPv4MappedToIPv6 ? previousHop.MapToIPv4() : previousHop;
                }

                return currentHop;
            }

            return remoteIp;
        }

        public static bool IsIpInSubnet(IPAddress clientIp, string subnet)
        {
            if (string.IsNullOrWhiteSpace(subnet)) return false;
            if (string.Equals(subnet.Trim(), "ANY", StringComparison.OrdinalIgnoreCase)) return true;

            if (clientIp.IsIPv4MappedToIPv6)
                clientIp = clientIp.MapToIPv4();

            if (clientIp.AddressFamily != AddressFamily.InterNetwork)
            {
                // Client is IPv6 and subnet is not ANY -> rejected per Section 6.3
                return false;
            }

            var parts = subnet.Trim().Split('/');
            if (parts.Length != 2) return false;

            if (!IPAddress.TryParse(parts[0], out var networkIp) || networkIp.AddressFamily != AddressFamily.InterNetwork)
                return false;

            if (!int.TryParse(parts[1], out var prefixLength) || prefixLength < 0 || prefixLength > 32)
                return false;

            if (prefixLength == 0) return true;

            var clientBytes = clientIp.GetAddressBytes();
            var networkBytes = networkIp.GetAddressBytes();

            uint clientInt = (uint)((clientBytes[0] << 24) | (clientBytes[1] << 16) | (clientBytes[2] << 8) | clientBytes[3]);
            uint networkInt = (uint)((networkBytes[0] << 24) | (networkBytes[1] << 16) | (networkBytes[2] << 8) | networkBytes[3]);

            uint mask = prefixLength == 32 ? 0xFFFFFFFF : ~(0xFFFFFFFF >> prefixLength);

            return (clientInt & mask) == (networkInt & mask);
        }
    }
}
