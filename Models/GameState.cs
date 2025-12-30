namespace NationsCities.Models;

/// <summary>
/// Aktualny stan gry w pokoju.
/// </summary>
public class GameState
{
    /// <summary>
    /// Numer aktualnej rundy (1-indexed).
    /// </summary>
    public int CurrentRound { get; set; } = 1;

    /// <summary>
    /// Całkowita liczba rund.
    /// </summary>
    public int TotalRounds { get; set; } = 10;

    /// <summary>
    /// Aktualna litera rundy.
    /// </summary>
    public char CurrentLetter { get; set; }

    /// <summary>
    /// Lista już użytych liter.
    /// </summary>
    public List<char> UsedLetters { get; set; } = [];

    /// <summary>
    /// Aktywne kategorie w grze.
    /// </summary>
    public List<Category> Categories { get; set; } = [];

    /// <summary>
    /// Aktualna faza rundy.
    /// </summary>
    public RoundPhase Phase { get; set; } = RoundPhase.Waiting;

    /// <summary>
    /// ConnectionId gracza który wcisnął STOP (null jeśli nikt).
    /// </summary>
    public string? StopTriggeredBy { get; set; }

    /// <summary>
    /// Czas zakończenia countdown (null jeśli nie aktywny).
    /// </summary>
    public DateTime? CountdownEndTime { get; set; }

    /// <summary>
    /// Odpowiedzi graczy w bieżącej rundzie.
    /// Key = ConnectionId gracza, Value = odpowiedzi.
    /// </summary>
    public Dictionary<string, PlayerAnswers> RoundAnswers { get; set; } = [];

    /// <summary>
    /// Wszystkie odpowiedzi do głosowania (po zakończeniu rundy).
    /// </summary>
    public List<AnswerForVoting> AnswersForVoting { get; set; } = [];

    /// <summary>
    /// ConnectionId graczy którzy przesłali głosy.
    /// </summary>
    public HashSet<string> VotesSubmittedBy { get; set; } = [];
}

/// <summary>
/// Faza rundy gry.
/// </summary>
public enum RoundPhase
{
    /// <summary>Oczekiwanie na rozpoczęcie.</summary>
    Waiting,
    
    /// <summary>Gracze wpisują odpowiedzi.</summary>
    Answering,
    
    /// <summary>Countdown po wciśnięciu STOP.</summary>
    Countdown,
    
    /// <summary>Głosowanie nad odpowiedziami.</summary>
    Voting,
    
    /// <summary>Wyświetlanie wyników rundy.</summary>
    Results
}
