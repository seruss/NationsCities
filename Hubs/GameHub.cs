using Microsoft.AspNetCore.SignalR;
using NationsCities.Models;
using NationsCities.Services;

namespace NationsCities.Hubs;

/// <summary>
/// Hub SignalR do komunikacji w czasie rzeczywistym między graczami.
/// Wszystkie operacje domenowe używają SessionId jako klucza gracza.
/// ConnectionId jest używany wyłącznie do transportu SignalR (Groups, Clients.Client).
/// </summary>
public class GameHub : Hub
{
    private readonly RoomService _roomService;
    private readonly GameService _gameService;
    private readonly ILogger<GameHub> _logger;

    public GameHub(RoomService roomService, GameService gameService, ILogger<GameHub> logger)
    {
        _roomService = roomService;
        _gameService = gameService;
        _logger = logger;
    }

    // ===== RESOLVER =====

    /// <summary>
    /// Tłumaczy bieżące połączenie na stabilny SessionId.
    /// </summary>
    private string? ResolveSessionId()
        => _roomService.GetSessionByConnection(Context.ConnectionId);

    /// <summary>
    /// Tłumaczy SessionId na bieżący ConnectionId (do wysyłki SignalR).
    /// </summary>
    private string? ResolveConnectionId(string sessionId)
        => _roomService.GetConnectionId(sessionId);

    #region Połączenia

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var sessionId = ResolveSessionId();
        var roomCode = sessionId != null ? _roomService.GetRoomCodeBySession(sessionId) : null;
        
