using System.IO.Compression;
using System.Text;
using Avalon.Crashes.Validation;
using Xunit;

namespace Avalon.Crashes.UnitTests.Validation;

public sealed class CrashArchiveReaderShould : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "avalon-crash-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Should_replace_nul_and_reapply_the_utf8_byte_limit()
    {
        CrashInput input = await Read(CrashFixture.Archive(log: new byte[CrashLimits.LogBytes]));
        Assert.Equal(new string('\ufffd', CrashLimits.LogBytes / 3), input.LogTail);
        Assert.True(Encoding.UTF8.GetByteCount(input.LogTail!) <= CrashLimits.LogBytes);
        Assert.True(input.Quality.HasFlag(CrashQualityFlags.Truncated));
        Assert.True(input.Quality.HasFlag(CrashQualityFlags.SanitizedText));
    }

    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.SmallestSize)]
    public async Task Should_accept_schema_one_and_normalize_unprefixed_addresses(CompressionLevel compression)
    {
        CrashInput input = await Read(CrashFixture.Archive(compression: compression));
        Assert.Equal("0x000000001234abcd", input.Metadata.ExceptionAddress);
        Assert.Equal(CrashFixture.PdbKey, input.Runtime.PdbKey);
        Assert.Equal("runtime.exe", Assert.Single(input.Modules).Name);
        Assert.Equal(CrashFixture.Dump(), await File.ReadAllBytesAsync(input.DumpPath));
        Assert.Null(input.LogTail);
    }

    [Theory]
    [InlineData("extra", CrashFailureCode.UnsafeArchive)]
    [InlineData("duplicate", CrashFailureCode.UnsafeArchive)]
    [InlineData("traversal", CrashFailureCode.UnsafeArchive)]
    [InlineData("metadata", CrashFailureCode.MetadataTooLarge)]
    [InlineData("log", CrashFailureCode.LogTooLarge)]
    [InlineData("expanded", CrashFailureCode.ArchiveTooLarge)]
    [InlineData("download", CrashFailureCode.ArchiveTooLarge)]
    [InlineData("payloadCrc", CrashFailureCode.UnsafeArchive)]
    [InlineData("neededVersion", CrashFailureCode.UnsafeArchive)]
    [InlineData("zipTime", CrashFailureCode.UnsafeArchive)]
    [InlineData("unfinishedDeflate", CrashFailureCode.UnsafeArchive)]
    [InlineData("badDeflate", CrashFailureCode.UnsafeArchive)]
    [InlineData("reparse", CrashFailureCode.UnsafeArchive)]
    [InlineData("wrongType", CrashFailureCode.InvalidMetadata)]
    [InlineData("addressOverflow", CrashFailureCode.InvalidMetadata)]
    [InlineData("method", CrashFailureCode.UnsafeArchive)]
    [InlineData("length", CrashFailureCode.UnsafeArchive)]
    [InlineData("crc", CrashFailureCode.UnsafeArchive)]
    [InlineData("local", CrashFailureCode.UnsafeArchive)]
    [InlineData("encryption", CrashFailureCode.UnsafeArchive)]
    [InlineData("link", CrashFailureCode.UnsafeArchive)]
    [InlineData("directory", CrashFailureCode.UnsafeArchive)]
    [InlineData("schema", CrashFailureCode.UnsupportedSchema)]
    [InlineData("jsonDuplicate", CrashFailureCode.InvalidMetadata)]
    [InlineData("uintOverflow", CrashFailureCode.InvalidMetadata)]
    [InlineData("negative", CrashFailureCode.InvalidMetadata)]
    [InlineData("fraction", CrashFailureCode.InvalidMetadata)]
    [InlineData("version", CrashFailureCode.InvalidMetadata)]
    [InlineData("time", CrashFailureCode.InvalidMetadata)]
    public async Task Should_reject_unsafe_or_oversized_archives(string kind, CrashFailureCode expected)
    {
        byte[] archive = kind switch
        {
            "unfinishedDeflate" => CrashFixture.Archive(compression: CompressionLevel.SmallestSize),
            "badDeflate" => CrashFixture.Archive(compression: CompressionLevel.SmallestSize),
            "wrongType" => CrashFixture.Archive(metadata: CrashFixture.Metadata.Replace("3221225477", "\"3221225477\"", StringComparison.Ordinal)),
            "addressOverflow" => CrashFixture.Archive(metadata: CrashFixture.Metadata.Replace("000000001234ABCD", "10000000000000000", StringComparison.Ordinal)),
            "extra" => CrashFixture.Archive(extraName: "other"),
            "duplicate" => CrashFixture.Archive(extraName: "dump.dmp"),
            "traversal" => CrashFixture.Archive(extraName: "../dump.dmp"),
            "directory" => CrashFixture.Archive(extraName: "dir/"),
            "metadata" => CrashFixture.Archive(metadata: CrashFixture.Metadata.PadRight(4097)),
            "log" => CrashFixture.Archive(log: new byte[65537]),
            "expanded" => CrashFixture.Archive(dump: CrashFixture.Dump(4 * 1024 * 1024 - Encoding.UTF8.GetByteCount(CrashFixture.Metadata) + 1), compression: CompressionLevel.SmallestSize),
            "download" => new byte[4 * 1024 * 1024 + 1],
            "schema" => CrashFixture.Archive(metadata: CrashFixture.Metadata.Replace("\"schema\":1", "\"schema\":3", StringComparison.Ordinal)),
            "jsonDuplicate" => CrashFixture.Archive(metadata: CrashFixture.Metadata.Replace("\"schema\":1", "\"schema\":1,\"schema\":1", StringComparison.Ordinal)),
            "uintOverflow" => CrashFixture.Archive(metadata: CrashFixture.Metadata.Replace("3221225477", "4294967296", StringComparison.Ordinal)),
            "negative" => CrashFixture.Archive(metadata: CrashFixture.Metadata.Replace("3221225477", "-1", StringComparison.Ordinal)),
            "fraction" => CrashFixture.Archive(metadata: CrashFixture.Metadata.Replace("3221225477", "1.2", StringComparison.Ordinal)),
            "version" => CrashFixture.Archive(metadata: CrashFixture.Metadata.Replace(CrashFixture.Version, "bad/version", StringComparison.Ordinal)),
            "time" => CrashFixture.Archive(metadata: CrashFixture.Metadata.Replace("12:13:14Z", "12:13:14+00:00", StringComparison.Ordinal)),
            _ => CrashFixture.Archive()
        };
        int central = Find(archive, [0x50, 0x4b, 0x01, 0x02]);
        if (kind == "neededVersion")
            archive[central + 6] = 45;
        if (kind == "zipTime")
            archive[10] ^= 1;
        if (kind == "unfinishedDeflate")
            archive[38] &= 0xfe;
        if (kind == "badDeflate")
            archive[38] = 0x07;
        if (kind == "reparse")
            CrashFixture.Put32(archive, central + 38, 0x400);
        if (kind == "crc")
            archive[central + 16] ^= 1;
        if (kind == "payloadCrc")
        {
            archive[central + 16] ^= 1;
            archive[14] ^= 1;
        }
        if (kind == "method")
            archive[central + 10] = 99;
        if (kind == "length")
        {
            CrashFixture.Put32(archive, central + 24, 511);
            CrashFixture.Put32(archive, 22, 511);
        }
        if (kind == "local")
            archive[30] ^= 1;
        if (kind == "encryption")
            archive[central + 8] |= 1;
        if (kind == "link")
            CrashFixture.Put32(archive, central + 38, 0xa1ff0000);
        CrashInputException error = await Assert.ThrowsAsync<CrashInputException>(() => Read(archive));
        Assert.Equal(expected, error.Code);
        Assert.False(File.Exists(Path.Combine(_directory, "dump.dmp")));
    }

    /// <summary>
    /// Client#459: schema 2 adds the GPU and the game's last state, each optional and bounded. A malformed one is
    /// dropped and the report still read: a bad optional field is never a reason to lose a crash.
    /// </summary>
    [Theory]
    [InlineData(",\"gpuVendorId\":4318,\"gpuDeviceId\":9860,\"gpuName\":\"NVIDIA GeForce RTX 4090\",\"driverVersion\":\"581.57\",\"lastState\":\"in-world\"",
        4318u, 9860u, "NVIDIA GeForce RTX 4090", "581.57", "in-world")]
    [InlineData("", null, null, null, null, null)]
    [InlineData(",\"gpuVendorId\":-1,\"gpuDeviceId\":\"9860\",\"gpuName\":\"bad\\u0001name\",\"driverVersion\":\"581/57\",\"lastState\":\"combat\"",
        null, null, null, null, null)]
    [InlineData(",\"gpuName\":\"Intel(R) Arc(TM) A770 Graphics\",\"lastState\":\"loading\"",
        null, null, "Intel(R) Arc(TM) A770 Graphics", null, "loading")]
    public async Task Should_read_schema_two_and_drop_malformed_optional_fields(
        string extra, uint? vendor, uint? device, string? name, string? driver, string? state)
    {
        string metadata = CrashFixture.Metadata.Replace("\"schema\":1", "\"schema\":2", StringComparison.Ordinal)
            .Replace("}", extra + "}", StringComparison.Ordinal);
        CrashMetadata read = (await Read(CrashFixture.Archive(metadata: metadata))).Metadata;
        Assert.Equal((2, vendor, device, name, driver, state),
            (read.Schema, read.GpuVendorId, read.GpuDeviceId, read.GpuName, read.VulkanDriverVersion, read.LastState));
    }

    [Fact]
    public async Task Should_preserve_a_log_cut_mid_utf8()
    {
        byte[] log = Enumerable.Repeat((byte)'a', 65536).ToArray();
        log[0] = 0x82;
        log[^1] = 0xe2;
        CrashInput input = await Read(CrashFixture.Archive(log: log));
        Assert.StartsWith("\ufffd", input.LogTail);
        Assert.True(Encoding.UTF8.GetByteCount(input.LogTail!) <= 64 * 1024);
        Assert.True(input.Quality.HasFlag(CrashQualityFlags.Truncated));
    }

    [Fact]
    public async Task Should_accept_exact_expanded_and_entry_boundaries()
    {
        string metadata = CrashFixture.Metadata.PadRight(4096);
        CrashInput input = await Read(CrashFixture.Archive(dump: CrashFixture.Dump(4 * 1024 * 1024 - 4096 - 65536), metadata: metadata, log: Enumerable.Repeat((byte)'a', 65536).ToArray(), compression: CompressionLevel.SmallestSize));
        Assert.Equal(65536, Encoding.UTF8.GetByteCount(input.LogTail!));
    }

    [Fact]
    public async Task Should_accept_exact_download_bound_and_reject_one_extra_streamed_byte()
    {
        byte[] initial = CrashFixture.Archive();
        byte[] archive = CrashFixture.Archive(dump: CrashFixture.Dump(CrashLimits.ArchiveBytes - initial.Length + 512));
        Assert.Equal(CrashLimits.ArchiveBytes, archive.Length);
        CrashInput input = await new CrashArchiveReader().ReadAsync(new NonSeekableStream(archive), CrashFixture.Version, _directory, CancellationToken.None);
        Assert.True(File.Exists(input.DumpPath));
        byte[] oversized = [.. archive, 0];
        CrashInputException error = await Assert.ThrowsAsync<CrashInputException>(() => new CrashArchiveReader().ReadAsync(new NonSeekableStream(oversized), CrashFixture.Version, _directory, CancellationToken.None));
        Assert.Equal(CrashFailureCode.ArchiveTooLarge, error.Code);
    }

    [Fact]
    public async Task Should_discard_unknown_metadata_and_accept_data_descriptors()
    {
        using var output = new MemoryStream();
        using (var stream = new NonSeekableStream(output))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream dump = archive.CreateEntry("dump.dmp").Open())
                dump.Write(CrashFixture.Dump());
            using (Stream meta = archive.CreateEntry("meta.json").Open())
                meta.Write(Encoding.UTF8.GetBytes(CrashFixture.Metadata.Replace("{", "{\"unknown\":{\"secret\":\"discard\"},", StringComparison.Ordinal)));
        }
        CrashInput input = await Read(output.ToArray());
        Assert.DoesNotContain("discard", System.Text.Json.JsonSerializer.Serialize(input.Metadata));
    }

    [Fact]
    public async Task Should_honor_cancellation_before_extracting()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CrashArchiveReader().ReadAsync(new MemoryStream(CrashFixture.Archive()), CrashFixture.Version, _directory, cancellation.Token));
        Assert.False(Directory.Exists(_directory));
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly Stream _inner;
        internal NonSeekableStream(byte[] bytes) => _inner = new MemoryStream(bytes);
        internal NonSeekableStream(Stream inner) => _inner = inner;
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private Task<CrashInput> Read(byte[] zip) => new CrashArchiveReader().ReadAsync(new MemoryStream(zip), CrashFixture.Version, _directory, CancellationToken.None);

    private static int Find(byte[] bytes, byte[] needle) => bytes.AsSpan().IndexOf(needle);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
