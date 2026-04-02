using NationsCities.Models;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NationsCities.Services;

/// <summary>
/// Serwis zarządzania pokojami gry.
/// Klucz domenowy gracza: SessionId (stabilny, przeżywa reconnect).
/// ConnectionId jest używany wyłącznie jako adres transportowy SignalR.
/// </summary>
public class RoomService
{
    private readonly ILogger<RoomService> _logger;
    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private readonly ConcurrentDictionary<string, string> _sessionRooms = new(); // SessionId -> RoomCode
    private readonly ConcurrentDictionary<string, string> _sessionConnections = new(); // SessionId -> ConnectionId (transport)
    
    // Pending disconnections for lobby grace period (SessionId -> PendingDisconnection)
    private readonly ConcurrentDictionary<string, PendingDisconnection> _pendingDisconnections = new();
    
    private const string RoomCodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ"; // bez I, O
    private static readonly TimeSpan LobbyDisconnectGracePeriod = TimeSpan.FromSeconds(5);

    public RoomService(ILogger<RoomService> logger)
    {
        _logger = logger;
    }

    // ===== RESOLVER: ConnectionId <-> SessionId =====

    /// <summary>
    /// Zwraca SessionId gracza na podstawie bieżącego ConnectionId.
    /// Używane w hubie do tłumaczenia Context.ConnectionId → stabilny klucz.
    /// </summary>
    public string? GetSessionByConnection(string connectionId)
    {
        foreach (var kvp in _sessionConnections)
        {
            if (kvp.Value == connectionId)
                return kvp.Key;
        }
        return null;
    }

    /// <summary>
    /// Zwraca bieżący ConnectionId gracza na podstawie SessionId.
    /// Używane do wysyłki wiadomości SignalR.
    /// </summary>
    public string? GetConnectionId(string sessionId)
    {
        _sessionConnections.TryGetValue(sessionId, out var connectionId);
        return connectionId;
    }

    /// <summary>
    /// Aktualizuje ConnectionId gracza po ponownym połączeniu (rejoin/reconnect).
    /// </summary>
    public void UpdatePlayerConnection(string sessionId, string newConnectionId)
    {
        _sessionConnections[sessionId] = newConnectionId;
    }

    // ===== POKOJE =====

    /// <summary>
    /// Tworzy nowy pokój.
    /// </summary>
    public Room CreateRoom(string connectionId, string nickname, string sessionId)
    {
        var roomCode = GenerateUniqueRoomCode();
        var host = new Player
        {
            SessionId = sessionId,
            ConnectionId = connectionId,
            Nickname = nickname,
            AvatarColor = Player.GenerateAvatarColor(),
            IsHost = true,
            IsReady = true
        };

        var room = new Room
        {
            Code = roomCode,
            HostSessionId = sessionId,
            Players = [host]
        };

        _rooms[roomCode] = room;
        _sessionRooms[sessionId] = roomCode;
        _sessionConnections[sessionId] = connectionId;

        return room;
    }

    /// <summary>
    /// Pobiera listę dostępnych pokoi publicznych (do których można dołączyć).
    /// </summary>
    public List<PublicRoomInfo> GetPublicRooms()
    {
        return _rooms.Values
            .Where(r => r.IsPublic && 
                        r.Players.Count < r.Settings.MaxPlayers &&
                        (r.CurrentGame == null || r.CurrentGame.Phase == RoundPhase.Waiting))
            .Select(r => new PublicRoomInfo
            {
                Code = r.Code,
                HostNickname = r.Players.FirstOrDefault(p => p.IsHost)?.Nickname ?? "?",
                PlayerCount = r.Players.Count,
                MaxPlayers = r.Settings.MaxPlayers
            })
            .ToList();
    }

