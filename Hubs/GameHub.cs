using Microsoft.AspNetCore.SignalR;
using NationsCities.Models;
using NationsCities.Services;

namespace NationsCities.Hubs;

/// <summary>
/// Hub SignalR do komunikacji w czasie rzeczywistym między graczami.
/// </summary>
public class GameHub : Hub
{
    private readonly RoomService _roomService;
    private readonly GameService _gameService;

    public GameHub(RoomService roomService, GameService gameService)
    {
        _roomService = roomService;
        _gameService = gameService;
    }

    #region Połączenia

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var result = _roomService.LeaveRoom(Context.ConnectionId);
        if (result.Room != null && !result.RoomDeleted)
        {
            await Clients.Group(result.Room.Code).SendAsync("OnPlayerLeft", Context.ConnectionId);
            
            if (result.NewHostId != null)
            {
                await Clients.Group(result.Room.Code).SendAsync("OnNewHost", result.NewHostId);
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    #endregion

    #region Pokój

    /// <summary>
    /// Tworzy nowy pokój.
    /// </summary>
    public async Task CreateRoom(string nickname)
    {
        var room = _roomService.CreateRoom(Context.ConnectionId, nickname);
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
        await Clients.Caller.SendAsync("OnRoomCreated", room.Code);
    }

    /// <summary>
    /// Dołącza do pokoju.
    /// </summary>
    public async Task JoinRoom(string roomCode, string nickname)
    {
        var result = _roomService.JoinRoom(roomCode, Context.ConnectionId, nickname);
        
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("OnJoinError", result.Error);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode.ToUpperInvariant());
        await Clients.Caller.SendAsync("OnRoomCreated", roomCode.ToUpperInvariant()); // Reuse for join
        await Clients.OthersInGroup(roomCode.ToUpperInvariant()).SendAsync("OnPlayerJoined", nickname, Context.ConnectionId);
    }

    /// <summary>
    /// Pobiera listę dostępnych pokoi publicznych.
    /// </summary>
    public List<PublicRoomInfo> GetPublicRooms()
    {
        return _roomService.GetPublicRooms();
    }

    /// <summary>
    /// Ustawia pokój jako publiczny lub prywatny (tylko host).
    /// </summary>
    public async Task SetRoomPublic(string roomCode, bool isPublic)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room == null)
        {
            await Clients.Caller.SendAsync("OnError", "Pokój nie istnieje.");
            return;
        }

        var caller = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
        if (caller?.IsHost != true)
        {
            await Clients.Caller.SendAsync("OnError", "Tylko host może zmienić widoczność pokoju.");
            return;
        }

