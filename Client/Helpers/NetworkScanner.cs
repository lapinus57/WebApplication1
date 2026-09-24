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
    public sealed record NetworkScanProgress(int Tested, int Total);

    public static class NetworkScanner
    {
        private const int ProbeConcurrency = 48;
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(750);

        public static async Task<string?> FindServerAsync(
            int port = 5000,
            CancellationToken cancellationToken = default,
            IProgress<NetworkScanProgress>? progress = null)
        {
            var candidates = GetCandidates().ToArray();
            var tested = 0;
            string? found = null;

            using var handler = new SocketsHttpHandler
            {
                ConnectTimeout = ProbeTimeout,
                MaxConnectionsPerServer = ProbeConcurrency
            };
            using var http = new HttpClient(handler) { Timeout = ProbeTimeout };
            using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                await Parallel.ForEachAsync(
                    candidates,
                    new ParallelOptions
                    {
                        CancellationToken = scanCancellation.Token,
                        MaxDegreeOfParallelism = ProbeConcurrency
                    },
                    async (ip, ct) =>
                    {
                        try
                        {
                            var address = $"http://{ip}:{port}";
                            using var request = new HttpRequestMessage(HttpMethod.Get, $"{address}/api/discovery");
                            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                            if (response.IsSuccessStatusCode &&
                                response.Headers.TryGetValues("X-EyeChat-Server", out var values) &&
                                values.Contains("1", StringComparer.Ordinal))
                            {
                                if (Interlocked.CompareExchange(ref found, address, null) is null)
                                    scanCancellation.Cancel();
                            }
                        }
                        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
                        {
                            // An unreachable address is expected while scanning a local network.
                        }
                        catch (Exception ex)
                        {
                            Logger.LogException("[NetworkScanner] Probe failed", ex, "CLI23");
                        }
                        finally
                        {
                            progress?.Report(new NetworkScanProgress(Interlocked.Increment(ref tested), candidates.Length));
                        }
                    });
            }
            catch (OperationCanceledException) when (found is not null && !cancellationToken.IsCancellationRequested)
            {
                // Finding a server cancels outstanding probes so the result can be returned immediately.
            }

            cancellationToken.ThrowIfCancellationRequested();
            return found;
        }

        private static IEnumerable<string> GetCandidates()
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // These are cheap probes and make discovery immediate when client and server share a PC.
            candidates.Add("127.0.0.1");
            candidates.Add("localhost");

            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    networkInterface.OperationalStatus != OperationalStatus.Up)
                    continue;

                var properties = networkInterface.GetIPProperties();
                foreach (var gateway in properties.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
                    candidates.Add(gateway.Address.ToString());

                foreach (var unicast in properties.UnicastAddresses.Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork))
                {
                    var bytes = unicast.Address.GetAddressBytes();
                    candidates.Add(unicast.Address.ToString());

                    // Limit each adapter to its surrounding /24. This covers typical LANs without
                    // turning a broad corporate subnet into a scan of tens of thousands of hosts.
                    for (var host = 1; host <= 254; host++)
                        candidates.Add($"{bytes[0]}.{bytes[1]}.{bytes[2]}.{host}");
                }
            }

            return candidates;
        }
    }
}
