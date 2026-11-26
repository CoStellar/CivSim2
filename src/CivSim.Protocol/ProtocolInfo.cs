namespace CivSim.Protocol;

/// <summary>Version information shared by the client and the server.</summary>
public static class ProtocolInfo
{
    /// <summary>
    /// Version of the game: year.month.release (ADR 0031: v26.11.1 is the first release of November 2026). Raise it with every
    /// release; the release tag is v + this. Client and server will refuse to play with another game version (multiplayer, MP2).
    /// </summary>
    public const string GameVersion = "26.11.1";

    /// <summary>What the menu shows next to the version while the game is a demo.</summary>
    public const string GameStage = "Demo";

    /// <summary>Protocol version. Bump on every incompatible message change and add a compatibility test.</summary>
    public const int Version = 50; // 50: the observer full view (table of civilizations, intentions log, watching through one civilization, the spectators chat); 49: candidates, observers and the fallen (the whole map), votes on a new player and on removing one (MP4b); 48: a game with others: players with ping, chat to all or allies, the speed vote, the pause (MP4); 47: the suffix of a repeated nickname in LobbySeat (shown in another colour); 46: the lobby of a multiplayer game (LobbyState, Ready, Chat, Start, GameStarting; MP3); 45: game version, password and return key in Hello, return key in Welcome (multiplayer, MP2); 44: LookSeed in PlayContent (colours and emblems of the civilizations differ from game to game); 43: free building places in PlacesMessage (slots shown while placing); 42: peace from negotiation (terms of goods, a settlement and a length; war weariness); 41: ore signs (Signs in the view, SearchSigns); 40: replacing a building by another kind on its places; 39: start pick distance rule for the client (PickMaxHexes); 38: 38: help for a new player (food balance, event info, tutorial, start without a time limit); 37: the end of the game screen (Won in the view); 36: saving the game; 35: sieges, army supplies (provisions, food caravans to units), the army limit and veterans of each kind, (guard, fighters, taking a fallen settlement); 34: route limits, gateways; 33: battle feedback (sizes, strikes, charges, cover numbers); 32: battle (arms and armour, sizes, split, stances, guards, formations); 31: prospectors and surveyors, what is known of the ground; 30: test scenarios and debug actions; 29: branches of the technology tree; 28: building upgrades, patrols to resume, the hex overview; 27: rations of the units (Patryk, 2026-11-19); 26: the Stone Age rebuild; 25: fog with memory (PlayCellsMessage, remembered buildings and settlements), drawn paths and patrols; 24: alerts, eras as levels, technology categories; 23: materials missing at a construction (porters killed); 22: the new game (ADR 0028): PlayContent, PlayView, PlayCommand, Places; 17: area tasks (stage 6 PR 4a); 18: levers of the helmsman, the AI's decisions for a player, choosing a civilization (PR P1); 19: player commands for levers 1, 2, 4, 5, research and proposals in the player's view; 20: settlement looks, settlers' food, milestones (PR P2b); 21: warriors ready for a campaign in the settlement view (playtest P-03)
}
