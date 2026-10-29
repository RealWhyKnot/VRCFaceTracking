using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Mdns.Parsing;
using VRCFaceTracking.Core.Mdns.Types;

namespace VRCFaceTracking.Core.Mdns;

public sealed partial class MulticastDnsService : ObservableObject
{
    private static readonly IPAddress MulticastIp = IPAddress.Parse("224.0.0.251");
    private const int MulticastPost = 5353;
    private static readonly IPEndPoint MdnsEndpointIp4 = new(MulticastIp, MulticastPost);
    private readonly List<IPAddress> _localIpAddresses;
    private readonly ILogger<MulticastDnsService> _logger;

    private static readonly Dictionary<IPAddress, UdpClient> Senders = [];
    private static readonly Dictionary<UdpClient, CancellationToken> Receivers = [];
    private static readonly ConcurrentDictionary<string, AdvertisedService> Services = new(StringComparer.Ordinal);

    public event EventHandler? VrcClientDiscovered;

    [ObservableProperty] private IPEndPoint? _vrchatClientEndpoint;

    private static List<NetworkInterface> GetIpv4NetInterfaces() => [.. NetworkInterface.GetAllNetworkInterfaces()
        .Where(net =>
            net.OperationalStatus == OperationalStatus.Up &&
            net.NetworkInterfaceType != NetworkInterfaceType.Loopback && (!OperatingSystem.IsWindows() || (
            net.SupportsMulticast &&
            net.GetIPProperties().MulticastAddresses.Count != 0)))];

    private static IPAddress? GetIpv4Address(NetworkInterface net) => net.GetIPProperties()
        .UnicastAddresses
        .Select(addr => addr.Address)
        .FirstOrDefault(addr => addr.AddressFamily == AddressFamily.InterNetwork);

