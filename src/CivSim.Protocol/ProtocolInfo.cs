namespace CivSim.Protocol;

/// <summary>Version information shared by the client and the server.</summary>
public static class ProtocolInfo
{
    /// <summary>Protocol version. Bump on every incompatible message change and add a compatibility test.</summary>
    public const int Version = 35; // 35: sieges and army supplies (provisions, food caravans to units), (guard, fighters, taking a fallen settlement); 34: route limits, gateways; 33: battle feedback (sizes, strikes, charges, cover numbers); 32: battle (arms and armour, sizes, split, stances, guards, formations); 31: prospectors and surveyors, what is known of the ground; 30: test scenarios and debug actions; 29: branches of the technology tree; 28: building upgrades, patrols to resume, the hex overview; 27: rations of the units (Patryk, 2026-11-19); 26: the Stone Age rebuild; 25: fog with memory (PlayCellsMessage, remembered buildings and settlements), drawn paths and patrols; 24: alerts, eras as levels, technology categories; 23: materials missing at a construction (porters killed); 22: the new game (ADR 0028): PlayContent, PlayView, PlayCommand, Places; 17: area tasks (stage 6 PR 4a); 18: levers of the helmsman, the AI's decisions for a player, choosing a civilization (PR P1); 19: player commands for levers 1, 2, 4, 5, research and proposals in the player's view; 20: settlement looks, settlers' food, milestones (PR P2b); 21: warriors ready for a campaign in the settlement view (playtest P-03)
}
