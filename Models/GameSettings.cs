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

    /// <summary>
    /// Litery dostępne do losowania (konfigurowalne przez hosta).
    /// Domyślnie polskie litery bez trudnych znaków diakrytycznych.
    /// </summary>
    public List<char> AvailableLetters { get; set; } = DefaultPolishLetters.ToList();

    /// <summary>
    /// Domyślny zestaw liter (bez Ć, Ł, Ń, Ó, Ś, Ź, Ż, Q, V, X, Y).
    /// </summary>
    public static readonly char[] DefaultPolishLetters =
        "ABCDEFGHIJKLMNOPRSTUWZ".ToCharArray();

    /// <summary>
    /// Pełny alfabet do wyboru w ustawieniach (wszystkie litery z blueprintu).
    /// </summary>
    public static readonly char[] FullAlphabet =
        "ABCĆDEFĘGHIJKLŁMNŃOÓPRSŚTUWXYZ".ToCharArray();
}
