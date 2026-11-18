using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OnYourMark
{
    public static class LocalAddress
    {
        // Prefer a live Wi-Fi or Ethernet LAN over virtual/tunnel adapters.
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
                    int priority = nic.NetworkInterfaceType switch
                    {
                        NetworkInterfaceType.Wireless80211 => 0,
                        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => 2,
                        _ => 6
                    };
                    foreach (var item in properties.UnicastAddresses)
                        if (item.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(item.Address))
                            candidates.Add((priority + (gateway ? 0 : 1), item.Address.ToString()));
                }
                catch (NetworkInformationException) { /* The interface disappeared while changing networks. */ }
            }
            return candidates.OrderBy(c => c.priority).Select(c => c.address).FirstOrDefault();
        }
    }
}
