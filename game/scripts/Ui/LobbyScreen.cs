using System;
using System.Collections.Generic;
using System.Globalization;
using CivSim.Game.Net;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using Godot;

namespace CivSim.Game.Ui;

/// <summary>
/// The lobby of a multiplayer game (MP3): the players, the settings (read-only), the chat, Ready and, for the host, Start. Says
/// hello on its connection; when the game starts (or the server is already playing) it hands the connection on with
/// <see cref="Started"/>. Every text from the server is shown as plain text, never as markup (multiplayer plan, chapter 9).
/// </summary>
public partial class LobbyScreen : CanvasLayer
{
    private readonly IConnection _connection;
    private readonly string _name;
    private readonly string _password;
    private readonly string _returnKey;
    private readonly string _where;
    private readonly List<IProtocolMessage> _early = [];
    private Label _title = null!;
    private VBoxContainer _seats = null!;
    private VBoxContainer _settings = null!;
    private VBoxContainer _chat = null!;
    private ScrollContainer _chatScroll = null!;
    private LineEdit _say = null!;
    private CheckButton _ready = null!;
    private Button _start = null!;
    private Label _status = null!;
    private bool _done;
    private string _chatShown = "";

    // Colour of the suffix added to a repeated nickname.
    private static readonly Color SuffixColour = new(0.55f, 0.8f, 1f);
    private double _time;
    private bool _chatted;
    private bool _startSent;
    private LobbyStateMessage? _last;

    /// <summary>Creates the screen for a connection to a lobby (or to a game in progress).</summary>
    public LobbyScreen(IConnection connection, string name, string password, string returnKey, string where)
    {
        _connection = connection;
        _name = name;
        _password = password;
        _returnKey = returnKey;
        _where = where;
    }

    /// <summary>Required by Godot; not used.</summary>
    public LobbyScreen()
        : this(null!, "", "", "", "")
    {
    }

    /// <summary>The game starts: the connection to play on (it says hello to the game itself).</summary>
    public event Action<IConnection, bool>? Started;

    /// <summary>The server turned the player away or the connection was lost: why.</summary>
    public event Action<string>? Failed;

    /// <summary>The player left the lobby.</summary>
    public event Action? Left;

    /// <summary>For automated checks: press Ready once the lobby is shown.</summary>
    public bool AutoReady { get; set; }

    /// <summary>For automated checks: the host starts the game once this many players are in and ready (0 = never).</summary>
    public int AutoStartWith { get; set; }

    /// <summary>For automated checks: a chat line said a second after joining.</summary>
    public string? AutoChat { get; set; }

    /// <summary>For automated checks: a screenshot of the lobby after <see cref="ScreenshotAfter"/> seconds.</summary>
    public string? Screenshot { get; set; }

    /// <summary>Seconds before <see cref="Screenshot"/>.</summary>
    public double ScreenshotAfter { get; set; } = 3;

    /// <inheritdoc />
    public override void _Ready()
    {
        var root = new PanelContainer { Theme = PixelTheme.Current };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var center = new CenterContainer();
        root.AddChild(center);
        var page = new VBoxContainer { CustomMinimumSize = new Vector2(1100, 720) };
        page.AddThemeConstantOverride("separation", 12);
        center.AddChild(page);

        _title = new Label { Text = "Connecting to " + _where + "...", ThemeTypeVariation = "Heading" };
        _title.AddThemeFontSizeOverride("font_size", 32);
        page.AddChild(_title);

        var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 24);
        page.AddChild(columns);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
        left.AddThemeConstantOverride("separation", 8);
        columns.AddChild(left);
        left.AddChild(new Label { Text = "Players", ThemeTypeVariation = "Heading" });
        _seats = new VBoxContainer();
        left.AddChild(_seats);
        left.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
        left.AddChild(new Label { Text = "Settings", ThemeTypeVariation = "Heading" });
        _settings = new VBoxContainer();
        left.AddChild(_settings);

        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        columns.AddChild(right);
        right.AddChild(new Label { Text = "Chat", ThemeTypeVariation = "Heading" });
        _chatScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        right.AddChild(_chatScroll);
        _chat = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _chatScroll.AddChild(_chat);
        var line = new HBoxContainer();
        right.AddChild(line);
        _say = new LineEdit { PlaceholderText = "Say something to everyone", MaxLength = LobbyChatMessage.MaxLength, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _say.TextSubmitted += _ => Say();
        line.AddChild(_say);
        var send = new Button { Text = "Send" };
        send.Pressed += Say;
        line.AddChild(send);

        _status = new Label { Modulate = new Color(1f, 0.6f, 0.5f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        page.AddChild(_status);
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 16);
        page.AddChild(footer);
        _ready = new CheckButton { Text = "Ready", Visible = false, TooltipText = "The host starts the game when every player is ready" };
        _ready.Toggled += on => _connection.Send(new LobbyReadyMessage(on));
        footer.AddChild(_ready);
        _start = new Button { Text = "Start the game", Visible = false, Disabled = true, CustomMinimumSize = new Vector2(220, 48) };
        _start.Pressed += () => _connection.Send(new LobbyStartMessage());
        footer.AddChild(_start);
        footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var leave = new Button { Text = "Leave", CustomMinimumSize = new Vector2(160, 48) };
        leave.Pressed += () =>
        {
            _done = true;
            _connection.Close();
            Left?.Invoke();
        };
        footer.AddChild(leave);

        _connection.Send(new HelloMessage(ProtocolInfo.Version, _name, Password: _password, ReturnKey: _returnKey));
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_done)
        {
            return;
        }

        _time += delta;
        Automate();
        while (!_done && _connection.TryReceive(out IProtocolMessage? message))
        {
            switch (message)
            {
                case LobbyStateMessage state:
                    _last = state;
                    Show(state);
                    break;
                case GameStartingMessage:
                    _done = true;
                    Started?.Invoke(_connection, true); // the game itself wants a hello now
                    return;
                case WelcomeMessage:
                    // A game in progress let us in at once: the game screen gets what came with the welcome too.
                    _early.Add(message);
                    while (_connection.TryReceive(out IProtocolMessage? more))
                    {
                        _early.Add(more!);
                    }

                    _done = true;
                    Started?.Invoke(new BufferedConnection(_connection, _early), false);
                    return;
                case ConnectionRejectedMessage rejected:
                    _done = true;
                    Failed?.Invoke(rejected.Reason);
                    return;
                case CommandRejectedMessage refused:
                    _status.Text = refused.Reason;
                    break;
                default:
                    break;
            }
        }

        if (!_done && !_connection.IsOpen)
        {
            _done = true;
            Failed?.Invoke(_connection is TcpConnection { CloseReason: { } why } ? why : "The connection to the server was lost.");
        }
    }

