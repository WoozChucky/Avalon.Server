namespace Avalon.Crashes.Worker.Processing;

/// <summary>Owns dedicated ephemeral scratch. Clean stale directories before intake, with no active attempts.</summary>
public sealed class CrashWorkspace
{
    private readonly string _root;
    private readonly long _limit;
    private readonly SemaphoreSlim _quotaLock = new(1, 1);
    private long _reserved;

    public CrashWorkspace(CrashToolOptions options)
    {
        _root = Path.GetFullPath(options.ScratchRoot);
        _limit = options.ScratchLimitBytes;
        if (_root == Path.GetPathRoot(_root) || _limit is <= 0 or > 2L * 1024 * 1024 * 1024)
            throw new ArgumentException("Scratch requires a dedicated root and a positive limit at most 2 GiB.", nameof(options));
    }

    public Task<CrashAttemptWorkspace> CreateAsync(CrashReportId reportId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        VerifyRoot();
        Directory.CreateDirectory(_root);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string path = Path.Combine(_root, $"{reportId.Value:N}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return Task.FromResult(new CrashAttemptWorkspace(this, path));
    }

    public Task CleanupStaleAsync(CancellationToken ct)
    {
        VerifyRoot();
        if (Directory.Exists(_root))
        {
            foreach (string path in Directory.EnumerateDirectories(_root))
            {
                ct.ThrowIfCancellationRequested();
                if (IsOwnedName(Path.GetFileName(path)))
                    DeleteAttempt(path);
            }
        }
        return Task.CompletedTask;
    }

    internal void VerifyAttempt(string path)
    {
        VerifyRoot();
        if (!string.Equals(Path.GetDirectoryName(path), _root, PathComparison) || !IsOwnedName(Path.GetFileName(path)))
            throw new IOException("Unverified scratch child.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Scratch attempt is a link.");
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static bool IsOwnedName(string name) => name.Length == 65 && name[32] == '-'
        && Guid.TryParseExact(name.AsSpan(0, 32), "N", out _) && Guid.TryParseExact(name.AsSpan(33), "N", out _);

    private void VerifyRoot()
    {
        for (DirectoryInfo? directory = new(_root); directory is not null; directory = directory.Parent)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Scratch root or ancestor is a link.");
        }
    }

    internal void DeleteAttempt(string path)
    {
        VerifyRoot();
        if (!string.Equals(Path.GetDirectoryName(path), _root, PathComparison) || !IsOwnedName(Path.GetFileName(path)))
            throw new IOException("Unverified scratch child.");
        if (Directory.Exists(path))
            DeleteWithoutFollowingLinks(path);
    }

    private static void DeleteWithoutFollowingLinks(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
        {
            foreach (string child in Directory.EnumerateFileSystemEntries(directory))
            {
                if ((File.GetAttributes(child) & FileAttributes.Directory) != 0)
                    DeleteWithoutFollowingLinks(child);
                else
                    File.Delete(child);
            }
        }
        Directory.Delete(directory);
    }

    private static long Measure(string directory)
    {
        long bytes = 0;
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Scratch contains a link.");
            bytes = checked(bytes + ((attributes & FileAttributes.Directory) != 0 ? Measure(path) : new FileInfo(path).Length));
        }
        return bytes;
    }

    internal async Task<Stream> CreateFileAsync(string directory, string name, long maxBytes, CancellationToken ct)
    {
        string destination = await ReserveAsync(directory, name, maxBytes, ct);
        try
        {
            var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 8192, FileOptions.Asynchronous);
            return new BoundedScratchStream(file, maxBytes, () => Interlocked.Add(ref _reserved, -maxBytes));
        }
        catch
        {
            Interlocked.Add(ref _reserved, -maxBytes);
            throw;
        }
    }

    internal async Task<string> WriteExternalAsync(string directory, string name, long maxBytes,
        Func<string, long, CancellationToken, Task> boundedWriter, CancellationToken ct)
    {
        string destination = await ReserveAsync(directory, name, maxBytes, ct);
        try
        {
            await boundedWriter(destination, maxBytes, ct);
            VerifyAttempt(directory);
            if ((File.GetAttributes(destination) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("External writer did not create a regular scratch file.");
            if (new FileInfo(destination).Length > maxBytes)
                throw new CrashScratchLimitException();
            return destination;
        }
        catch
        {
            File.Delete(destination);
            throw;
        }
        finally
        {
            Interlocked.Add(ref _reserved, -maxBytes);
        }
    }

    private async Task<string> ReserveAsync(string directory, string name, long maxBytes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name is "." or ".." || name.Contains('\\'))
            throw new ArgumentException("Scratch file must be a basename.", nameof(name));
        if (maxBytes <= 0 || maxBytes > _limit)
            throw new CrashScratchLimitException();
        await _quotaLock.WaitAsync(ct);
        try
        {
            VerifyAttempt(directory);
            string destination = Path.Combine(directory, name);
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new IOException("Scratch destination already exists.");
            if (maxBytes > _limit - Measure(_root) - Interlocked.Read(ref _reserved))
                throw new CrashScratchLimitException();
            Interlocked.Add(ref _reserved, maxBytes);
            return destination;
        }
        finally
        {
            _quotaLock.Release();
        }
    }

    private sealed class BoundedScratchStream(FileStream file, long limit, Action release) : Stream
    {
        private long _written;
        private bool _disposed;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_disposed;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        public override void Flush() => file.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => file.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            Check(count);
            file.Write(buffer, offset, count);
            _written += count;
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Check(buffer.Length);
            await file.WriteAsync(buffer, cancellationToken);
            _written += buffer.Length;
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        private void Check(int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (count > limit - _written)
                throw new CrashScratchLimitException();
        }
        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                file.Dispose();
                _disposed = true;
                release();
            }
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                await file.DisposeAsync();
                _disposed = true;
                release();
            }
            GC.SuppressFinalize(this);
        }
    }
}

public sealed class CrashScratchLimitException() : IOException("Scratch byte budget exceeded.");

/// <summary>Dispose after every attempt, including successful parsing. Native failures dispose it before returning.</summary>
public sealed class CrashAttemptWorkspace : IAsyncDisposable
{
    private readonly CrashWorkspace _owner;
    private bool _disposed;
    public string DirectoryPath { get; }

    internal CrashAttemptWorkspace(CrashWorkspace owner, string path)
    {
        _owner = owner;
        DirectoryPath = path;
    }

    public Task<Stream> CreateFileAsync(string name, long maxBytes, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _owner.CreateFileAsync(DirectoryPath, name, maxBytes, ct);
    }

    public async Task<string> WriteAsync(string name, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        await using Stream file = await CreateFileAsync(name, Math.Max(1, data.Length), ct);
        await file.WriteAsync(data, ct);
        return Path.Combine(DirectoryPath, name);
    }

    /// <summary>
    /// Reserves before calling an already byte-bounded adapter (for example ICrashObjectStore.DownloadAsync).
    /// The adapter receives a verified, nonexistent destination and must enforce maxBytes while streaming.
    /// Partial or oversized files are removed. Do not use this with an unbounded writer.
    /// </summary>
    public Task<string> WriteExternalAsync(string name, long maxBytes,
        Func<string, long, CancellationToken, Task> boundedWriter, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _owner.WriteExternalAsync(DirectoryPath, name, maxBytes, boundedWriter, ct);
    }

    internal void Verify() => _owner.VerifyAttempt(DirectoryPath);
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _owner.DeleteAttempt(DirectoryPath);
            _disposed = true;
        }
        return ValueTask.CompletedTask;
    }
}
