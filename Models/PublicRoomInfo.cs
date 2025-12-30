namespace NationsCities.Models;

/// <summary>
/// Informacje o pokoju publicznym do wyświetlenia w liście.
/// </summary>
public class PublicRoomInfo
{
    public string Code { get; set; } = string.Empty;
    public string HostNickname { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
}
