namespace NationsCities.Models;

/// <summary>
/// Represents the current phase of the game from the client's perspective.
/// Used by the SPA GameFlow component to determine which view to render.
/// </summary>
public enum GamePhase
{
    /// <summary>Initial screen - create or join room.</summary>
    Home,
    
    /// <summary>Room created/joined, waiting for players.</summary>
    Lobby,
    
    /// <summary>Active game round - players entering answers.</summary>
    Playing,
    
    /// <summary>Voting on answers.</summary>
    Voting,
    
    /// <summary>Scoreboard after round.</summary>
    RoundResults,
    
    /// <summary>Game over summary.</summary>
    FinalResults,
    
    /// <summary>Connection lost, attempting reconnect.</summary>
    Disconnected,
    
    /// <summary>Error state.</summary>
    Error
}
