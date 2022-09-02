using System.Collections.Generic;
using System.Linq;
using GameOffsets.Native;

namespace KalandraOptimizer;

public class PathFinder
{
    private readonly bool[][] _grid;

    private readonly int _dimension2;
    private readonly int _dimension1;

    public PathFinder(bool[][] grid)
    {
        _grid = grid;
        _dimension1 = _grid.Length;
        _dimension2 = _grid[0].Length;
    }

    private bool IsTilePathable(Vector2i tile)
    {
        if (tile.X < 0 || tile.X >= _dimension2)
        {
            return false;
        }

        if (tile.Y < 0 || tile.Y >= _dimension1)
        {
            return false;
        }

        return _grid[tile.Y][tile.X];
    }

    private static readonly IReadOnlyList<Vector2i> NeighborOffsets = new List<Vector2i>
    {
        new Vector2i(0, 1),
        new Vector2i(1, 0),
        new Vector2i(0, -1),
        new Vector2i(-1, 0),
    };

    private static IEnumerable<Vector2i> GetNeighbors(Vector2i tile)
    {
        return NeighborOffsets.Select(offset => tile + offset);
    }

    public int[][] GetDistanceField(Vector2i target)
    {
        var exactDistanceField = new Dictionary<Vector2i, int>
        {
            [target] = 0
        };
        var visitedTiles = new HashSet<Vector2i>();
        var queue = new BinaryHeap<int, Vector2i>();
        queue.Add(0, target);
        visitedTiles.Add(target);

        while (queue.TryRemoveTop(out var top))
        {
            var current = top.Value;
            var currentDistance = top.Key;

            foreach (var neighbor in GetNeighbors(current))
            {
                TryEnqueueTile(neighbor, currentDistance);
            }
        }

        void TryEnqueueTile(Vector2i coord, int previousScore)
        {
            if (!IsTilePathable(coord))
            {
                return;
            }

            if (visitedTiles.Contains(coord))
            {
                return;
            }

            visitedTiles.Add(coord);
            var exactDistance = previousScore + 1;
            exactDistanceField.TryAdd(coord, exactDistance);
            queue.Add(exactDistance, coord);
        }

        return Enumerable.Range(0, _dimension1)
            .Select(y => Enumerable.Range(0, _dimension2)
                .Select(x => exactDistanceField.GetValueOrDefault(new Vector2i(x, y), int.MaxValue))
                .ToArray()).ToArray();
    }
}