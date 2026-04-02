using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using NationsCities.Models;

namespace NationsCities.Services;

/// <summary>
/// Centralized client-side state management for the SPA game flow.
/// Manages the single SignalR connection and all game state.
/// </summary>
public class ClientGameStateService : IAsyncDisposable
{
    private readonly NavigationManager _navigation;
    private readonly IJSRuntime _jsRuntime;
    private readonly RoomService _roomService;
    private readonly ILogger<ClientGameStateService> _logger;
    
    private HubConnection? _hubConnection;
    private DotNetObjectReference<ClientGameStateService>? _dotNetRef;
    private bool _isInitialized;
    private string? _tabId;

    // ===== STATE =====
    
    /// <summary>Current phase of the game.</summary>
    public GamePhase CurrentPhase { get; private set; } = GamePhase.Home;
    
    /// <summary>Unique session ID (persists across refreshes via localStorage).</summary>
    public string? SessionId { get; private set; }
    
    /// <summary>Current room code.</summary>
    public string? RoomCode { get; private set; }
    
    /// <summary>Player's nickname.</summary>
    public string? Nickname { get; private set; }
    
    /// <summary>Current room data.</summary>
    public Room? CurrentRoom { get; private set; }
    
    /// <summary>Last error message.</summary>
    public string? LastError { get; private set; }

    // ===== COMPUTED =====
    
    /// <summary>Whether the current player is the host.</summary>
    public bool IsHost => MyPlayer?.IsHost ?? false;
    
    /// <summary>Whether connected to the hub.</summary>
    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
    
    /// <summary>Current game state (shortcut).</summary>
    public GameState? CurrentGame => CurrentRoom?.CurrentGame;
    
    /// <summary>The current player.</summary>
    public Player? MyPlayer => CurrentRoom?.Players
        .FirstOrDefault(p => p.Nickname.Equals(Nickname, StringComparison.OrdinalIgnoreCase));

    // ===== EVENTS =====
    
    /// <summary>Fired when any state changes.</summary>
    public event Action? OnStateChanged;
    
    /// <summary>Fired when an error occurs.</summary>
    public event Action<string>? OnError;
    
    /// <summary>Fired when user tries to navigate away (back button).</summary>
    public event Action? OnNavigateAway;
    
    /// <summary>Fired when a chat message is received.</summary>
    public event Action<string, string, bool>? OnChatMessage;

    /// <summary>Fired when public rooms list changes (for join screen).</summary>
    public event Action<List<PublicRoomInfo>>? OnPublicRoomsUpdated;
    
    /// <summary>Fired when stop is triggered (playerId, endTime).</summary>
    public event Action<string, DateTime>? OnStopTriggered;
    
    /// <summary>Fired when time is added to countdown (new endTime).</summary>
    public event Action<DateTime>? OnTimeAdded;
    
    /// <summary>Fired when round ends (answer count).</summary>
    public event Action<int>? OnRoundEnded;
    
    /// <summary>Fired when voting ends.</summary>
    public event Action? OnVotingEnded;
    
    /// <summary>Fired when votes submitted count changes.</summary>
    public event Action<int>? OnVotesSubmittedChanged;
    
    /// <summary>Fired when a new round starts (letter).</summary>
    public event Action<char>? OnNewRound;

    /// <summary>Fired when the letter is rerolled mid-round (new letter).</summary>
    public event Action<char>? OnLetterRerolled;

    public ClientGameStateService(
        NavigationManager navigation, 
        IJSRuntime jsRuntime,
        RoomService roomService,
        ILogger<ClientGameStateService> logger)
    {
        _navigation = navigation;
        _jsRuntime = jsRuntime;
        _roomService = roomService;
        _logger = logger;
    }

    // ===== LIFECYCLE =====

    /// <summary>
    /// Initialize the service - check for existing session, setup navigation guard.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        try
        {
            // Generate or retrieve session ID
            SessionId = await _jsRuntime.InvokeAsync<string?>("gameSession.getOrCreateSessionId");
            _tabId = await _jsRuntime.InvokeAsync<string>("gameSession.getTabId");
            
            _logger.LogInformation("Initialized: SessionId={SessionId}, TabId={TabId}", SessionId, _tabId);
            
            // Setup navigation guard for back button
            _dotNetRef = DotNetObjectReference.Create(this);
            await _jsRuntime.InvokeVoidAsync("gameSession.setupNavigationGuard", _dotNetRef);
            
            // Check for existing session to reconnect
            var savedSession = await _jsRuntime.InvokeAsync<SavedGameSession?>("gameSession.load");
            if (savedSession != null && !string.IsNullOrEmpty(savedSession.RoomCode))
            {
                _logger.LogInformation("Found saved session: room={RoomCode}, nickname={Nickname}", savedSession.RoomCode, savedSession.Nickname);
                RoomCode = savedSession.RoomCode;
                Nickname = savedSession.Nickname;
                await ReconnectAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Init error");
        }
    }

