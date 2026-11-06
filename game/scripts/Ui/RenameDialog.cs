using System;
using System.Collections.Generic;
using CivSim.Game.Net;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Ui;

/// <summary>
/// Rename a civilization or a settlement (GDD 5.6). Shows other names in the same people's language as buttons
/// ("More names" asks for the next ones) and a field for one's own name. The server checks that the name is free.
/// </summary>
public partial class RenameDialog : CenterContainer
{
    private const int Count = 8;
    private readonly ClientSession _session;
    private readonly NameTarget _target;
    private readonly int _id;
    private readonly int _owner;
    private readonly string _current;
    private GridContainer _names = null!;
    private LineEdit _edit = null!;
    private Label _error = null!;
    private int _page;

    /// <summary>Creates the dialog for one civilization (owner = id) or one settlement.</summary>
    public RenameDialog(ClientSession session, NameTarget target, int id, int owner, string current)
    {
        _session = session;
        _target = target;
        _id = id;
        _owner = owner;
        _current = current;
    }

    /// <summary>Required by Godot; not used.</summary>
    public RenameDialog()
        : this(null!, NameTarget.Civilization, 0, 0, string.Empty)
    {
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Theme = PixelTheme.Current;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // the map underneath does not take clicks while the dialog is open
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(540, 0) };
        AddChild(panel);
        var box = new VBoxContainer();
        panel.AddChild(box);
        string what = _target == NameTarget.Civilization ? "people" : "settlement";
        box.AddChild(new Label { Text = $"Rename the {what} {_current}", ThemeTypeVariation = "Heading" });
        box.AddChild(new Label { Text = "Names in the same language:", Modulate = new Color(0.8f, 0.8f, 0.8f) });
        _names = new GridContainer { Columns = 4 };
        box.AddChild(_names);
        var more = new Button { Text = "More names" };
        more.Pressed += () => _session.RequestNames(_target, _id, ++_page, Count);
        box.AddChild(more);
        box.AddChild(new Label { Text = "Or your own:", Modulate = new Color(0.8f, 0.8f, 0.8f) });
        _edit = new LineEdit { Text = _current, MaxLength = 40, SelectAllOnFocus = true };
        _edit.TextSubmitted += _ => Apply();
        box.AddChild(_edit);
        _error = new Label { Modulate = new Color(1f, 0.45f, 0.4f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_error);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        box.AddChild(buttons);
        var cancel = new Button { Text = "Cancel" };
        cancel.Pressed += QueueFree;
        buttons.AddChild(cancel);
        var ok = new Button { Text = "Rename" };
        ok.Pressed += Apply;
        buttons.AddChild(ok);

        _session.NamesReceived += OnNames;
        _session.CommandRefused += OnRefused;
        _session.RequestNames(_target, _id, 0, Count);
        _edit.GrabFocus();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _session.NamesReceived -= OnNames;
        _session.CommandRefused -= OnRefused;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // The dialog closes itself once the view shows the new name.
        if (_sent is { } name && CurrentName() == name)
        {
            QueueFree();
        }
    }

    private string? _sent;

    private void OnNames(NameSuggestionsMessage m)
    {
        if (m.Target != _target || m.Id != _id)
        {
            return;
        }

        foreach (Node child in _names.GetChildren())
        {
            child.QueueFree();
        }

        foreach (string name in m.Names)
        {
            var b = new Button { Text = name, CustomMinimumSize = new Vector2(118, 0) };
            string n = name;
            b.Pressed += () => _edit.Text = n;
            _names.AddChild(b);
        }
    }

    private void OnRefused(string reason) => _error.Text = reason;

    private void Apply()
    {
        string name = _edit.Text.Trim();
        if (name.Length == 0)
        {
            _error.Text = "Type a name or pick one above.";
            return;
        }

        if (name == _current)
        {
            QueueFree();
            return;
        }

        _error.Text = string.Empty;
        _sent = name;
        if (_target == NameTarget.Civilization)
        {
            _session.RenameCivilization(_id, name);
        }
        else
        {
            _session.RenameSettlement(_owner, _id, name);
        }
    }

    private string? CurrentName()
    {
        if (_session.View is not { } v)
        {
            return null;
        }

        if (_target == NameTarget.Civilization)
        {
            foreach (CivilizationView c in v.Civilizations)
            {
                if (c.Id == _id)
                {
                    return c.Name;
                }
            }

            return null;
        }

        IReadOnlyList<SettlementView> settlements = v.Settlements ?? [];
        foreach (SettlementView s in settlements)
        {
            if (s.Id == _id)
            {
                return s.Name;
            }
        }

        return null;
    }
}
