using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace SpectrumStardewMod.Gameplay;

/// <summary>Routes approved monster types through Stardew's built-in debug command parser.</summary>
/// <remarks>
/// All command execution must happen on the game thread and on the host.
/// Network callbacks must enqueue requests for a later game tick; they must never
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

    public bool TrySpawnRandomMonster()
    {
        return this.TryRunMonsterCommand(MonsterCatalog.GetRandomType(), "Random monster");
    }

    public bool TrySpawnGreenSlime()
    {
        return this.TryRunMonsterCommand("GreenSlime", "Test Green Slime");
    }

    private bool TryRunMonsterCommand(string monsterType, string description)
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

        if (Game1.eventUp || Game1.currentLocation is null || Game1.player is null || Game1.game1 is null)
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

            int tileX = (int)tile.X;
            int tileY = (int)tile.Y;

            // The game owns monster construction. parseDebugInput invokes the same
            // built-in DebugCommands handler used by SMAPI's "debug" console route.
            // The type comes only from our local allowlist; coordinates are integers.
            string command = $"monster {monsterType} {tileX} {tileY}";
            bool handled = Game1.game1.parseDebugInput(command);

            if (!handled)
            {
                this.monitor.Log($"Stardew rejected the monster command: {command}", LogLevel.Warn);
                return false;
            }

            this.monitor.Log($"{description} ({monsterType}) requested at {location.Name}, tile {tileX},{tileY}.", LogLevel.Info);
            return true;
        }

        this.monitor.Log("Spawn rejected: couldn't find a clear tile near the player.", LogLevel.Warn);
        return false;
    }
}
