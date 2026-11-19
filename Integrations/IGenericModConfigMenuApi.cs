using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley.Menus;

namespace GenericModConfigMenu
{
    /// <summary>
    /// The subset of the Generic Mod Config Menu API used by Spectrum.
    /// Keep this interface limited to methods supported by GMCM 1.16.0.
    /// </summary>
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);

        void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);

        void AddParagraph(IManifest mod, Func<string> text);

        void AddComplexOption(
            IManifest mod,
            Func<string> name,
            Action<SpriteBatch, Vector2> draw,
            Func<string>? tooltip = null,
            Action? beforeMenuOpened = null,
            Action? beforeSave = null,
            Action? afterSave = null,
            Action? beforeReset = null,
            Action? afterReset = null,
            Action? beforeMenuClosed = null,
            Func<int>? height = null,
            string? fieldId = null
        );

        bool TryGetCurrentMenu(out IManifest? mod, out string? page);

        void AddBoolOption(
            IManifest mod,
            Func<bool> getValue,
            Action<bool> setValue,
            Func<string> name,
            Func<string>? tooltip = null,
            string? fieldId = null
        );

        void AddTextOption(
            IManifest mod,
            Func<string> getValue,
            Action<string> setValue,
            Func<string> name,
            Func<string>? tooltip = null,
            string[]? allowedValues = null,
            Func<string, string>? formatAllowedValue = null,
            string? fieldId = null
        );
    }
}
