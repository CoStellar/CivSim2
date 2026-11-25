using System;
using System.Globalization;
using System.Linq;
using System.Text;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// A game with other players (multiplayer plan, MP4): who plays with what ping and pauses left, the vote going on with Yes and
/// No, who paused the game, and the chat to everyone or to our allies (key Y). Everything from other players is shown as plain
/// text, never as markup. Shown only while two or more players play.
/// </summary>
public partial class PlayGame
{
    // Colour of the suffix the game added to a repeated nickname (as in the lobby).
    private static readonly Color SuffixColour = new(0.55f, 0.8f, 1f);

    private Control? _mpRoot;   // the right column (advisor, alerts): the players' list goes first in it
    private Control? _mpScreen; // the whole screen: the vote and the chat
    private PanelContainer? _playersPanel;
    private VBoxContainer? _playersList;
    private PanelContainer? _votePanel;
    private Label? _voteText;
    private Button? _voteYes;
    private Button? _voteNo;
    private Label? _pauseText;
    private PanelContainer? _chatPanel;
    private VBoxContainer? _chatList;
    private ScrollContainer? _chatScroll;
    private LineEdit? _chatInput;
    private CheckButton? _chatAllies;
    private string _playersShown = "";
    private int _chatShown;

    // Called from the regular refresh of the interface.
    private void UpdateMultiplayer()
    {
        if (_mpRoot is null || _session is null)
        {
            return;
        }

        PlayPlayersMessage? m = _session.Players;
        if (m is null)
        {
            if (_playersPanel is not null)
            {
                _playersPanel.Visible = false;
                _votePanel!.Visible = false;
                _chatPanel!.Visible = _session.ChatLines.Count > 0; // the lines stay readable after the others left
            }

            return;
        }

        if (_playersPanel is null)
        {
            BuildMultiplayerUi(_mpRoot, _mpScreen!);
        }

        _playersPanel!.Visible = true;
        _chatPanel!.Visible = true;
        ShowPlayers(m);
        ShowVote(m);
        ShowChat();
    }

