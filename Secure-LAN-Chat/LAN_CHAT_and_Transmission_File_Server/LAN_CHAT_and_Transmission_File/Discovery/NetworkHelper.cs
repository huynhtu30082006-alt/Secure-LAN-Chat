using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LanChatServer;

public static class NetworkHelper
{
    public static IPAddress GetServerIpForClient(IPAddress clientIp)
    {
        if (IPAddress.IsLoopback(clientIp))
        {
            return IPAddress.Loopback;
        }

        var candidates = new List<IPAddress>();

        foreach (NetworkInterface network in
                 NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            IPInterfaceProperties properties = network.GetIPProperties();

            foreach (UnicastIPAddressInformation unicast
                     in properties.UnicastAddresses)
            {
                IPAddress localIp = unicast.Address;

                if (localIp.AddressFamily != AddressFamily.InterNetwork ||
                    IPAddress.IsLoopback(localIp))
                {
                    continue;
                }

                candidates.Add(localIp);

                byte[] clientBytes = clientIp.GetAddressBytes();
                byte[] localBytes = localIp.GetAddressBytes();
                byte[] maskBytes = unicast.IPv4Mask.GetAddressBytes();

                bool sameSubnet = true;

                for (int i = 0; i < 4; i++)
                {
                    if ((clientBytes[i] & maskBytes[i]) !=
                        (localBytes[i] & maskBytes[i]))
                    {
                        sameSubnet = false;
                        break;
                    }
                }

                if (sameSubnet)
                {
                    return localIp;
                }
            }
        }

        return candidates.FirstOrDefault() ?? IPAddress.None;
    }
}