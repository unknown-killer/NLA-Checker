using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Register_CheckerTest
{
    public enum RdpStatus { NlaSupported, StandardOrTls, NegotiationFailure, NotRdp, Unavailable }

    public sealed class ProbeResult
    {
        public RdpStatus Status { get; private set; }
        public string Detail { get; private set; }
        public ProbeResult(RdpStatus status, string detail) { Status = status; Detail = detail; }
    }

    public static class RdpProbe
    {
        private static readonly byte[] Request = {
            3, 0, 0, 19, 14, 0xE0, 0, 0, 0, 0, 0,
            1, 0, 8, 0, 0x0B, 0, 0, 0 // TLS, HYBRID and HYBRID_EX
        };

        public static async Task<ProbeResult> CheckAsync(ScanTarget target, int timeoutMs, CancellationToken cancellationToken)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (timeoutMs <= 0) throw new ArgumentOutOfRangeException("timeoutMs");
            cancellationToken.ThrowIfCancellationRequested();
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var client = new TcpClient(target.Address.AddressFamily))
            {
                deadline.CancelAfter(timeoutMs);
                // Closing the socket also cancels ConnectAsync on .NET Framework.
                using (deadline.Token.Register(() => client.Close()))
                {
                    try
                    {
                        await client.ConnectAsync(target.Address, target.Port).ConfigureAwait(false);
                        using (NetworkStream stream = client.GetStream())
                        {
                            await stream.WriteAsync(Request, 0, Request.Length, deadline.Token).ConfigureAwait(false);
                            var header = new byte[4];
                            if (!await ReadExactlyAsync(stream, header, 0, 4, deadline.Token).ConfigureAwait(false))
                                return new ProbeResult(RdpStatus.NotRdp, "Truncated TPKT header");
                            int length = (header[2] << 8) | header[3];
                            if (header[0] != 3 || header[1] != 0 || length < 11 || length > 256)
                                return new ProbeResult(RdpStatus.NotRdp, "Invalid TPKT header");
                            var packet = new byte[length];
                            Buffer.BlockCopy(header, 0, packet, 0, 4);
                            if (!await ReadExactlyAsync(stream, packet, 4, length - 4, deadline.Token).ConfigureAwait(false))
                                return new ProbeResult(RdpStatus.NotRdp, "Truncated connection confirm");
                            return ParseResponse(packet);
                        }
                    }
                    catch (Exception ex) when (ex is SocketException || ex is IOException || ex is ObjectDisposedException || ex is OperationCanceledException || ex is InvalidOperationException)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return new ProbeResult(RdpStatus.Unavailable, deadline.IsCancellationRequested ? "Timeout" : ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
        }

        private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] data, int offset, int count, CancellationToken token)
        {
            while (count > 0)
            {
                int read = await stream.ReadAsync(data, offset, count, token).ConfigureAwait(false);
                if (read == 0) return false;
                offset += read;
                count -= read;
            }
            return true;
        }

        public static ProbeResult ParseResponse(byte[] packet)
        {
            if (packet == null || packet.Length < 11 || packet.Length > 256 || packet[0] != 3 || packet[1] != 0 ||
                ((packet[2] << 8) | packet[3]) != packet.Length || packet[4] != packet.Length - 5 ||
                packet[5] != 0xD0 || packet[6] != 0 || packet[7] != 0 || packet[10] != 0)
                return new ProbeResult(RdpStatus.NotRdp, "Invalid X.224 connection confirm");
            if (packet.Length == 11) return new ProbeResult(RdpStatus.StandardOrTls, "Legacy RDP security");
            if (packet.Length != 19 || packet[13] != 8 || packet[14] != 0)
                return new ProbeResult(RdpStatus.NotRdp, "Invalid negotiation structure");
            uint code = (uint)(packet[15] | packet[16] << 8 | packet[17] << 16 | packet[18] << 24);
            if (packet[11] == 2)
            {
                switch (code)
                {
                    case 0: return new ProbeResult(RdpStatus.StandardOrTls, "Standard RDP selected");
                    case 1: return new ProbeResult(RdpStatus.StandardOrTls, "TLS selected");
                    case 2: return new ProbeResult(RdpStatus.NlaSupported, "CredSSP selected");
                    case 8: return new ProbeResult(RdpStatus.NlaSupported, "CredSSP with early authorization selected");
                    default: return new ProbeResult(RdpStatus.NegotiationFailure, "Unexpected selected protocol: " + code);
                }
            }
            if (packet[11] == 3 && packet[12] == 0)
                return code == 5
                    ? new ProbeResult(RdpStatus.NlaSupported, "Server requires CredSSP")
                    : new ProbeResult(RdpStatus.NegotiationFailure, "Negotiation failure code: " + code);
            return new ProbeResult(RdpStatus.NotRdp, "Unknown negotiation message");
        }
    }
}
