using System;
using System.Configuration;
using System.Net;

namespace Ow.Utils
{
    public static class ServerSettings
    {
        private const string BindAddressKey = "BindAddress";

        public static string BindAddress => ConfigurationManager.AppSettings[BindAddressKey] ?? "0.0.0.0";

        public static IPAddress ResolveBindAddress()
        {
            var configured = BindAddress?.Trim();
            if (string.IsNullOrWhiteSpace(configured) || configured == "0.0.0.0" || configured == "*")
            {
                return IPAddress.Any;
            }

            if (configured.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return IPAddress.Loopback;
            }

            if (IPAddress.TryParse(configured, out var parsedAddress))
            {
                return parsedAddress;
            }

            Out.WriteLine($"Invalid BindAddress '{configured}', falling back to 0.0.0.0", "ServerSettings");
            return IPAddress.Any;
        }
    }
}
