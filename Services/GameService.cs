using NationsCities.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace NationsCities.Services;

/// <summary>
/// Serwis logiki gry.
/// </summary>
public class GameService
{
    private readonly RoomService _roomService;
    
    // Per-room locks to prevent race conditions in TriggerStop
    private readonly ConcurrentDictionary<string, object> _roomLocks = new();
    
    // Litery bez trudnych polskich znaków diakrytycznych (Ć, Ł, Ń, Ó, Ś, Ź, Ż) oraz Q, V, X, Y
    private static readonly char[] PolishLetters = 
        "ABCDEFGHIJKLMNOPRSTUWZ".ToCharArray();

    public GameService(RoomService roomService)
    {
        _roomService = roomService;
    }

    /// <summary>
    /// Rozpoczyna grę w pokoju.
    /// </summary>
    public (bool Success, string? Error) StartGame(string hostConnectionId)
    {
        var room = _roomService.GetRoomByPlayer(hostConnectionId);
        if (room == null)
        {
            return (false, "Nie jesteś w pokoju.");
        }

        if (room.HostConnectionId != hostConnectionId)
        {
            return (false, "Tylko host może rozpocząć grę.");
        }

        if (room.Players.Count < 2)
        {
            return (false, "Potrzeba minimum 2 graczy.");
        }

        if (!room.Players.All(p => p.IsReady))
        {
            return (false, "Nie wszyscy gracze są gotowi.");
        }

        room.CurrentGame = new GameState
        {
            TotalRounds = room.Settings.RoundCount,
            Categories = room.Settings.SelectedCategories.ToList(),
            Phase = RoundPhase.Waiting
        };

        return (true, null);
    }

    /// <summary>
    /// Resetuje grę i przygotowuje pokój do nowej rozgrywki.
    /// </summary>
    public void ResetGameForLobby(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room == null) return;

        // Reset game state
        room.CurrentGame = null;

