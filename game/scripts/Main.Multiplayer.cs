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
    private (string Address, int Port, string Password)? _joining;

    // Command-line steps for automated checks of two windows (e.g. --mp-host 7777 --mp-start 2 / --mp-join 127.0.0.1:7777 --mp-ready).
    private string? _mpHost;
    private string? _mpJoin;
    private string? _mpName;
    private string? _mpChat;
    private bool _mpReady;
    private int _mpStart;
    private string? _mpLobbyShot;
    private double _mpLobbySeconds = 3;
    private string? _mpSay;
    private int _mpPropose = -1; // a speed to ask for in the game (0 pauses)

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
            int port = int.Parse(_mpHost, CultureInfo.InvariantCulture);
            PlayOptions options = NewGameFromSettings(s) with { HumanSeats = Math.Max(2, _mpStart), StartSpeed = 1, Tutorial = false };
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

    // Join: connect in the background (the window keeps drawing), then the lobby screen.
    private void Join(string address, int port, string password)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            StartMenu.Notice = "Type the host's address.";
            ShowMenu();
            return;
        }

        _menu?.QueueFree();
        _menu = null;
        _joining = (address.Trim(), port, password);
        _connecting = Task.Run(() => TcpConnection.Connect(address.Trim(), port, TimeSpan.FromSeconds(8)));
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
            StartMenu.Notice = $"Could not connect to {j.Address}:{j.Port}: {why}";
            ShowMenu();
            return;
        }

        string where = j.Address + ":" + j.Port.ToString(CultureInfo.InvariantCulture);
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

        _menu?.QueueFree();
        _menu = null;
        var owned = new Owned(listener, lobby);
        ShowLobby(lobby.Connect(), config.Password, "", "this computer, port " + listener.Port.ToString(CultureInfo.InvariantCulture), owned);
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

    // The listener and the lobby (with its game) of a game this computer hosts, closed together with the game screen.
    private sealed class Owned(PlayListener listener, PlayLobby lobby) : IDisposable
    {
        public void Dispose()
        {
            listener.Dispose();
            lobby.Dispose();
        }
    }
}
