using System.Collections.Generic;
using CivSim.Protocol.Messages;

namespace CivSim.Game.Net;

/// <summary>
/// The client's record of every civilization over time (stage 5 statistics), built from the views the server sends
/// to observers. Fixed memory: when full, every second sample is dropped, so old times are kept more sparsely
/// (CLAUDE.md rule 7, history with constant memory).
/// </summary>
public sealed class CivHistory
{
    /// <summary>Samples kept at most.</summary>
    public const int Capacity = 600;

    private readonly List<Sample> _samples = [];
    private int _stride = 1;
    private int _skipped;

    /// <summary>One moment: turn, year and per civilization id its people, settlements and technologies.</summary>
    public sealed record Sample(long Turn, long Year, Dictionary<int, (long People, int Settlements, int Technologies)> Civs);

    /// <summary>Samples, oldest first.</summary>
    public IReadOnlyList<Sample> Samples => _samples;

    /// <summary>Records a view (only views with observer statistics count).</summary>
    public void Add(GameViewMessage view)
    {
        if (view.Civilizations.Count == 0 || view.Civilizations[0].People < 0)
        {
            return;
        }

        if (++_skipped < _stride && _samples.Count > 0)
        {
            return;
        }

        _skipped = 0;
        var civs = new Dictionary<int, (long, int, int)>();
        foreach (CivilizationView c in view.Civilizations)
        {
            civs[c.Id] = (c.People, c.Settlements, c.Technologies);
        }

        _samples.Add(new Sample(view.Turn, view.Month / 12, civs));
        if (_samples.Count >= Capacity)
        {
            for (int i = _samples.Count - 2; i > 0; i -= 2)
            {
                _samples.RemoveAt(i);
            }

            _stride *= 2;
        }
    }
}