    /// <summary>
    /// Initialize with a room code from URL (deep link).
    /// </summary>
    public async Task InitializeWithRoomCodeAsync(string roomCode)
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }
        
        // If we don't have a session for this room, show home to join
        if (RoomCode != roomCode)
        {
            RoomCode = roomCode;
            // Will need nickname - show join modal in HomeView
            SetPhase(GamePhase.Home);
        }
    }

    private async Task EnsureConnectedAsync()
    {
        if (_hubConnection?.State == HubConnectionState.Connected) return;
        
        _logger.LogDebug("EnsureConnectedAsync: creating new hub connection");
        _hubConnection?.DisposeAsync().AsTask().Wait(500);
        
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(_navigation.ToAbsoluteUri("/gamehub"))
            .WithAutomaticReconnect(new[] { 
                TimeSpan.Zero, 
                TimeSpan.FromMilliseconds(500), 
                TimeSpan.FromSeconds(1), 
                TimeSpan.FromSeconds(2), 
                TimeSpan.FromSeconds(3), 
                TimeSpan.FromSeconds(5), 
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(20)
            })
            .Build();

        _hubConnection.Reconnecting += error =>
        {
            _logger.LogWarning(error, "Hub connection reconnecting");
            SetPhase(GamePhase.Disconnected);
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += async _ =>
        {
            _logger.LogInformation("Hub connection reconnected, calling ReconnectAsync");
            await ReconnectAsync();
        };

        _hubConnection.Closed += error =>
        {
            _logger.LogWarning(error, "Hub connection closed, currentPhase={Phase}", CurrentPhase);
            if (CurrentPhase != GamePhase.Home)
            {
                SetPhase(GamePhase.Error);
                LastError = "Połączenie zostało zamknięte.";
                NotifyStateChanged();
            }
            return Task.CompletedTask;
        };

        SetupHubCallbacks();
        await _hubConnection.StartAsync();
    }

    private void SetupHubCallbacks()
    {
        if (_hubConnection == null) return;

        // Room events
        _hubConnection.On<string>("OnRoomCreated", async roomCode =>
        {
            RoomCode = roomCode;
            CurrentRoom = _roomService.GetRoom(roomCode);
            await SaveSessionAsync();
            SetPhase(GamePhase.Lobby);
        });

        _hubConnection.On<string, string>("OnPlayerJoined", (nickname, sessionId) =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            NotifyStateChanged();
        });

        _hubConnection.On<string>("OnPlayerLeft", nickname =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            NotifyStateChanged();
        });

        _hubConnection.On<string>("OnPlayerKicked", sessionId =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            NotifyStateChanged();
        });

        _hubConnection.On<string, bool>("OnPlayerReadyChanged", (sessionId, isReady) =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            NotifyStateChanged();
        });

        _hubConnection.On<string, string>("OnNewHost", (nickname, sessionId) =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            NotifyStateChanged();
        });

        _hubConnection.On("OnKicked", async () =>
        {
            await ClearSessionAsync();
            LastError = "Zostałeś wyrzucony z pokoju.";
            SetPhase(GamePhase.Home);
        });

        _hubConnection.On<string>("OnJoinError", error =>
        {
            LastError = error;
            OnError?.Invoke(error);
            NotifyStateChanged();
        });

        _hubConnection.On<List<string>, int>("OnSettingsUpdated", (categories, roundCount) =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            NotifyStateChanged();
        });

        _hubConnection.On<bool>("OnRoomVisibilityChanged", isPublic =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            NotifyStateChanged();
        });

        // Game events
        _hubConnection.On<List<string>, int>("OnGameStarted", (categories, roundCount) =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            // Note: OnRoundStarted will follow with actual letter
            NotifyStateChanged();
        });

        _hubConnection.On<char, int>("OnRoundStarted", (letter, roundNumber) =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            _ = StartAntiCheatAsync();
            SetPhase(GamePhase.Playing);
        });

        _hubConnection.On<char>("OnNewRound", letter =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            _ = ResumeAntiCheatAsync(CurrentGame?.CurrentRound ?? 1);
            OnNewRound?.Invoke(letter);
            SetPhase(GamePhase.Playing);
        });

        _hubConnection.On<string, DateTime>("OnStopTriggered", (triggeredBy, endTime) =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            OnStopTriggered?.Invoke(triggeredBy, endTime);
            NotifyStateChanged();
        });

        _hubConnection.On<char>("OnLetterRerolled", newLetter =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            OnLetterRerolled?.Invoke(newLetter);
            NotifyStateChanged();
        });

        _hubConnection.On<int>("OnLetterSettingsUpdated", letterCount =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            NotifyStateChanged();
        });

        _hubConnection.On<int>("OnMaxPlayersUpdated", maxPlayers =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            NotifyStateChanged();
        });

        _hubConnection.On<DateTime>("OnTimeAdded", endTime =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            OnTimeAdded?.Invoke(endTime);
            NotifyStateChanged();
        });

        _hubConnection.On<int>("OnRoundEnded", answerCount =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            _ = PauseAntiCheatAsync();
            OnRoundEnded?.Invoke(answerCount);
            SetPhase(GamePhase.Voting);
        });

        _hubConnection.On("OnVotingEnded", () =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            OnVotingEnded?.Invoke();
            SetPhase(GamePhase.RoundResults);
        });

        _hubConnection.On("OnGameFinished", () =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            OnVotingEnded?.Invoke();
            SetPhase(GamePhase.FinalResults);
        });

        _hubConnection.On<string>("OnReturnToLobby", roomCode =>
        {
            CurrentRoom = _roomService.GetRoom(roomCode);
            _ = ClearAntiCheatAsync();
            SetPhase(GamePhase.Lobby);
        });

        _hubConnection.On("OnGameEnded", async () =>
        {
            await ClearSessionAsync();
            SetPhase(GamePhase.Home);
        });

        // Anti-cheat: server recorded a violation — refresh state so UI re-renders
        _hubConnection.On<string, string, double, int>("OnAntiCheatViolation", 
            (sessionId, violationType, durationSeconds, penalty) =>
        {
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            NotifyStateChanged();
        });

        // Chat
        _hubConnection.On<string, string, bool>("OnChatMessage", (nickname, message, isSystem) =>
        {
            // Refresh room so ChatMessages list is up-to-date
            CurrentRoom = _roomService.GetRoom(RoomCode ?? "");
            OnChatMessage?.Invoke(nickname, message, isSystem);
            NotifyStateChanged();
        });

        // Public rooms real-time updates
        _hubConnection.On<List<PublicRoomInfo>>("OnPublicRoomsUpdated", rooms =>
        {
            OnPublicRoomsUpdated?.Invoke(rooms);
        });

        // Voting events
        _hubConnection.On<string, int, int, int>("OnVoteCast", (answerId, valid, invalid, duplicate) =>
        {
            NotifyStateChanged();
        });

        _hubConnection.On<int>("OnVotesSubmitted", count =>
        {
            OnVotesSubmittedChanged?.Invoke(count);
            NotifyStateChanged();
        });
    }

    // ===== ROOM ACTIONS =====

    /// <summary>Create a new room.</summary>
    public async Task CreateRoomAsync(string nickname)
    {
        Nickname = nickname;
        await EnsureConnectedAsync();
        
        if (_hubConnection == null) return;
        await _hubConnection.InvokeAsync("CreateRoom", nickname, SessionId);
    }

    /// <summary>Join an existing room.</summary>
    public async Task JoinRoomAsync(string roomCode, string nickname)
    {
        RoomCode = roomCode.ToUpperInvariant();
        Nickname = nickname;
        await EnsureConnectedAsync();
        
        if (_hubConnection == null) return;
        await _hubConnection.InvokeAsync("JoinRoom", RoomCode, nickname, SessionId);
    }

    /// <summary>Reconnect to existing session.</summary>
    public async Task ReconnectAsync()
    {
        if (string.IsNullOrEmpty(RoomCode) || string.IsNullOrEmpty(SessionId)) return;
        
        await EnsureConnectedAsync();
        if (_hubConnection == null) return;

        try
        {
            var snapshot = await _hubConnection.InvokeAsync<GameStateSnapshot?>(
                "ReconnectSession", RoomCode, SessionId, Nickname);

            if (snapshot?.Room != null)
            {
                CurrentRoom = snapshot.Room;
                Nickname = snapshot.Nickname;
                
                _logger.LogInformation("ReconnectAsync: success — phase={Phase}, nickname={Nickname}, room={RoomCode}", snapshot.Phase, snapshot.Nickname, RoomCode);
                
                // ALWAYS re-register the Blazor handler after reconnect.
                _logger.LogDebug("ReconnectAsync: re-registering anti-cheat handler");
                _ = RegisterAntiCheatHandlerOnlyAsync();
                
                if (snapshot.Phase == GamePhase.Playing)
                {
                    _logger.LogDebug("ReconnectAsync: resuming anti-cheat tracking for round {Round}", CurrentGame?.CurrentRound ?? 1);
                    _ = ResumeAntiCheatAsync(CurrentGame?.CurrentRound ?? 1);
                }
                
                SetPhase(snapshot.Phase);
            }
            else
            {
                // Session invalid - go home
                await ClearSessionAsync();
                SetPhase(GamePhase.Home);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReconnectAsync failed");
            SetPhase(GamePhase.Home);
        }
    }

    /// <summary>Leave the current game.</summary>
    public async Task LeaveGameAsync()
    {
        if (_hubConnection != null && !string.IsNullOrEmpty(RoomCode))
        {
            try
            {
                await _hubConnection.InvokeAsync("LeaveRoom", RoomCode);
            }
            catch { }
        }
        
        await ClearSessionAsync();
        await ClearAntiCheatAsync();
        SetPhase(GamePhase.Home);
    }

    // ===== GAME ACTIONS =====

    /// <summary>Set ready status.</summary>
    public async Task SetReadyAsync(bool ready)
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("SetReady", RoomCode, ready);
    }

    /// <summary>Start the game (host only).</summary>
    public async Task StartGameAsync()
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("StartGame", RoomCode);
    }

    /// <summary>Submit answers for current round.</summary>
    public async Task SubmitAnswersAsync(Dictionary<string, string> answers)
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("SubmitAnswers", RoomCode, answers);
    }

    /// <summary>Trigger STOP button.</summary>
    public async Task TriggerStopAsync(Dictionary<string, string> answers)
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("TriggerStop", RoomCode, answers);
    }

    /// <summary>Add seconds to countdown (stop triggerer only).</summary>
    public async Task AddTimeAsync(int seconds)
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("AddTime", RoomCode, seconds);
    }

    /// <summary>Vote on an answer.</summary>
    public async Task VoteAnswerAsync(string answerId, string voteType)
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("VoteAnswer", RoomCode, answerId, voteType);
    }

    /// <summary>Submit votes for current round.</summary>
    public async Task SubmitVotesAsync()
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("SubmitVotes", RoomCode);
    }

    /// <summary>Force end voting (host only).</summary>
    public async Task FinalizeVotingAsync()
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("FinalizeVoting", RoomCode);
    }

    /// <summary>Start next round (host only).</summary>
    public async Task StartNextRoundAsync()
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("StartNextRound", RoomCode);
    }

    /// <summary>Reroll the letter mid-round (host only).</summary>
    public async Task RerollLetterAsync()
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("RerollLetter", RoomCode);
    }

    /// <summary>Update available letters (host only, in lobby).</summary>
    public async Task UpdateLetterSettingsAsync(List<char> letters)
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("UpdateLetterSettings", RoomCode, letters);
    }

    /// <summary>Return to lobby after game.</summary>
    public async Task ReturnToLobbyAsync()
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await ClearAntiCheatAsync();
        await _hubConnection.InvokeAsync("ReturnToLobby", RoomCode);
    }

    /// <summary>End the game completely.</summary>
    public async Task EndGameAsync()
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await ClearAntiCheatAsync();
        await _hubConnection.InvokeAsync("EndGame", RoomCode);
    }

    /// <summary>Send chat message.</summary>
    public async Task SendChatMessageAsync(string message)
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("SendChatMessage", RoomCode, message);
    }

    /// <summary>Connect, fetch initial public rooms list and subscribe to real-time updates.</summary>
    public async Task<List<PublicRoomInfo>> StartPublicRoomsBrowsingAsync()
    {
        await EnsureConnectedAsync();
        if (_hubConnection == null) return [];
        var rooms = await _hubConnection.InvokeAsync<List<PublicRoomInfo>>("GetPublicRooms");
        await _hubConnection.InvokeAsync("SubscribePublicRooms");
        return rooms;
    }

    /// <summary>Unsubscribe from real-time public rooms updates.</summary>
    public async Task StopPublicRoomsBrowsingAsync()
    {
        try
        {
            if (_hubConnection?.State == HubConnectionState.Connected)
                await _hubConnection.InvokeAsync("UnsubscribePublicRooms");
        }
        catch { }
    }

    /// <summary>Update game settings - categories and round count (host only).</summary>
    public async Task UpdateGameSettingsAsync(List<string> categories, int roundCount)
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("UpdateGameSettings", RoomCode, categories, roundCount);
    }

    /// <summary>Update max players (host only).</summary>
    public async Task UpdateMaxPlayersAsync(int maxPlayers)
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("UpdateMaxPlayers", RoomCode, maxPlayers);
    }

    /// <summary>Set room visibility (host only).</summary>
    public async Task SetRoomPublicAsync(bool isPublic)
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("SetRoomPublic", RoomCode, isPublic);
    }

    /// <summary>Kick player (host only).</summary>
    public async Task KickPlayerAsync(string sessionId)
    {
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode)) return;
        await _hubConnection.InvokeAsync("KickPlayer", RoomCode, sessionId);
    }

    // ===== ANTI-CHEAT =====

    private async Task StartAntiCheatAsync()
    {
        try
        {
            _logger.LogDebug("StartAntiCheatAsync: registering handler, dotNetRef null={IsNull}", _dotNetRef == null);
            if (_dotNetRef != null)
            {
                await _jsRuntime.InvokeVoidAsync("registerAntiCheatHandler", _dotNetRef);
            }
            _logger.LogDebug("StartAntiCheatAsync: calling startTracking, room={RoomCode}, round={Round}", RoomCode, CurrentGame?.CurrentRound ?? 1);
            await _jsRuntime.InvokeVoidAsync("antiCheatTracker.startTracking", 
                RoomCode, CurrentGame?.CurrentRound ?? 1);
            _logger.LogInformation("StartAntiCheatAsync: tracking started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anti-cheat start error");
        }
    }

    private async Task PauseAntiCheatAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("antiCheatTracker.pauseTracking");
        }
        catch { }
    }
    
    /// <summary>Public method for RoundView to start anti-cheat tracking.</summary>
    public async Task StartAntiCheatTrackingAsync(int roundNumber)
    {
        try
        {
            _logger.LogDebug("StartAntiCheatTrackingAsync: round={Round}, dotNetRef null={IsNull}", roundNumber, _dotNetRef == null);
            if (_dotNetRef != null)
            {
                await _jsRuntime.InvokeVoidAsync("registerAntiCheatHandler", _dotNetRef);
            }
            
            if (roundNumber > 1)
            {
                _logger.LogDebug("StartAntiCheatTrackingAsync: resumeTracking (round > 1)");
                await _jsRuntime.InvokeVoidAsync("antiCheatTracker.resumeTracking", RoomCode, roundNumber);
            }
            else
            {
                _logger.LogDebug("StartAntiCheatTrackingAsync: startTracking (round 1)");
                await _jsRuntime.InvokeVoidAsync("antiCheatTracker.startTracking", RoomCode, roundNumber);
            }
            _logger.LogInformation("StartAntiCheatTrackingAsync: tracking active for round {Round}", roundNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anti-cheat tracking start error");
        }
    }
    
    /// <summary>Public method for RoundView to pause anti-cheat tracking.</summary>
    public async Task PauseAntiCheatTrackingAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("antiCheatTracker.pauseTracking");
        }
        catch { }
    }

    private async Task ResumeAntiCheatAsync(int roundNumber)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("antiCheatTracker.resumeTracking", 
                RoomCode, roundNumber);
        }
        catch { }
    }

    /// <summary>
    /// Registers the Blazor handler so pending violations in the JS queue can be flushed.
    /// Does NOT start/resume tracking — used when reconnecting to Voting/Results phase.
    /// </summary>
    private async Task RegisterAntiCheatHandlerOnlyAsync()
    {
        try
        {
            _logger.LogDebug("RegisterAntiCheatHandlerOnlyAsync: dotNetRef null={IsNull}", _dotNetRef == null);
            if (_dotNetRef != null)
            {
                await _jsRuntime.InvokeVoidAsync("registerAntiCheatHandler", _dotNetRef);
                _logger.LogDebug("RegisterAntiCheatHandlerOnlyAsync: handler registered OK");
            }
            else
            {
                _logger.LogWarning("RegisterAntiCheatHandlerOnlyAsync: dotNetRef is null, cannot register");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Register anti-cheat handler error");
        }
    }

    private async Task ClearAntiCheatAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("antiCheatTracker.clearSession");
        }
        catch { }
    }

    /// <summary>Called from JavaScript when a violation is detected. Returns true if reported to server.</summary>
    [JSInvokable]
    public async Task<bool> ReportViolationFromJS(string violationType, double durationSeconds, int roundNumber)
    {
        _logger.LogInformation("ReportViolationFromJS: type={Type}, duration={Duration}s, round={Round}, phase={Phase}, hubState={HubState}",
            violationType, durationSeconds, roundNumber, CurrentPhase, _hubConnection?.State);
        
        if (_hubConnection == null || string.IsNullOrEmpty(RoomCode))
        {
            _logger.LogWarning("ReportViolationFromJS: BLOCKED — hub={HubNull}, room={RoomCode}", _hubConnection == null, RoomCode);
            return false;
        }

        if (CurrentPhase == GamePhase.Home || CurrentPhase == GamePhase.Lobby || CurrentPhase == GamePhase.Error)
        {
            _logger.LogWarning("ReportViolationFromJS: BLOCKED — phase={Phase} not active game", CurrentPhase);
            return false;
        }
        
        try
        {
            _logger.LogDebug("ReportViolationFromJS: invoking hub ReportViolation...");
            var reported = await _hubConnection.InvokeAsync<bool>(
                "ReportViolation",
                RoomCode,
                violationType,
                durationSeconds,
                roundNumber);

            _logger.LogInformation("ReportViolationFromJS: hub returned {Reported}", reported);
            return reported;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReportViolationFromJS: hub invocation failed");
            return false;
        }
    }

    /// <summary>Called from JavaScript when back button is pressed.</summary>
    [JSInvokable]
    public void OnBackButtonPressed()
    {
        if (CurrentPhase != GamePhase.Home)
        {
            OnNavigateAway?.Invoke();
        }
    }

    /// <summary>Called from JavaScript when duplicate tab is detected.</summary>
    [JSInvokable]
    public void OnDuplicateTabDetected()
    {
        SetPhase(GamePhase.Error);
        LastError = "Gra jest otwarta w innej karcie.";
        NotifyStateChanged();
    }

    // ===== HELPERS =====

    private void SetPhase(GamePhase newPhase)
    {
        if (CurrentPhase == newPhase) return;
        
        _logger.LogInformation("Phase change: {OldPhase} → {NewPhase}", CurrentPhase, newPhase);
        CurrentPhase = newPhase;
        
        // Sync to JS for anti-cheat
        _ = _jsRuntime.InvokeVoidAsync("setGamePhase", newPhase.ToString());
        
        // Sync URL
        SyncUrlToPhase();
        
        NotifyStateChanged();
    }

    private void SyncUrlToPhase()
    {
        var targetUrl = CurrentPhase switch
        {
            GamePhase.Home => "/",
            _ when !string.IsNullOrEmpty(RoomCode) => $"/room/{RoomCode}",
            _ => "/"
        };

        var currentPath = new Uri(_navigation.Uri).AbsolutePath;
        if (currentPath != targetUrl && (currentPath == "/" || currentPath.StartsWith("/room")))
        {
            // Use JS history.replaceState to update the URL without triggering
            // Blazor's navigation pipeline. This avoids NavigationLock firing
            // on non-dispatcher threads when hub callbacks change the phase.
            _ = _jsRuntime.InvokeVoidAsync("history.replaceState", null, "", targetUrl);
        }
    }

    private void NotifyStateChanged()
    {
        OnStateChanged?.Invoke();
    }

    private async Task SaveSessionAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("gameSession.save", SessionId, RoomCode, Nickname);
        }
        catch { }
    }

    private async Task ClearSessionAsync()
    {
        RoomCode = null;
        Nickname = null;
        CurrentRoom = null;
        LastError = null;
        
        try
        {
            await _jsRuntime.InvokeVoidAsync("gameSession.clear");
        }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        _dotNetRef?.Dispose();
        
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }
    }

    // Helper class for saved session
    private record SavedGameSession(string? SessionId, string? RoomCode, string? Nickname);
}
