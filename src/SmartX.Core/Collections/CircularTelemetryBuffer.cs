using System.Collections;

namespace SmartX.Core.Collections;

// a fixed ring of slots where the oldest reading drops out the back as a new one comes in the front
// like a dashcam loop, it never fills the disk because it keeps writing over itself
public sealed class CircularTelemetryBuffer<T> : IReadOnlyCollection<T>
{
    private readonly T[] _items;
    private int _head;
    private int _count;
    private long _totalWritten;
    private readonly object _gate = new();

    public CircularTelemetryBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
        }

        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    public int Count
    {
        get { lock (_gate) { return _count; } }
    }

    public long TotalWritten
    {
        get { lock (_gate) { return _totalWritten; } }
    }

    public bool IsFull
    {
        get { lock (_gate) { return _count == _items.Length; } }
    }

    // hands back whatever got pushed out, so the caller can file it away without a second pass
    public bool Write(T item, out T evicted)
    {
        lock (_gate)
        {
            var wasFull = _count == _items.Length;
            evicted = wasFull ? _items[_head] : default!;

            _items[_head] = item;
            _head = (_head + 1) % _items.Length;
            _totalWritten++;

            if (!wasFull)
            {
                _count++;
            }

            return wasFull;
        }
    }

    public void Write(T item) => Write(item, out _);

    public T this[int index]
    {
        get
        {
            lock (_gate)
            {
                if ((uint)index >= (uint)_count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                var start = (_head - _count + _items.Length) % _items.Length;
                return _items[(start + index) % _items.Length];
            }
        }
    }

    public T Newest
    {
        get
        {
            lock (_gate)
            {
                if (_count == 0) return default!;
                return _items[(_head - 1 + _items.Length) % _items.Length];
            }
        }
    }

    public T[] Snapshot()
    {
        lock (_gate)
        {
            var result = new T[_count];
            var start = (_head - _count + _items.Length) % _items.Length;

            for (var i = 0; i < _count; i++)
            {
                result[i] = _items[(start + i) % _items.Length];
            }

            return result;
        }
    }

    public T[] SnapshotNewest(int take)
    {
        lock (_gate)
        {
            var n = Math.Min(take, _count);
            var result = new T[n];
            var start = (_head - n + _items.Length) % _items.Length;

            for (var i = 0; i < n; i++)
            {
                result[i] = _items[(start + i) % _items.Length];
            }

            return result;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_items, 0, _items.Length);
            _head = 0;
            _count = 0;
        }
    }

    public Enumerator GetEnumerator() => new(Snapshot());

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Snapshot()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Snapshot().GetEnumerator();

    public struct Enumerator
    {
        private readonly T[] _snapshot;
        private int _index;

        internal Enumerator(T[] snapshot)
        {
            _snapshot = snapshot;
            _index = -1;
        }

        public readonly T Current => _snapshot[_index];

        public bool MoveNext() => ++_index < _snapshot.Length;
    }
}
