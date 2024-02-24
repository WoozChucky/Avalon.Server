using System.Collections.Concurrent;
using System.Diagnostics;

namespace Avalon.Common.Threading;

public class RingBuffer<T>
{
    private readonly ConcurrentQueue<T> _queue;
    private readonly SemaphoreSlim _signal;
    private readonly string _name;
    private readonly int _capacity;

    public RingBuffer(string name, int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentException("Capacity must be greater than 0", nameof(capacity));
        _queue = new ConcurrentQueue<T>();
        _signal = new SemaphoreSlim(0);

        _name = name;
        _capacity = capacity;
    }

    public void Enqueue(T item)
    {
        Stopwatch? stopwatch = null;

        while (_queue.Count >= _capacity)
        {
            stopwatch ??= Stopwatch.StartNew();

            if (_queue.TryDequeue(out _))
                _signal.Wait();
        }

        if (stopwatch != null)
        {
            stopwatch.Stop();
            Console.WriteLine($"[RingBuffer({_name})]: Time spent waiting: {stopwatch.ElapsedMilliseconds}ms");
        }

        _queue.Enqueue(item);
        _signal.Release();
    }

    public async Task<T?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        _queue.TryDequeue(out var item);
        return item;
    }
}
