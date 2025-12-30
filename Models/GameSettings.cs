namespace NationsCities.Models;

/// <summary>
/// Ustawienia gry.
/// </summary>
public class GameSettings
{
    /// <summary>
    /// Liczba rund (domyślnie 10).
    /// </summary>
    public int RoundCount { get; set; } = 10;

    /// <summary>
    /// Czas na odpowiedź w sekundach (domyślnie 60).
    /// </summary>
    public int RoundTimeSeconds { get; set; } = 60;

    /// <summary>
    /// Czas countdown po STOP w sekundach (domyślnie 10).
    /// </summary>
    public int CountdownSeconds { get; set; } = 10;

    /// <summary>
    /// Czas na głosowanie w sekundach (domyślnie 45).
    /// </summary>
    public int VotingTimeSeconds { get; set; } = 45;

    /// <summary>
    /// Wybrane kategorie gry.
    /// </summary>
    public List<Category> SelectedCategories { get; set; } = Category.StandardCategories.Take(5).ToList();

    /// <summary>
    /// Maksymalna liczba graczy.
    /// </summary>
    public int MaxPlayers { get; set; } = 10;
}
