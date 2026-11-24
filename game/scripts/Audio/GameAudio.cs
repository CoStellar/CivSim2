using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Game.Map;
using CivSim.Game.Net;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Audio;

/// <summary>
/// Sound of a running game (child of PlayGame): watches the player's view and the commands sent and tells the
/// <see cref="AudioManager"/> what to play. Music follows the era, ambient follows what the camera looks at (terrain,
/// season, our settlements near, our units fighting), effects follow commands and what changed since the last view.
/// It only reads the view the client already has, so it changes nothing in the game.
/// </summary>
public partial class GameAudio : Node
{
    // Alert kinds (PlayGame.AlertKinds): 0 under attack and 1 enemies near sound the horn; the rest a soft bell.
    private static readonly int[] DangerAlerts = [0, 1];

    private const double AmbientEvery = 0.5;

    private readonly ClientSession _session;
    private readonly Func<Camera2D> _camera;
    private readonly Dictionary<int, int> _buildingState = [];
    private readonly Dictionary<int, bool> _atWar = [];
    private readonly HashSet<int> _alertKinds = [];
    private PlayViewMessage? _last;
    private bool _primed;
    private int _era = -1;
    private int _known;
    private int _settlements;
    private bool _ended;
    private double _ambientIn;

    /// <summary>Sound for a game seen through <paramref name="session"/>; the camera tells where the player looks.</summary>
    public GameAudio(ClientSession session, Func<Camera2D> camera)
    {
        _session = session;
        _camera = camera;
        _session.PlaySent += CommandSent;
        _session.CommandRefused += Refused;
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _session.PlaySent -= CommandSent;
        _session.CommandRefused -= Refused;
        if (AudioManager.Instance is { } audio)
        {
            audio.ClearAmbient();
            audio.SetMusic("menu");
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (AudioManager.Instance is not { } audio || _session.PlayView is not { } v)
        {
            return;
        }

        if (!ReferenceEquals(v, _last))
        {
            _last = v;
            Compare(audio, v);
        }

        _ambientIn -= delta;
        if (_ambientIn <= 0)
        {
            _ambientIn = AmbientEvery;
            Ambient(audio, v);
        }
    }

    // What changed since the last view: era, research, buildings, settlements, wars, alerts, the end.
    private void Compare(AudioManager audio, PlayViewMessage v)
    {
        int known = v.Techs.Count(t => t.Known);
        int settlements = v.Settlements.Count(s => s.Civ == v.Civ);
        bool first = !_primed;
        _primed = true;
        if (v.Era != _era)
        {
            if (!first && v.Era > _era)
            {
                AudioManager.Play("era_up");
            }

            _era = v.Era;
            audio.SetMusic(EraKey(v.Era));
        }
        else if (!first && known > _known)
        {
            AudioManager.Play("research_done");
        }

        _known = known;
        if (!first && settlements > _settlements && !v.Picking)
        {
            AudioManager.Play("settlement_founded");
        }

        _settlements = settlements;
        foreach (PlayBuildingView b in v.Buildings)
        {
            if (b.Civ != v.Civ || b.Remembered)
            {
                continue;
            }

            if (!first && _buildingState.TryGetValue(b.Id, out int was) && was is 0 or 1 && b.State is 2 or 3)
            {
                AudioManager.Play("build_done");
            }

            _buildingState[b.Id] = b.State;
        }

        foreach (PlayPeopleView p in v.Peoples)
        {
            if (!first && _atWar.TryGetValue(p.Civ, out bool war) && war != p.AtWar && !p.Dead)
            {
                AudioManager.Play(p.AtWar ? "war_declared" : "peace");
            }

            _atWar[p.Civ] = p.AtWar;
        }

        var kinds = (v.Alerts ?? []).Select(a => a.Kind).ToHashSet();
        foreach (int kind in kinds.Where(k => !_alertKinds.Contains(k)))
        {
            if (!first)
            {
                AudioManager.Play(DangerAlerts.Contains(kind) ? "alert_danger" : "alert_notice");
            }
        }

        _alertKinds.Clear();
        _alertKinds.UnionWith(kinds);
        if (!_ended && (v.Won || v.Lost))
        {
            _ended = true;
            audio.StopMusic();
            audio.ClearAmbient();
            AudioManager.Play(v.Won ? "victory" : "defeat");
        }
    }

    // The music key of an era: its name in snake case ("Stone Age" → stone_age), as sounds.json names the playlists.
    private string EraKey(int era)
    {
        IReadOnlyList<string>? eras = _session.PlayContent?.Eras;
        return eras is not null && era >= 0 && era < eras.Count ? eras[era].Trim().ToLowerInvariant().Replace(' ', '_') : "stone_age";
    }

    // The ambient layers: the terrain under the middle of the screen (cold in winter), our settlement when the camera is near,
    // the battle when our units fight in view. Further out the land sounds quieter.
    private void Ambient(AudioManager audio, PlayViewMessage v)
    {
        if (_ended)
        {
            return;
        }

        Camera2D camera = _camera();
        float zoom = camera.Zoom.X;
        float near = Math.Clamp((zoom - 0.25f) / 1.0f, 0.35f, 1f);
        string? land = null;
        if (_session.Worlds.TryGetValue(0, out ClientWorld? w) && _session.Content is { } content)
        {
            (int col, int row) = HexLayout.CellAt(camera.Position);
            if (row >= 0 && row < w.Height)
            {
                int i = w.Index(col, row);
                if (w.Visibility[i] != MapChunkMessage.Unknown && w.Biome[i] < content.Biomes.Count)
                {
                    BiomeInfo biome = content.Biomes[w.Biome[i]];
                    land = v.Winter && !biome.IsWater ? "cold" : audio.AmbientOfBiome(biome.Key);
                }
            }
        }

        Vector2 centre = camera.Position;
        Vector2 seen = camera.GetViewportRect().Size / camera.Zoom;
        float reach = HexLayout.ColumnStep * 6;
        float settlement = 0;
        foreach (PlaySettlementView s in v.Settlements)
        {
            if (s.Civ == v.Civ)
            {
                float d = HexLayout.Center(s.Col, s.Row).DistanceTo(centre);
                settlement = Math.Max(settlement, Math.Clamp(1 - (d / reach), 0, 1));
            }
        }

        float battle = 0;
        foreach (PlayUnitView u in v.Units)
        {
            if (u.Civ == v.Civ && u.Fighting)
            {
                Vector2 off = HexLayout.Center(u.Col, u.Row) - centre;
                battle = Math.Max(battle, Math.Abs(off.X) < seen.X / 2 && Math.Abs(off.Y) < seen.Y / 2 ? 1f : 0.3f);
            }
        }

        if (v.Alerts?.Any(a => a.Kind == 0) == true)
        {
            battle = Math.Max(battle, 0.3f);
        }

        foreach (string layer in audio.AmbientLayers)
        {
            float level = layer switch
            {
                "settlement" => settlement * near * 0.8f,
                "battle" => battle,
                _ => layer == land ? near * (1 - (0.4f * settlement)) : 0,
            };
            audio.SetAmbient(layer, level);
        }
    }

    private void CommandSent(PlayCommandKind kind)
    {
        string? sound = kind switch
        {
            PlayCommandKind.Build or PlayCommandKind.BuildRoad or PlayCommandKind.BuildWork or PlayCommandKind.Upgrade
                or PlayCommandKind.UpgradeBuilding or PlayCommandKind.ReplaceBuilding => "build_placed",
            PlayCommandKind.Demolish or PlayCommandKind.RemoveWork => "tear_down",
            PlayCommandKind.Move or PlayCommandKind.Patrol or PlayCommandKind.Formation or PlayCommandKind.Guard
                or PlayCommandKind.ResumePatrol or PlayCommandKind.Board or PlayCommandKind.Land or PlayCommandKind.SearchSigns => "unit_order",
            PlayCommandKind.Attack or PlayCommandKind.AttackWall => "unit_attack",
            PlayCommandKind.Train => "unit_trained",
            PlayCommandKind.SendCaravan or PlayCommandKind.Route or PlayCommandKind.SupplyUnit or PlayCommandKind.BringMaterials => "caravan_sent",
            _ => null,
        };
        if (sound is not null)
        {
            AudioManager.Play(sound);
        }
    }

    private void Refused(string reason) => AudioManager.Play("refused");
}
