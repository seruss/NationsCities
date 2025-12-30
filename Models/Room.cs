namespace NationsCities.Models;

/// <summary>
/// Reprezentuje pokój gry.
/// </summary>
public class Room
{
    /// <summary>
    /// 4-literowy kod pokoju (np. "ABCD").
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// ConnectionId hosta (twórcy pokoju).
    /// </summary>
    public string HostConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Lista graczy w pokoju.
    /// </summary>
    public List<Player> Players { get; set; } = [];

    /// <summary>
    /// Ustawienia gry.
    /// </summary>
    public GameSettings Settings { get; set; } = new();

    /// <summary>
    /// Aktualny stan gry (null jeśli gra nie rozpoczęta).
    /// </summary>
    public GameState? CurrentGame { get; set; }

    /// <summary>
    /// Czy pokój jest publiczny (dostępny dla wszystkich).
    /// </summary>
    public bool IsPublic { get; set; } = false;

    /// <summary>
    /// Data utworzenia pokoju.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
