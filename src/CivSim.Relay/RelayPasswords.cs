using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CivSim.Relay;

/// <summary>
/// Passwords to open games on the relay (multiplayer plan, R2-3 and R3-3): a file with one password per line, each with an optional
/// last day (<c>otter 2026-12-31</c>); lines starting with <c>#</c> are notes. The file is read again when it changes (looked at once a
/// minute at most), so a new password needs no restart; an old one still works <see cref="RelayConfig.GraceDays"/> days after its last day.
/// </summary>
public sealed class RelayPasswords
{
    private static readonly TimeSpan LookEvery = TimeSpan.FromMinutes(1);

    private readonly string _path;
    private readonly int _graceDays;
    private readonly Func<DateTime> _now;
    private readonly Action<string> _log;
    private List<(byte[] Password, DateTime? LastDay)> _entries = [];
    private DateTime _looked = DateTime.MinValue;
    private DateTime _written = DateTime.MinValue;

    /// <summary>Passwords from <paramref name="path"/>; an empty path lets anyone open games.</summary>
    public RelayPasswords(string path, int graceDays, Func<DateTime> now, Action<string> log)
    {
        _path = path ?? "";
        _graceDays = graceDays;
        _now = now;
        _log = log;
    }

    /// <summary>True when anyone may open games (no file set).</summary>
    public bool Open => _path.Length == 0;

    /// <summary>Passwords that work today.</summary>
    public int Working
    {
        get
        {
            lock (this)
            {
                Refresh();
                DateTime today = _now().Date;
                return _entries.FindAll(e => Works(e.LastDay, today)).Count;
            }
        }
    }

    /// <summary>True when <paramref name="typed"/> may open a game today (compared in constant time).</summary>
    public bool Check(string typed)
    {
        if (Open)
        {
            return true;
        }

        byte[] t = Encoding.UTF8.GetBytes(typed ?? "");
        lock (this)
        {
            Refresh();
            DateTime today = _now().Date;
            bool ok = false;
            foreach ((byte[] password, DateTime? lastDay) in _entries)
            {
                ok |= CryptographicOperations.FixedTimeEquals(password, t) && Works(lastDay, today);
            }

            return ok && t.Length > 0;
        }
    }

    private bool Works(DateTime? lastDay, DateTime today) => lastDay is not { } last || today <= last.AddDays(_graceDays);

    private void Refresh()
    {
        DateTime now = _now();
        if (now - _looked < LookEvery)
        {
            return;
        }

        _looked = now;
        try
        {
            DateTime written = File.GetLastWriteTimeUtc(_path);
            if (!File.Exists(_path))
            {
                if (_entries.Count > 0 || _written == DateTime.MinValue)
                {
                    _log($"No passwords file {_path}: nobody can open games until it exists.");
                }

                _entries = [];
                _written = DateTime.MaxValue;
                return;
            }

            if (written == _written)
            {
                return;
            }

            var entries = new List<(byte[], DateTime?)>();
            foreach (string raw in File.ReadAllLines(_path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                DateTime? lastDay = null;
                if (parts.Length >= 2)
                {
                    if (!DateTime.TryParseExact(parts[^1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day))
                    {
                        _log($"Passwords file: \"{parts[^1]}\" is not a day like 2026-12-31; that line is left out.");
                        continue;
                    }

                    lastDay = day;
                }

                entries.Add((Encoding.UTF8.GetBytes(parts[0]), lastDay));
            }

            _entries = entries;
            _written = written;
            _log($"Passwords file read: {entries.Count} passwords, {entries.FindAll(e => Works(e.Item2, now.Date)).Count} work today.");
        }
        catch (IOException e)
        {
            _log("The passwords file could not be read: " + e.Message);
        }
        catch (UnauthorizedAccessException e)
        {
            _log("The passwords file could not be read: " + e.Message);
        }
    }
}
