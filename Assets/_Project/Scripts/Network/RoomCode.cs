using System;
using System.Net;
using System.Net.Sockets;

namespace OnYourMark
{
    public static class RoomCode
    {
        public const int Length = 6;
        public static string Encode(string ipv4) => Convert.ToBase64String(IPAddress.Parse(ipv4).GetAddressBytes()).TrimEnd('=');

        public static bool TryDecode(string input, out string address)
        {
            address = null;
            string code = input?.Trim();
            if (code == null || code.Length != Length) return false;
            try
            {
                var bytes = Convert.FromBase64String(code + "==");
                if (bytes.Length != 4 || bytes[0] >= 224 || Array.TrueForAll(bytes, b => b == 0)) return false;
                var ip = new IPAddress(bytes).ToString();
                if (Encode(ip) != code) return false;
                address = ip;
                return true;
            }
            catch (FormatException) { return false; }
        }

        // The command-line test launcher also accepts an explicit IPv4 address.
        public static string FromLaunchArgument(string input) =>
            IPAddress.TryParse(input, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork ? Encode(ip.ToString()) : input;
    }
}
