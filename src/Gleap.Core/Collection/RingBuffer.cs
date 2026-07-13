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

    public void Clear() => _items.Clear();
}
