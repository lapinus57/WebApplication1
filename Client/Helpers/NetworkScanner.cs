using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Client.Helpers
{
    public static class NetworkScanner
    {
        public static async Task<string?> FindServerAsync(int port = 5000)
        {
            var networks = new List<IPAddress>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var bytes = ua.Address.GetAddressBytes();
                        networks.Add(IPAddress.Parse($"{bytes[0]}.{bytes[1]}.{bytes[2]}.0"));
                    }
                }
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            using var ping = new Ping();
            foreach (var baseIp in networks.Distinct())
            {
                var bytes = baseIp.GetAddressBytes();
                for (int i = 1; i < 255; i++)
                {
                    var ip = $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{i}";
                    try
                    {
                        var reply = await ping.SendPingAsync(ip, 500);
                        if (reply.Status != IPStatus.Success)
                            continue;
                        var url = $"http://{ip}:{port}/";
                        using var response = await http.GetAsync(url);
                        if (response.IsSuccessStatusCode)
                            return $"http://{ip}:{port}";
                    }
                    catch
                    {
                    }
                }
            }
            return null;
        }
    }
}
