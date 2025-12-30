namespace NationsCities.Models;

/// <summary>
/// Odpowiedzi gracza w rundzie.
/// </summary>
public class PlayerAnswers
{
    /// <summary>
    /// ConnectionId gracza.
    /// </summary>
    public string PlayerConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Odpowiedzi gracza (Key = nazwa kategorii, Value = odpowiedź).
    /// </summary>
    public Dictionary<string, string> Answers { get; set; } = [];

    /// <summary>
    /// Czas wysłania odpowiedzi.
    /// </summary>
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Czy odpowiedzi zostały automatycznie wysłane (timeout).
    /// </summary>
    public bool AutoSubmitted { get; set; }
}

/// <summary>
/// Odpowiedź do głosowania.
/// </summary>
public class AnswerForVoting
{
    /// <summary>
    /// Unikalny identyfikator odpowiedzi.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Nazwa kategorii.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Treść odpowiedzi.
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Lista ConnectionId graczy którzy dali tę odpowiedź.
    /// </summary>
    public List<string> SubmittedBy { get; set; } = [];

    /// <summary>
    /// Lista nicków graczy którzy dali tę odpowiedź (dla filtrowania).
    /// </summary>
    public List<string> SubmitterNicknames { get; set; } = [];

    /// <summary>
    /// Głosy za (valid).
    /// </summary>
    public List<string> VotesValid { get; set; } = [];

    /// <summary>
    /// Głosy przeciw (invalid).
    /// </summary>
    public List<string> VotesInvalid { get; set; } = [];

    /// <summary>
    /// Status walidacji.
    /// </summary>
    public AnswerStatus Status { get; set; } = AnswerStatus.Pending;

    /// <summary>
    /// Czy odpowiedź jest duplikatem (więcej niż 1 gracz).
    /// </summary>
    public bool IsDuplicate => SubmittedBy.Count > 1;
}

/// <summary>
/// Status odpowiedzi.
/// </summary>
public enum AnswerStatus
{
    /// <summary>Oczekuje na głosowanie.</summary>
    Pending,
    
    /// <summary>Zaakceptowana.</summary>
    Valid,
    
    /// <summary>Odrzucona.</summary>
    Invalid,
    
    /// <summary>Sporny przypadek.</summary>
    Contested
}