    public MulticastDnsService(ILogger<MulticastDnsService> logger)
    {
        _logger = logger;

        _localIpAddresses = [.. GetIpv4NetInterfaces().Select(GetIpv4Address).OfType<IPAddress>()];

        var cts = new CancellationTokenSource();
        try
        {
            var receiver = new UdpClient(AddressFamily.InterNetwork);
            receiver.Client.ExclusiveAddressUse = false;
            receiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, optionValue: true);
            receiver.Client.Bind(new IPEndPoint(IPAddress.Any, MulticastPost));
            Receivers.Add(receiver, cts.Token);

            foreach (IPAddress ipAddress in _localIpAddresses)
            {
                try
                {
                    receiver.JoinMulticastGroup(MulticastIp, ipAddress);

                    var sender = new UdpClient(ipAddress.AddressFamily);
                    sender.Client.ExclusiveAddressUse = false;
                    sender.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, optionValue: true);
                    sender.Client.Bind(new IPEndPoint(ipAddress, MulticastPost));
                    sender.JoinMulticastGroup(MulticastIp, ipAddress);
                    sender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, optionValue: true);

                    Receivers.Add(sender, cts.Token);
                    Senders.Add(ipAddress, sender);
                }
                catch (SocketException e)
                {
                    LogSkippingMDNS(_logger, ipAddress, e.Message);
                }
            }
        }
        catch (SocketException e)
        {
            LogMDNSCouldNotStartOSCQuery(_logger, e);
        }

        foreach (KeyValuePair<UdpClient, CancellationToken> sender in Receivers)
        {
            UdpClient client = sender.Key;
            CancellationToken token = sender.Value;
            _ = Task.Run(() => ListenAsync(client, token), token);
        }
    }

    private async Task ResolveDnsQueriesAsync(DnsPacket packet, IPEndPoint remoteEndpoint, CancellationToken cancellationToken)
    {
        if (packet.Opcode != 0 || VrchatClientEndpoint == null || remoteEndpoint.Port != MulticastPost)
        {
            return;
        }

        foreach (DnsQuestion question in packet.Questions)
        {
            if (question.Labels.Count != 3
                || !string.Equals(question.Labels[2], "local", StringComparison.Ordinal)
                || !Services.TryGetValue($"{question.Labels[0]}.{question.Labels[1]}", out AdvertisedService? service))
            {
                continue;
            }

            byte[] bytes = BuildServiceResponse(question, service).Serialize();
            foreach (KeyValuePair<IPAddress, UdpClient> sender in Senders)
            {
                await sender.Value.SendAsync(bytes, MdnsEndpointIp4, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static DnsPacket BuildServiceResponse(DnsQuestion question, AdvertisedService service)
    {
        var qualifiedServiceName = new List<string>
        {
            service.ServiceName,
            question.Labels[0],
            question.Labels[1],
            question.Labels[2],
        };

        var serviceName = new List<string>
        {
            service.ServiceName,
            question.Labels[0].Trim('_'),
            question.Labels[1].Trim('_'),
        };

        var txt = new TxtRecord { Text = ["txtvers=1"] };
        var srv = new SrvRecord
        {
            Port = (ushort)service.Port,
            Target = serviceName,
        };
        var aRecord = new ARecord { Address = service.Address };
        var ptrRecord = new PtrRecord
        {
            DomainLabels = qualifiedServiceName,
        };

        return new DnsPacket
        {
            IsConflict = true,
            Id = 0,
            Opcode = 0,
            IsResponse = true,
            ResponseCode = 0,
            IsTentative = false,
            IsTruncated = false,
            Questions = [],
            Answers = [new DnsResource(ptrRecord, question.Labels)],
            Authorities = [],
            Additionals =
            [
                new DnsResource(txt, qualifiedServiceName),
                new DnsResource(srv, qualifiedServiceName),
                new DnsResource(aRecord, serviceName),
            ],
        };
    }

    public async Task SendQueryAsync(string labels, CancellationToken cancellationToken)
    {
        var dnsPacket = new DnsPacket();
        var dnsQuestion = new DnsQuestion([.. labels.Split('.')], 255, 1);
        dnsPacket.Questions = [dnsQuestion];
        dnsPacket.IsResponse = false;
        dnsPacket.Opcode = 0;
        dnsPacket.IsTruncated = false;

        byte[] bytes = dnsPacket.Serialize();
        foreach (KeyValuePair<IPAddress, UdpClient> sender in Senders)
        {
            try
            {
                await sender.Value.SendAsync(bytes, MdnsEndpointIp4, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                LogMDNSQuerySendFailed(_logger, sender.Key, e.Message);
            }
        }
    }

    internal void ResolveVrChatClient(DnsPacket packet, IPEndPoint remoteEndpoint)
    {
        if (!packet.IsResponse || packet.Answers.Length <= 0 || packet.Answers[0].Type != 12)
        {
            return;
        }

        if (packet.Answers[0].Data is not PtrRecord ptrRecord
            || ptrRecord.DomainLabels.Count != 4
            || (!ptrRecord.DomainLabels[0].StartsWith("VRChat-Client", StringComparison.Ordinal) && !ptrRecord.DomainLabels[0].StartsWith("ChilloutVR-GameClient", StringComparison.Ordinal)))
        {
            return;
        }

        if (packet.Answers[0].Labels.Count != 3 || !string.Equals(packet.Answers[0].Labels[0], "_oscjson", StringComparison.Ordinal) ||
!string.Equals(packet.Answers[0].Labels[1], "_tcp", StringComparison.Ordinal) || !string.Equals(packet.Answers[0].Labels[2], "local", StringComparison.Ordinal))
        {
            return;
        }

        DnsResource? aRecord = packet.Additionals.FirstOrDefault(r => r.Type == 1);
        DnsResource? srvRecord = packet.Additionals.FirstOrDefault(r => r.Type == 33);
        if (aRecord == null || srvRecord == null)
        {
            return;
        }

        var vrChatClientIp = aRecord.Data as ARecord;
        var vrChatClientPort = srvRecord.Data as SrvRecord;

        if (vrChatClientIp?.Address == null || vrChatClientPort?.Port == null)
        {
            return;
        }

        IPAddress hostAddress = vrChatClientIp.Address;

        if (IPAddress.IsLoopback(hostAddress) && !_localIpAddresses.Contains(remoteEndpoint.Address))
        {
            hostAddress = remoteEndpoint.Address;
        }

        VrchatClientEndpoint = new IPEndPoint(hostAddress, vrChatClientPort.Port);
        VrcClientDiscovered?.Invoke(this, EventArgs.Empty);
    }

    private async Task ListenAsync(UdpClient client, CancellationToken cancellationToken)
    {
        int consecutiveFailures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                consecutiveFailures = 0;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException e)
            {
                if (++consecutiveFailures >= 10)
                {
                    LogMDNSListenerStoppedAfterRepeated(_logger, e.Message);
                    return;
                }
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!_localIpAddresses.Any(i => i.Equals(result.RemoteEndPoint.Address)))
            {
                continue;
            }

            try
            {
                using var reader = new BigReader(result.Buffer);
                var packet = new DnsPacket(reader);

                ResolveVrChatClient(packet, result.RemoteEndPoint);
                await ResolveDnsQueriesAsync(packet, result.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is EndOfStreamException or InvalidDataException or SocketException)
            {
                LogFailedHandleMDNSPacket(_logger, e, result.RemoteEndPoint);
            }
        }
    }

    public void Advertise(string serviceName, AdvertisedService advertisement)
    {
        LogAdvertisingServiceAdvertisement(_logger, serviceName, advertisement);

        Services[serviceName] = advertisement;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping mDNS on {Address}: {Message}")]
    private static partial void LogSkippingMDNS(ILogger logger, IPAddress address, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "mDNS could not start; OSCQuery discovery will be unavailable")]
    private static partial void LogMDNSCouldNotStartOSCQuery(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "mDNS query send failed on {Address}: {Message}")]
    private static partial void LogMDNSQuerySendFailed(ILogger logger, IPAddress address, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "mDNS listener stopped after repeated socket errors: {Message}")]
    private static partial void LogMDNSListenerStoppedAfterRepeated(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to handle mDNS packet from {Endpoint}")]
    private static partial void LogFailedHandleMDNSPacket(ILogger logger, Exception exception, IPEndPoint endpoint);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Advertising service {ServiceName} with advertisement {Advertisement}")]
    private static partial void LogAdvertisingServiceAdvertisement(ILogger logger, string serviceName, AdvertisedService advertisement);
}
