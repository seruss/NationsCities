using NationsCities.Models;
using System.Collections.Concurrent;

namespace NationsCities.Services;

/// <summary>
/// Serwis zarządzania pokojami gry.
/// </summary>
public class RoomService
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private readonly ConcurrentDictionary<string, string> _playerRooms = new(); // ConnectionId -> RoomCode

    private const string RoomCodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ"; // bez I, O

    /// <summary>
    /// Tworzy nowy pokój.
    /// </summary>
    public Room CreateRoom(string hostConnectionId, string hostNickname, string? sessionId = null)
    {
        var roomCode = GenerateUniqueRoomCode();
        var host = new Player
        {
            SessionId = sessionId ?? string.Empty,
            ConnectionId = hostConnectionId,
            Nickname = hostNickname,
            AvatarColor = Player.GenerateAvatarColor(),
            IsHost = true,
            IsReady = true
        };

        var room = new Room
        {
            Code = roomCode,
            HostConnectionId = hostConnectionId,
            Players = [host]
        };

        _rooms[roomCode] = room;
        _playerRooms[hostConnectionId] = roomCode;

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
    public (bool Success, string? Error, Room? Room) JoinRoom(string roomCode, string connectionId, string nickname, string? sessionId = null)
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
            SessionId = sessionId ?? string.Empty,
            ConnectionId = connectionId,
            Nickname = nickname,
            AvatarColor = Player.GenerateAvatarColor()
        };

        room.Players.Add(player);
        _playerRooms[connectionId] = roomCode;

        return (true, null, room);
    }

    /// <summary>
    /// Usuwa gracza z pokoju.
    /// </summary>
    public (Room? Room, bool RoomDeleted, string? NewHostId) LeaveRoom(string connectionId)
    {
        if (!_playerRooms.TryRemove(connectionId, out var roomCode))
        {
            return (null, false, null);
        }

        if (!_rooms.TryGetValue(roomCode, out var room))
        {
            return (null, false, null);
        }

        var player = room.Players.FirstOrDefault(p => p.ConnectionId == connectionId);
        if (player == null)
        {
            return (null, false, null);
        }

        room.Players.Remove(player);

        // Jeśli pokój pusty - usuń
        if (room.Players.Count == 0)
        {
            _rooms.TryRemove(roomCode, out _);
            return (room, true, null);
        }

        // Jeśli host wyszedł - przydziel nowego
        string? newHostId = null;
        if (player.IsHost)
        {
            var newHost = room.Players.First();
            newHost.IsHost = true;
            room.HostConnectionId = newHost.ConnectionId;
            newHostId = newHost.ConnectionId;
        }

        return (room, false, newHostId);
    }

    /// <summary>
    /// Wyrzuca gracza z pokoju (tylko host).
    /// </summary>
    public (bool Success, string? Error) KickPlayer(string hostConnectionId, string playerConnectionId)
    {
        var room = GetRoomByPlayer(hostConnectionId);
        if (room == null)
        {
            return (false, "Nie jesteś w pokoju.");
        }

        if (room.HostConnectionId != hostConnectionId)
        {
            return (false, "Tylko host może wyrzucać graczy.");
        }

        if (hostConnectionId == playerConnectionId)
        {
            return (false, "Nie możesz wyrzucić siebie.");
        }

        var player = room.Players.FirstOrDefault(p => p.ConnectionId == playerConnectionId);
        if (player == null)
        {
            return (false, "Gracz nie istnieje.");
        }

        room.Players.Remove(player);
        _playerRooms.TryRemove(playerConnectionId, out _);

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
    /// Pobiera pokój gracza.
    /// </summary>
    public Room? GetRoomByPlayer(string connectionId)
    {
        if (!_playerRooms.TryGetValue(connectionId, out var roomCode))
        {
            return null;
        }
        return GetRoom(roomCode);
    }

    /// <summary>
    /// Pobiera kod pokoju gracza.
    /// </summary>
    public string? GetRoomCode(string connectionId)
    {
        _playerRooms.TryGetValue(connectionId, out var roomCode);
        return roomCode;
    }

    /// <summary>
    /// Ustawia stan gotowości gracza.
    /// </summary>
    public bool SetPlayerReady(string connectionId, bool isReady)
    {
        var room = GetRoomByPlayer(connectionId);
        var player = room?.Players.FirstOrDefault(p => p.ConnectionId == connectionId);
        if (player == null) return false;

        player.IsReady = isReady;
        return true;
    }

    /// <summary>
    /// Aktualizuje ConnectionId gracza po ponownym połączeniu (rejoin).
    /// </summary>
    public void UpdatePlayerConnection(string roomCode, string oldConnectionId, string newConnectionId)
    {
        // Remove old mapping
        _playerRooms.TryRemove(oldConnectionId, out _);
        
        // Add new mapping
        _playerRooms[newConnectionId] = roomCode;
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
            _playerRooms.TryRemove(player.ConnectionId, out _);
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
            
            // Usuń puste pokoje (brak graczy) po krótszym czasie
            if (room.Players.Count == 0 && inactiveTime > emptyRoomThreshold)
            {
                if (_rooms.TryRemove(room.Code, out _))
                {
                    removedCount++;
                }
            }
            // Usuń pokoje z graczami po dłuższym czasie nieaktywności
            else if (inactiveTime > staleRoomThreshold)
            {
                // Wyczyść mapowania graczy
                foreach (var player in room.Players)
                {
                    _playerRooms.TryRemove(player.ConnectionId, out _);
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
}
