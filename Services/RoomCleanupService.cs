using NationsCities.Services;

namespace NationsCities.Services;

/// <summary>
/// Serwis czyszczący nieaktywne pokoje i stare gry.
/// </summary>
public class RoomCleanupService : BackgroundService
{
    private readonly RoomService _roomService;
    private readonly ILogger<RoomCleanupService> _logger;

    // Interwał sprawdzania - co 5 minut
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    
    // Próg dla pustych pokoi - 10 minut
    private static readonly TimeSpan EmptyRoomThreshold = TimeSpan.FromMinutes(10);
    
    // Próg dla nieaktywnych pokoi z graczami - 1 godzina
    private static readonly TimeSpan StaleRoomThreshold = TimeSpan.FromHours(1);

    public RoomCleanupService(RoomService roomService, ILogger<RoomCleanupService> logger)
    {
        _roomService = roomService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RoomCleanupService uruchomiony");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
                
                var roomCount = _roomService.GetRoomCount();
                var removedCount = _roomService.CleanupInactiveRooms(
                    EmptyRoomThreshold, 
                    StaleRoomThreshold);

                if (removedCount > 0)
                {
                    _logger.LogInformation(
                        "Wyczyszczono {RemovedCount} nieaktywnych pokoi. Pozostało: {RoomCount}",
                        removedCount,
                        roomCount - removedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas czyszczenia pokoi");
            }
        }

        _logger.LogInformation("RoomCleanupService zatrzymany");
    }
}
