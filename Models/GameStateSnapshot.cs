namespace NationsCities.Models;

/// <summary>
/// Snapshot of the current game state sent to clients on reconnect.
/// Contains all information needed to restore the client's UI to the correct state.
/// </summary>
public class GameStateSnapshot
{
    /// <summary>
    /// The room the player is in.
    /// </summary>
    public Room? Room { get; set; }
    
    /// <summary>
    /// Current phase of the game.
    /// </summary>
    public GamePhase Phase { get; set; }
    
    /// <summary>
    /// Seconds remaining in current countdown (if applicable).
    /// </summary>
    public int? SecondsRemaining { get; set; }
    
    /// <summary>
    /// Whether the current player is the host.
    /// </summary>
    public bool IsHost { get; set; }
    
    /// <summary>
    /// The player's nickname.
    /// </summary>
    public string Nickname { get; set; } = string.Empty;
}
