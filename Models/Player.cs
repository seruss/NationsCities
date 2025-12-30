namespace NationsCities.Models;

/// <summary>
/// Reprezentuje gracza w pokoju.
/// </summary>
public class Player
{
    /// <summary>
    /// SignalR ConnectionId gracza.
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Nick gracza.
    /// </summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>
    /// Kolor avatara (HSL).
    /// </summary>
    public string AvatarColor { get; set; } = string.Empty;

    /// <summary>
    /// Czy gracz jest gotowy do gry.
    /// </summary>
    public bool IsReady { get; set; }

    /// <summary>
    /// Czy gracz jest hostem pokoju.
    /// </summary>
    public bool IsHost { get; set; }

    /// <summary>
    /// Łączny wynik gracza.
    /// </summary>
    public int TotalScore { get; set; }

    /// <summary>
    /// Punkty zdobyte w bieżącej rundzie.
    /// </summary>
    public int RoundScore { get; set; }

    /// <summary>
    /// Lista naruszeń anty-cheat.
    /// </summary>
    public List<Violation> Violations { get; set; } = [];

    /// <summary>
    /// Data dołączenia do pokoju.
    /// </summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Generuje losowy kolor avatara.
    /// </summary>
    public static string GenerateAvatarColor()
    {
        var random = new Random();
        var hue = random.Next(0, 360);
        return $"hsl({hue}, 70%, 50%)";
    }

    /// <summary>
    /// Zwraca inicjały gracza (max 2 litery).
    /// </summary>
    public string GetInitials()
    {
        if (string.IsNullOrWhiteSpace(Nickname)) return "?";
        
        var parts = Nickname.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        }
        return Nickname.Length >= 2 
            ? Nickname[..2].ToUpper() 
            : Nickname.ToUpper();
    }
}
