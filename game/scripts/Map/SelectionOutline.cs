using Godot;

namespace CivSim.Game.Map;

/// <summary>White outline around the selected hex.</summary>
public partial class SelectionOutline : Node2D
{
    /// <inheritdoc />
    public override void _Draw()
    {
        var points = new Vector2[7];
        for (int i = 0; i < 6; i++)
        {
            points[i] = HexLayout.Corners[i];
        }

        points[6] = points[0];
        DrawPolyline(points, Colors.White, 3f);
    }
}
