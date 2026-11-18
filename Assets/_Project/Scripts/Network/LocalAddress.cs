using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OnYourMark
{
    /// <summary>이 PC의 LAN IPv4 주소. Host가 상대에게 알려 줄 주소를 화면에 보여 줄 때 쓴다.</summary>
    public static class LocalAddress
    {
        static string cached;

        public static string Get()
        {
            if (cached != null) return cached;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        return cached = addr.Address.ToString();
            }
            return cached = "127.0.0.1";
        }
    }
}
