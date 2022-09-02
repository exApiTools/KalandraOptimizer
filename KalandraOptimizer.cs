using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore;
using ExileCore.Shared.Helpers;
using GameOffsets.Native;
using ImGuiNET;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace KalandraOptimizer;

public class KalandraOptimizer : BaseSettingsPlugin<KalandraOptimizerSettings>
{
    private bool _showWindow = false;
    private readonly Stack<TabletState> _undoStack = new Stack<TabletState>();
    private readonly Stack<TabletState> _redoStack = new Stack<TabletState>();
    private TabletState _currentState;
    private double _currentStateScore;
    private Dictionary<int, List<TransformationSteps>> _transformations = new Dictionary<int, List<TransformationSteps>>();
    private Vector2i? _selectedTile;
    private Dictionary<Vector2i, TransformationSteps> _selectedTileTransformations = new Dictionary<Vector2i, TransformationSteps>();

    private TabletState GetGameTabletState()
    {
        if (ImGui.IsKeyDown(ImGuiKey.ModCtrl))
        {
            return new TabletState(new[] { new[] { TileType.Encounter, TileType.Entrance, TileType.Empty } });
        }

        var tiles = GameController.IngameState.IngameUi.KalandraTabletWindow.Tiles;
        var maxX = tiles.Max(x => x.TileX);
        var maxY = tiles.Max(x => x.TileY);
        var data = Enumerable.Range(0, maxX + 1).Select(_ => new TileType[maxY + 1]).ToArray();
        foreach (var tile in tiles)
        {
            data[tile.TileX][tile.TileY] = tile.Room.Id switch
            {
                "NULL" => TileType.Water,
                "ENTRANCE" => TileType.Entrance,
                "Empty" => TileType.Empty,
                _ => TileType.Encounter
            };
        }


        var tabletState = new TabletState(data);
        if (!tabletState.IsValidCoord(tabletState.EntranceCoord))
        {
            LogError("Your tablet does not contain an entrance (or is not loaded properly)");
            return null;
        }

        return tabletState;
    }

    public override Job Tick()
    {
        return null;
    }

    private record TransformationSteps(List<ITabletTransformation> Steps, double ResultScore, TabletState ResultingTablet)
    {
    }

    double ScoringFunc(ICollection<int> c) => c.Sum(x => x * x);

    private IEnumerable<TransformationSteps> GetTransformations(TabletState initialState, int maxDepth, List<ITabletTransformation> priorTransformations)
    {
        if (maxDepth == 0)
        {
            return Enumerable.Repeat(new TransformationSteps(priorTransformations.ToList(), initialState.GetScore(ScoringFunc), initialState), 1);
        }

        return initialState.GetTranformations().SelectMany(tr =>
        {
            var deeperTransformations = GetTransformations(initialState.ApplyTransformation(tr), maxDepth - 1, priorTransformations.Append(tr).ToList());

            return deeperTransformations;
        }).Concat(priorTransformations.Any()
            ? Enumerable.Repeat(new TransformationSteps(priorTransformations.ToList(), initialState.GetScore(ScoringFunc), initialState), 1)
            : Enumerable.Empty<TransformationSteps>());
    }

