namespace Avalon.Common.Threading;

public class LockedQueue<T> where T : class
{
    private readonly object _lock = new object();
    private readonly Deque<T> _queue = new Deque<T>();
    private volatile bool _canceled = false;

    // Adds an item to the queue.
    public void Add(T item)
    {
        lock (_lock)
        {
            _queue.AddToBack(item);
        }
    }

    // Gets the next result in the queue, if any.
    public bool Next(out T? result)
    {
        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                result = null;
                return false;
            }

            result = _queue.RemoveFromFront();
            return true;
        }
    }

    // Gets the next result in the queue if it passes the check.
    public bool Next(out T? result, Func<T, bool> check)
    {
        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                result = null;
                return false;
            }

            result = _queue.Peek();
            if (!check(result))
            {
                result = null;
                return false;
            }

            _queue.RemoveFromFront();
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
                _queue.RemoveFromFront();
            }
        }
    }
}

internal class Deque<T>
{
    private readonly LinkedList<T> _list = [];

    // Adds an item to the back of the deque.
    public void AddToBack(T item)
    {
        _list.AddLast(item);
    }

    // Adds an item to the front of the deque.
    public void AddToFront(T item)
    {
        _list.AddFirst(item);
    }

    // Removes and returns the item from the front of the deque.
    public T RemoveFromFront()
    {
        if (_list.Count == 0)
            throw new InvalidOperationException("Deque is empty.");

        var value = _list.First.Value;
        _list.RemoveFirst();
        return value;
    }

    // Peeks at the item from the front of the deque without removing it.
    public T Peek()
    {
        if (_list.Count == 0)
            throw new InvalidOperationException("Deque is empty.");

        return _list.First.Value;
    }

    // Gets the count of items in the deque.
    public int Count => _list.Count;
}
