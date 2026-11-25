using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Sim.Content;
using CivSim.Sim.State;

namespace CivSim.Server;

/// <summary>
/// A game with other players (multiplayer plan, MP4): who plays with what ping, the chat to everyone or to allies, the vote on
/// the speed and the pauses everyone sees. Runs on the game's thread, from <see cref="Pump"/>.
/// </summary>
public sealed partial class PlayHost
{
    /// <summary>Kind of a vote: the speed of the game.</summary>
    public const int VoteSpeed = 0;

    private long _lastPing;
    private long _lastPlayers;
    private bool _playersDirty;
    private int _shownPlayers;
    private Vote? _vote;
    private int _nextVote = 1;
    private SimulationSpeed _speedBeforePause = SimulationSpeed.X1;
    private readonly Dictionary<int, long> _proposedAt = [];

    /// <summary>The speed now (tests).</summary>
    internal SimulationSpeed Speed => _speed;

    private MultiplayerRules Rules => _game.Content.Multiplayer;

    private long Seconds(int s) => s * Stopwatch.Frequency;

    // Players taking part now, in the order they came.
    private List<PlayClient> Playing() => _clients.FindAll(c => c.Civ >= 0 && c.Connection.IsOpen);

    // Every round of Pump: pings, the end of a vote, the list of players.
    private void Social()
    {
        long now = Clock();
        List<PlayClient> playing = Playing();
        if (now - _lastPing >= Seconds(Rules.PingEverySeconds))
        {
            _lastPing = now;
            foreach (PlayClient c in playing)
            {
                c.Connection.Send(new PlayPingMessage(now));
            }
        }

        if (_vote is { } vote && (now >= vote.Until || vote.Votes.Count >= vote.Eligible.Count))
        {
            Resolve(vote);
        }

        // The list goes to everyone while two or more play, and once more when it drops below (so the panel can close).
        bool show = playing.Count > 1 || _shownPlayers > 1;
        if (show && (_playersDirty || now - _lastPlayers >= Stopwatch.Frequency))
        {
            _lastPlayers = now;
            _playersDirty = false;
            _shownPlayers = playing.Count;
            SendPlayers(playing, now);
        }
    }

    private void SendPlayers(List<PlayClient> playing, long now)
    {
        PlayPlayerInfo[] players = [.. playing.Select(c => new PlayPlayerInfo(c.Name ?? "Player", c.SuffixLength, c.Civ, c.PingMs,
            Math.Max(0, PausesPerPlayer - _pausesUsed.GetValueOrDefault(c.Civ)), c.Host))];
        string pausedBy = _speed == SimulationSpeed.Paused && _pausedBy is not null && playing.Count > 1 ? _pausedBy.Name ?? "Player" : "";
        int protectedFor = pausedBy.Length == 0 ? 0 : Math.Max(0, (int)Math.Ceiling((PauseProtection - Stopwatch.GetElapsedTime(_pausedAt)).TotalSeconds));
        for (int i = 0; i < playing.Count; i++)
        {
            PlayClient c = playing[i];
            PlayVoteInfo? vote = _vote is { } v
                ? new PlayVoteInfo(v.Id, v.Kind, v.Value, v.By, (int)Math.Max(0, (v.Until - now + Stopwatch.Frequency - 1) / Stopwatch.Frequency),
                    v.Votes.Count(x => x.Value), v.Votes.Count(x => !x.Value), v.Eligible.Count, v.Eligible.Contains(c.Civ), v.Votes.ContainsKey(c.Civ))
                : null;
            c.Connection.Send(new PlayPlayersMessage(players, vote, pausedBy, protectedFor, i));
        }
    }

    // A line of the game itself (votes, pauses, players coming and going) to everyone playing.
    private void Announce(string text)
    {
        foreach (PlayClient c in Playing())
        {
            c.Connection.Send(new PlayChatLineMessage("", 0, -1, false, text));
        }
    }

