using LidarViewer.Core.Models;

namespace LidarViewer.Core.Playback;

/// <summary>
/// Maly cache LRU klatek (ostatnio uzyte zostaja). Bezpieczny watkowo; wczytanie klatki odbywa sie
/// poza blokada, wiec dwa watki moga rownolegle wczytac te sama klatke - wygrywa pierwsza zapisana.
/// </summary>
internal sealed class FrameCache
{
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<int, LinkedListNode<(int Index, PointCloud Cloud)>> _nodes = [];
    private readonly LinkedList<(int Index, PointCloud Cloud)> _order = new();

    public FrameCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    public PointCloud GetOrLoad(int index, Func<int, PointCloud> load)
    {
        lock (_gate)
        {
            if (_nodes.TryGetValue(index, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                return node.Value.Cloud;
            }
        }

        PointCloud cloud = load(index);

        lock (_gate)
        {
            if (_nodes.TryGetValue(index, out var existing))
                return existing.Value.Cloud;

            _nodes[index] = _order.AddFirst((index, cloud));
            while (_order.Count > _capacity)
            {
                _nodes.Remove(_order.Last!.Value.Index);
                _order.RemoveLast();
            }
        }

        return cloud;
    }
}
