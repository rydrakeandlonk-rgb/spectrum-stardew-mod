using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using SpectrumStardewMod.Networking;

namespace SpectrumStardewMod.UI;

/// <summary>The in-game Spectrum controls for the multiplayer host.</summary>
internal sealed class SpectrumMenu : IClickableMenu
{
    private readonly Func<bool> isConnected;
    private readonly Func<bool> isHost;
    private readonly Func<HostSettings> getSettings;
    private readonly Func<string> getOverride;
    private readonly Func<bool> getSafetyGate;
    private readonly Func<string> getSpawnStateReason;
    private readonly Action<HostSettings> updateSettings;
    private readonly Action clearLoadedMonsters;
    private readonly Func<string> getPairingStatus;
    private readonly Action openControlCenter;
    private bool confirmClear;

    private Rectangle spawnBounds;
    private Rectangle difficultyBounds;
    private Rectangle chatBounds;
    private Rectangle debugBounds;
    private Rectangle clearBounds;
    private Rectangle confirmBounds;
    private Rectangle cancelBounds;
    private Rectangle connectBounds;
    private Rectangle closeBounds;

    public SpectrumMenu(
        Func<bool> isConnected,
        Func<bool> isHost,
        Func<HostSettings> getSettings,
        Func<string> getOverride,
        Func<bool> getSafetyGate,
        Func<string> getSpawnStateReason,
        Action<HostSettings> updateSettings,
        Action clearLoadedMonsters,
        Func<string> getPairingStatus,
        Action openControlCenter
    ) : base(0, 0, 720, 570)
    {
        this.isConnected = isConnected;
        this.isHost = isHost;
        this.getSettings = getSettings;
        this.getOverride = getOverride;
        this.getSafetyGate = getSafetyGate;
        this.getSpawnStateReason = getSpawnStateReason;
        this.updateSettings = updateSettings;
        this.clearLoadedMonsters = clearLoadedMonsters;
        this.getPairingStatus = getPairingStatus;
        this.openControlCenter = openControlCenter;

        this.xPositionOnScreen = Math.Max(16, (Game1.uiViewport.Width - this.width) / 2);
        this.yPositionOnScreen = Math.Max(16, (Game1.uiViewport.Height - this.height) / 2);
        this.SetButtonBounds();
    }

    private void SetButtonBounds()
    {
        int left = this.xPositionOnScreen + 48;
        int right = this.xPositionOnScreen + 372;
        int top = this.yPositionOnScreen + 190;
        this.spawnBounds = new Rectangle(left, top, 280, 54);
        this.difficultyBounds = new Rectangle(right, top, 280, 54);
        this.chatBounds = new Rectangle(left, top + 72, 280, 54);
        this.debugBounds = new Rectangle(right, top + 72, 280, 54);
        this.clearBounds = new Rectangle(left, top + 150, 280, 54);
        this.connectBounds = new Rectangle(right, top + 150, 280, 54);
        this.confirmBounds = new Rectangle(left, top + 230, 180, 48);
        this.cancelBounds = new Rectangle(left + 195, top + 230, 180, 48);
        this.closeBounds = new Rectangle(this.xPositionOnScreen + this.width - 64, this.yPositionOnScreen + 20, 36, 36);
    }

    private bool CanControl => this.isHost();
    private bool CanSyncSettings => this.isHost() && this.isConnected();

    private void Save(HostSettings settings)
    {
        if (this.CanControl)
            this.updateSettings(settings);
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.closeBounds.Contains(x, y))
        {
            Game1.playSound("bigDeSelect");
            Game1.exitActiveMenu();
            return;
        }

        if (this.connectBounds.Contains(x, y))
        {
            if (this.getPairingStatus().StartsWith("Requesting", StringComparison.OrdinalIgnoreCase))
                return;
            this.openControlCenter();
            Game1.playSound("smallSelect");
            return;
        }

        if (this.confirmClear && this.confirmBounds.Contains(x, y))
        {
            this.confirmClear = false;
            this.clearLoadedMonsters();
            Game1.playSound("thudStep");
            return;
        }

        if (this.confirmClear && this.cancelBounds.Contains(x, y))
        {
            this.confirmClear = false;
            Game1.playSound("smallSelect");
            return;
        }

        if (!this.CanControl)
            return;

