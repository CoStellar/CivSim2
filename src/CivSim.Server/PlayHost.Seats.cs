using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Sim.State;

namespace CivSim.Server;

/// <summary>
/// Who may lead which civilization in a game with other players (multiplayer plan, chapter 19; MP4b): a newcomer in a running game
/// waits as a candidate (no map, only the civilizations it may ask for) or watches the whole map as an observer; the players vote
/// on a candidate and on removing a player; the civilization of a removed player goes to the AI and nobody else may take it.
/// </summary>
public sealed partial class PlayHost
{
    /// <summary>Kind of a vote: a candidate asks to lead a civilization.</summary>
    public const int VoteJoin = PlayVoteInfo.Join;

    /// <summary>Kind of a vote: a player is to be removed.</summary>
    public const int VoteKick = PlayVoteInfo.Kick;

    // Civilizations of removed players: the AI leads them and nobody else may take them (R3-9).
    private readonly HashSet<int> _locked = [];

    // Return keys of removed players and their civilizations: they come back as candidates for it, by a vote.
    private readonly Dictionary<string, int> _removed = new(StringComparer.Ordinal);

    private Civ? _watcher;

    // The nickname of the player of each civilization, kept also while the player is away (the save keeps the places, MP5).
    private readonly Dictionary<int, string> _seatNames = [];

    // Who leads which civilization now, and whose civilization waits for a player away (their return key still holds it).
    private List<SavedSeat> SeatsNow()
    {
        var seats = new List<SavedSeat>();
        foreach ((string key, int civ) in _returnKeys.OrderBy(k => k.Value))
        {
            PlayClient? now = _clients.Find(c => c.Civ == civ && c.Connection.IsOpen);
            string name = now?.Name ?? _seatNames.GetValueOrDefault(civ) ?? "Player";
            seats.Add(new SavedSeat(civ, _game.Civs[civ].Name, name, key, now?.Host == true));
        }

        return seats;
    }

    /// <summary>The return key of the player of a civilization (tests).</summary>
    internal string ReturnKeyOf(int civ) => _returnKeys.FirstOrDefault(k => k.Value == civ).Key ?? "";

    private static void Reject(PlayClient client, string? why)
    {
        if (why is not null)
        {
            client.Connection.Send(new CommandRejectedMessage(0, why));
        }
    }

    private int RoleOf(PlayClient c) =>
        c.Civ >= 0 && _game.Civs[c.Civ].Dead ? PlayPlayerInfo.Fallen : c.Role;

    // A newcomer that leads nothing yet: a candidate (no map) or an observer (the whole map, for good in this game).
    private void Wait(PlayClient client, HelloMessage hello, int role)
    {
        Name(client, hello);
        client.Role = role;
        client.Connection.Send(new WelcomeMessage(ProtocolInfo.Version, WelcomeMessage.NoCiv, WelcomeMessage.NoCiv));
        SendContent(client);
        client.SentContent = true;
        _playersDirty = true;
        Announce(role == PlayPlayerInfo.Observer
            ? $"{client.Name} watches the game."
            : client.Removed is not null
                ? $"{client.Name} is back and asks to lead their civilization again."
                : $"{client.Name} joined and waits to lead a civilization; the players vote when they ask for one.");
    }

    // A candidate gives up waiting and watches the whole map; it cannot lead a civilization in this game any more (R3-7).
    private void Watch(PlayClient client)
    {
        if (_vote?.Target == client)
        {
            _vote = null;
        }

        client.Role = PlayPlayerInfo.Observer;
        client.Removed = null;
        _playersDirty = true;
        Announce($"{client.Name} watches the game.");
    }

    // Civilizations the AI leads that a candidate may ask for: not fallen, not kept for a player who lost the connection, not of a
    // removed player, and only while there is a place for one more player.
    private List<PlayTakeableCiv> Takeable()
    {
        var list = new List<PlayTakeableCiv>();
        if (_options.HumanSeats > 0 && _clients.Count(c => c.Civ >= 0) >= _options.HumanSeats)
        {
            return list;
        }

        for (int c = 0; c < _game.Civs.Count; c++)
        {
            if (MayTake(c))
            {
                list.Add(Takeable(c));
            }
        }

        return list;
    }

