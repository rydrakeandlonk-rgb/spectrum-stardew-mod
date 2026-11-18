# Repository Instructions

## Rule #1 — Nothing runs locally

All development, compilation, validation, packaging and release preparation must happen through GitHub and connected cloud services.

- Do not ask the user to run a local terminal, shell, PowerShell, Bash, dotnet, NuGet, or Git command.
- Do not require local dependency installation, builds, tests, or manual packaging.
- Use the repository's GitHub Actions workflows for cloud compilation and artifact creation.
- Keep generated build outputs out of source control; publish them as short-retention workflow artifacts.
- The only expected local activity is installing a verified mod artifact into Stardew Valley and performing necessary in-game validation.
- Never claim that a successful CI build proves the mod works in-game or in multiplayer.

## Safety and release requirements

- Never commit Twitch tokens, host tokens, service credentials, API secrets, or private configuration.
- Keep the mod's host token one-time and revocable.
- Keep game-host access disabled until Control Center eligibility and gateway authentication are verified.
- Do not publish a public release until the private integration has passed in-game and multiplayer checks.
- Keep the build workflow targeted to mod source changes and cancel superseded builds.