        if (roomCode == null || sessionId == null)
        {
            _logger.LogDebug("OnDisconnectedAsync: conn={ConnectionId} — no room mapping, skipping", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
            return;
        }
        
        var room = _roomService.GetRoom(roomCode);
        
        // DON'T remove players when a game is in progress
        if (room?.CurrentGame != null)
        {
            _logger.LogDebug("OnDisconnectedAsync: conn={ConnectionId} — game in progress in room {RoomCode}, keeping player", Context.ConnectionId, roomCode);
            await base.OnDisconnectedAsync(exception);
            return;
        }
        
        var player = room?.Players.FirstOrDefault(p => p.SessionId == sessionId);
        if (player == null)
        {
            _logger.LogDebug("OnDisconnectedAsync: conn={ConnectionId} — player not found in room {RoomCode}", Context.ConnectionId, roomCode);
            await base.OnDisconnectedAsync(exception);
            return;
        }
        
        _logger.LogInformation("OnDisconnectedAsync: {Nickname} (conn={ConnectionId}) in room {RoomCode}, IsHost={IsHost}", player.Nickname, Context.ConnectionId, roomCode, player.IsHost);
        
        // LOBBY DISCONNECTION: Schedule removal with grace period
        if (!string.IsNullOrEmpty(player.SessionId))
        {
            _roomService.SchedulePlayerRemoval(
                player.SessionId, 
                player.ConnectionId, 
                roomCode, 
                player.Nickname);
            
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(6));
                var removals = _roomService.ProcessExpiredDisconnections();
                
                foreach (var removal in removals)
                {
                    if (!removal.RoomDeleted)
                    {
                        await Clients.Group(removal.RoomCode).SendAsync("OnPlayerLeft", removal.Nickname);
                        
                        if (removal.NewHostSessionId != null && removal.NewHostNickname != null)
                        {
                            var newHostConnId = _roomService.GetConnectionId(removal.NewHostSessionId);
                            await Clients.Group(removal.RoomCode).SendAsync("OnNewHost", removal.NewHostNickname, removal.NewHostSessionId);
                        }
                    }
                }
            });
        }
        else
        {
            // No session ID - immediate removal (legacy)
            _logger.LogWarning("OnDisconnectedAsync: {Nickname} has no sessionId, immediate removal", player.Nickname);
            var result = _roomService.LeaveRoom(sessionId);
            if (result.Room != null && !result.RoomDeleted)
            {
                await Clients.Group(result.Room.Code).SendAsync("OnPlayerLeft", player.Nickname);
                
                if (result.NewHostSessionId != null && result.NewHostNickname != null)
                {
                    await Clients.Group(result.Room.Code).SendAsync("OnNewHost", result.NewHostNickname, result.NewHostSessionId);
                }
            }
        }
        
        await base.OnDisconnectedAsync(exception);
    }

    #endregion

    #region Pokój

    /// <summary>
    /// Tworzy nowy pokój.
    /// </summary>
    public async Task CreateRoom(string nickname, string? sessionId = null)
    {
        var sid = sessionId ?? Guid.NewGuid().ToString("N");
        _logger.LogInformation("CreateRoom: nickname={Nickname}, session={SessionId}, conn={ConnectionId}", nickname, sid, Context.ConnectionId);
        var room = _roomService.CreateRoom(Context.ConnectionId, nickname, sid);
        AddSystemMessageInternal(room, "Naciśnij 'Jestem gotowy' gdy chcesz grać!");
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
        await Clients.Caller.SendAsync("OnRoomCreated", room.Code);
    }

    /// <summary>
    /// Dołącza do pokoju.
    /// </summary>
    public async Task JoinRoom(string roomCode, string nickname, string? sessionId = null)
    {
        var sid = sessionId ?? Guid.NewGuid().ToString("N");
        var result = _roomService.JoinRoom(roomCode, Context.ConnectionId, nickname, sid);
        
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("OnJoinError", result.Error);
            return;
        }

        var rCode = roomCode.ToUpperInvariant();
        var room = _roomService.GetRoom(rCode);
        AddSystemMessageInternal(room, $"{nickname} dołączył do pokoju.");
        
        await Groups.AddToGroupAsync(Context.ConnectionId, rCode);
        await Clients.Caller.SendAsync("OnRoomCreated", rCode);
        await Clients.OthersInGroup(rCode).SendAsync("OnPlayerJoined", nickname, sid);
        await Clients.OthersInGroup(rCode).SendAsync("OnChatMessage", "System", $"{nickname} dołączył do pokoju.", true);
    }

    /// <summary>
    /// Reconnects a player using their session ID.
    /// </summary>
    public async Task<GameStateSnapshot?> ReconnectSession(string roomCode, string sessionId, string? nickname)
    {
        _logger.LogInformation("ReconnectSession: room={RoomCode}, session={SessionId}, nickname={Nickname}, conn={ConnectionId}", roomCode, sessionId, nickname, Context.ConnectionId);
        _roomService.CancelPendingRemoval(sessionId);
        if (!string.IsNullOrEmpty(nickname))
        {
            _roomService.CancelPendingRemovalByNickname(roomCode, nickname);
        }
        
        var room = _roomService.GetRoom(roomCode);
        if (room == null) return null;

        var player = room.Players.FirstOrDefault(p => p.SessionId == sessionId);
        
        if (player == null && !string.IsNullOrEmpty(nickname))
        {
            player = room.Players.FirstOrDefault(p => 
                p.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase));
        }
        
        if (player == null) return null;

        // Update transport ConnectionId
        player.ConnectionId = Context.ConnectionId;
        
        if (string.IsNullOrEmpty(player.SessionId))
        {
            player.SessionId = sessionId;
        }

        // Update transport mapping
        _roomService.UpdatePlayerConnection(player.SessionId, Context.ConnectionId);

        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

        var phase = DeterminePhase(room);
        _logger.LogInformation("ReconnectSession: success — {Nickname} reconnected to {RoomCode}, phase={Phase}", player.Nickname, roomCode, phase);

        return new GameStateSnapshot
        {
            Room = room,
            Phase = phase,
            SecondsRemaining = CalculateRemainingSeconds(room),
            IsHost = player.IsHost,
            Nickname = player.Nickname
        };
    }

    private GamePhase DeterminePhase(Room room)
    {
        if (room.CurrentGame == null) return GamePhase.Lobby;
        
        return room.CurrentGame.Phase switch
        {
            RoundPhase.Waiting => GamePhase.Lobby,
            RoundPhase.Answering => GamePhase.Playing,
            RoundPhase.Countdown => GamePhase.Playing,
            RoundPhase.Voting => GamePhase.Voting,
            RoundPhase.Results => GamePhase.RoundResults,
            RoundPhase.FinalResults => GamePhase.FinalResults,
            _ => GamePhase.Lobby
        };
    }

    private int? CalculateRemainingSeconds(Room room)
    {
        if (room.CurrentGame?.Phase != RoundPhase.Countdown)
            return null;
            
        var endTime = room.CurrentGame.CountdownEndTime;
        if (endTime == null) return null;
        
        var remaining = (endTime.Value - DateTime.UtcNow).TotalSeconds;
        return Math.Max(0, (int)remaining);
    }

    public List<PublicRoomInfo> GetPublicRooms()
    {
        return _roomService.GetPublicRooms();
    }

    public async Task SubscribePublicRooms()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "public-lobby");
    }

    public async Task UnsubscribePublicRooms()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "public-lobby");
    }

    public async Task SetRoomPublic(string roomCode, bool isPublic)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        var room = _roomService.GetRoom(roomCode);
        if (room == null)
        {
            await Clients.Caller.SendAsync("OnError", "Pokój nie istnieje.");
            return;
        }

        if (room.HostSessionId != sessionId)
        {
            await Clients.Caller.SendAsync("OnError", "Tylko host może zmienić widoczność pokoju.");
            return;
        }

        _roomService.SetRoomPublic(roomCode, isPublic);
        await Clients.Group(roomCode).SendAsync("OnRoomVisibilityChanged", isPublic);

        var updatedRooms = _roomService.GetPublicRooms();
        await Clients.Group("public-lobby").SendAsync("OnPublicRoomsUpdated", updatedRooms);
    }

    public async Task LeaveRoom(string roomCode)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        // Capture nickname before removal
        var room = _roomService.GetRoom(roomCode);
        var leavingNickname = room?.Players.FirstOrDefault(p => p.SessionId == sessionId)?.Nickname;
        
        var result = _roomService.LeaveRoom(sessionId);
        if (result.Room != null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomCode);
            
            if (!result.RoomDeleted)
            {
                await Clients.Group(roomCode).SendAsync("OnPlayerLeft", leavingNickname ?? sessionId);
                
                if (result.NewHostSessionId != null && result.NewHostNickname != null)
                {
                    await Clients.Group(roomCode).SendAsync("OnNewHost", result.NewHostNickname, result.NewHostSessionId);
                }
            }
        }
    }

    public async Task KickPlayer(string roomCode, string playerSessionId)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        var result = _roomService.KickPlayer(sessionId, playerSessionId);
        
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("OnError", result.Error);
            return;
        }

        // Send kick notification to the kicked player's current connection
        var kickedConnId = _roomService.GetConnectionId(playerSessionId);
        if (kickedConnId != null)
        {
            await Clients.Client(kickedConnId).SendAsync("OnKicked");
            await Groups.RemoveFromGroupAsync(kickedConnId, roomCode);
        }
        await Clients.Group(roomCode).SendAsync("OnPlayerKicked", playerSessionId);
    }

    /// <summary>
    /// Dołącza do gry (używane przy nawigacji do strony gry).
    /// </summary>
    public async Task<bool> JoinGame(string roomCode, string nickname)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room == null) return false;

        var player = room.Players.FirstOrDefault(p => p.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase));
        if (player == null) return false;

        // Update transport ConnectionId
        player.ConnectionId = Context.ConnectionId;
        _roomService.UpdatePlayerConnection(player.SessionId, Context.ConnectionId);
        
        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
        return true;
    }

    #endregion

    #region Stan gotowości

    public async Task SetReady(string roomCode, bool isReady)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        if (_roomService.SetPlayerReady(sessionId, isReady))
        {
            await Clients.Group(roomCode).SendAsync("OnPlayerReadyChanged", sessionId, isReady);
        }
    }

    #endregion

    #region Ustawienia

    public async Task UpdateGameSettings(string roomCode, List<string> categoryNames, int roundCount)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        var room = _roomService.GetRoom(roomCode);
        if (room == null)
        {
            await Clients.Caller.SendAsync("OnError", "Pokój nie istnieje.");
            return;
        }

        if (room.HostSessionId != sessionId)
        {
            await Clients.Caller.SendAsync("OnError", "Tylko host może zmieniać ustawienia.");
            return;
        }

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

        var maxRounds = room.Settings.AvailableLetters.Count;
        if (roundCount > maxRounds)
        {
            roundCount = maxRounds;
        }

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
                selectedCategories.Add(new Category { Name = name, Icon = "star", IsCustom = true });
            }
        }

        room.Settings.SelectedCategories = selectedCategories;
        room.Settings.RoundCount = roundCount;

        await Clients.Group(roomCode).SendAsync("OnSettingsUpdated", 
            selectedCategories.Select(c => c.Name).ToList(), 
            roundCount);
    }

    public async Task UpdateLetterSettings(string roomCode, List<char> letters)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        var room = _roomService.GetRoom(roomCode);
        if (room == null)
        {
            await Clients.Caller.SendAsync("OnError", "Pokój nie istnieje.");
            return;
        }

        if (room.HostSessionId != sessionId)
        {
            await Clients.Caller.SendAsync("OnError", "Tylko host może zmieniać ustawienia.");
            return;
        }

        var minLetters = 5;
        if (letters == null || letters.Count < minLetters)
        {
            await Clients.Caller.SendAsync("OnError", $"Wybierz co najmniej {minLetters} liter (tyle ile rund).");
            return;
        }

        var validLetters = letters
            .Where(l => GameSettings.FullAlphabet.Contains(l))
            .Distinct()
            .ToList();

        if (validLetters.Count < minLetters)
        {
            await Clients.Caller.SendAsync("OnError", $"Wybierz co najmniej {minLetters} poprawnych liter.");
            return;
        }

        room.Settings.AvailableLetters = validLetters;

        await Clients.Group(roomCode).SendAsync("OnLetterSettingsUpdated", validLetters.Count);
    }

    public async Task UpdateMaxPlayers(string roomCode, int maxPlayers)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        var room = _roomService.GetRoom(roomCode);
        if (room == null)
        {
            await Clients.Caller.SendAsync("OnError", "Pokój nie istnieje.");
            return;
        }

        if (room.HostSessionId != sessionId)
        {
            await Clients.Caller.SendAsync("OnError", "Tylko host może zmieniać ustawienia.");
            return;
        }

        // Cannot set below current player count
        if (maxPlayers < room.Players.Count)
        {
            maxPlayers = room.Players.Count;
        }

        // Clamp to valid range
        if (maxPlayers < 2) maxPlayers = 2;
        if (maxPlayers > 50) maxPlayers = 50;

        room.Settings.MaxPlayers = maxPlayers;
        await Clients.Group(roomCode).SendAsync("OnMaxPlayersUpdated", maxPlayers);

        // Update public lobby if room is public
        if (room.IsPublic)
        {
            var updatedRooms = _roomService.GetPublicRooms();
            await Clients.Group("public-lobby").SendAsync("OnPublicRoomsUpdated", updatedRooms);
        }
    }

    #endregion

    #region Gra

    public async Task StartGame(string roomCode)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        var room = _roomService.GetRoom(roomCode);
        if (room == null) return;

        _gameService.ResetGameForLobby(roomCode);

        var result = _gameService.StartGame(sessionId);
        
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("OnError", result.Error);
            return;
        }

        room = _roomService.GetRoom(roomCode);
        if (room?.CurrentGame != null)
        {
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

    public async Task TriggerStop(string roomCode, Dictionary<string, string> answers)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        _logger.LogDebug("TriggerStop: session={SessionId}, conn={ConnectionId}, room={RoomCode}", sessionId, Context.ConnectionId, roomCode);
        
        var room = _roomService.GetRoom(roomCode);
        var countdownSeconds = room?.Settings.CountdownSeconds ?? 10;
        
        var result = _gameService.TriggerStop(roomCode, sessionId, countdownSeconds);
        
        if (result.Success)
        {
            _gameService.SubmitAnswers(roomCode, sessionId, answers);
            await Clients.Caller.SendAsync("OnAnswersSubmitted");
            
            await Clients.Group(roomCode).SendAsync("OnStopTriggered", sessionId, result.EndTime);
            await Clients.Group(roomCode).SendAsync("OnPlayerSubmitted", sessionId);
            
            if (room?.CurrentGame != null)
            {
                var allSubmitted = room.Players.All(p => 
                    room.CurrentGame.RoundAnswers.ContainsKey(p.SessionId));
                
                if (allSubmitted)
                {
                    await EndRound(roomCode);
                }
            }
        }
        else
        {
            if (_gameService.SubmitAnswers(roomCode, sessionId, answers))
            {
                await Clients.Caller.SendAsync("OnAnswersSubmitted");
                await Clients.Group(roomCode).SendAsync("OnPlayerSubmitted", sessionId);
                
                if (room?.CurrentGame != null)
                {
                    var allSubmitted = room.Players.All(p => 
                        room.CurrentGame.RoundAnswers.ContainsKey(p.SessionId));
                    
                    if (allSubmitted)
                    {
                        await EndRound(roomCode);
                    }
                }
            }
        }
    }

    public async Task AddTime(string roomCode, int additionalSeconds)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        var result = _gameService.AddTime(roomCode, sessionId, additionalSeconds);
        
        if (result.Success)
        {
            await Clients.Group(roomCode).SendAsync("OnTimeAdded", result.NewEndTime);
        }
    }

    public async Task SubmitAnswers(string roomCode, Dictionary<string, string> answers)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        if (_gameService.SubmitAnswers(roomCode, sessionId, answers))
        {
            await Clients.Caller.SendAsync("OnAnswersSubmitted");
            await Clients.Group(roomCode).SendAsync("OnPlayerSubmitted", sessionId);
            
            var room = _roomService.GetRoom(roomCode);
            if (room?.CurrentGame != null)
            {
                var allSubmitted = room.Players.All(p => 
                    room.CurrentGame.RoundAnswers.ContainsKey(p.SessionId));
                
                if (allSubmitted)
                {
                    await EndRound(roomCode);
                }
            }
        }
    }

    public async Task EndRound(string roomCode)
    {
        var answersForVoting = _gameService.EndRoundAndPrepareVoting(roomCode);
        await Clients.Group(roomCode).SendAsync("OnRoundEnded", answersForVoting.Count);
    }

    public async Task StartNextRound(string roomCode)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        var room = _roomService.GetRoom(roomCode);
        if (room == null) return;
        
        var caller = room.Players.FirstOrDefault(p => p.SessionId == sessionId);
        if (caller?.IsHost != true)
        {
            await Clients.Caller.SendAsync("OnError", "Tylko host może rozpocząć następną rundę.");
            return;
        }

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

    public async Task RerollLetter(string roomCode)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        var result = _gameService.RerollLetter(roomCode, sessionId);

        if (result.Success)
        {
            await Clients.Group(roomCode).SendAsync("OnLetterRerolled", result.NewLetter);
        }
        else
        {
            await Clients.Caller.SendAsync("OnError", result.Error);
        }
    }

    #endregion

    #region Głosowanie

    public async Task VoteAnswer(string roomCode, string answerId, string voteType)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        var room = _roomService.GetRoom(roomCode);
        var answer = room?.CurrentGame?.AnswersForVoting.FirstOrDefault(a => a.Id == answerId);
        
        if (answer != null)
        {
            answer.VotesValid.Remove(sessionId);
            answer.VotesInvalid.Remove(sessionId);
            answer.VotesDuplicate.Remove(sessionId);
            
            switch (voteType.ToLowerInvariant())
            {
                case "valid":
                    answer.VotesValid.Add(sessionId);
                    break;
                case "invalid":
                    answer.VotesInvalid.Add(sessionId);
                    break;
                case "duplicate":
                    answer.VotesDuplicate.Add(sessionId);
                    break;
            }

            await Clients.Group(roomCode).SendAsync("OnVoteCast", answerId, 
                answer.VotesValid.Count, answer.VotesInvalid.Count, answer.VotesDuplicate.Count);
        }
    }

    public async Task SubmitVotes(string roomCode)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        var room = _roomService.GetRoom(roomCode);
        if (room?.CurrentGame == null) return;

        room.CurrentGame.VotesSubmittedBy.Add(sessionId);

        var submittedCount = room.CurrentGame.VotesSubmittedBy.Count;
        var totalPlayers = room.Players.Count;

        await Clients.Group(roomCode).SendAsync("OnVotesSubmitted", submittedCount);

        if (submittedCount >= totalPlayers)
        {
            await FinalizeVoting(roomCode);
        }
    }

    public async Task FinalizeVoting(string roomCode)
    {
        _gameService.FinalizeVotingAndCalculateScores(roomCode);
        
        await Clients.Group(roomCode).SendAsync("OnVotingEnded");
    }

    public async Task EndGame(string roomCode)
    {
        var sessionId = ResolveSessionId();
        if (sessionId == null) return;
        
        var room = _roomService.GetRoom(roomCode);
        if (room == null) return;
        
        if (room.HostSessionId != sessionId)
        {
            await Clients.Caller.SendAsync("OnError", "Tylko host może zakończyć grę.");
            return;
        }

        if (room.CurrentGame != null)
        {
            room.CurrentGame.Phase = RoundPhase.FinalResults;
            
            foreach (var player in room.Players)
            {
                player.IsReady = player.IsHost;
            }
        }

        await Clients.Group(roomCode).SendAsync("OnGameFinished");
    }

    public async Task ReturnToLobby(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room == null) return;

        await Clients.Caller.SendAsync("OnReturnToLobby", roomCode);
    }

    #endregion

    #region Chat

    public async Task SendChatMessage(string roomCode, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        
        if (message.Length > 200)
        {
            message = message[..200];
        }

        var sessionId = ResolveSessionId();
        if (sessionId == null) return;

        var room = _roomService.GetRoom(roomCode);
        var player = room?.Players.FirstOrDefault(p => p.SessionId == sessionId);
        if (room != null && player != null)
        {
            room.ChatMessages.Add(new ChatMessage
            {
                Nickname = player.Nickname,
                Text = message,
                IsSystem = false
            });
            
            if (room.ChatMessages.Count > 50)
            {
                room.ChatMessages.RemoveAt(0);
            }

            await Clients.Group(roomCode).SendAsync("OnChatMessage", player.Nickname, message, false);
        }
    }

    public void AddSystemMessage(string roomCode, string text)
    {
        var room = _roomService.GetRoom(roomCode);
        AddSystemMessageInternal(room, text);
    }

    private void AddSystemMessageInternal(Room? room, string text)
    {
        if (room == null) return;
        room.ChatMessages.Add(new ChatMessage
        {
            Nickname = "System",
            Text = text,
            IsSystem = true
        });
        if (room.ChatMessages.Count > 50)
            room.ChatMessages.RemoveAt(0);
    }

    #endregion

    #region Anty-cheat

    public async Task<bool> ReportViolation(string roomCode, string violationType, double durationSeconds, int roundNumber)
    {
        var sessionId = ResolveSessionId();
        _logger.LogInformation("ReportViolation: room={RoomCode}, type={ViolationType}, duration={Duration}s, round={Round}, session={SessionId}, conn={ConnectionId}",
            roomCode, violationType, durationSeconds, roundNumber, sessionId, Context.ConnectionId);
        
        if (sessionId == null)
        {
            _logger.LogWarning("ReportViolation: REJECTED — could not resolve session for conn={ConnectionId}", Context.ConnectionId);
            return false;
        }

        var room = _roomService.GetRoom(roomCode);
        var player = room?.Players.FirstOrDefault(p => p.SessionId == sessionId);
        
        _logger.LogDebug("ReportViolation: room found={RoomFound}, player found={PlayerFound}, playerNickname={Nickname}",
            room != null, player != null, player?.Nickname);
        
        if (player != null && Enum.TryParse<ViolationType>(violationType, out var type))
        {
            var penalty = Violation.CalculatePenalty(player.Violations, type, durationSeconds);
            
            var violation = new Violation
            {
                Type = type,
                DurationSeconds = durationSeconds,
                Penalty = penalty,
                RoundNumber = roundNumber
            };
            
            player.Violations.Add(violation);
            player.TotalScore -= penalty;
            player.RoundScore -= penalty;
            
            _logger.LogInformation("ReportViolation: ACCEPTED — {Nickname} violation #{Count}, penalty={Penalty}, totalScore={TotalScore}, roundScore={RoundScore}",
                player.Nickname, player.Violations.Count, penalty, player.TotalScore, player.RoundScore);

            await Clients.Group(roomCode).SendAsync("OnAntiCheatViolation", 
                sessionId, 
                violationType, 
                durationSeconds, 
                penalty);
                
            return true;
        }
        
        _logger.LogWarning("ReportViolation: NOT PROCESSED — player null={PlayerNull}, parsedType={ParsedOk}",
            player == null, Enum.TryParse<ViolationType>(violationType, out _));
        return false;
    }

    #endregion
}
