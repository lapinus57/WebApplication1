using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
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

            foreach (var baseIp in networks.Distinct())
            {
                var bytes = baseIp.GetAddressBytes();
                var ips = Enumerable.Range(1, 254)
                                    .Select(i => $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{i}")
                                    .ToList();

                string? found = null;

                await Parallel.ForEachAsync(ips, new ParallelOptions { MaxDegreeOfParallelism = 32 }, async (ip, ct) =>
                {
                    if (found != null)
                        return;

                    try
                    {
                        using var ping = new Ping();
                        var reply = await ping.SendPingAsync(ip, 200);
                        if (reply.Status != IPStatus.Success)
                            return;

                        var url = $"http://{ip}:{port}/";
                        using var response = await http.GetAsync(url, ct);
                        if (response.IsSuccessStatusCode)
                            Interlocked.CompareExchange(ref found, $"http://{ip}:{port}", null);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException("[NetworkScanner] Probe failed", ex, "CLI23");
                    }
                });

                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