    private PlayTakeableCiv Takeable(int civ)
    {
        Civ c = _game.Civs[civ];
        return new PlayTakeableCiv(civ, c.Name, c.Settlements.Count, c.People);
    }

    private bool MayTake(int civ) =>
        civ >= 0 && civ < _game.Civs.Count && _game.Civs[civ].IsAi && !_game.Civs[civ].Dead && !_locked.Contains(civ)
        && !_returnKeys.ContainsValue(civ) && !_clients.Exists(x => x.Civ == civ);

    // Players who vote on players: those leading a civilization still in the game for a while (R4-1); the one the vote is about
    // too, if it qualifies (R4-2). When nobody has played long enough yet, every player votes (otherwise nobody could).
    private HashSet<int> PlayerVoters()
    {
        long now = Clock();
        List<PlayClient> players = Playing().FindAll(c => !_game.Civs[c.Civ].Dead);
        List<PlayClient> long_ = players.FindAll(c => now - c.Since >= Seconds(Rules.VoteAfterSeconds));
        return [.. (long_.Count > 0 ? long_ : players).Select(c => c.Civ)];
    }

    // A candidate asks to lead a civilization: the players vote (30 s). With no player to vote, it leads it at once.
    private string? AskToLead(PlayClient client, int civ)
    {
        bool removed = client.Removed is { } key && _removed.TryGetValue(key, out int old);
        if (removed && civ != _removed[client.Removed!])
        {
            return "You may ask only for the civilization you led.";
        }

        if (!removed && !Takeable().Exists(t => t.Civ == civ))
        {
            return "That civilization cannot be taken: another player leads it, keeps it for a return, or it is gone.";
        }

        HashSet<int> voters = PlayerVoters();
        if (voters.Count == 0)
        {
            Admit(client, civ);
            return null;
        }

        if (_vote is not null)
        {
            return "A vote is on already: ask again when it ends.";
        }

        _vote = new Vote(_nextVote++, VoteJoin, civ, client.Name ?? "Player", Clock() + Seconds(Rules.PlayerVoteSeconds), voters)
        {
            About = _game.Civs[civ].Name, // the name of a civilization: the announcement says it to everyone anyway
            Target = client,
        };
        _playersDirty = true;
        Announce($"{client.Name} asks to lead {_game.Civs[civ].Name}. Vote in the players' list ({Rules.PlayerVoteSeconds} s).");
        return null;
    }

    // A candidate taken in: the civilization is its own; a removed player's civilization opens again.
    private void Admit(PlayClient client, int civ)
    {
        if (client.Removed is { } key)
        {
            _removed.Remove(key);
            _locked.Remove(civ);
            _returnKeys[key] = civ; // the same return key as before
            client.Removed = null;
        }

        Seat(client, civ);
    }

    // Removes a player: the host at once, another player by a vote (30 s; the proposer says yes, the one concerned votes too).
    private string? Kick(PlayClient from, string name)
    {
        PlayClient? target = Present().Find(c => c.Name == name);
        if (target is null)
        {
            return "No one of that name is in the game.";
        }

        if (target == from)
        {
            return "Leave the game from the menu instead.";
        }

        if (target.Host)
        {
            return "The host cannot be removed.";
        }

        if (from.Host)
        {
            Remove(target, "by the host");
            return null;
        }

        if (target.Civ < 0)
        {
            return "Only the host removes candidates and observers.";
        }

        HashSet<int> voters = PlayerVoters();
        if (!voters.Contains(from.Civ))
        {
            return $"You may propose this after {Rules.VoteAfterSeconds / 60} minutes in the game.";
        }

        if (_vote is not null)
        {
            return "A vote is on already: wait for its end.";
        }

        long now = Clock();
        if (_proposedAt.TryGetValue(from.Civ, out long at) && now - at < Seconds(Rules.ProposeCooldownSeconds))
        {
            return $"You may propose a change again in {(Seconds(Rules.ProposeCooldownSeconds) - (now - at) + System.Diagnostics.Stopwatch.Frequency - 1) / System.Diagnostics.Stopwatch.Frequency} s.";
        }

        _proposedAt[from.Civ] = now;
        _vote = new Vote(_nextVote++, VoteKick, target.Civ, from.Name ?? "Player", now + Seconds(Rules.PlayerVoteSeconds), voters)
        {
            About = target.Name ?? "Player",
            Target = target,
        };
        _vote.Votes[from.Civ] = true;
        _playersDirty = true;
        Announce($"{from.Name} proposes to remove {target.Name}. Vote in the players' list ({Rules.PlayerVoteSeconds} s).");
        return null;
    }

