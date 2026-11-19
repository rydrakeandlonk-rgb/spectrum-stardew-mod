using System;

namespace SpectrumStardewMod.Networking;

/// <summary>
/// Typed command relay sent by a farmhand to the multiplayer host.
/// Public settable properties and a parameterless constructor support SMAPI serialization.
/// </summary>
public sealed class ForwardedSpawnCommand
{
    public string CommandId { get; set; } = "";
    public string Game { get; set; } = "";
    public string Name { get; set; } = "";
    public string TwitchUserName { get; set; } = "";
    public string RewardTitle { get; set; } = "";
    public string RedeemedAt { get; set; } = "";
    public string BehaviorKey { get; set; } = "";
    public string UserInput { get; set; } = "";
    public string MonsterType { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public int Count { get; set; } = 1;
    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 3;
    public string SourceBroadcasterUserId { get; set; } = "";
    public bool ChatResponses { get; set; } = true;

    internal static ForwardedSpawnCommand FromCommand(GameCommand command, string broadcasterUserId, bool chatResponses) => new()
    {
        CommandId = command.CommandId,
        Game = command.Game,
        Name = command.Name,
        TwitchUserName = command.TwitchUserName,
        RewardTitle = command.RewardTitle,
        RedeemedAt = command.RedeemedAt,
        BehaviorKey = command.BehaviorKey,
        UserInput = command.UserInput,
        MonsterType = command.MonsterType,
        Difficulty = command.Difficulty,
        Count = command.Count,
        MinCount = command.MinCount,
        MaxCount = command.MaxCount,
        SourceBroadcasterUserId = broadcasterUserId,
        ChatResponses = chatResponses
    };

    internal GameCommand ToGameCommand() => new(
        this.CommandId, this.Game, this.Name, this.TwitchUserName, this.RewardTitle,
        this.RedeemedAt, this.BehaviorKey, this.UserInput, this.MonsterType,
        this.Difficulty, this.Count, this.MinCount, this.MaxCount
    );
}

/// <summary>Host response sent only after a command is completed or rejected.</summary>
public sealed class SpawnCommandResult
{
    public string CommandId { get; set; } = "";
    public bool Success { get; set; }
    public string Error { get; set; } = "";

    public SpawnCommandResult() { }

    public SpawnCommandResult(string commandId, bool success, string error)
    {
        this.CommandId = commandId;
        this.Success = success;
        this.Error = error;
    }
}

/// <summary>Typed remote clear action relayed by a farmhand to the host.</summary>
public sealed class ForwardedClearAction
{
    public string ActionId { get; set; } = "";
    public string Name { get; set; } = "";

    public ForwardedClearAction() { }

    public ForwardedClearAction(string actionId, string name)
    {
        this.ActionId = actionId;
        this.Name = name;
    }
}

/// <summary>Host result for a relayed Control Center clear action.</summary>
public sealed class ClearActionResult
{
    public string ActionId { get; set; } = "";
    public bool Success { get; set; }
    public int RemovedCount { get; set; }
    public string Error { get; set; } = "";
    public string Detail { get; set; } = "";

    public ClearActionResult() { }

    public ClearActionResult(string actionId, bool success, int removedCount, string error, string detail = "")
    {
        this.ActionId = actionId;
        this.Success = success;
        this.RemovedCount = removedCount;
        this.Error = error;
        this.Detail = detail;
    }
}
