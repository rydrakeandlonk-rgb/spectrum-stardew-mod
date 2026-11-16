using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Monsters;

namespace SpectrumStardewMod.Gameplay;

/// <summary>Creates vanilla monsters in the current game world.</summary>
/// <remarks>
/// All world mutations must happen on the game thread and on the host.
/// Network callbacks will enqueue requests for a later game tick; they must never
/// call this class directly from a background thread.
/// </remarks>
internal sealed class MonsterSpawner
{
    private static readonly Point[] NearbyTileOffsets =
    {
        new Point(1, 0), new Point(-1, 0), new Point(0, 1), new Point(0, -1),
        new Point(1, 1), new Point(-1, 1), new Point(1, -1), new Point(-1, -1),
        new Point(2, 0), new Point(-2, 0), new Point(0, 2), new Point(0, -2)
    };

    private readonly IMonitor monitor;

    public MonsterSpawner(IMonitor monitor)
    {
        this.monitor = monitor;
    }

    public bool TrySpawnGreenSlime()
    {
        if (!Context.IsWorldReady)
        {
            this.monitor.Log("Spawn rejected: no save is currently loaded.", LogLevel.Warn);
            return false;
        }

        if (!Context.IsMainPlayer)
        {
            this.monitor.Log("Spawn rejected: only the multiplayer host can create monsters.", LogLevel.Warn);
            return false;
        }

        if (Game1.eventUp || Game1.currentLocation is null || Game1.player is null)
        {
            this.monitor.Log("Spawn rejected: the current game state isn't suitable for a spawn.", LogLevel.Warn);
            return false;
        }

        GameLocation location = Game1.currentLocation;
        Vector2 playerTile = Game1.player.Tile;

        foreach (Point offset in NearbyTileOffsets)
        {
            Vector2 tile = new(playerTile.X + offset.X, playerTile.Y + offset.Y);

            if (!location.isTileLocationTotallyClearAndPlaceable(tile))
                continue;

            Vector2 pixelPosition = tile * 64f;
            var slime = new GreenSlime(pixelPosition, 0);
            location.characters.Add(slime);

            this.monitor.Log($"Spawned a test Green Slime in {location.Name} near {Game1.player.Name}.", LogLevel.Info);
            return true;
        }

        this.monitor.Log("Spawn rejected: couldn't find a clear tile near the player.", LogLevel.Warn);
        return false;
    }
}
