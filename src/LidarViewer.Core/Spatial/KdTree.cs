namespace LidarViewer.Core.Spatial;

public sealed class KdTree
{
    private readonly float[] _positions;
    private readonly int[] _nodeIndex;
    private readonly int[] _left;
    private readonly int[] _right;
    private readonly int[] _axis;
    private readonly int _root;
    private int _nodeCount;

    public KdTree(float[] positions, int count)
    {
        ArgumentNullException.ThrowIfNull(positions);

        if (count < 0 || count > positions.Length / 3)
            throw new ArgumentOutOfRangeException(nameof(count));

        _positions = positions;
        _nodeIndex = new int[count];
        _left = new int[count];
        _right = new int[count];
        _axis = new int[count];

        var order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;

        _root = Build(order, 0, count, 0);
    }

    public int Count => _nodeCount;

    private int Build(int[] order, int begin, int end, int depth)
    {
        if (begin >= end) return -1;

        int axis = WidestAxis(order, begin, end);
        int mid = begin + ((end - begin) >> 1);
        Select(order, begin, end, mid, axis);

        int node = _nodeCount++;
        _nodeIndex[node] = order[mid];
        _axis[node] = axis;
        _left[node] = Build(order, begin, mid, depth + 1);
        _right[node] = Build(order, mid + 1, end, depth + 1);
        return node;
    }

    private int WidestAxis(int[] order, int begin, int end)
    {
        var min = new float[3] { float.MaxValue, float.MaxValue, float.MaxValue };
        var max = new float[3] { float.MinValue, float.MinValue, float.MinValue };

        for (int i = begin; i < end; i++)
        {
            int point = order[i];
            for (int axis = 0; axis < 3; axis++)
            {
                float value = _positions[point * 3 + axis];
                if (value < min[axis]) min[axis] = value;
                if (value > max[axis]) max[axis] = value;
            }
        }

        int widest = 0;
        float best = max[0] - min[0];
        for (int axis = 1; axis < 3; axis++)
        {
            float extent = max[axis] - min[axis];
            if (extent > best)
            {
                best = extent;
                widest = axis;
            }
        }

        return widest;
    }

    private void Select(int[] order, int begin, int end, int target, int axis)
    {
        int left = begin;
        int right = end - 1;

        while (left < right)
        {
            double pivot = _positions[order[left] * 3 + axis];
            int i = left;
            int j = right;

            while (i <= j)
            {
                while (i <= j && _positions[order[i] * 3 + axis] < pivot) i++;
                while (i <= j && _positions[order[j] * 3 + axis] > pivot) j--;
                if (i <= j)
                {
                    (order[i], order[j]) = (order[j], order[i]);
                    i++;
                    j--;
                }
            }

            if (target <= j) right = j;
            else if (target >= i) left = i;
            else break;
        }
    }

    public double SumOfNearestDistances(int pointIndex, int k, double[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (k <= 0) return 0;

        float px = _positions[pointIndex * 3];
        float py = _positions[pointIndex * 3 + 1];
        float pz = _positions[pointIndex * 3 + 2];

        int found = 0;
        Search(_root, px, py, pz, pointIndex, k, buffer, ref found);

        double sum = 0;
        for (int i = 0; i < found; i++) sum += Math.Sqrt(buffer[i]);
        return sum;
    }

    private void Search(
        int node, float px, float py, float pz, int skipIndex, int k,
        double[] best, ref int found)
    {
        if (node < 0) return;

        int index = _nodeIndex[node];
        if (index != skipIndex)
        {
            double dx = _positions[index * 3] - px;
            double dy = _positions[index * 3 + 1] - py;
            double dz = _positions[index * 3 + 2] - pz;
            double squared = dx * dx + dy * dy + dz * dz;

            if (found < k || squared < best[found - 1])
            {
                int slot = found < k ? found : k - 1;
                best[slot] = squared;
                if (found < k) found++;

                while (slot > 0 && best[slot - 1] > best[slot])
                {
                    (best[slot - 1], best[slot]) = (best[slot], best[slot - 1]);
                    slot--;
                }
            }
        }

        int axis = _axis[node];
        float pivot = axis switch
        {
            0 => _positions[index * 3],
            1 => _positions[index * 3 + 1],
            _ => _positions[index * 3 + 2],
        };

        float coordinate = axis switch
        {
            0 => px,
            1 => py,
            _ => pz,
        };

        bool leftFirst = coordinate < pivot;
        int near = leftFirst ? _left[node] : _right[node];
        int far = leftFirst ? _right[node] : _left[node];

        Search(near, px, py, pz, skipIndex, k, best, ref found);

        double delta = pivot - coordinate;
        if (found < k || delta * delta <= best[found - 1])
            Search(far, px, py, pz, skipIndex, k, best, ref found);
    }
}
