using System;
using System.Collections.Generic;
using System.Linq;
using GameOffsets.Native;

namespace KalandraOptimizer;

public interface ITabletTransformation
{
}

public record SwapEntrance(Vector2i Coord) : ITabletTransformation;

public record SwapWaterAndEmpty(Vector2i WaterCoord, Vector2i EmptyCoord) : ITabletTransformation;

public record TurnWaterToEmpty(Vector2i WaterCoord) : ITabletTransformation;

public class TabletState : IEquatable<TabletState>
{
    public readonly int Width;
    public readonly int Height;
    private readonly TileState[] _tiles;

    public TabletState(TileType[][] tiles)
    {
        Width = tiles.Length;
        Height = tiles[0].Length;
        _tiles = new TileState[Width * Height];
        for (int i = 0; i < Width * Height; i++)
        {
            var coord = LinearTo2D(i);
            _tiles[i] = new TileState { Type = tiles[coord.X][coord.Y] };
        }

        if (IsValidCoord(EntranceCoord))
        {
            RecalculateDistance();
        }
    }

    private TabletState(int width, int height, TileState[] tiles)
    {
        Width = width;
        Height = height;
        _tiles = tiles;
    }

    public Vector2i LinearTo2D(int linearIndex)
    {
        return new Vector2i(linearIndex / Height, linearIndex % Height);
    }

    public int Convert2DToLinear(Vector2i coord)
    {
        return coord.Y + coord.X * Height;
    }

    public ref TileState this[Vector2i coord] => ref _tiles[Convert2DToLinear(coord)];

    public Vector2i EntranceCoord => LinearTo2D(Array.FindIndex(_tiles, x => x.Type == TileType.Entrance));

    public bool IsValidCoord(Vector2i coord) => coord.X >= 0 && coord.X < Width && coord.Y >= 0 && coord.Y < Height;

    public void RecalculateDistance()
    {
        var pathabilityGrid = Enumerable.Range(0, Height)
            .Select(y => Enumerable.Range(0, Width)
                .Select(x => this[new Vector2i(x, y)].Type != TileType.Water)
                .ToArray()).ToArray();
        var distanceField = new PathFinder(pathabilityGrid).GetDistanceField(EntranceCoord);
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                _tiles[Convert2DToLinear(new Vector2i(x, y))].Distance = distanceField[y][x] switch { int.MaxValue => 0, var s => s };
            }
        }
    }

    public TabletState Clone()
    {
        return new TabletState(width: Width, height: Height, tiles: _tiles.ToArray());
    }

    public TabletState Swap(Vector2i coord1, Vector2i coord2)
    {
        var clone = Clone();
        (clone[coord1], clone[coord2]) = (clone[coord2], clone[coord1]);
        clone.RecalculateDistance();
        return clone;
    }

    public TabletState ChangeType(Vector2i coord, TileType state)
    {
        var clone = Clone();
        clone[coord].Type = state;
        clone.RecalculateDistance();
        return clone;
    }

    public IEnumerable<ITabletTransformation> GetTranformations()
    {
        for (int i = 0; i < _tiles.Length; i++)
        {
            var iCoord = LinearTo2D(i);
            if (_tiles[i].Type == TileType.Empty)
            {
                yield return new SwapEntrance(iCoord);
            }

            if (_tiles[i].Type == TileType.Water)
            {
                yield return new TurnWaterToEmpty(iCoord);

                for (int j = 0; j < _tiles.Length; j++)
                {
                    if (_tiles[j].Type == TileType.Empty)
                    {
                        var jCoord = LinearTo2D(j);
                        yield return new SwapWaterAndEmpty(iCoord, jCoord);
                    }
                }
            }
        }
    }

    public TabletState ApplyTransformation(ITabletTransformation transformation)
    {
        return transformation switch
        {
            SwapEntrance(var coord) => Swap(EntranceCoord, coord),
            TurnWaterToEmpty(var coord) => ChangeType(coord, TileType.Empty),
            SwapWaterAndEmpty(var waterCoord, var emptyCoord) => Swap(waterCoord, emptyCoord),
            _ => throw new Exception("Unknown transformation")
        };
    }

    public double GetScore(Func<ICollection<int>, double> scoringFunc)
    {
        return scoringFunc(_tiles.Select(x => x.Distance).Where(x => x != 0).ToList());
    }

    public bool Equals(TabletState other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Width == other.Width && Height == other.Height && _tiles.SequenceEqual(other._tiles);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((TabletState)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Width, Height, _tiles.Aggregate(0, HashCode.Combine));
    }
}