    /// <summary>
    /// Ustawia pokój jako publiczny lub prywatny (tylko host).
    /// </summary>
    public bool SetRoomPublic(string roomCode, bool isPublic)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
        {
            return false;
        }
        room.IsPublic = isPublic;
        return true;
    }

    /// <summary>
    /// Dołącza gracza do pokoju.
    /// </summary>
    public (bool Success, string? Error, Room? Room) JoinRoom(string roomCode, string connectionId, string nickname, string sessionId)
    {
        roomCode = roomCode.ToUpperInvariant();

        if (!_rooms.TryGetValue(roomCode, out var room))
        {
            return (false, "Pokój nie istnieje.", null);
        }

        if (room.Players.Count >= room.Settings.MaxPlayers)
        {
            return (false, "Pokój jest pełny.", null);
        }

        if (room.Players.Any(p => p.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, "Ten nick jest już zajęty.", null);
        }

        if (room.CurrentGame != null && room.CurrentGame.Phase != RoundPhase.Waiting)
        {
            return (false, "Gra już się rozpoczęła.", null);
        }

        var player = new Player
        {
            SessionId = sessionId,
            ConnectionId = connectionId,
            Nickname = nickname,
            AvatarColor = Player.GenerateAvatarColor()
        };

        room.Players.Add(player);
        _sessionRooms[sessionId] = roomCode;
        _sessionConnections[sessionId] = connectionId;

        return (true, null, room);
    }

    /// <summary>
    /// Usuwa gracza z pokoju po SessionId.
    /// </summary>
    public (Room? Room, bool RoomDeleted, string? NewHostSessionId, string? NewHostNickname) LeaveRoom(string sessionId)
    {
        if (!_sessionRooms.TryRemove(sessionId, out var roomCode))
        {
            return (null, false, null, null);
        }

        _sessionConnections.TryRemove(sessionId, out _);

        if (!_rooms.TryGetValue(roomCode, out var room))
        {
            return (null, false, null, null);
        }

        var player = room.Players.FirstOrDefault(p => p.SessionId == sessionId);
        if (player == null)
        {
            return (null, false, null, null);
        }

        room.Players.Remove(player);

        // Jeśli pokój pusty - usuń
        if (room.Players.Count == 0)
        {
            _rooms.TryRemove(roomCode, out _);
            return (room, true, null, null);
        }

        // Jeśli host wyszedł - przydziel nowego
        string? newHostSessionId = null;
        string? newHostNickname = null;
        if (player.IsHost)
        {
            var newHost = room.Players.First();
            newHost.IsHost = true;
            room.HostSessionId = newHost.SessionId;
            newHostSessionId = newHost.SessionId;
            newHostNickname = newHost.Nickname;
        }

        return (room, false, newHostSessionId, newHostNickname);
    }

    /// <summary>
    /// Wyrzuca gracza z pokoju (tylko host). Parametry = SessionId.
    /// </summary>
    public (bool Success, string? Error) KickPlayer(string hostSessionId, string playerSessionId)
    {
        var room = GetRoomBySession(hostSessionId);
        if (room == null)
        {
            return (false, "Nie jesteś w pokoju.");
        }

        if (room.HostSessionId != hostSessionId)
        {
            return (false, "Tylko host może wyrzucać graczy.");
        }

        if (hostSessionId == playerSessionId)
        {
            return (false, "Nie możesz wyrzucić siebie.");
        }

        var player = room.Players.FirstOrDefault(p => p.SessionId == playerSessionId);
        if (player == null)
        {
            return (false, "Gracz nie istnieje.");
        }

        room.Players.Remove(player);
        _sessionRooms.TryRemove(playerSessionId, out _);
        _sessionConnections.TryRemove(playerSessionId, out _);

        return (true, null);
    }

    /// <summary>
    /// Pobiera pokój po kodzie.
    /// </summary>
    public Room? GetRoom(string roomCode)
    {
        _rooms.TryGetValue(roomCode.ToUpperInvariant(), out var room);
        return room;
    }

    /// <summary>
    /// Pobiera pokój gracza po SessionId.
    /// </summary>
    public Room? GetRoomBySession(string sessionId)
    {
        if (!_sessionRooms.TryGetValue(sessionId, out var roomCode))
        {
            return null;
        }
        return GetRoom(roomCode);
    }

    /// <summary>
    /// Pobiera kod pokoju gracza po SessionId.
    /// </summary>
    public string? GetRoomCodeBySession(string sessionId)
    {
        _sessionRooms.TryGetValue(sessionId, out var roomCode);
        return roomCode;
    }

    /// <summary>
    /// Pobiera kod pokoju po ConnectionId (potrzebne w OnDisconnectedAsync).
    /// </summary>
    public string? GetRoomCode(string connectionId)
    {
        var sessionId = GetSessionByConnection(connectionId);
        if (sessionId == null) return null;
        return GetRoomCodeBySession(sessionId);
    }

    /// <summary>
    /// Ustawia stan gotowości gracza.
    /// </summary>
    public bool SetPlayerReady(string sessionId, bool isReady)
    {
        var room = GetRoomBySession(sessionId);
        var player = room?.Players.FirstOrDefault(p => p.SessionId == sessionId);
        if (player == null) return false;

        player.IsReady = isReady;
        return true;
    }

    /// <summary>
    /// Sprawdza czy wszyscy gracze są gotowi.
    /// </summary>
    public bool AllPlayersReady(string roomCode)
    {
        var room = GetRoom(roomCode);
        return room != null && room.Players.All(p => p.IsReady);
    }

    /// <summary>
    /// Usuwa pokój.
    /// </summary>
    public bool DeleteRoom(string roomCode)
    {
        roomCode = roomCode.ToUpperInvariant();
        
        if (!_rooms.TryRemove(roomCode, out var room))
        {
            return false;
        }

        // Remove all player mappings
        foreach (var player in room.Players)
        {
            _sessionRooms.TryRemove(player.SessionId, out _);
            _sessionConnections.TryRemove(player.SessionId, out _);
        }

        return true;
    }

    private string GenerateUniqueRoomCode()
    {
        var random = new Random();
        string code;
        do
        {
            code = new string(Enumerable.Repeat(RoomCodeChars, 4)
                .Select(s => s[random.Next(s.Length)])
                .ToArray());
        } while (_rooms.ContainsKey(code));

        return code;
    }

    /// <summary>
    /// Aktualizuje czas ostatniej aktywności pokoju.
    /// </summary>
    public void UpdateRoomActivity(string roomCode)
    {
        if (_rooms.TryGetValue(roomCode.ToUpperInvariant(), out var room))
        {
            room.LastActivityAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Pobiera listę nieaktywnych pokoi do usunięcia.
    /// </summary>
    public List<(string Code, int PlayerCount, TimeSpan InactiveTime)> GetInactiveRooms(TimeSpan inactiveThreshold)
    {
        var now = DateTime.UtcNow;
        return _rooms.Values
            .Where(r => now - r.LastActivityAt > inactiveThreshold)
            .Select(r => (r.Code, r.Players.Count, now - r.LastActivityAt))
            .ToList();
    }

    /// <summary>
    /// Usuwa nieaktywne pokoje (puste lub bez aktywności przez dłuższy czas).
    /// </summary>
    public int CleanupInactiveRooms(TimeSpan emptyRoomThreshold, TimeSpan staleRoomThreshold)
    {
        var now = DateTime.UtcNow;
        var removedCount = 0;

        foreach (var room in _rooms.Values.ToList())
        {
            var inactiveTime = now - room.LastActivityAt;
            
            if (room.Players.Count == 0 && inactiveTime > emptyRoomThreshold)
            {
                if (_rooms.TryRemove(room.Code, out _))
                {
                    removedCount++;
                }
            }
            else if (inactiveTime > staleRoomThreshold)
            {
                foreach (var player in room.Players)
                {
                    _sessionRooms.TryRemove(player.SessionId, out _);
                    _sessionConnections.TryRemove(player.SessionId, out _);
                }
                
                if (_rooms.TryRemove(room.Code, out _))
                {
                    removedCount++;
                }
            }
        }

        return removedCount;
    }

    /// <summary>
    /// Pobiera całkowitą liczbę aktywnych pokoi.
    /// </summary>
    public int GetRoomCount() => _rooms.Count;

    #region Lobby Disconnection Grace Period

    /// <summary>
    /// Schedules a player for removal after grace period (for lobby refresh handling).
    /// Returns the scheduled removal time.
    /// </summary>
    public DateTime SchedulePlayerRemoval(string sessionId, string connectionId, string roomCode, string nickname)
    {
        var removalTime = DateTime.UtcNow.Add(LobbyDisconnectGracePeriod);
        
        var pending = new PendingDisconnection
        {
            SessionId = sessionId,
            ConnectionId = connectionId,
            RoomCode = roomCode,
            Nickname = nickname,
            ScheduledRemovalTime = removalTime
        };
        
        _pendingDisconnections[sessionId] = pending;
        _logger.LogInformation("Scheduled removal for {Nickname} (session={SessionId}) at {RemovalTime:HH:mm:ss}", nickname, sessionId, removalTime);
        
        return removalTime;
    }

    /// <summary>
    /// Cancels a pending disconnection if player reconnects.
    /// </summary>
    public bool CancelPendingRemoval(string sessionId)
    {
        if (_pendingDisconnections.TryRemove(sessionId, out var pending))
        {
            _logger.LogInformation("Cancelled pending removal for {Nickname} (session={SessionId})", pending.Nickname, sessionId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Cancels pending removal by nickname (fallback when sessionId not matched).
    /// </summary>
    public bool CancelPendingRemovalByNickname(string roomCode, string nickname)
    {
        foreach (var kvp in _pendingDisconnections)
        {
            if (kvp.Value.RoomCode == roomCode && 
                kvp.Value.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase))
            {
                if (_pendingDisconnections.TryRemove(kvp.Key, out var pending))
                {
                    _logger.LogInformation("Cancelled pending removal for {Nickname} by nickname", pending.Nickname);
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Processes expired pending disconnections.
    /// </summary>
    public List<(string RoomCode, string Nickname, string? NewHostSessionId, string? NewHostNickname, bool RoomDeleted)> ProcessExpiredDisconnections()
    {
        var results = new List<(string RoomCode, string Nickname, string? NewHostSessionId, string? NewHostNickname, bool RoomDeleted)>();
        var now = DateTime.UtcNow;

        foreach (var kvp in _pendingDisconnections.ToList())
        {
            if (kvp.Value.ScheduledRemovalTime <= now)
            {
                if (_pendingDisconnections.TryRemove(kvp.Key, out var pending))
                {
                    _logger.LogDebug("Processing expired disconnection for {Nickname}", pending.Nickname);
                    
                    var room = GetRoom(pending.RoomCode);
                    if (room != null)
                    {
                        var player = room.Players.FirstOrDefault(p => 
                            p.SessionId == pending.SessionId || 
                            p.Nickname.Equals(pending.Nickname, StringComparison.OrdinalIgnoreCase));
                        
                        if (player != null)
                        {
                            // GUARD: If the player has already reconnected (connectionId changed),
                            // do NOT remove them.
                            if (player.ConnectionId != pending.ConnectionId)
                            {
                                _logger.LogInformation("Skipping removal for {Nickname} — player reconnected (conn changed)", pending.Nickname);
                                continue;
                            }
                            
                            room.Players.Remove(player);
                            _sessionRooms.TryRemove(player.SessionId, out _);
                            _sessionConnections.TryRemove(player.SessionId, out _);
                            
                            string? newHostSessionId = null;
                            string? newHostNickname = null;
                            bool roomDeleted = false;
                            
                            if (room.Players.Count == 0)
                            {
                                _rooms.TryRemove(pending.RoomCode, out _);
                                roomDeleted = true;
                            }
                            else if (player.IsHost)
                            {
                                var newHost = room.Players.First();
                                newHost.IsHost = true;
                                room.HostSessionId = newHost.SessionId;
                                newHostSessionId = newHost.SessionId;
                                newHostNickname = newHost.Nickname;
                            }
                            
                            results.Add((pending.RoomCode, pending.Nickname, newHostSessionId, newHostNickname, roomDeleted));
                        }
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Checks if there's a pending disconnection for this session.
    /// </summary>
    public bool HasPendingDisconnection(string sessionId)
    {
        return _pendingDisconnections.ContainsKey(sessionId);
    }

    /// <summary>
    /// Gets pending disconnection info if exists.
    /// </summary>
    public PendingDisconnection? GetPendingDisconnection(string sessionId)
    {
        _pendingDisconnections.TryGetValue(sessionId, out var pending);
        return pending;
    }

    #endregion
}

/// <summary>
/// Represents a player pending removal after grace period.
/// </summary>
public record PendingDisconnection
{
    public required string SessionId { get; init; }
    public required string ConnectionId { get; init; }
    public required string RoomCode { get; init; }
    public required string Nickname { get; init; }
    public required DateTime ScheduledRemovalTime { get; init; }
}
