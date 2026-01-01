namespace NationsCities.Models;

/// <summary>
/// Naruszenie anty-cheat gracza.
/// </summary>
public class Violation
{
    /// <summary>
    /// Typ naruszenia.
    /// </summary>
    public ViolationType Type { get; set; }

    /// <summary>
    /// Czas trwania naruszenia w sekundach.
    /// </summary>
    public double DurationSeconds { get; set; }

    /// <summary>
    /// Kara punktowa.
    /// </summary>
    public int Penalty { get; set; }

    /// <summary>
    /// Numer rundy w której wystąpiło naruszenie.
    /// </summary>
    public int RoundNumber { get; set; }

    /// <summary>
    /// Czas wystąpienia.
    /// </summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Oblicza karę na podstawie historii naruszeń (progresywna).
    /// </summary>
    public static int CalculatePenalty(List<Violation> previousViolations, ViolationType type, double durationSeconds)
    {
        // Krótkie naruszenia (<2s) = tylko notice
        if (durationSeconds < 2) return 0;

        // Count ALL previous violations (not just those with penalty > 0)
        var previousCount = previousViolations.Count;

        // Progresywna kara (doubled): 1st=0 (warning), 2nd=-10, 3rd=-20, 4th+=-30
        return previousCount switch
        {
            0 => 0,   // Pierwsze = ostrzeżenie
            1 => 10,  // Drugie = -10 pkt
            2 => 20,  // Trzecie = -20 pkt
            _ => 30   // Każde kolejne = -30 pkt
        };
    }
}

/// <summary>
/// Typ naruszenia anty-cheat.
/// </summary>
public enum ViolationType
{
    /// <summary>Utrata fokusu okna (Page Visibility API).</summary>
    FocusLost,
    
    /// <summary>Przełączenie karty/okna (blur event).</summary>
    TabSwitch,
    
    /// <summary>Niestabilne połączenie.</summary>
    ConnectionUnstable
}

/// <summary>
/// Poziom naruszenia.
/// </summary>
public enum ViolationSeverity
{
    /// <summary>Tylko informacja.</summary>
    Notice,
    
    /// <summary>Ostrzeżenie.</summary>
    Warning,
    
    /// <summary>Kara punktowa.</summary>
    Penalty
}
