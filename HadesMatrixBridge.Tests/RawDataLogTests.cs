using HadesMatrixBridge.HadesClient;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;

namespace HadesMatrixBridge.Tests
{
    public class RawDataLogTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"hades-raw-{Guid.NewGuid()}");
        private readonly TestClock _clock = new();

        private RawDataLog CreateLog(int puppetId = 1) =>
            new(_directory, puppetId, NullLogger.Instance, _clock);

        private async Task<RawDataRecord[]> ReadRecords(int puppetId = 1, string date = "2026-09-25")
        {
            var lines = await File.ReadAllLinesAsync(Path.Combine(_directory, $"puppet-{puppetId}-{date}.jsonl"));
            return lines.Select(line => JsonSerializer.Deserialize<RawDataRecord>(line, JsonSerializerOptions.Web)!).ToArray();
        }

        private static string Text(RawDataRecord record) => Encoding.UTF8.GetString(Convert.FromBase64String(record.DataBase64));

        [Fact]
        public async Task Preserves_All_Bytes_And_Appends_Across_Reconnects()
        {
            byte[] bytes = [0xFF, 0xFB, 0x01, 0x1B, 0x5B, 0x31, 0x6D, 0x00, 0x0D, 0x0A, 0x80, 0xE2];
            var log = CreateLog();
            await log.WriteAsync(bytes);
            await log.WriteAsync(new byte[] { 0x82, 0xAC });
            await CreateLog().WriteAsync("name:"u8.ToArray());

            var records = await ReadRecords();
            Assert.Equal(3, records.Length);
            Assert.Equal(bytes, Convert.FromBase64String(records[0].DataBase64));
            Assert.Equal(new byte[] { 0x82, 0xAC }, Convert.FromBase64String(records[1].DataBase64));
            Assert.Equal("name:", Text(records[2]));
            Assert.Equal(records[0].SessionId, records[1].SessionId);
            Assert.NotEqual(records[0].SessionId, records[2].SessionId);
            Assert.All(records, record =>
            {
                Assert.Equal(1, record.Version);
                Assert.Equal(1, record.PuppetId);
                Assert.Equal(_clock.Now, record.TimestampUtc);
            });
            Assert.Single(Directory.GetFiles(_directory));
        }

        [Fact]
        public async Task Separates_Puppets_And_Rolls_At_Utc_Midnight()
        {
            var first = CreateLog();
            var second = CreateLog(2);
            _clock.Now = new DateTimeOffset(2026, 9, 25, 23, 59, 59, TimeSpan.Zero);
            await first.WriteAsync("first day"u8.ToArray());
            await second.WriteAsync("other puppet"u8.ToArray());
            _clock.Now = _clock.Now.AddSeconds(1);
            await first.WriteAsync("second day"u8.ToArray());

            var firstDay = Assert.Single(await ReadRecords());
            var otherPuppet = Assert.Single(await ReadRecords(2));
            var secondDay = Assert.Single(await ReadRecords(date: "2026-09-26"));
            Assert.Equal("first day", Text(firstDay));
            Assert.Equal("other puppet", Text(otherPuppet));
            Assert.Equal(2, otherPuppet.PuppetId);
            Assert.Equal("second day", Text(secondDay));
            Assert.Equal(firstDay.SessionId, secondDay.SessionId);
            Assert.Equal(3, Directory.GetFiles(_directory).Length);
        }

        [Fact]
        public async Task Timing_Is_Monotonic_Even_When_The_Wall_Clock_Changes()
        {
            var log = CreateLog();
            _clock.Timestamp = 125;
            await log.WriteAsync("partial"u8.ToArray());
            _clock.Now = _clock.Now.AddMinutes(-5);
            _clock.Timestamp = 425;
            await log.WriteAsync(" line\n"u8.ToArray());

            var records = await ReadRecords();
            Assert.Equal(125, records[0].ElapsedMilliseconds);
            Assert.Equal(425, records[1].ElapsedMilliseconds);
            Assert.True(records[1].TimestampUtc < records[0].TimestampUtc);

            var reconnected = CreateLog();
            _clock.Timestamp = 450;
            await reconnected.WriteAsync("new session"u8.ToArray());
            Assert.Equal(25, (await ReadRecords())[2].ElapsedMilliseconds);
        }

        [Fact]
        public async Task Creates_Nothing_Until_Data_Is_Received()
        {
            await CreateLog().WriteAsync(ReadOnlyMemory<byte>.Empty);
            Assert.False(Directory.Exists(_directory));
        }

        [Fact]
        public async Task File_Errors_Do_Not_Interrupt_Processing_And_Retry_On_New_Connection()
        {
            // A file in place of the directory forces a deterministic I/O failure.
            await File.WriteAllTextAsync(_directory, "blocked");
            var log = CreateLog();
            await log.WriteAsync("first"u8.ToArray());
            File.Delete(_directory);
            await log.WriteAsync("still disabled"u8.ToArray());
            Assert.False(Directory.Exists(_directory));

            await CreateLog().WriteAsync("reconnected"u8.ToArray());
            Assert.Equal("reconnected", Text(Assert.Single(await ReadRecords())));
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
            else if (File.Exists(_directory))
                File.Delete(_directory);
        }

        private sealed class TestClock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
            public long Timestamp { get; set; }
            public override long TimestampFrequency => 1000;
            public override long GetTimestamp() => Timestamp;
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