    private void BuildMultiplayerUi(Control column, Control root)
    {
        // Players: first in the right column, above the advisor.
        _playersPanel = new PanelContainer();
        column.AddChild(_playersPanel);
        column.MoveChild(_playersPanel, 0);
        var box = new VBoxContainer();
        _playersPanel.AddChild(box);
        box.AddChild(UiKit.Label("PLAYERS", 13, UiKit.Heading));
        _playersList = new VBoxContainer();
        box.AddChild(_playersList);
        _pauseText = UiKit.Label("", 13, UiKit.Warn, wrap: true);
        box.AddChild(_pauseText);

        // The vote: in the same panel, under the players, with Yes and No.
        _votePanel = new PanelContainer { Visible = false };
        _votePanel.AddThemeStyleboxOverride("panel", UiKit.Flat(new Color(0.2f, 0.16f, 0.08f, 0.9f), UiKit.Warn, 4, 1));
        box.AddChild(_votePanel);
        var vote = new VBoxContainer();
        _votePanel.AddChild(vote);
        _voteText = UiKit.Label("", 13, UiKit.Text, wrap: true);
        vote.AddChild(_voteText);
        var voteRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        voteRow.AddThemeConstantOverride("separation", 10);
        vote.AddChild(voteRow);
        _voteYes = UiKit.Button("Yes", () => CastVote(true), "Agree with the change");
        _voteNo = UiKit.Button("No", () => CastVote(false), "Keep it as it is");
        voteRow.AddChild(_voteYes);
        voteRow.AddChild(_voteNo);

        // The chat: at the right above the chronicle.
        _chatPanel = new PanelContainer { AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -UiKit.W(440), OffsetRight = -8, OffsetTop = ChronicleBottom - 330, OffsetBottom = ChronicleBottom - 122 };
        root.AddChild(_chatPanel);
        var chat = new VBoxContainer();
        _chatPanel.AddChild(chat);
        chat.AddChild(UiKit.Label("CHAT (Y)", 13, UiKit.Heading));
        _chatScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        chat.AddChild(_chatScroll);
        _chatList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _chatScroll.AddChild(_chatList);
        var line = new HBoxContainer();
        chat.AddChild(line);
        _chatInput = new LineEdit { PlaceholderText = "Say something (Enter)", MaxLength = PlayChatMessage.MaxLength, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _chatInput.TextSubmitted += _ => SendChat();
        line.AddChild(_chatInput);
        _chatAllies = new CheckButton { Text = "Allies", TooltipText = "Only our allies read it", FocusMode = Control.FocusModeEnum.None };
        line.AddChild(_chatAllies);
    }

    private void ShowPlayers(PlayPlayersMessage m)
    {
        var sig = new StringBuilder();
        foreach (PlayPlayerInfo p in m.Players)
        {
            sig.Append(CultureInfo.InvariantCulture, $"{p.Name}|{p.Civ}|{p.PingMs / 10}|{p.PausesLeft}|{p.Host};");
        }

        sig.Append(m.You).Append(m.PausedBy).Append(m.PauseProtectedSeconds);
        if (sig.ToString() == _playersShown)
        {
            return;
        }

        _playersShown = sig.ToString();
        foreach (Node n in _playersList!.GetChildren())
        {
            n.QueueFree();
        }

        for (int i = 0; i < m.Players.Count; i++)
        {
            PlayPlayerInfo p = m.Players[i];
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new ColorRect { Color = PlayOverlay.CivColor(p.Civ), CustomMinimumSize = new Vector2(10, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            RichTextLabel name = Parts(false, NameParts(p.Name, p.SuffixLength, i == m.You ? UiKit.Heading : UiKit.Text)
                .Append(((p.Host ? " (host)" : ""), UiKit.Muted)));
            name.TooltipText = p.Name + (p.SuffixLength > 0 ? "\nThe end of the nickname was added by the game: another player has the same one." : "");
            row.AddChild(name);
            Label ping = UiKit.Label(string.Create(CultureInfo.InvariantCulture, $"{p.PingMs} ms, {p.PausesLeft} pauses"), 13, p.PingMs > 250 ? UiKit.Warn : UiKit.Muted);
            ping.TooltipText = "Time a message takes there and back; pauses left";
            row.AddChild(ping);
            _playersList.AddChild(row);
        }

        _pauseText!.Text = m.PausedBy.Length == 0 ? ""
            : m.PauseProtectedSeconds > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Paused by {m.PausedBy}; others may end the pause in {m.PauseProtectedSeconds} s.")
                : $"Paused by {m.PausedBy}; anyone may end the pause.";
        _pauseText.Visible = _pauseText.Text.Length > 0;
    }

    private void ShowVote(PlayPlayersMessage m)
    {
        if (m.Vote is not { } v)
        {
            _votePanel!.Visible = false;
            return;
        }

        _votePanel!.Visible = true;
        string what = v.Kind == 0 ? string.Create(CultureInfo.InvariantCulture, $"speed x{v.Value}") : "a change";
        _voteText!.Text = string.Create(CultureInfo.InvariantCulture,
            $"{v.By} proposes {what}. Yes {v.Yes}, no {v.No} of {v.Eligible}; more than half must say yes. {v.SecondsLeft} s.");
        _voteYes!.Visible = v.CanVote && !v.Voted;
        _voteNo!.Visible = v.CanVote && !v.Voted;
    }

    private void CastVote(bool yes)
    {
        if (_session?.Players?.Vote is { } v)
        {
            _session.Vote(v.Id, yes);
            _voteYes!.Visible = false;
            _voteNo!.Visible = false;
        }
    }

    private void ShowChat()
    {
        if (_session is null || _chatList is null || _session.ChatLines.Count == _chatShown && _chatShown < ClientChatKept)
        {
            return;
        }

        foreach (Node n in _chatList.GetChildren())
        {
            n.QueueFree();
        }

        foreach (PlayChatLineMessage l in _session.ChatLines.Skip(Math.Max(0, _session.ChatLines.Count - 30)))
        {
            _chatList.AddChild(ChatLine(l));
        }

        _chatShown = _session.ChatLines.Count;
        Callable.From(() => _chatScroll!.ScrollVertical = (int)_chatScroll.GetVScrollBar().MaxValue).CallDeferred();
    }

    private const int ClientChatKept = Net.ClientSession.ChatKept;

    // One line: the game's own lines in grey; a player's name in the colour of the civilization, the suffix apart, then the text.
    private static Control ChatLine(PlayChatLineMessage l)
    {
        if (l.From.Length == 0)
        {
            return UiKit.Label(l.Text, 13, UiKit.Muted, wrap: true);
        }

        return Parts(true, NameParts(l.From, l.SuffixLength, PlayOverlay.CivColor(l.FromCiv).Lightened(0.3f))
            .Prepend((l.Allies ? "[allies] " : "", UiKit.Warn))
            .Append((": " + l.Text, UiKit.Text)));
    }

    // A nickname in parts: what the player chose, and in another colour the end the game added to tell two players apart.
    private static System.Collections.Generic.IEnumerable<(string Text, Color Colour)> NameParts(string name, int suffix, Color colour)
    {
        int cut = name.Length - Math.Clamp(suffix, 0, name.Length);
        yield return (name[..cut], colour);
        yield return (name[cut..], SuffixColour);
    }

    // Text in colours, added as plain text (never read as markup: players write it). Wrapped (chat) or cut at the edge (list).
    private static RichTextLabel Parts(bool wrap, System.Collections.Generic.IEnumerable<(string Text, Color Colour)> parts)
    {
        var t = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollActive = false,
            FitContent = wrap,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Pass,
            CustomMinimumSize = new Vector2(0, wrap ? 0 : Ui.PixelTheme.FontSize + 8),
        };
        t.AddThemeFontSizeOverride("normal_font_size", Ui.PixelTheme.FontSize);
        foreach ((string text, Color colour) in parts)
        {
            if (text.Length > 0)
            {
                t.PushColor(colour);
                t.AddText(text);
                t.Pop();
            }
        }

        return t;
    }

    private void SendChat()
    {
        if (_chatInput is null || _session is null)
        {
            return;
        }

        string text = _chatInput.Text.Trim();
        if (text.Length > 0)
        {
            _session.Chat(text, _chatAllies?.ButtonPressed == true);
        }

        _chatInput.Text = "";
        _chatInput.ReleaseFocus();
    }

    // While we write in the chat, the letters do not move the map.
    private bool ChatTyping => _chatInput?.HasFocus() == true;

    // Key Y: write in the chat (only in a game with others).
    private bool FocusChat()
    {
        if (_chatInput is null || _chatPanel is not { Visible: true })
        {
            return false;
        }

        _chatInput.GrabFocus();
        return true;
    }
}
