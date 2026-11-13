using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Register_CheckerTest
{
    public sealed class ScanTarget
    {
        public IPAddress Address { get; private set; }
        public int Port { get; private set; }
        public string Endpoint { get; private set; }

        private ScanTarget(IPAddress address, int port)
        {
            Address = address;
            Port = port;
            Endpoint = address.AddressFamily == AddressFamily.InterNetworkV6
                ? "[" + address + "]:" + port : address + ":" + port;
        }

        public static bool TryParse(string value, out ScanTarget target)
        {
            target = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string host = value.Trim();
            int port = 3389;
            if (host.StartsWith("[", StringComparison.Ordinal))
            {
                int end = host.IndexOf(']');
                if (end < 0) return false;
                string suffix = host.Substring(end + 1);
                if (suffix.Length > 0 && (suffix[0] != ':' || !TryPort(suffix.Substring(1), out port))) return false;
                host = host.Substring(1, end - 1);
                if (host.IndexOf(':') < 0) return false;
            }
            else if (host.IndexOf(':') >= 0 && host.IndexOf(':') == host.LastIndexOf(':'))
            {
                int separator = host.IndexOf(':');
                if (!TryPort(host.Substring(separator + 1), out port)) return false;
                host = host.Substring(0, separator);
            }
            IPAddress address;
            if (!IPAddress.TryParse(host, out address)) return false;
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                // IPAddress.TryParse also accepts abbreviated, integer and octal IPv4 forms.
                string[] parts = host.Split('.');
                if (parts.Length != 4) return false;
                foreach (string part in parts)
                {
                    byte octet;
                    if (part.Length == 0 || (part.Length > 1 && part[0] == '0') ||
                        !byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out octet)) return false;
                }
            }
            target = new ScanTarget(address, port);
            return true;
        }

        private static bool TryPort(string text, out int port)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port > 0 && port <= 65535;
        }
    }

    public sealed class InputList
    {
        public ScanTarget[] Targets { get; private set; }
        public int Invalid { get; private set; }
        public int Duplicates { get; private set; }
        public int Ignored { get; private set; }

        public static InputList Load(string path, CancellationToken cancellationToken)
        {
            var result = new InputList();
            var targets = new List<ScanTarget>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in File.ReadLines(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string value = line.Trim();
                if (value.Length == 0 || value.StartsWith("#", StringComparison.Ordinal)) { result.Ignored++; continue; }
                ScanTarget target;
                if (!ScanTarget.TryParse(value, out target)) { result.Invalid++; continue; }
                if (!seen.Add(target.Endpoint)) { result.Duplicates++; continue; }
                targets.Add(target);
            }
            result.Targets = targets.ToArray();
            return result;
        }
    }
}