        // Reset all player scores and violations (but NOT ready state)
        foreach (var player in room.Players)
        {
            player.TotalScore = 0;
            player.RoundScore = 0;
            player.Violations.Clear();
            // NOTE: Don't reset IsReady - players who returned to lobby want to play again
        }
    }

    /// <summary>
    /// Rozpoczyna nową rundę.
    /// </summary>
    public (bool Success, char Letter, string? Error) StartRound(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room?.CurrentGame == null)
        {
            return (false, default, "Gra nie jest aktywna.");
        }

        var game = room.CurrentGame;
        
        if (game.CurrentRound > game.TotalRounds)
        {
            return (false, default, "Gra zakończona.");
        }

        var letter = SelectRandomLetter(game.UsedLetters);
        game.CurrentLetter = letter;
        game.UsedLetters.Add(letter);
        game.Phase = RoundPhase.Answering;
        game.StopTriggeredBy = null;
        game.CountdownEndTime = null;
        game.RoundAnswers.Clear();
        game.AnswersForVoting.Clear();

        // Reset punktów rundy
        foreach (var player in room.Players)
        {
            player.RoundScore = 0;
        }

        return (true, letter, null);
    }

    /// <summary>
    /// Gracz wciska STOP.
    /// </summary>
    public (bool Success, DateTime EndTime, string? Error) TriggerStop(string roomCode, string connectionId, int countdownSeconds)
    {
        // Get or create a lock object for this room
        var roomLock = _roomLocks.GetOrAdd(roomCode, _ => new object());
        
        lock (roomLock)
        {
            var room = _roomService.GetRoom(roomCode);
            if (room?.CurrentGame == null)
            {
                return (false, default, "Gra nie jest aktywna.");
            }

            var game = room.CurrentGame;

            if (game.Phase != RoundPhase.Answering)
            {
                return (false, default, "Nie można teraz zatrzymać.");
            }

            if (game.StopTriggeredBy != null)
            {
                return (false, default, "STOP już wciśnięty.");
            }

            game.StopTriggeredBy = connectionId;
            game.Phase = RoundPhase.Countdown;
            game.CountdownEndTime = DateTime.UtcNow.AddSeconds(countdownSeconds);

            return (true, game.CountdownEndTime.Value, null);
        }
    }

    /// <summary>
    /// Dodaje czas do countdown (tylko gracz który wcisnął STOP).
    /// </summary>
    public (bool Success, DateTime NewEndTime, string? Error) AddTime(string roomCode, string connectionId, int additionalSeconds)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room?.CurrentGame == null)
        {
            return (false, default, "Gra nie jest aktywna.");
        }

        var game = room.CurrentGame;

        if (game.Phase != RoundPhase.Countdown)
        {
            return (false, default, "Nie ma aktywnego countdown.");
        }

        if (game.StopTriggeredBy != connectionId)
        {
            return (false, default, "Tylko gracz który wcisnął STOP może dodać czas.");
        }

        game.CountdownEndTime = game.CountdownEndTime?.AddSeconds(additionalSeconds);
        return (true, game.CountdownEndTime!.Value, null);
    }

    /// <summary>
    /// Zapisuje odpowiedzi gracza.
    /// </summary>
    public bool SubmitAnswers(string roomCode, string connectionId, Dictionary<string, string> answers, bool autoSubmitted = false)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room?.CurrentGame == null) return false;

        var game = room.CurrentGame;
        
        // Normalizacja odpowiedzi
        var normalizedAnswers = answers.ToDictionary(
            kvp => kvp.Key,
            kvp => NormalizeAnswer(kvp.Value)
        );

        game.RoundAnswers[connectionId] = new PlayerAnswers
        {
            PlayerConnectionId = connectionId,
            Answers = normalizedAnswers,
            AutoSubmitted = autoSubmitted
        };

        return true;
    }

    /// <summary>
    /// Kończy rundę i przygotowuje odpowiedzi do głosowania.
    /// </summary>
    public List<AnswerForVoting> EndRoundAndPrepareVoting(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room?.CurrentGame == null) return [];

        var game = room.CurrentGame;
        game.Phase = RoundPhase.Voting;
        game.VotesSubmittedBy.Clear(); // Reset votes submitted tracking

        var answersForVoting = new List<AnswerForVoting>();

        foreach (var category in game.Categories)
        {
            // Dictionary: normalized answer -> (primary answer, list of all original variants)
            var categoryAnswerGroups = new Dictionary<string, (AnswerForVoting Primary, HashSet<string> Variants)>();

            foreach (var (playerId, playerAnswers) in game.RoundAnswers)
            {
                if (playerAnswers?.Answers == null) continue;
                if (!playerAnswers.Answers.TryGetValue(category.Name, out var answer)) continue;
                if (string.IsNullOrWhiteSpace(answer)) continue;

                var player = room.Players.FirstOrDefault(p => p.ConnectionId == playerId);
                var nickname = player?.Nickname ?? "";
                var normalizedAnswer = NormalizeForDuplicateCheck(answer);

                if (categoryAnswerGroups.TryGetValue(normalizedAnswer, out var existing))
                {
                    // Add this player to existing group
                    existing.Primary.SubmittedBy.Add(playerId);
                    if (!string.IsNullOrEmpty(nickname))
                        existing.Primary.SubmitterNicknames.Add(nickname);
                    
                    // Track different spelling variants
                    existing.Variants.Add(answer.Trim());
                }
                else
                {
                    // Create new answer group
                    var answerForVoting = new AnswerForVoting
                    {
                        Category = category.Name,
                        Answer = answer.Trim(),
                        SubmittedBy = [playerId],
                        SubmitterNicknames = string.IsNullOrEmpty(nickname) ? [] : [nickname]
                    };
                    var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { answer.Trim() };
                    categoryAnswerGroups[normalizedAnswer] = (answerForVoting, variants);
                    answersForVoting.Add(answerForVoting);
                }
            }

            // Post-process: mark auto-detected duplicates (groups with multiple different spellings)
            foreach (var (_, (primary, variants)) in categoryAnswerGroups)
            {
                if (variants.Count > 1)
                {
                    // Multiple different spellings found - this is an auto-detected duplicate group
                    primary.IsAutoDetectedDuplicate = true;
                    primary.DuplicateGroupAnswers = variants.ToList();
                }
            }
        }

        game.AnswersForVoting = answersForVoting;
        return answersForVoting;
    }

    /// <summary>
    /// Kończy głosowanie i oblicza punkty.
    /// Algorytm konsensusu: większość głosów decyduje o statusie odpowiedzi.
    /// Punktacja:
    /// - 15 pkt: unikalna poprawna odpowiedź (nikt inny nie dał poprawnej w kategorii)
    /// - 10 pkt: poprawna odpowiedź, ale inni też dali poprawne (różne odpowiedzi)
    /// - 5 pkt: poprawna odpowiedź, ale duplikat (ta sama odpowiedź od wielu graczy)
    /// - 0 pkt: niepoprawna lub brak odpowiedzi
    /// </summary>
    public void FinalizeVotingAndCalculateScores(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room?.CurrentGame == null) return;

        var game = room.CurrentGame;

        // Krok 1: Ustal status każdej odpowiedzi na podstawie głosów (konsensus)
        foreach (var answer in game.AnswersForVoting)
        {
            var validVotes = answer.VotesValid.Count;
            var invalidVotes = answer.VotesInvalid.Count;
            var duplicateVotes = answer.VotesDuplicate.Count;
            
            // Znajdź maksymalną liczbę głosów
            var maxVotes = Math.Max(validVotes, Math.Max(invalidVotes, duplicateVotes));
            
            if (maxVotes == 0)
            {
                // Brak głosów - domyślnie poprawna
                answer.Status = AnswerStatus.Valid;
            }
            else if (duplicateVotes == maxVotes)
            {
                // Duplikat wygrywa (lub remis z duplikatem) - oznacz jako duplikat
                answer.Status = AnswerStatus.Valid; // Duplikat też jest "poprawny", ale z mniejszą liczbą punktów
            }
            else if (validVotes > invalidVotes)
            {
                answer.Status = AnswerStatus.Valid;
            }
            else if (invalidVotes > validVotes)
            {
                answer.Status = AnswerStatus.Invalid;
            }
            else
            {
                // Remis valid/invalid - contested (0 punktów)
                answer.Status = AnswerStatus.Contested;
            }
        }

        // Krok 2: Policz ile poprawnych odpowiedzi jest w każdej kategorii
        var correctAnswersPerCategory = game.AnswersForVoting
            .Where(a => a.Status == AnswerStatus.Valid)
            .GroupBy(a => a.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        // Krok 3: Przydziel punkty
        foreach (var answer in game.AnswersForVoting)
        {
            if (answer.Status != AnswerStatus.Valid) continue;

            var validVotes = answer.VotesValid.Count;
            var duplicateVotes = answer.VotesDuplicate.Count;
            
            // Sprawdź czy odpowiedź została oznaczona jako duplikat przez głosowanie
            var isVotedDuplicate = duplicateVotes >= validVotes && duplicateVotes > 0;
            
            // Sprawdź ile poprawnych odpowiedzi jest w tej kategorii
            var correctInCategory = correctAnswersPerCategory.GetValueOrDefault(answer.Category, 1);
            
            int points;
            if (answer.IsDuplicate || isVotedDuplicate)
            {
                // Duplikat (ta sama odpowiedź od wielu graczy lub zagłosowano jako duplikat)
                points = 5;
            }
            else if (correctInCategory == 1)
            {
                // Unikalna poprawna odpowiedź - nikt inny nie dał poprawnej w kategorii
                points = 15;
            }
            else
            {
                // Poprawna, ale nie unikalna (inne poprawne odpowiedzi w kategorii)
                points = 10;
            }

            foreach (var nickname in answer.SubmitterNicknames)
            {
                var player = room.Players.FirstOrDefault(p => 
                    p.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase));
                if (player != null)
                {
                    player.RoundScore += points;
                    player.TotalScore += points;
                }
            }
        }

        game.Phase = RoundPhase.Results;
    }

    /// <summary>
    /// Przechodzi do następnej rundy lub kończy grę.
    /// </summary>
    public bool NextRoundOrEndGame(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room?.CurrentGame == null) return false;

        var game = room.CurrentGame;
        game.CurrentRound++;

        if (game.CurrentRound > game.TotalRounds)
        {
            // Koniec gry
            game.Phase = RoundPhase.Waiting;
            return false; // false = koniec gry
        }

        game.Phase = RoundPhase.Waiting;
        return true; // true = jest następna runda
    }

    /// <summary>
    /// Losuje literę (wykluczając użyte).
    /// </summary>
    private static char SelectRandomLetter(List<char> usedLetters)
    {
        var available = PolishLetters.Except(usedLetters).ToArray();
        if (available.Length == 0)
        {
            // Wszystkie użyte - reset
            available = PolishLetters;
        }

        var random = new Random();
        return available[random.Next(available.Length)];
    }

    /// <summary>
    /// Normalizuje odpowiedź (trim, lowercase).
    /// </summary>
    private static string NormalizeAnswer(string answer)
    {
        return answer?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Normalizuje do porównania duplikatów (usuwa polskie diakrytyki, lowercase).
    /// </summary>
    private static string NormalizeForDuplicateCheck(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return string.Empty;

        var normalized = answer.Trim().ToLowerInvariant();
        
        // Usuwanie polskich znaków diakrytycznych
        normalized = RemovePolishDiacritics(normalized);
        
        // Normalizuj spacje
        normalized = string.Join(" ", normalized.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
        
        return normalized;
    }

    /// <summary>
    /// Usuwa polskie znaki diakrytyczne (ą→a, ć→c, ę→e, ł→l, ń→n, ó→o, ś→s, ź→z, ż→z).
    /// </summary>
    private static string RemovePolishDiacritics(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(c switch
            {
                'ą' => 'a',
                'ć' => 'c',
                'ę' => 'e',
                'ł' => 'l',
                'ń' => 'n',
                'ó' => 'o',
                'ś' => 's',
                'ź' => 'z',
                'ż' => 'z',
                // Already lowercase at this point, but just in case
                'Ą' => 'a',
                'Ć' => 'c',
                'Ę' => 'e',
                'Ł' => 'l',
                'Ń' => 'n',
                'Ó' => 'o',
                'Ś' => 's',
                'Ź' => 'z',
                'Ż' => 'z',
                _ => c
            });
        }
        return sb.ToString();
    }
}
