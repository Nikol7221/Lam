using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Tracks.Engine
{
    internal sealed class TranscodingJob : IDisposable
    {
        private const int MaxLogLines = 200;

        private readonly LinkedList<string> _log = new();
        private readonly object _logSync = new();
        private readonly CancellationTokenSource _cts = new();

        public TranscodingJob(string id, string streamId, string outputDirectory, Process process, TranscodingStartContext context)
        {
            Id = id;
            StreamId = streamId;
            OutputDirectory = outputDirectory;
            Process = process;
            Context = context;
            StartedUtc = DateTime.UtcNow;
            LastAccessUtc = StartedUtc;
        }

        public string Id { get; }

        public string StreamId { get; }

        public string OutputDirectory { get; }

        public Process Process { get; }

        public TranscodingStartContext Context { get; }

        public DateTime StartedUtc { get; }

        public DateTime LastAccessUtc { get; private set; }

        public int? ExitCode { get; private set; }

        public bool HasExited => Process.HasExited;

        public CancellationToken CancellationToken => _cts.Token;

        public void UpdateLastAccess()
            => LastAccessUtc = DateTime.UtcNow;

        public void AppendLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            lock (_logSync)
            {
                foreach (var part in line.Split('\n'))
                {
                    var trimmed = part.TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    if (trimmed.Length > 2000)
                        trimmed = trimmed[..2000];

                    _log.AddLast(trimmed);
                    if (_log.Count > MaxLogLines)
                        _log.RemoveFirst();
                }
            }
        }

        public string[] SnapshotLog()
        {
            lock (_logSync)
                return _log.ToArray();
        }

        public void SignalExit()
        {
            if (Process.HasExited)
                ExitCode = Process.ExitCode;
        }

        public void StopBackground()
            => _cts.Cancel();

        public void Dispose()
        {
            try
            {
                StopBackground();
            }
            catch { }

            try
            {
                Process.Dispose();
            }
            catch { }
        }
    }
}
