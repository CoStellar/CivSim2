using System;
using System.Globalization;
using System.Net.Sockets;
using System.Threading.Tasks;
using CivSim.Game.Ui;
using CivSim.Protocol.Transport;
using CivSim.Server;
using Godot;

namespace CivSim.Game;

/// <summary>
/// Multiplayer from the start menu (MP3): joining a game on another computer, hosting one (a lobby and a listener on this
/// computer), the lobby screen and then the game screen on the network connection.
/// </summary>
public partial class Main
{
    private LobbyScreen? _lobbyScreen;
    private Task<TcpConnection>? _connecting;
    private (string Address, int Port, string Password, string? Code)? _joining;

    // Command-line steps for automated checks of two windows (e.g. --mp-host 7777 --mp-start 2 / --mp-join 127.0.0.1:7777 --mp-ready).
    private string? _mpHost;
    private string? _mpJoin;
    private string? _mpName;
    private string? _mpChat;
    private bool _mpReady;
    private bool _mpWatch;
    private string? _mpKick;
    private int _mpAsk = -1;
    private int _mpStart;
    private int _mpSeats;
    private string? _mpLobbyShot;
    private double _mpLobbySeconds = 3;
    private string? _mpSay;
    private int _mpPropose = -1; // a speed to ask for in the game (0 pauses)
    private string? _mpLoad;
    private int _mpEye = -2; // a spectator watches through these eyes shortly before the screenshot (−1: the whole map)
    private bool _mpTable; // a spectator opens the table of civilizations shortly before the screenshot
    private string? _mpRelay; // --mp-host through this relay (MP6)

    // Reads the multiplayer arguments; true when one of them starts the run (no menu).
    private bool MultiplayerArguments(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string next = i + 1 < args.Length ? args[i + 1] : string.Empty;
            switch (args[i])
            {
                case "--mp-host":
                    _mpHost = next;
                    break;
                case "--mp-load":
                    _mpLoad = next;
                    break;
                case "--mp-eye":
                    _mpEye = int.Parse(next, CultureInfo.InvariantCulture);
                    break;
                case "--mp-table":
                    _mpTable = true;
                    break;
                case "--mp-relay":
                    _mpRelay = next;
                    break;
                case "--mp-join":
                    _mpJoin = next;
                    break;
                case "--mp-name":
                    _mpName = next;
                    break;
                case "--mp-chat":
                    _mpChat = next;
                    break;
                case "--mp-ready":
                    _mpReady = true;
                    break;
                case "--mp-watch":
                    _mpWatch = true;
                    break;
                case "--mp-kick":
                    _mpKick = next;
                    break;
                case "--mp-ask":
                    _mpAsk = int.Parse(next, CultureInfo.InvariantCulture);
                    break;
                case "--mp-seats":
                    _mpSeats = int.Parse(next, CultureInfo.InvariantCulture);
                    break;
                case "--mp-start":
                    _mpStart = int.Parse(next, CultureInfo.InvariantCulture);
                    break;
                case "--mp-lobby-screenshot":
                    _mpLobbyShot = next;
                    break;
                case "--mp-say":
                    _mpSay = next;
                    break;
                case "--mp-propose":
                    _mpPropose = int.Parse(next, CultureInfo.InvariantCulture);
                    break;
                case "--mp-lobby-seconds":
                    _mpLobbySeconds = double.Parse(next, CultureInfo.InvariantCulture);
                    break;
            }
        }

        if (_mpName is not null)
        {
            _saved.Start.Nickname = _mpName;
        }

        if (_mpHost is not null)
        {
            StartSettings s = _saved.Start;
            if (_mpRelay is not null)
            {
                s.HostThroughRelay = true;
                s.RelayAddress = _mpRelay;
            }

            int port = int.Parse(_mpHost, CultureInfo.InvariantCulture);
            PlayOptions options = _mpLoad is not null
                ? new PlayOptions(0) { LoadPath = System.IO.File.Exists(_mpLoad) ? _mpLoad : PlaySaves.PathOf(_mpLoad), HumanSeats = Math.Max(Math.Max(2, _mpStart), _mpSeats), StartSpeed = 1 }
                : NewGameFromSettings(s) with { HumanSeats = Math.Max(Math.Max(2, _mpStart), _mpSeats), StartSpeed = 1, Tutorial = false };
            Host(options, new LobbyConfig { GameName = s.HostGameName.Length > 0 ? s.HostGameName : "CivSim game" }, port);
            return true;
        }

