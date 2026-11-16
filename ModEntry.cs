using StardewModdingAPI;
using StardewValley;
using SpectrumStardewMod.Gameplay;

namespace SpectrumStardewMod;

/// <summary>The SMAPI entry point for Spectrum Stardew Mod.</summary>
internal sealed class ModEntry : Mod
{
    private MonsterSpawner? monsterSpawner;

    public override void Entry(IModHelper helper)
    {
        this.monsterSpawner = new MonsterSpawner(this.Monitor);

        helper.ConsoleCommands.Add(
            "spectrum_spawn_test",
            "Spawn a test Green Slime near the host player. Usage: spectrum_spawn_test",
            this.OnSpawnTestCommand
        );

        this.Monitor.Log("Spectrum Stardew Mod loaded. Use 'spectrum_spawn_test' in the SMAPI console to test a slime spawn.", LogLevel.Info);
    }

    private void OnSpawnTestCommand(string command, string[] args)
    {
        if (this.monsterSpawner is null)
            return;

        this.monsterSpawner.TrySpawnGreenSlime();
    }
}