    public override void Render()
    {
        if (Settings.ShowWindowHotkey.PressedOnce())
        {
            _showWindow = !_showWindow;
        }

        if (_showWindow)
        {
            if (ImGui.Begin("Tablet simulator", ref _showWindow))
            {
                if (ImGui.Button("Load from game (have your tablet window open)"))
                {
                    var newState = GetGameTabletState();
                    UpdateState(newState, false);
                }

                ImGui.BeginDisabled(_undoStack.Count == 0);
                ImGui.SameLine();
                if (ImGui.Button("Undo") && _undoStack.TryPop(out var previous))
                {
                    if (_currentState != null)
                    {
                        _redoStack.Push(_currentState);
                    }

                    SetState(previous);
                }

                ImGui.EndDisabled();

                ImGui.BeginDisabled(_redoStack.Count == 0);
                ImGui.SameLine();
                if (ImGui.Button("Redo") && _redoStack.TryPop(out var next))
                {
                    UpdateState(next, true);
                }

                ImGui.EndDisabled();

                if (_selectedTile != null)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Clear selection"))
                    {
                        _selectedTile = null;
                        _selectedTileTransformations = new Dictionary<Vector2i, TransformationSteps>();
                    }
                }

                if (_currentState != null)
                {
                    if (ImGui.BeginTable("##mainTable", 2, ImGuiTableFlags.SizingFixedFit))
                    {
                        ImGui.TableNextColumn();
                        for (int y = _currentState.Height - 1; y >= 0; y--)
                        {
                            for (int x = 0; x < _currentState.Width; x++)
                            {
                                var coord = new Vector2i(x, y);
                                var tileState = _currentState[coord];
                                ImGui.PushStyleColor(ImGuiCol.Button, (tileState.Type switch
                                {
                                    TileType.Empty => new Color(62, 66, 70),
                                    TileType.Water => new Color(35, 43, 54),
                                    TileType.Entrance => new Color(53, 112, 137),
                                    TileType.Encounter => new Color(219, 179, 106)
                                }).ToImgui());
                                ImGui.PushStyleColor(ImGuiCol.Border, Color.Green.ToImgui());
                                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, _selectedTileTransformations.ContainsKey(coord) ? 1 : 0);
                                var tileText = $"({x},{y})\n{tileState.Type}";
                                if (_selectedTileTransformations.TryGetValue(coord, out var step))
                                {
                                    tileText += $"\n({step.ResultScore - _currentStateScore:+#;-#;0})";
                                }

                                if (ImGui.Button(tileText, new Vector2(80, 80)))
                                {
                                    if (_selectedTile == null)
                                    {
                                        var selectedTileTransformations = (from steps in GetTransformations(_currentState, 1, new List<ITabletTransformation>())
                                            let otherTile = GetInteractableTile(_currentState, steps.Steps[0], coord)
                                            where otherTile != null
                                            select (steps, otherTile.Value)).ToDictionary(p => p.Value, p => p.steps);
                                        if (selectedTileTransformations.Any())
                                        {
                                            _selectedTile = coord;
                                            _selectedTileTransformations = selectedTileTransformations;
                                        }
                                    }
                                    else if (_selectedTileTransformations.TryGetValue(coord, out step))
                                    {
                                        UpdateState(step.ResultingTablet, false);
                                    }
                                }
                                else if (ImGui.IsItemHovered() && _selectedTileTransformations.TryGetValue(coord, out step))
                                {
                                    ImGui.SetTooltip($"{step.Steps[0]}: {step.ResultScore} ({step.ResultScore - _currentStateScore:+#;-#;0})");
                                }

                                ImGui.PopStyleVar();
                                ImGui.PopStyleColor(2);
                                ImGui.SameLine();
                            }

                            ImGui.NewLine();
                        }

                        ImGui.TableNextColumn();
                        ImGui.Text("Top possible ways to improve");
                        ImGui.PushStyleColor(ImGuiCol.Button, new Color(53, 112, 137).ToImgui());
                        if (ImGui.BeginTabBar("Step count"))
                        {
                            foreach (var (depth, transformations) in _transformations.OrderBy(x => x.Key))
                            {
                                if (ImGui.BeginTabItem(depth.ToString()))
                                {
                                    foreach (var transformation in transformations.Take(Settings.TopOptionsCount))
                                    {
                                        if (ImGui.Button(
                                                $"{string.Join(", ", transformation.Steps)}: {transformation.ResultScore} ({transformation.ResultScore - _currentStateScore:+#;-#;0})"))
                                        {
                                            UpdateState(transformation.ResultingTablet, false);
                                        }
                                    }

                                    ImGui.EndTabItem();
                                }
                            }

                            ImGui.EndTabBar();
                        }

                        ImGui.PopStyleColor();
                        ImGui.EndTable();
                    }
                }

                ImGui.End();
            }
        }
    }

    private Vector2i? GetInteractableTile(TabletState tablet, ITabletTransformation transformation, Vector2i tile)
    {
        return transformation switch
        {
            SwapEntrance(var coord) when tablet.EntranceCoord.Equals(tile) => coord,
            SwapEntrance(var coord) when coord.Equals(tile) => tablet.EntranceCoord,
            SwapWaterAndEmpty(var water, var empty) when water.Equals(tile) => empty,
            SwapWaterAndEmpty(var water, var empty) when empty.Equals(tile) => water,
            TurnWaterToEmpty(var water) when water.Equals(tile) => water,
            _ => null
        };
    }

    private void UpdateState(TabletState newState, bool isRedo)
    {
        _selectedTile = null;
        _selectedTileTransformations = new Dictionary<Vector2i, TransformationSteps>();
        if (_currentState != null)
        {
            _undoStack.Push(_currentState);
        }

        SetState(newState);
        if (!isRedo)
        {
            _redoStack.Clear();
        }
    }

    private void SetState(TabletState state)
    {
        _currentState = state;
        _currentStateScore = state.GetScore(ScoringFunc);
        _transformations = GetTransformations(_currentState, Settings.SearchDepth, new List<ITabletTransformation>())
            .GroupBy(x => x.Steps.Count).ToDictionary(x => x.Key, x => x.OrderByDescending(steps => steps.ResultScore).ToList());
    }
}

public static class ImguiExt
{
    public static bool AutocompleteInput(string id, ref string input, IReadOnlyCollection<string> items)
    {
        var modified = ImGui.InputText($"{id}##input", ref input, 200);
        if (ImGui.IsItemActive())
        {
            ImGui.OpenPopup($"{id}##window");
        }

        var pos = ImGui.GetItemRectMin();
        pos.Y += ImGui.GetItemRectSize().Y;
        var size = new Vector2(ImGui.GetItemRectSize().X, ImGui.GetTextLineHeightWithSpacing() * 15);
        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSizeConstraints(Vector2.Zero, size);
        if (ImGui.BeginPopup($"{id}##window",
                ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.ChildWindow))
        {
            if (!ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                ImGui.CloseCurrentPopup();
            }

            //ImGui.PushAllowKeyboardFocus(false);
            var inputCopy = input;
            foreach (var item in items.Where(x => x.Contains(inputCopy, StringComparison.InvariantCultureIgnoreCase)))
            {
                if (ImGui.Selectable(item, item == input))
                {
                    input = item;
                    modified = true;
                    ImGui.CloseCurrentPopup();
                }
            }

            //ImGui.PopAllowKeyboardFocus();
            ImGui.EndPopup();
        }

        return modified;
    }
}