    private void Chat(PlayClient from, PlayChatMessage chat)
    {
        string text = Nicknames.Clean(chat.Text, PlayChatMessage.MaxLength);
        if (text.Length == 0 || from.Civ < 0)
        {
            return;
        }

        if (!from.MayChat(Clock(), Rules.ChatPerSecond, Rules.ChatBurst))
        {
            from.Connection.Send(new CommandRejectedMessage(0, "Not so fast: wait a moment before the next line."));
            return;
        }

        List<PlayClient> to = chat.Allies
            ? Playing().FindAll(c => c == from || (c.Civ != from.Civ && _game.Has(from.Civ, c.Civ, TreatyKind.Alliance)))
            : Playing();
        if (chat.Allies && to.Count == 1)
        {
            from.Connection.Send(new CommandRejectedMessage(0, "No ally of ours has a player to write to."));
            return;
        }

        var line = new PlayChatLineMessage(from.Name ?? "Player", from.SuffixLength, from.Civ, chat.Allies, text);
        foreach (PlayClient c in to)
        {
            c.Connection.Send(line);
        }
    }

    private void Pong(PlayClient client, PlayPingMessage ping)
    {
        long now = Clock();
        if (ping.Stamp <= now && now - ping.Stamp < Seconds(60))
        {
            client.PingMs = (int)((now - ping.Stamp) * 1000 / Stopwatch.Frequency);
        }
    }

    // With other players a change of speed is put to the vote (multiplayer plan, chapter 11); the one who proposes says yes.
    private string? ProposeSpeed(PlayClient client, SimulationSpeed speed)
    {
        if (speed == _speed)
        {
            return null;
        }

        if (_vote is not null)
        {
            return "A vote is on already: wait for its end.";
        }

        long now = Clock();
        if (_proposedAt.TryGetValue(client.Civ, out long at) && now - at < Seconds(Rules.ProposeCooldownSeconds))
        {
            return $"You may propose a change again in {(Seconds(Rules.ProposeCooldownSeconds) - (now - at) + Stopwatch.Frequency - 1) / Stopwatch.Frequency} s.";
        }

        _proposedAt[client.Civ] = now;
        var eligible = new HashSet<int>(Playing().Where(c => !_game.Civs[c.Civ].Dead).Select(c => c.Civ));
        _vote = new Vote(_nextVote++, VoteSpeed, (int)speed, client.Name ?? "Player", now + Seconds(Rules.SpeedVoteSeconds), eligible);
        _vote.Votes[client.Civ] = true;
        _playersDirty = true;
        Announce($"{client.Name} proposes speed x{(int)speed}. Vote in the players' list, top right ({Rules.SpeedVoteSeconds} s).");
        return null;
    }

    private void Cast(PlayClient client, PlayVoteMessage cast)
    {
        if (_vote is { } v && v.Id == cast.Id && v.Eligible.Contains(client.Civ) && !v.Votes.ContainsKey(client.Civ))
        {
            v.Votes[client.Civ] = cast.Yes;
            _playersDirty = true;
        }
    }

    // More than half of those who may vote must say yes; a tie and no vote are no (decision R4-2).
    private void Resolve(Vote v)
    {
        _vote = null;
        _playersDirty = true;
        int yes = v.Votes.Count(x => x.Value);
        bool passed = yes * 2 > v.Eligible.Count;
        if (v.Kind == VoteSpeed)
        {
            var speed = (SimulationSpeed)v.Value;
            if (passed)
            {
                if (_speed == SimulationSpeed.Paused)
                {
                    _speedBeforePause = speed; // the game goes on at it after the pause
                }
                else
                {
                    _speed = speed;
                }
            }

            Announce(passed
                ? $"Speed x{v.Value}: {yes} of {v.Eligible.Count} said yes."
                : $"The speed stays: only {yes} of {v.Eligible.Count} said yes.");
        }
    }

    private sealed class Vote(int id, int kind, int value, string by, long until, HashSet<int> eligible)
    {
        public int Id { get; } = id;

        public int Kind { get; } = kind;

        public int Value { get; } = value;

        public string By { get; } = by;

        public long Until { get; } = until;

        public HashSet<int> Eligible { get; } = eligible;

        public Dictionary<int, bool> Votes { get; } = [];
    }
}
