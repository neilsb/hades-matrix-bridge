using System.Globalization;
using System.Text.Json;

namespace HadesMatrixBridge.HadesClient
{
    /// <summary>
    /// Records incoming reads with lossless bytes and monotonic timing as JSON Lines.
    /// Used by a single connection's read loop; each instance represents a new session.
    /// </summary>
    internal sealed class RawDataLog
    {
        private readonly string _directory;
        private readonly int _puppetId;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly Guid _sessionId = Guid.NewGuid();
        private readonly long _startedAt;
        private bool _failed;

        public RawDataLog(string directory, int puppetId, ILogger logger, TimeProvider? timeProvider = null)
        {
            _directory = directory;
            _puppetId = puppetId;
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _startedAt = _timeProvider.GetTimestamp();
        }

        public async Task WriteAsync(ReadOnlyMemory<byte> data)
        {
            if (_failed || data.IsEmpty)
            {
                return;
            }

            try
            {
                // Sample before doing any file I/O. Wall-clock adjustments must not alter replay gaps.
                var timestampUtc = _timeProvider.GetUtcNow();
                var elapsed = _timeProvider.GetElapsedTime(_startedAt, _timeProvider.GetTimestamp());
                var record = new RawDataRecord(1, _puppetId, _sessionId, timestampUtc,
                    elapsed.TotalMilliseconds, Convert.ToBase64String(data.Span));
                var date = timestampUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, $"puppet-{_puppetId}-{date}.jsonl");

                // Append across reconnects/restarts, and close after each read so captures are
                // immediately available and no file handles survive a disconnect or day change.
                await using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                    bufferSize: 4096, useAsync: true);
                await JsonSerializer.SerializeAsync(file, record, JsonSerializerOptions.Web);
                await file.WriteAsync("\n"u8.ToArray());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // A debug capture failure must not take down the Hades connection or spam logs.
                _failed = true;
                _logger.LogError(ex,
                    "Raw Hades logging failed for puppet {PuppetId} in {Directory}; disabled until reconnect",
                    _puppetId, _directory);
            }
        }
    }

    internal sealed record RawDataRecord(int Version, int PuppetId, Guid SessionId,
        DateTimeOffset TimestampUtc, double ElapsedMilliseconds, string DataBase64);
}