    private void Automate()
    {
        if (AutoChat is not null && !_chatted && _time > 1 && _last is not null)
        {
            _chatted = true;
            _connection.Send(new LobbyChatMessage(AutoChat));
        }

        if (AutoReady && _last is not null && _ready.Visible && !_ready.ButtonPressed)
        {
            _ready.ButtonPressed = true; // sends Ready like a click
        }

        if (AutoStartWith > 0 && !_startSent && _last is { CanStart: true } s && s.Seats.Count >= AutoStartWith && _time > ScreenshotAfter + 1)
        {
            _startSent = true;
            _connection.Send(new LobbyStartMessage());
        }

        if (Screenshot is not null && _time >= ScreenshotAfter)
        {
            GetViewport().GetTexture().GetImage().SavePng(Screenshot);
            GD.Print($"screenshot: {Screenshot}");
            Screenshot = null;
        }
    }

    private void Say()
    {
        if (_say.Text.Trim().Length > 0)
        {
            _connection.Send(new LobbyChatMessage(_say.Text));
            _say.Text = "";
        }
    }

    private void Show(LobbyStateMessage state)
    {
        _title.Text = state.GameName;
        foreach (Node n in _seats.GetChildren())
        {
            n.QueueFree();
        }

        for (int i = 0; i < state.HumanSeats; i++)
        {
            if (i >= state.Seats.Count)
            {
                _seats.AddChild(new Label { Text = $"{i + 1}. (free)", Modulate = new Color(0.6f, 0.6f, 0.6f) });
                continue;
            }

            // The suffix the server added to a repeated nickname is shown in another colour (Patryk, 2026-11-25).
            LobbySeat seat = state.Seats[i];
            int cut = seat.Name.Length - Math.Clamp(seat.SuffixLength, 0, seat.Name.Length);
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 0);
            line.AddChild(new Label { Text = $"{i + 1}. {seat.Name[..cut]}" });
            if (cut < seat.Name.Length)
            {
                line.AddChild(new Label { Text = seat.Name[cut..], Modulate = SuffixColour, TooltipText = "Added by the game: another player has this nickname", MouseFilter = Control.MouseFilterEnum.Pass });
            }

            line.AddChild(new Label { Text = $"{(seat.Host ? " (host)" : "")}{(i == state.You ? " - you" : "")}: {(seat.Ready ? "ready" : "not ready")}" });
            _seats.AddChild(line);
        }

        if (_settings.GetChildCount() == 0)
        {
            foreach (string s in state.Settings)
            {
                _settings.AddChild(new Label { Text = s, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(0.8f, 0.8f, 0.8f) });
            }
        }

        string chatKey = state.Chat.Count.ToString(CultureInfo.InvariantCulture) + "|" + (state.Chat.Count > 0 ? state.Chat[^1] : "");
        if (chatKey != _chatShown)
        {
            foreach (Node n in _chat.GetChildren())
            {
                n.QueueFree();
            }

            foreach (string c in state.Chat)
            {
                _chat.AddChild(new Label { Text = c, AutowrapMode = TextServer.AutowrapMode.WordSmart });
            }

            _chatShown = chatKey;
            Callable.From(() => _chatScroll.ScrollVertical = (int)_chatScroll.GetVScrollBar().MaxValue).CallDeferred();
        }

        bool host = state.You >= 0 && state.You < state.Seats.Count && state.Seats[state.You].Host;
        _ready.Visible = !host;
        _start.Visible = host;
        _start.Disabled = !state.CanStart;
        _start.TooltipText = state.CanStart ? "" : "Waiting for every player to be ready";
        const string alone = "Others join with your address and port. You may also start alone: the AI leads the other civilizations and free places can be taken later.";
        if (host && state.Seats.Count == 1)
        {
            _status.Text = alone;
        }
        else if (_status.Text == alone)
        {
            _status.Text = "";
        }
    }
}
