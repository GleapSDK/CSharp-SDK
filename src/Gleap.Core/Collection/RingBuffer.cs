using System.Collections.Generic;
using System.Linq;

namespace GleapSDK.Collection;

/// <summary>Bounded FIFO buffer: adding past capacity drops the oldest entry.</summary>
public sealed class RingBuffer<T>
{
    private readonly int _capacity;
    private readonly LinkedList<T> _items = new();

    public RingBuffer(int capacity) => _capacity = capacity;

    public int Count => _items.Count;

    public void Add(T item)
    {
        _items.AddLast(item);
        while (_items.Count > _capacity)
        {
            _items.RemoveFirst();
        }
    }

    public IReadOnlyList<T> Snapshot() => _items.ToList();

    /// <summary>Drops the oldest <paramref name="count"/> entries. Used to retire exactly the items that
    /// were handed off (e.g. flushed to the server) without discarding anything added since.</summary>
    public void RemoveFirst(int count)
    {
        for (var i = 0; i < count && _items.Count > 0; i++)
        {
            _items.RemoveFirst();
        }
    }

    public void Clear() => _items.Clear();
}