        HostSettings settings = this.getSettings();
        if (this.spawnBounds.Contains(x, y))
        {
            if (!this.CanSyncSettings) return;
            this.Save(settings with { SpawnEnabled = !settings.SpawnEnabled });
            Game1.playSound("smallSelect");
        }
        else if (this.difficultyBounds.Contains(x, y))
        {
            if (!this.CanSyncSettings) return;
            string next = settings.Difficulty switch
            {
                "easy" => "normal",
                "normal" => "hard",
                _ => "easy"
            };
            this.Save(settings with { Difficulty = next });
            Game1.playSound("smallSelect");
        }
        else if (this.chatBounds.Contains(x, y))
        {
            if (!this.CanSyncSettings) return;
            this.Save(settings with { ChatResponses = !settings.ChatResponses });
            Game1.playSound("smallSelect");
        }
        else if (this.debugBounds.Contains(x, y))
        {
            if (!this.CanSyncSettings) return;
            this.Save(settings with { DebugMessages = !settings.DebugMessages });
            Game1.playSound("smallSelect");
        }
        else if (this.clearBounds.Contains(x, y))
        {
            this.confirmClear = true;
            Game1.playSound("smallSelect");
        }
    }

    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);
    }

    private static void DrawButton(SpriteBatch batch, Rectangle bounds, string label, bool enabled, bool selected = false)
    {
        Color fill = !enabled
            ? new Color(72, 72, 78)
            : selected
                ? new Color(83, 57, 125)
                : new Color(54, 47, 68);
        batch.Draw(Game1.staminaRect, bounds, fill);
        Vector2 size = Game1.smallFont.MeasureString(label);
        Vector2 position = new(
            bounds.X + (bounds.Width - size.X) / 2f,
            bounds.Y + (bounds.Height - size.Y) / 2f
        );
        batch.DrawString(Game1.smallFont, label, position, enabled ? Color.White : Color.LightGray);
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.65f);
        Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, false, true);

        b.DrawString(Game1.dialogueFont, "Spectrum Monster Spawner",
            new Vector2(this.xPositionOnScreen + 48, this.yPositionOnScreen + 36), Game1.textColor);

        bool connected = this.isConnected();
        bool host = this.isHost();
        HostSettings settings = this.getSettings();
        string connectionText = connected ? "Gateway: Connected" : "Gateway: Not connected";
        b.DrawString(Game1.smallFont, connectionText,
            new Vector2(this.xPositionOnScreen + 52, this.yPositionOnScreen + 112),
            connected ? Color.LightGreen : Color.OrangeRed);

        string authorityText = host ? "World authority: Host" : "World authority: Farmhand (view only)";
        b.DrawString(Game1.smallFont, authorityText,
            new Vector2(this.xPositionOnScreen + 300, this.yPositionOnScreen + 112),
            host ? Color.LightGreen : Color.LightGray);

        string overrideText = "Admin override: " + this.getOverride().Replace("_", " ");
        b.DrawString(Game1.smallFont, overrideText,
            new Vector2(this.xPositionOnScreen + 52, this.yPositionOnScreen + 146), Game1.textColor);

        string gateText = "Control Center safety gate: " + (this.getSafetyGate() ? "OPEN" : "CLOSED");
        b.DrawString(Game1.smallFont, gateText,
            new Vector2(this.xPositionOnScreen + 372, this.yPositionOnScreen + 146),
            this.getSafetyGate() ? Color.LightGreen : Color.OrangeRed);

        DrawButton(b, this.spawnBounds,
            settings.SpawnEnabled ? "Stop Spawns" : "Start Spawns",
            this.CanSyncSettings, settings.SpawnEnabled);
        DrawButton(b, this.difficultyBounds,
            "Difficulty: " + settings.Difficulty,
            this.CanSyncSettings, true);
        DrawButton(b, this.chatBounds,
            "Chat responses: " + (settings.ChatResponses ? "On" : "Off"),
            this.CanSyncSettings, settings.ChatResponses);
        DrawButton(b, this.debugBounds,
            "Debug messages: " + (settings.DebugMessages ? "On" : "Off"),
            this.CanSyncSettings, settings.DebugMessages);
        DrawButton(b, this.clearBounds, "Clear all loaded monsters", this.CanControl, false);
        string pairingStatus = this.getPairingStatus();
        bool requestingPairing = pairingStatus.StartsWith("Requesting", StringComparison.OrdinalIgnoreCase);
        bool awaitingApproval = pairingStatus.StartsWith("Waiting for approval", StringComparison.OrdinalIgnoreCase);
        DrawButton(b, this.connectBounds,
            connected || pairingStatus.StartsWith("Twitch account authorised", StringComparison.OrdinalIgnoreCase)
                ? "Open Spectrum Control Center"
                : requestingPairing
                    ? "Pairing…"
                    : awaitingApproval
                        ? "Open pairing approval"
                        : "Connect Twitch account",
            !requestingPairing, false);

        if (this.confirmClear)
        {
            b.DrawString(Game1.smallFont, "This clears hostile monsters across all loaded locations.",
                new Vector2(this.clearBounds.X, this.clearBounds.Y + 62), Color.OrangeRed);
            DrawButton(b, this.confirmBounds, "Confirm clear", this.CanControl, true);
            DrawButton(b, this.cancelBounds, "Cancel", true, false);
        }
        else
        {
            string status = pairingStatus.Length > 0
                ? pairingStatus
                : !host
                    ? "Only the multiplayer host can change settings or clear the shared world."
                    : this.getSpawnStateReason();
            b.DrawString(Game1.smallFont, status,
                new Vector2(this.xPositionOnScreen + 52, this.yPositionOnScreen + 485), Game1.textColor);
        }

        DrawButton(b, this.closeBounds, "X", true, false);
        drawMouse(b);
    }
}
