using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OYM.Net
{
    /// Room codes for the title's friend panel (8 characters, shown XXXX-XXXX, the GameSettings code alphabet: no
    /// 0 / O / 1 / I): the host's LAN IPv4 address (32 bits) + an 8-bit check, 5 bits per character. The same idea as
    /// the team build's RoomCode (address in the code, no lobby server), but case-insensitive and readable aloud.
    public static class CoopCode
    {
        // team repo port: the team's GameSettings dropped these (its title uses 6-character room codes); the showcase
        // co-op keeps its 8-character LAN codes here
        const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        const int CodeLength = 8;

        static int Check(byte[] b) => (b[0] * 7 + b[1] * 13 + b[2] * 29 + b[3] * 31 + 0x5A) & 0xFF;

        public static string Encode(string ipv4)
        {
            if (!IPAddress.TryParse(ipv4, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return null;
            var b = ip.GetAddressBytes();
            ulong v = ((ulong)b[0] << 32) | ((ulong)b[1] << 24) | ((ulong)b[2] << 16) | ((ulong)b[3] << 8) | (ulong)(byte)Check(b);
            var chars = new char[CodeLength];
            for (int i = CodeLength - 1; i >= 0; i--) { chars[i] = CodeAlphabet[(int)(v & 31)]; v >>= 5; }
            return new string(chars);
        }

        /// 8 code characters (no dash) -> the host's address; false for a mistyped code (the check byte catches most).
        public static bool TryDecode(string code, out string address)
        {
            address = null;
            if (code == null) return false;
            code = code.Replace("-", "").Trim().ToUpperInvariant();
            if (code.Length != CodeLength) return false;
            ulong v = 0;
            foreach (char ch in code)
            {
                int k = CodeAlphabet.IndexOf(ch);
                if (k < 0) return false;
                v = (v << 5) | (uint)k;
            }
            var b = new[] { (byte)(v >> 32), (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8) };
            if ((int)(v & 0xFF) != Check(b) || b[0] == 0 || b[0] >= 224) return false;
            address = new IPAddress(b).ToString();
            return true;
        }

        /// A launch argument: a code, or a plain IPv4 address (two instances on one PC: -coopJoin 127.0.0.1).
        public static bool TryParseTarget(string s, out string address)
        {
            address = null;
            if (string.IsNullOrEmpty(s)) return false;
            if (IPAddress.TryParse(s, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork) { address = ip.ToString(); return true; }
            return TryDecode(s, out address);
        }
    }

    /// This PC's LAN address (ported from the team build's LocalAddress): a live Wi-Fi or Ethernet adapter with a
    /// gateway first, virtual / tunnel adapters last.
    public static class LocalAddress
    {
        public static string Get()
        {
            var candidates = new List<(int priority, string address)>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                try
                {
                    var properties = nic.GetIPProperties();
                    bool gateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
                    string d = (nic.Description ?? "").ToLowerInvariant();
                    bool virt = d.Contains("virtual") || d.Contains("vmware") || d.Contains("hyper-v") || d.Contains("vpn") || d.Contains("tap") || d.Contains("loopback");
                    int priority = nic.NetworkInterfaceType switch
                    {
                        NetworkInterfaceType.Wireless80211 => 0,
                        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => 2,
                        _ => 6
                    };
                    if (virt) priority += 8;
                    foreach (var item in properties.UnicastAddresses)
                        if (item.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(item.Address))
                            candidates.Add((priority + (gateway ? 0 : 1), item.Address.ToString()));
                }
                catch (NetworkInformationException) { /* the adapter went away while changing networks */ }
            }
            return candidates.OrderBy(c => c.priority).Select(c => c.address).FirstOrDefault();
        }
    }
}