    // The player goes; the AI leads its civilization, which nobody else may take; it may come back as a candidate (R3-9).
    private void Remove(PlayClient target, string how)
    {
        int civ = target.Civ;
        target.Civ = -1; // not "left": removed
        if (civ >= 0)
        {
            string? key = _returnKeys.FirstOrDefault(k => k.Value == civ).Key;
            if (key is not null)
            {
                _returnKeys.Remove(key);
                _removed[key] = civ;
            }

            _locked.Add(civ);
            if (_pausedBy == target && _speed == SimulationSpeed.Paused)
            {
                _speed = _speedBeforePause; // the pause of a removed player ends with it
                _pausedBy = null;
            }

            SetLeader(civ, player: false);
        }

        if (_vote?.Target == target)
        {
            _vote = null;
        }

        Drop(target, "You were removed from the game. Join again to ask the players to take you back.");
        _playersDirty = true;
        Announce(civ >= 0
            ? $"{target.Name} was removed {how}; the AI leads {_game.Civs[civ].Name} and no one else may take it."
            : $"{target.Name} was removed {how}.");
    }

    private void ResolvePlayer(Vote v, bool passed, int yes)
    {
        PlayClient? target = v.Target;
        string count = $"{yes} of {v.Eligible.Count} said yes";
        if (v.Kind == VoteJoin)
        {
            bool still = target is { Role: PlayPlayerInfo.Candidate } && target.Connection.IsOpen
                && (target.Removed is not null ? _removed.ContainsValue(v.Value) : MayTake(v.Value));
            if (passed && still)
            {
                Announce($"Taken in: {count}.");
                Admit(target!, v.Value);
            }
            else
            {
                Announce(passed ? $"{v.By} cannot lead {v.About} any more." : $"{v.By} is not taken in: {count}.");
            }
        }
        else if (v.Kind == VoteKick)
        {
            if (passed && target is { Civ: >= 0 } && target.Connection.IsOpen)
            {
                Remove(target, $"by a vote ({count})");
            }
            else
            {
                Announce($"{v.About} stays: {count}.");
            }
        }
    }

    // A civilization of no one that knows the whole map and sees everywhere: the views of observers and of fallen players
    // (P6, R3-7). It is not in the game, so the game's state and hash do not change.
    private Civ Watcher()
    {
        if (_watcher is not null)
        {
            return _watcher;
        }

        int cells = _game.Geo.CellCount;
        var w = new Civ(-1, "Observers", _game.Content.Goods.Count)
        {
            Known = Enumerable.Repeat(true, cells).ToArray(),
            RockSeen = new byte[cells],
            SoilSeen = Enumerable.Repeat(true, cells).ToArray(),
            Vision = Enumerable.Repeat((short)1, cells).ToArray(),
            SeenOwner = Enumerable.Repeat(CivSim.Core.Map.WorldMap.NoOwner, cells).ToArray(),
            SeenRoad = new byte[cells],
            SeenWorks = new ushort[cells],
            KnownTech = Enumerable.Repeat(true, _game.Content.Techs.Count).ToArray(),
            KindKills = new int[_game.Content.Units.Count],
            KindLevel = new int[_game.Content.Units.Count],
            KindPending = new int[_game.Content.Units.Count],
            KindPicks = new int[_game.Content.Units.Count * Math.Max(1, _game.Content.Veterans.Options.Length)],
            Experience = new long[_game.Content.Techs.Count],
            ExperienceRate = new int[_game.Content.Techs.Count],
            Dead = true,
        };
        _watcher = w;
        return w;
    }
}
