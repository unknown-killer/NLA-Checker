using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Register_CheckerTest
{
    public sealed class ScanSession
    {
        private readonly ScanTarget[] _targets;
        private readonly int[] _completed;
        private readonly int[] _counts = new int[5];
        private readonly int _concurrency, _timeoutMs, _attempts;
        private readonly Func<ScanTarget, int, CancellationToken, Task<ProbeResult>> _probe;
        private int _next = -1, _checked, _started;
        public int Total { get { return _targets.Length; } }
        public int Checked { get { return Volatile.Read(ref _checked); } }
        public string OutputDirectory { get; private set; }
        public int Count(RdpStatus status) { return Volatile.Read(ref _counts[(int)status]); }

        public ScanSession(ScanTarget[] targets, int concurrency, int timeoutMs, int attempts, string outputDirectory,
            Func<ScanTarget, int, CancellationToken, Task<ProbeResult>> probe = null)
        {
            if (targets == null) throw new ArgumentNullException("targets");
            if (concurrency < 1 || concurrency > 512) throw new ArgumentOutOfRangeException("concurrency");
            if (timeoutMs < 1) throw new ArgumentOutOfRangeException("timeoutMs");
            if (attempts < 1 || attempts > 10) throw new ArgumentOutOfRangeException("attempts");
            _targets = (ScanTarget[])targets.Clone();
            foreach (var target in _targets) if (target == null) throw new ArgumentException("Null target.", "targets");
            _completed = new int[targets.Length];
            _concurrency = concurrency; _timeoutMs = timeoutMs; _attempts = attempts;
            OutputDirectory = outputDirectory;
            _probe = probe ?? RdpProbe.CheckAsync;
        }

        public async Task RunAsync(CancellationToken token)
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("A session can only run once.");
            Directory.CreateDirectory(OutputDirectory);
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var output = new ResultWriter(OutputDirectory))
            {
                var workers = new Task[Math.Min(_concurrency, Total)];
                for (int i = 0; i < workers.Length; i++) workers[i] = WorkerAsync(output, stop);
                try { await Task.WhenAll(workers).ConfigureAwait(false); }
                finally
                {
                    // All workers have exited; in-flight cancelled targets remain unchecked.
                    SaveUnchecked(Path.Combine(OutputDirectory, "Not_Checked.txt"));
                }
            }
        }

        private async Task WorkerAsync(ResultWriter output, CancellationTokenSource stop)
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    int index = Interlocked.Increment(ref _next);
                    if (index >= Total) return;
                    ProbeResult result = null;
                    int attempt;
                    var watch = Stopwatch.StartNew();
                    for (attempt = 1; attempt <= _attempts; attempt++)
                    {
                        stop.Token.ThrowIfCancellationRequested();
                        result = await _probe(_targets[index], _timeoutMs, stop.Token).ConfigureAwait(false);
                        if (result.Status != RdpStatus.Unavailable || attempt == _attempts) break;
                    }
                    stop.Token.ThrowIfCancellationRequested();
                    output.Write(_targets[index], result, attempt, watch.ElapsedMilliseconds);
                    Volatile.Write(ref _completed[index], 1);
                    Interlocked.Increment(ref _counts[(int)result.Status]);
                    Interlocked.Increment(ref _checked);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch { stop.Cancel(); throw; }
        }

        public void SaveUnchecked(string path)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
                for (int i = 0; i < Total; i++)
                    if (Volatile.Read(ref _completed[i]) == 0) writer.WriteLine(_targets[i].Endpoint);
        }
    }

    internal sealed class ResultWriter : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Dictionary<RdpStatus, StreamWriter> _files = new Dictionary<RdpStatus, StreamWriter>();
        private StreamWriter _csv;
        private readonly Stopwatch _flushWatch = Stopwatch.StartNew();

        public ResultWriter(string directory)
        {
            try
            {
                Add(directory, RdpStatus.NlaSupported, "NLA.txt");
                Add(directory, RdpStatus.StandardOrTls, "No_NLA.txt");
                Add(directory, RdpStatus.NegotiationFailure, "Negotiation_Failed.txt");
                Add(directory, RdpStatus.NotRdp, "Not_RDP.txt");
                Add(directory, RdpStatus.Unavailable, "Error.txt");
                _csv = Open(Path.Combine(directory, "Results.csv"));
                _csv.WriteLine("Endpoint,Status,Attempts,ElapsedMs,Detail");
            }
            catch { Dispose(); throw; }
        }

        private static StreamWriter Open(string path)
        {
            return new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false), 16384);
        }
        private void Add(string directory, RdpStatus status, string file) { _files.Add(status, Open(Path.Combine(directory, file))); }
        private static string Quote(string text) { return "\"" + text.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\""; }

        public void Write(ScanTarget target, ProbeResult result, int attempts, long elapsedMs)
        {
            lock (_gate)
            {
                _files[result.Status].WriteLine(target.Endpoint);
                _csv.WriteLine(Quote(target.Endpoint) + "," + result.Status + "," + attempts.ToString(CultureInfo.InvariantCulture) + "," + elapsedMs.ToString(CultureInfo.InvariantCulture) + "," + Quote(result.Detail));
                if (_flushWatch.ElapsedMilliseconds >= 1000)
                {
                    foreach (var writer in _files.Values) writer.Flush();
                    _csv.Flush();
                    _flushWatch.Restart();
                }
            }
        }

        public void Dispose()
        {
            // Attempt every close even if one flush fails (for example a full disk).
            Exception failure = null;
            foreach (var writer in _files.Values)
                try { writer.Dispose(); } catch (Exception ex) { failure = failure ?? ex; }
            if (_csv != null) try { _csv.Dispose(); } catch (Exception ex) { failure = failure ?? ex; }
            if (failure != null) throw failure;
        }
    }
}