        if (_mpJoin is not null)
        {
            int colon = _mpJoin.LastIndexOf(':');
            Join(_mpJoin[..colon], int.Parse(_mpJoin[(colon + 1)..], CultureInfo.InvariantCulture), "");
            return true;
        }

        return false;
    }

    // The game of New game, as the menu builds it (for --mp-host without the menu).
    private static PlayOptions NewGameFromSettings(StartSettings s) => StartMenu.NewGameOptions(s);

    private string Nickname() => _saved.Start.Nickname.Trim() is { Length: > 0 } n ? n : "Player";

    // Join: connect in the background (the window keeps drawing), then the lobby screen. With a room code the address is a relay's (MP6).
    private void Join(string address, int port, string password, string? code = null)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            StartMenu.Notice = code is null ? "Type the host's address." : "Type the relay's address.";
            ShowMenu();
            return;
        }

        _menu?.QueueFree();
        _menu = null;
        _joining = (address.Trim(), port, password, code);
        _connecting = code is null
            ? Task.Run(() => TcpConnection.Connect(address.Trim(), port, TimeSpan.FromSeconds(8)))
            : Task.Run(() => TcpConnection.JoinRelay(address.Trim(), port, code, TimeSpan.FromSeconds(8)));
    }

    // "relay.example.com" or "relay.example.com:7701" → address and port (7700 by default).
    private static (string Host, int Port) RelayTarget(string text)
    {
        string t = text.Trim();
        int colon = t.LastIndexOf(':');
        if (colon > 0 && t.IndexOf(':') == colon && int.TryParse(t[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is >= 1 and <= 65535)
        {
            return (t[..colon], port);
        }

        return (t, RelayWire.DefaultPort);
    }

    // Called every frame: a connection that came through opens the lobby screen.
    private void JoinProgress()
    {
        if (_connecting is not { IsCompleted: true } task || _joining is not { } j)
        {
            return;
        }

        _connecting = null;
        _joining = null;
        if (task.IsFaulted || task.IsCanceled)
        {
            string why = task.Exception?.InnerException is SocketException se ? se.Message : task.Exception?.InnerException?.Message ?? "no answer";
            StartMenu.Notice = task.Exception?.InnerException is RelayException ? why : $"Could not connect to {j.Address}:{j.Port}: {why}";
            ShowMenu();
            return;
        }

        string where = j.Address + ":" + j.Port.ToString(CultureInfo.InvariantCulture) + (j.Code is { } code ? ", code " + RelayWire.ShowCode(RelayWire.NormalizeCode(code) ?? code) : "");
        string returnKey = _saved.Start.LastServer == where ? _saved.Start.LastReturnKey : "";
        ShowLobby(task.Result, j.Password, returnKey, where, owned: null);
    }

    // Host: the lobby and the listener on this computer; the host joins its own lobby in memory.
    private void Host(PlayOptions options, LobbyConfig config, int port)
    {
        PlayLobby lobby;
        try
        {
            lobby = PlayLobby.Start(options, config, _dataDirectory);
        }
        catch (Exception e) when (e is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            StartMenu.Notice = "The game could not start: " + e.Message;
            ShowMenu();
            return;
        }

        PlayListener listener;
        try
        {
            listener = new PlayListener(lobby.Accept, port);
        }
        catch (SocketException e)
        {
            lobby.Dispose();
            StartMenu.Notice = $"Port {port.ToString(CultureInfo.InvariantCulture)} cannot be used ({e.Message}). Pick another port.";
            ShowMenu();
            return;
        }

        // Through a relay (MP6): a room with a code; nobody opens a port. The code is the first line of the lobby's settings.
        RelayHost? relay = null;
        if (_saved.Start.HostThroughRelay)
        {
            (string relayHost, int relayPort) = RelayTarget(_saved.Start.RelayAddress);
            try
            {
                if (relayHost.Length == 0)
                {
                    throw new RelayException("Type the relay's address, or let players join by your address.");
                }

                relay = new RelayHost(lobby.Accept, relayHost, relayPort, _saved.Start.RelayPassword, TimeSpan.FromSeconds(8));
            }
            catch (RelayException e)
            {
                listener.Dispose();
                lobby.Dispose();
                StartMenu.Notice = e.Message;
                ShowMenu();
                return;
            }

            lobby.Relay = $"Relay {relay.Address}, code {relay.Code}";
        }

        _menu?.QueueFree();
        _menu = null;
        var owned = new Owned(listener, lobby, relay);
        string where = relay is null ? "this computer, port " + listener.Port.ToString(CultureInfo.InvariantCulture) : $"relay {relay.Address}, code {relay.Code}";
        ShowLobby(lobby.Connect(), config.Password, "", where, owned);
    }

    private void ShowLobby(IConnection connection, string password, string returnKey, string where, IDisposable? owned)
    {
        StopGame();
        string name = Nickname();
        var screen = new LobbyScreen(connection, name, password, returnKey, where)
        {
            AutoReady = _mpReady,
            AutoStartWith = _mpStart,
            AutoChat = _mpChat,
            Screenshot = _mpLobbyShot,
            Watch = _mpWatch,
            ScreenshotAfter = _mpLobbySeconds,
        };
        _lobbyScreen = screen;
        screen.Started += (c, hello) =>
        {
            CloseLobbyScreen();
            StartNetworkPlay(c, name, password, hello, where, owned);
        };
        screen.Failed += why =>
        {
            CloseLobbyScreen();
            owned?.Dispose();
            StartMenu.Notice = "Multiplayer: " + why;
            CallDeferred(MethodName.ShowMenu);
        };
        screen.Left += () =>
        {
            CloseLobbyScreen();
            owned?.Dispose();
            CallDeferred(MethodName.ShowMenu);
        };
        AddChild(screen);
    }

    private void CloseLobbyScreen()
    {
        _lobbyScreen?.QueueFree();
        _lobbyScreen = null;
    }

    private void StartNetworkPlay(IConnection connection, string name, string password, bool hello, string where, IDisposable? owned)
    {
        _play = new Play.PlayGame(connection, name, password, hello, owned, _layers, _art, _dataDirectory);
        if (_playScreenshot is not null)
        {
            // In the game: a chat line and a speed proposal shortly before the screenshot (MP4 checks of two windows).
            var steps = new System.Collections.Generic.List<(double, Action<Play.PlayGame>)>();
            if (_mpSay is { } say)
            {
                steps.Add((_playSeconds - 10, g => g.ScriptChat(say)));
            }

            if (_mpPropose >= 0)
            {
                var speed = (CivSim.Protocol.Messages.SimulationSpeed)_mpPropose;
                steps.Add((_playSeconds - (_mpPropose == 0 ? 3 : 6), g => g.ScriptSpeed(speed)));
            }

            if (_mpKick is { } kick)
            {
                steps.Add((_playSeconds - 15, g => g.ScriptKick(kick)));
            }

            if (_mpAsk >= 0)
            {
                int ask = _mpAsk;
                steps.Add((_playSeconds - 6, g => g.ScriptAsk(ask)));
            }

            if (_mpEye >= -1)
            {
                int eye = _mpEye;
                steps.Add((_playSeconds - 8, g => g.ScriptEye(eye)));
            }

            if (_mpTable)
            {
                steps.Add((_playSeconds - 2, g => g.ScriptTable()));
            }

            _play.ScreenshotAfter(_playScreenshot, _playSeconds, steps);
        }

        _play.ReturnKeyReceived += key =>
        {
            // Kept so a lost connection can lead the same people again on the same server (MP2).
            _saved.Start.LastServer = where;
            _saved.Start.LastReturnKey = key;
            SaveSettings();
        };
        WirePlay();
        AddChild(_play);
    }

    // The listener, the relay room and the lobby (with its game) of a game this computer hosts, closed together with the game screen.
    private sealed class Owned(PlayListener listener, PlayLobby lobby, RelayHost? relay) : IDisposable
    {
        public void Dispose()
        {
            relay?.Dispose();
            listener.Dispose();
            lobby.Dispose();
        }
    }
}
