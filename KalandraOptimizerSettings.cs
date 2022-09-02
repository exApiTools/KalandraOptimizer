using System.Windows.Forms;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;

namespace KalandraOptimizer;

public class KalandraOptimizerSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new ToggleNode(true);
    public HotkeyNode ShowWindowHotkey { get; set; } = new HotkeyNode(Keys.Multiply);
    public RangeNode<int> TopOptionsCount { get; set; } = new RangeNode<int>(50, 0, 1000);
    public RangeNode<int> SearchDepth { get; set; } = new RangeNode<int>(2, 1, 3);
}