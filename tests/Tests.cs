using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Register_CheckerTest;

internal static class Tests
{
    private static int _passed;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "NlaCheckerTests_" + Guid.NewGuid().ToString("N"));
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Test(string name, Func<Task> body) { await body(); Console.WriteLine("PASS " + name); _passed++; }
    private static Task Sync(Action action) { action(); return Task.FromResult(0); }
    private static ScanTarget Target(string input) { ScanTarget target; Assert(ScanTarget.TryParse(input, out target), "Invalid test target: " + input); return target; }
    private static string Output() { return Path.Combine(Root, Guid.NewGuid().ToString("N")); }
    private static byte[] Packet(byte type, uint code, byte flags = 0)
    {
        return new byte[] { 3, 0, 0, 19, 14, 0xD0, 0, 0, 0x12, 0x34, 0, type, flags, 8, 0, (byte)code, (byte)(code >> 8), (byte)(code >> 16), (byte)(code >> 24) };
    }
    private static void Status(byte[] packet, RdpStatus status) { Assert(RdpProbe.ParseResponse(packet).Status == status, "Unexpected classification"); }
    public static int Main(string[] args)
    {
        Directory.CreateDirectory(Root);
        try
        {
            if (args.Length > 0 && args[0] == "--demo") { Demo().GetAwaiter().GetResult(); return 0; }
            Run().GetAwaiter().GetResult();
            Console.WriteLine(_passed + " test groups passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (!(args.Length > 0 && args[0] == "--demo")) Directory.Delete(Root, true); }
    }
    private static async Task Run()
    {
        await Test("IPv4 / IPv6 / port normalization", () => Sync(() => {
            Assert(Target(" 192.0.2.1 ").Endpoint == "192.0.2.1:3389", "Default port");
            Assert(Target("192.0.2.1:65535").Port == 65535, "High port");
            Assert(Target("[2001:db8::1]:3390").Port == 3390, "IPv6 port");
            Assert(Target("2001:0db8::1").Endpoint == "[2001:db8::1]:3389", "IPv6 normalization");
            Assert(Target("[::1]").Port == 3389, "Bracketed IPv6 default port");
        }));
        await Test("Invalid and ambiguous targets rejected", () => Sync(() => {
            foreach (string value in new[] { "", " ", "127.1", "12345", "01.2.3.4", "1.2.3.999", "host.example", "1.2.3.4:0", "1.2.3.4:65536", "1.2.3.4:-1", "1.2.3.4:+80", "1.2.3.4:", "[::1]:", "[::1]:x", "[::1]junk", "[127.0.0.1]" }) {
                ScanTarget target; Assert(!ScanTarget.TryParse(value, out target), "Accepted " + value);
            }
        }));
        await Test("Input streaming / deduplication / invalid counts", () => Sync(() => {
            string file = Path.Combine(Root, "input.txt");
            File.WriteAllLines(file, new[] { "# list", "", "127.0.0.1", "127.0.0.1:3389", "[::1]:3389", "::1", "bad" });
            var list = InputList.Load(file, CancellationToken.None);
            Assert(list.Targets.Length == 2 && list.Duplicates == 2 && list.Invalid == 1 && list.Ignored == 2, "Input totals");
        }));
        await Test("Input cancellation", () => Sync(() => {
            using (var cts = new CancellationTokenSource()) {
                cts.Cancel(); bool cancelled = false;
                try { InputList.Load(Path.Combine(Root, "input.txt"), cts.Token); } catch (OperationCanceledException) { cancelled = true; }
                Assert(cancelled, "Load did not cancel");
            }
        }));
        await Test("NLA selection including HYBRID_EX and nonzero flags", () => Sync(() => {
            Status(Packet(2, 2), RdpStatus.NlaSupported); Status(Packet(2, 8, 0x1B), RdpStatus.NlaSupported);
        }));
        await Test("TLS / standard / legacy mapping", () => Sync(() => {
            Status(Packet(2, 0), RdpStatus.StandardOrTls); Status(Packet(2, 1), RdpStatus.StandardOrTls);
            Status(new byte[] { 3, 0, 0, 11, 6, 0xD0, 0, 0, 0, 0, 0 }, RdpStatus.StandardOrTls);
        }));
        await Test("Only failure 5 implies CredSSP requirement", () => Sync(() => {
            Status(Packet(3, 5), RdpStatus.NlaSupported);
            foreach (uint code in new uint[] { 1, 2, 3, 4, 6, 99 }) Status(Packet(3, code), RdpStatus.NegotiationFailure);
            Status(Packet(2, 16), RdpStatus.NegotiationFailure);
        }));
        await Test("Malformed frames cannot masquerade as RDP", () => Sync(() => {
            Status(null, RdpStatus.NotRdp);
            for (int length = 0; length < 19; length++) Status(Packet(2, 2).Take(length).ToArray(), RdpStatus.NotRdp);
            foreach (int offset in new[] { 0, 1, 3, 4, 5, 6, 7, 10, 11, 13, 14 }) { var p = Packet(2, 2); p[offset] = 0xFF; Status(p, RdpStatus.NotRdp); }
            Status(Packet(3, 5, 1), RdpStatus.NotRdp);
        }));
        await Test("Random malformed parser input does not throw", () => Sync(() => {
            var random = new Random(42);
            for (int i = 0; i < 20000; i++) { var data = new byte[random.Next(300)]; random.NextBytes(data); RdpProbe.ParseResponse(data); }
        }));
        await Test("Fragmented network response", async () => {
            var result = await ProbeServer(async stream => { foreach (byte b in Packet(2, 2, 8)) { await stream.WriteAsync(new[] { b }, 0, 1); await Task.Delay(2); } });
            Assert(result.Status == RdpStatus.NlaSupported, "Fragmentation");
        });
        await Test("Legacy frame completes without waiting for 19 bytes", async () => {
            var watch = Stopwatch.StartNew();
            var result = await ProbeServer(async stream => { var p = new byte[] { 3, 0, 0, 11, 6, 0xD0, 0, 0, 0, 0, 0 }; await stream.WriteAsync(p, 0, p.Length); await Task.Delay(200); });
            Assert(result.Status == RdpStatus.StandardOrTls && watch.ElapsedMilliseconds < 1500, "Legacy response");
        });
        await Test("Truncated and non-RDP network replies", async () => {
            Assert((await ProbeServer(s => s.WriteAsync(new byte[] { 3 }, 0, 1))).Status == RdpStatus.NotRdp, "Truncation");
            Assert((await ProbeServer(s => s.WriteAsync(new byte[] { 72, 84, 84, 80 }, 0, 4))).Status == RdpStatus.NotRdp, "HTTP mistaken for RDP");
        });
        await Test("Whole-attempt timeout", async () => {
            var watch = Stopwatch.StartNew();
            var result = await ProbeServer(async s => { await Task.Delay(350); }, 100);
            Assert(result.Status == RdpStatus.Unavailable && result.Detail == "Timeout" && watch.ElapsedMilliseconds < 2000, "Timeout");
        });
        await Test("Cancellation closes an active socket", async () => {
            using (var cts = new CancellationTokenSource()) {
                cts.CancelAfter(80); bool cancelled = false;
                try { await ProbeServer(async s => { await Task.Delay(300); }, 10000, cts.Token); } catch (OperationCanceledException) { cancelled = true; }
                Assert(cancelled, "Socket cancellation");
            }
        });
        await Test("Connection refusal is unavailable", async () => {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            Assert((await RdpProbe.CheckAsync(Target("127.0.0.1:" + port), 1000, CancellationToken.None)).Status == RdpStatus.Unavailable, "Refused socket");
        });
        await Test("Bounded concurrency / exact-once accounting / corrected output mapping", async () => {
            var targets = Enumerable.Range(1, 1000).Select(i => Target("127.0.0.1:" + i)).ToArray();
            int active = 0, maximum = 0; var seen = new int[1001];
            var session = new ScanSession(targets, 16, 1000, 1, Output(), async (target, timeout, token) => {
                int current = Interlocked.Increment(ref active); int previous;
                do { previous = maximum; if (previous >= current) break; } while (Interlocked.CompareExchange(ref maximum, current, previous) != previous);
                await Task.Delay(1, token); Interlocked.Decrement(ref active); Interlocked.Increment(ref seen[target.Port]);
                return new ProbeResult((RdpStatus)(target.Port % 5), "test");
            });
            await session.RunAsync(CancellationToken.None);
            Assert(maximum <= 16 && maximum > 1 && session.Checked == 1000 && seen.Skip(1).All(n => n == 1), "Concurrency/accounting");
            Assert(session.Count(RdpStatus.NlaSupported) == 200, "Counter");
            Assert(File.ReadAllLines(Path.Combine(session.OutputDirectory, "NLA.txt")).All(s => Target(s).Port % 5 == 0), "NLA files reversed");
            Assert(File.ReadAllLines(Path.Combine(session.OutputDirectory, "No_NLA.txt")).All(s => Target(s).Port % 5 == 1), "No_NLA files reversed");
            Assert(File.ReadAllLines(Path.Combine(session.OutputDirectory, "Results.csv")).Length == 1001, "CSV rows");
            Assert(new FileInfo(Path.Combine(session.OutputDirectory, "Not_Checked.txt")).Length == 0, "Completed targets pending");
        });
        await Test("Retries apply only to unavailable targets", async () => {
            int calls = 0;
            var session = new ScanSession(new[] { Target("127.0.0.1") }, 1, 100, 3, Output(), (t, ms, ct) => Task.FromResult(new ProbeResult(++calls < 3 ? RdpStatus.Unavailable : RdpStatus.NlaSupported, "test")));
            await session.RunAsync(CancellationToken.None); Assert(calls == 3 && session.Checked == 1, "Retry count");
            calls = 0;
            session = new ScanSession(new[] { Target("127.0.0.1") }, 1, 100, 3, Output(), (t, ms, ct) => { calls++; return Task.FromResult(new ProbeResult(RdpStatus.NotRdp, "test")); });
            await session.RunAsync(CancellationToken.None); Assert(calls == 1, "Retried confirmed non-RDP");
        });
        await Test("Stop preserves queued and in-flight targets", async () => {
            using (var cts = new CancellationTokenSource()) {
                var ready = new TaskCompletionSource<bool>();
                var session = new ScanSession(Enumerable.Range(1, 100).Select(i => Target("127.0.0.1:" + i)).ToArray(), 8, 1000, 1, Output(), async (t, ms, ct) => { ready.TrySetResult(true); await Task.Delay(10000, ct); return new ProbeResult(RdpStatus.NlaSupported, "test"); });
                var run = session.RunAsync(cts.Token); await ready.Task; cts.Cancel(); await run;
                Assert(session.Checked == 0 && File.ReadAllLines(Path.Combine(session.OutputDirectory, "Not_Checked.txt")).Length == 100, "Lost in-flight targets");
            }
        });
        await Test("Worker failure cancels peers and remains visible", async () => {
            var session = new ScanSession(new[] { Target("127.0.0.1:1"), Target("127.0.0.1:2") }, 2, 1000, 1, Output(), async (t, ms, ct) => { await Task.Delay(10, ct); throw new IOException("injected failure"); });
            bool failed = false; try { await session.RunAsync(CancellationToken.None); } catch (IOException) { failed = true; }
            Assert(failed && session.Checked == 0, "Swallowed worker failure");
            Assert(File.ReadAllLines(Path.Combine(session.OutputDirectory, "Not_Checked.txt")).Length == 2, "Failure pending count");
        });
        await Test("Empty session and single-run guard", async () => {
            var session = new ScanSession(new ScanTarget[0], 10, 100, 1, Output()); await session.RunAsync(CancellationToken.None);
            bool failed = false; try { await session.RunAsync(CancellationToken.None); } catch (InvalidOperationException) { failed = true; }
            Assert(failed && session.Checked == 0, "Repeat run");
        });
        await Test("Output collision is reported without overwriting", async () => {
            string directory = Output(); Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "NLA.txt"), "preserve");
            bool failed = false; try { await new ScanSession(new ScanTarget[0], 1, 100, 1, directory).RunAsync(CancellationToken.None); } catch (IOException) { failed = true; }
            Assert(failed && File.ReadAllText(Path.Combine(directory, "NLA.txt")) == "preserve", "Output overwritten");
        });
        await Test("Dispatch benchmark (synthetic, not network throughput)", () => Sync(() => {
            const int count = 100000; var values = Enumerable.Range(0, count).Select(i => i.ToString()).ToList();
            var watch = Stopwatch.StartNew(); while (values.Count > 0) values.RemoveAt(0); watch.Stop(); double old = watch.Elapsed.TotalMilliseconds;
            int next = -1; watch.Restart(); while (Interlocked.Increment(ref next) < count) { } watch.Stop();
            Console.WriteLine("BENCHMARK 100000 targets: List.RemoveAt(0)=" + old.ToString("F2") + "ms; atomic index=" + watch.Elapsed.TotalMilliseconds.ToString("F2") + "ms");
        }));
    }
    private static async Task<ProbeResult> ProbeServer(Func<NetworkStream, Task> respond, int timeout = 2000, CancellationToken token = default(CancellationToken))
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () => { using (var client = await listener.AcceptTcpClientAsync()) using (var stream = client.GetStream()) { var request = new byte[19]; int n = 0; while (n < request.Length) { int read = await stream.ReadAsync(request, n, request.Length - n); if (read == 0) return; n += read; } Assert(request[15] == 11, "Requested protocols"); await respond(stream); } });
        try { return await RdpProbe.CheckAsync(Target("127.0.0.1:" + port), timeout, token); }
        finally { listener.Stop(); await server; }
    }
    private static async Task Demo()
    {
        // Loopback-only fixture for reproducible UI checks and screenshots.
        var listeners = new List<TcpListener>(); var endpoints = new List<string>();
        for (int i = 0; i < 50; i++) {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); listeners.Add(listener);
            endpoints.Add("127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port);
            int kind = i % 5;
            Task service = Task.Run(async () => {
                while (true) {
                    var client = await listener.AcceptTcpClientAsync();
                    Task reply = Task.Run(async () => {
                        using (client) using (var stream = client.GetStream()) {
                            try {
                                var request = new byte[19]; int n = 0; while (n < 19) { int read = await stream.ReadAsync(request, n, 19 - n); if (read == 0) return; n += read; }
                                await Task.Delay(2500);
                                byte[] response = kind == 0 ? Packet(2, 2) : kind == 1 ? Packet(2, 1) : kind == 2 ? Packet(3, 3) : new byte[] { 72, 84, 84, 80 };
                                if (kind == 4) { await Task.Delay(10000); return; }
                                await stream.WriteAsync(response, 0, response.Length);
                            } catch (IOException) { } catch (ObjectDisposedException) { }
                        }
                    });
                }
            });
        }
        string file = Path.GetFullPath("artifacts/loopback-demo.txt"); File.WriteAllLines(file, endpoints);
        Console.WriteLine("Loopback demo ready: " + file); await Task.Delay(TimeSpan.FromMinutes(30));
        foreach (var listener in listeners) listener.Stop();
    }
}
