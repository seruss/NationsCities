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

        // Grupowanie odpowiedzi po kategorii i treści
        var answersForVoting = new List<AnswerForVoting>();

        foreach (var category in game.Categories)
        {
            var categoryAnswers = new Dictionary<string, AnswerForVoting>(StringComparer.OrdinalIgnoreCase);

            foreach (var (playerId, playerAnswers) in game.RoundAnswers)
            {
                // Safety check for null playerAnswers
                if (playerAnswers?.Answers == null) continue;
                
                if (!playerAnswers.Answers.TryGetValue(category.Name, out var answer)) continue;
                if (string.IsNullOrWhiteSpace(answer)) continue;

                // Find player nickname
                var player = room.Players.FirstOrDefault(p => p.ConnectionId == playerId);
                var nickname = player?.Nickname ?? "";

                var normalizedAnswer = NormalizeForDuplicateCheck(answer);

                if (categoryAnswers.TryGetValue(normalizedAnswer, out var existing))
                {
                    existing.SubmittedBy.Add(playerId);
                    if (!string.IsNullOrEmpty(nickname))
                    {
                        existing.SubmitterNicknames.Add(nickname);
                    }
                }
                else
                {
                    var answerForVoting = new AnswerForVoting
                    {
                        Category = category.Name,
                        Answer = answer, // Oryginalna wersja
                        SubmittedBy = [playerId],
                        SubmitterNicknames = string.IsNullOrEmpty(nickname) ? [] : [nickname]
                    };
                    categoryAnswers[normalizedAnswer] = answerForVoting;
                    answersForVoting.Add(answerForVoting);
                }
            }
        }

        game.AnswersForVoting = answersForVoting;
        return answersForVoting;
    }

    /// <summary>
    /// Kończy głosowanie i oblicza punkty.
    /// </summary>
    public void FinalizeVotingAndCalculateScores(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room?.CurrentGame == null) return;

        var game = room.CurrentGame;

        foreach (var answer in game.AnswersForVoting)
        {
            // Ustal status odpowiedzi
            var validVotes = answer.VotesValid.Count;
            var invalidVotes = answer.VotesInvalid.Count;
            
            answer.Status = validVotes > invalidVotes 
                ? AnswerStatus.Valid 
                : invalidVotes > validVotes 
                    ? AnswerStatus.Invalid 
                    : AnswerStatus.Contested;

            // Przydziel punkty (use nicknames for reliable matching)
            if (answer.Status == AnswerStatus.Valid)
            {
                var points = answer.IsDuplicate ? 5 : 10;
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
    /// Normalizuje do porównania duplikatów (usuwa diakrytyki, lowercase).
    /// </summary>
    private static string NormalizeForDuplicateCheck(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return string.Empty;

        var normalized = answer.Trim().ToLowerInvariant();
        
        // Zachowaj polskie znaki przy porównaniu, ale normalizuj spacje
        normalized = string.Join(" ", normalized.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
        
        return normalized;
    }
}