        _roomService.SetRoomPublic(roomCode, isPublic);
        await Clients.Group(roomCode).SendAsync("OnRoomVisibilityChanged", isPublic);
    }

    /// <summary>
    /// Opuszcza pokój.
    /// </summary>
    public async Task LeaveRoom(string roomCode)
    {
        var result = _roomService.LeaveRoom(Context.ConnectionId);
        if (result.Room != null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomCode);
            
            if (!result.RoomDeleted)
            {
                await Clients.Group(roomCode).SendAsync("OnPlayerLeft", Context.ConnectionId);
                
                if (result.NewHostId != null)
                {
                    await Clients.Group(roomCode).SendAsync("OnNewHost", result.NewHostId);
                }
            }
        }
    }

    /// <summary>
    /// Wyrzuca gracza (tylko host).
    /// </summary>
    public async Task KickPlayer(string roomCode, string playerId)
    {
        var result = _roomService.KickPlayer(Context.ConnectionId, playerId);
        
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("OnError", result.Error);
            return;
        }

        await Clients.Client(playerId).SendAsync("OnKicked");
        await Groups.RemoveFromGroupAsync(playerId, roomCode);
        await Clients.Group(roomCode).SendAsync("OnPlayerKicked", playerId);
    }

    /// <summary>
    /// Dołącza do gry (używane przy nawigacji do strony gry).
    /// </summary>
    public async Task<bool> JoinGame(string roomCode, string nickname)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room == null)
        {
            return false;
        }

        // Find player by nickname and update their connection ID
        var player = room.Players.FirstOrDefault(p => p.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase));
        if (player != null)
        {
            // Update connection ID if reconnecting
            var oldConnectionId = player.ConnectionId;
            player.ConnectionId = Context.ConnectionId;
            
            // If this player is the host, update room's HostConnectionId too
            if (player.IsHost)
            {
                room.HostConnectionId = Context.ConnectionId;
            }
            
            // Update the _playerRooms mapping for this connection
            _roomService.UpdatePlayerConnection(roomCode, oldConnectionId, Context.ConnectionId);
            
            // Add to SignalR group
            await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
            return true;
        }

        return false;
    }

    #endregion

    #region Stan gotowości

    /// <summary>
    /// Ustawia gotowość gracza.
    /// </summary>
    public async Task SetReady(string roomCode, bool isReady)
    {
        if (_roomService.SetPlayerReady(Context.ConnectionId, isReady))
        {
            await Clients.Group(roomCode).SendAsync("OnPlayerReadyChanged", Context.ConnectionId, isReady);
        }
    }

    #endregion

    #region Ustawienia

    /// <summary>
    /// Aktualizuje ustawienia gry (tylko host).
    /// </summary>
    public async Task UpdateGameSettings(string roomCode, List<string> categoryNames, int roundCount)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room == null)
        {
            await Clients.Caller.SendAsync("OnError", "Pokój nie istnieje.");
            return;
        }

        // Check if caller is host
        var caller = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
        if (caller?.IsHost != true)
        {
            await Clients.Caller.SendAsync("OnError", "Tylko host może zmieniać ustawienia.");
            return;
        }

        // Validate input
        if (categoryNames == null || categoryNames.Count == 0)
        {
            await Clients.Caller.SendAsync("OnError", "Wybierz co najmniej jedną kategorię.");
            return;
        }

        if (roundCount < 1 || roundCount > 20)
        {
            await Clients.Caller.SendAsync("OnError", "Liczba rund musi być między 1 a 20.");
            return;
        }

        // Update settings - include both standard and custom categories
        var selectedCategories = new List<Category>();
        foreach (var name in categoryNames)
        {
            var standardCat = Category.StandardCategories.FirstOrDefault(c => c.Name == name);
            if (standardCat != null)
            {
                selectedCategories.Add(standardCat);
            }
            else
            {
                // Custom category
                selectedCategories.Add(new Category { Name = name, Icon = "star", IsCustom = true });
            }
        }

        room.Settings.SelectedCategories = selectedCategories;
        room.Settings.RoundCount = roundCount;

        // Notify all players in the room
        await Clients.Group(roomCode).SendAsync("OnSettingsUpdated", 
            selectedCategories.Select(c => c.Name).ToList(), 
            roundCount);
    }

    #endregion

    #region Gra

    /// <summary>
    /// Rozpoczyna grę (tylko host).
    /// </summary>
    public async Task StartGame(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room == null) return;

        // Reset scores and violations when starting a new game
        _gameService.ResetGameForLobby(roomCode);

        var result = _gameService.StartGame(Context.ConnectionId);
        
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("OnError", result.Error);
            return;
        }

        // Refresh room reference after StartGame may have modified it
        room = _roomService.GetRoom(roomCode);
        if (room?.CurrentGame != null)
        {
            // Start first round
            var roundResult = _gameService.StartRound(roomCode);
            
            await Clients.Group(roomCode).SendAsync("OnGameStarted", 
                room.CurrentGame.Categories.Select(c => c.Name).ToList(),
                room.CurrentGame.TotalRounds);
            
            if (roundResult.Success)
            {
                await Clients.Group(roomCode).SendAsync("OnRoundStarted", 
                    roundResult.Letter, 
                    room.CurrentGame.CurrentRound);
            }
        }
    }

    /// <summary>
    /// Gracz wciska STOP.
    /// </summary>
    public async Task TriggerStop(string roomCode, Dictionary<string, string> answers)
    {
        Console.WriteLine($"[TriggerStop] Called by {Context.ConnectionId}");
        
        var room = _roomService.GetRoom(roomCode);
        var countdownSeconds = room?.Settings.CountdownSeconds ?? 10;
        
        Console.WriteLine($"[TriggerStop] Room has {room?.Players.Count ?? 0} players");
        
        var result = _gameService.TriggerStop(roomCode, Context.ConnectionId, countdownSeconds);
        
        Console.WriteLine($"[TriggerStop] GameService.TriggerStop result: Success={result.Success}");
        
        if (result.Success)
        {
            // Auto-submit answers for the player who triggered STOP
            _gameService.SubmitAnswers(roomCode, Context.ConnectionId, answers);
            await Clients.Caller.SendAsync("OnAnswersSubmitted");
            
            // Notify all players about STOP
            await Clients.Group(roomCode).SendAsync("OnStopTriggered", Context.ConnectionId, result.EndTime);
            await Clients.Group(roomCode).SendAsync("OnPlayerSubmitted", Context.ConnectionId);
            
            // Check if all players already submitted (could happen in 2-player game)
            if (room?.CurrentGame != null)
            {
                var submittedCount = room.CurrentGame.RoundAnswers.Count;
                var playerCount = room.Players.Count;
                Console.WriteLine($"[TriggerStop] SUCCESS branch: Submitted={submittedCount}, Players={playerCount}");
                
                var allSubmitted = room.Players.All(p => 
                    room.CurrentGame.RoundAnswers.ContainsKey(p.ConnectionId));
                
                if (allSubmitted)
                {
                    Console.WriteLine($"[TriggerStop] All submitted in SUCCESS branch - calling EndRound");
                    await EndRound(roomCode);
                }
            }
        }
        else
        {
            Console.WriteLine($"[TriggerStop] ELSE branch - TriggerStop failed, submitting answers anyway");
            
            // TriggerStop failed (another player already triggered) - but still submit this player's answers
            // This prevents deadlock when both players click STOP simultaneously
            if (_gameService.SubmitAnswers(roomCode, Context.ConnectionId, answers))
            {
                Console.WriteLine($"[TriggerStop] ELSE branch - SubmitAnswers succeeded");
                
                await Clients.Caller.SendAsync("OnAnswersSubmitted");
                await Clients.Group(roomCode).SendAsync("OnPlayerSubmitted", Context.ConnectionId);
                
                // Check if all players submitted
                if (room?.CurrentGame != null)
                {
                    var submittedCount = room.CurrentGame.RoundAnswers.Count;
                    var playerCount = room.Players.Count;
                    Console.WriteLine($"[TriggerStop] ELSE branch: Submitted={submittedCount}, Players={playerCount}");
                    
                    var allSubmitted = room.Players.All(p => 
                        room.CurrentGame.RoundAnswers.ContainsKey(p.ConnectionId));
                    
                    if (allSubmitted)
                    {
                        Console.WriteLine($"[TriggerStop] All submitted in ELSE branch - calling EndRound");
                        await EndRound(roomCode);
                    }
                    else
                    {
                        Console.WriteLine($"[TriggerStop] NOT all submitted yet in ELSE branch");
                        foreach (var p in room.Players)
                        {
                            var hasAnswers = room.CurrentGame.RoundAnswers.ContainsKey(p.ConnectionId);
                            Console.WriteLine($"[TriggerStop]   Player {p.Nickname} ({p.ConnectionId}): HasAnswers={hasAnswers}");
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine($"[TriggerStop] ELSE branch - SubmitAnswers FAILED!");
            }
        }
    }

    /// <summary>
    /// Dodaje czas (tylko gracz który wcisnął STOP).
    /// </summary>
    public async Task AddTime(string roomCode, int additionalSeconds)
    {
        var result = _gameService.AddTime(roomCode, Context.ConnectionId, additionalSeconds);
        
        if (result.Success)
        {
            await Clients.Group(roomCode).SendAsync("OnTimeAdded", result.NewEndTime);
        }
    }

    /// <summary>
    /// Wysyła odpowiedzi.
    /// </summary>
    public async Task SubmitAnswers(string roomCode, Dictionary<string, string> answers)
    {
        if (_gameService.SubmitAnswers(roomCode, Context.ConnectionId, answers))
        {
            await Clients.Caller.SendAsync("OnAnswersSubmitted");
            await Clients.Group(roomCode).SendAsync("OnPlayerSubmitted", Context.ConnectionId);
            
            // Check if all players submitted
            var room = _roomService.GetRoom(roomCode);
            if (room?.CurrentGame != null)
            {
                var allSubmitted = room.Players.All(p => 
                    room.CurrentGame.RoundAnswers.ContainsKey(p.ConnectionId));
                
                if (allSubmitted)
                {
                    await EndRound(roomCode);
                }
            }
        }
    }

    /// <summary>
    /// Kończy rundę i przechodzi do głosowania.
    /// </summary>
    public async Task EndRound(string roomCode)
    {
        var answersForVoting = _gameService.EndRoundAndPrepareVoting(roomCode);
        
        // Notify all players to go to voting
        await Clients.Group(roomCode).SendAsync("OnRoundEnded", answersForVoting.Count);
    }

    /// <summary>
    /// Rozpoczyna następną rundę (tylko host).
    /// </summary>
    public async Task StartNextRound(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room == null) return;
        
        // Find player by connection ID and check IsHost
        var caller = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
        if (caller?.IsHost != true)
        {
            await Clients.Caller.SendAsync("OnError", "Tylko host może rozpocząć następną rundę.");
            return;
        }

        // Increment round number
        if (room.CurrentGame != null)
        {
            room.CurrentGame.CurrentRound++;
        }

        var roundResult = _gameService.StartRound(roomCode);
        
        if (roundResult.Success)
        {
            await Clients.Group(roomCode).SendAsync("OnRoundStarted", 
                roundResult.Letter, 
                room.CurrentGame?.CurrentRound ?? 1);
        }
        else
        {
            await Clients.Caller.SendAsync("OnError", roundResult.Error);
        }
    }

    #endregion

    #region Głosowanie

    /// <summary>
    /// Głosuje na odpowiedź.
    /// </summary>
    public async Task VoteAnswer(string roomCode, string answerId, bool isValid)
    {
        var room = _roomService.GetRoom(roomCode);
        var answer = room?.CurrentGame?.AnswersForVoting.FirstOrDefault(a => a.Id == answerId);
        
        if (answer != null)
        {
            // Usuń poprzedni głos
            answer.VotesValid.Remove(Context.ConnectionId);
            answer.VotesInvalid.Remove(Context.ConnectionId);
            
            // Dodaj nowy
            if (isValid)
            {
                answer.VotesValid.Add(Context.ConnectionId);
            }
            else
            {
                answer.VotesInvalid.Add(Context.ConnectionId);
            }

            await Clients.Group(roomCode).SendAsync("OnVoteCast", answerId, 
                answer.VotesValid.Count, answer.VotesInvalid.Count);
        }
    }

    /// <summary>
    /// Gracz przesyła swoje głosy.
    /// </summary>
    public async Task SubmitVotes(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room?.CurrentGame == null) return;

        // Find player by current connection
        var player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
        if (player == null) return;

        // Mark votes as submitted
        room.CurrentGame.VotesSubmittedBy.Add(Context.ConnectionId);

        var submittedCount = room.CurrentGame.VotesSubmittedBy.Count;
        var totalPlayers = room.Players.Count;

        // Notify all players of progress
        await Clients.Group(roomCode).SendAsync("OnVotesSubmitted", submittedCount);

        // Auto-end if all players have submitted
        if (submittedCount >= totalPlayers)
        {
            await FinalizeVoting(roomCode);
        }
    }

    /// <summary>
    /// Kończy głosowanie i przechodzi do tabeli wyników.
    /// </summary>
    public async Task FinalizeVoting(string roomCode)
    {
        _gameService.FinalizeVotingAndCalculateScores(roomCode);
        
        // Check if this was the last round - reset ready states for new game
        var room = _roomService.GetRoom(roomCode);
        if (room?.CurrentGame != null && room.CurrentGame.CurrentRound >= room.CurrentGame.TotalRounds)
        {
            foreach (var player in room.Players)
            {
                player.IsReady = player.IsHost; // Only host stays ready
            }
        }
        
        // Notify all players to go to scoreboard
        await Clients.Group(roomCode).SendAsync("OnVotingEnded");
    }

    /// <summary>
    /// Kończy grę i usuwa pokój (tylko host).
    /// </summary>
    public async Task EndGame(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        
        // Only host can end game
        if (room == null) return;
        
        var hostPlayer = room.Players.FirstOrDefault(p => p.IsHost);
        var caller = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
        
        if (caller?.IsHost != true)
        {
            await Clients.Caller.SendAsync("OnError", "Tylko host może zakończyć grę.");
            return;
        }

        // Notify all players game is ending
        await Clients.Group(roomCode).SendAsync("OnGameEnded");
        
        // Delete the room
        _roomService.DeleteRoom(roomCode);
    }

    /// <summary>
    /// Wraca do lobby (zachowuje pokój, graczy i wyniki - reset przy następnej grze).
    /// </summary>
    public async Task ReturnToLobby(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room == null) return;

        // Just notify all players to return to lobby (reset happens on next StartGame)
        await Clients.Group(roomCode).SendAsync("OnReturnToLobby", roomCode);
    }

    #endregion

    #region Chat

    /// <summary>
    /// Wysyła wiadomość na czacie.
    /// </summary>
    public async Task SendChatMessage(string roomCode, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        
        // Limit długości
        if (message.Length > 200)
        {
            message = message[..200];
        }

        // Persist message in room
        var room = _roomService.GetRoom(roomCode);
        var player = room?.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
        if (room != null && player != null)
        {
            room.ChatMessages.Add(new ChatMessage
            {
                Nickname = player.Nickname,
                Text = message,
                IsSystem = false
            });
            
            // Keep only last 50 messages
            if (room.ChatMessages.Count > 50)
            {
                room.ChatMessages.RemoveAt(0);
            }
        }

        await Clients.Group(roomCode).SendAsync("OnChatMessage", Context.ConnectionId, message);
    }

    /// <summary>
    /// Dodaje system message do czatu (używane przez serwer).
    /// </summary>
    public void AddSystemMessage(string roomCode, string text)
    {
        var room = _roomService.GetRoom(roomCode);
        room?.ChatMessages.Add(new ChatMessage
        {
            Nickname = "System",
            Text = text,
            IsSystem = true
        });
    }

    #endregion

    #region Anty-cheat

    /// <summary>
    /// Raportuje naruszenie.
    /// </summary>
    public async Task ReportViolation(string roomCode, string violationType, double durationSeconds)
    {
        Console.WriteLine($"[AntiCheat] ReportViolation called: room={roomCode}, type={violationType}, duration={durationSeconds}s, connectionId={Context.ConnectionId}");
        
        var room = _roomService.GetRoom(roomCode);
        var player = room?.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
        
        Console.WriteLine($"[AntiCheat] Player found: {player?.Nickname ?? "NULL"}, Room found: {room != null}");
        
        if (player != null && Enum.TryParse<ViolationType>(violationType, out var type))
        {
            var penalty = Violation.CalculatePenalty(player.Violations, type, durationSeconds);
            
            Console.WriteLine($"[AntiCheat] Penalty calculated: {penalty} points (previous violations: {player.Violations.Count})");
            
            var violation = new Violation
            {
                Type = type,
                DurationSeconds = durationSeconds,
                Penalty = penalty,
                RoundNumber = room?.CurrentGame?.CurrentRound ?? 0
            };
            
            player.Violations.Add(violation);
            player.TotalScore -= penalty;
            
            Console.WriteLine($"[AntiCheat] Violation added. Player {player.Nickname} now has TotalScore={player.TotalScore}, Violations={player.Violations.Count}");

            await Clients.Group(roomCode).SendAsync("OnAntiCheatViolation", 
                Context.ConnectionId, 
                violationType, 
                durationSeconds, 
                penalty);
                
            Console.WriteLine($"[AntiCheat] OnAntiCheatViolation broadcast sent");
        }
        else
        {
            Console.WriteLine($"[AntiCheat] Violation not processed - player null: {player == null}, parse failed: {!Enum.TryParse<ViolationType>(violationType, out _)}");
        }
    }

    #endregion
}
