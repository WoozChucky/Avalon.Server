using System.Collections.Concurrent;

namespace Avalon.Common.Threading;

public class RingBuffer<T>
{
    private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
    private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
    private readonly int _capacity;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentException("Capacity must be greater than 0", nameof(capacity));

        _capacity = capacity;
    }

    public void Enqueue(T item)
    {
        while (_queue.Count >= _capacity)
        {
            if (_queue.TryDequeue(out _))
                _signal.Wait();
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
