using System.Diagnostics.CodeAnalysis;

namespace Avalon.Common.Threading;

public class LockedQueue<T>
{
    private readonly object _lock = new object();
    private readonly Queue<T> _queue = new Queue<T>();
    private volatile bool _canceled = false;

    // Adds an item to the queue.
    public void Add(T item)
    {
        lock (_lock)
        {
            _queue.Enqueue(item);
        }
    }

    // Gets the next item from the queue, if any.
    public bool Next([MaybeNullWhen(false)] out T result)
    {
        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                result = default;
                return false;
            }

            result = _queue.Dequeue();
            return true;
        }
    }

    // Gets the next item from the queue if it passes the check.
    // Uses peek-first semantics: if the check fails the item stays at the head.
    public bool Next([MaybeNullWhen(false)] out T result, Func<T, bool> check)
    {
        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                result = default;
                return false;
            }

            T front = _queue.Peek();
            if (!check(front))
            {
                result = default;
                return false;
            }

            _queue.Dequeue();
            result = front;
            return true;
        }
    }

    // Peeks at the top of the queue. Check if the queue is empty before calling!
    public T Peek(bool autoUnlock = false)
    {
        Monitor.Enter(_lock);

        try
        {
            T result = _queue.Peek();

            if (autoUnlock)
            {
                Monitor.Exit(_lock);
            }

            return result;
        }
        catch (Exception)
        {
            if (Monitor.IsEntered(_lock))
            {
                Monitor.Exit(_lock);
            }
            throw;
        }
    }

    // Cancels the queue.
    public void Cancel()
    {
        lock (_lock)
        {
            _canceled = true;
        }
    }

    // Checks if the queue is cancelled.
    public bool IsCancelled()
    {
        lock (_lock)
        {
            return _canceled;
        }
    }

    // Checks if the queue is empty.
    public bool IsEmpty()
    {
        lock (_lock)
        {
            return _queue.Count == 0;
        }
    }

    // Removes the front element of the queue.
    public void PopFront()
    {
        lock (_lock)
        {
            if (_queue.Count > 0)
            {
                _queue.Dequeue();
            }
        }
    }
}
