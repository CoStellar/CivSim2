using System;
using Godot;

namespace CivSim.Game.Audio;

/// <summary>The volume sliders (all, music, ambient, effects, interface) and the mute switch, for any settings screen.</summary>
public static class AudioPanel
{
    private static readonly string[] Names = ["Music", "Ambient", "Effects", "Interface"];

    private static readonly string[] Tips =
    [
        "Music of the era",
        "Nature, the settlement and battles the camera looks at",
        "Building, orders, discoveries, alerts",
        "Button clicks and windows",
    ];

    /// <summary>A box with one slider row per volume; changes are heard and saved at once.</summary>
    public static Control Build()
    {
        var box = new VBoxContainer();
        if (AudioManager.Instance is not { } audio)
        {
            box.AddChild(new Label { Text = "Sound is not running." });
            return box;
        }

        var mute = new CheckBox { Text = "Sound off (key K)", ButtonPressed = audio.Settings.Muted, FocusMode = Control.FocusModeEnum.None };
        mute.Toggled += on =>
        {
            AudioSettings s = audio.Settings.Copy();
            s.Muted = on;
            audio.Apply(s);
        };
        box.AddChild(mute);
        Row(box, "All", "Everything together", audio.Settings.Master, v =>
        {
            AudioSettings s = audio.Settings.Copy();
            s.Master = v;
            audio.Apply(s);
        });
        for (int i = 0; i < Names.Length; i++)
        {
            int bus = i;
            Row(box, Names[i], Tips[i], audio.Settings.Volumes[i], v =>
            {
                AudioSettings s = audio.Settings.Copy();
                s.Volumes[bus] = v;
                audio.Apply(s);
            });
        }

        // The K key changes the switch while the panel is open.
        void Follow() => mute.SetPressedNoSignal(audio.Settings.Muted);
        audio.SettingsChanged += Follow;
        box.TreeExiting += () => audio.SettingsChanged -= Follow;
        return box;
    }

    private static void Row(VBoxContainer box, string name, string tip, int value, Action<int> changed)
    {
        var row = new HBoxContainer { TooltipText = tip };
        box.AddChild(row);
        row.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(110, 0), MouseFilter = Control.MouseFilterEnum.Pass });
        var slider = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, Value = value, CustomMinimumSize = new Vector2(220, 24), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, FocusMode = Control.FocusModeEnum.None, TooltipText = tip };
        var percent = new Label { Text = $"{value}%", CustomMinimumSize = new Vector2(52, 0) };
        slider.ValueChanged += v =>
        {
            percent.Text = $"{(int)v}%";
            changed((int)v);
        };
        row.AddChild(slider);
        row.AddChild(percent);
    }
}
