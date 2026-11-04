using CivSim.Protocol;
using Godot;

namespace CivSim.Game;

/// <summary>Startup scene. Shows that the client builds and sees the protocol.</summary>
public partial class Main : Node2D
{
    /// <inheritdoc />
    public override void _Ready()
    {
        var label = GetNode<Label>("StatusLabel");
        label.Text = $"CivSim 2 - client ready (protocol v{ProtocolInfo.Version})";
    }
}
