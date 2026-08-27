# Rocket League Account Switcher

![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![License](https://img.shields.io/badge/license-proprietary-lightgrey)

A Windows app for managing multiple Epic Games accounts and launching Rocket League straight into whichever one you pick, without signing in and out of the Epic launcher each time.

This is an unofficial tool with no connection to Epic Games or Psyonix. Only use accounts you own.

## Features

- One-click launch into any saved account
- Add accounts through an in-app Epic sign-in; your password is never stored
- 1v1, 2v2 and 3v3 ranks with MMR shown per account
- Logins held as encrypted tokens on your PC, with an optional master password for moving them between machines
- The account you launched most recently is marked as active

## Requirements

- Windows 10 or 11 (64-bit)
- [WebView2 runtime](https://developer.microsoft.com/microsoft-edge/webview2/), preinstalled on most Windows 11 systems
- Rocket League installed through the Epic Games Store

The installer bundles .NET, so nothing else is needed to run it.

## Installation

Download the latest installer from the [Releases](../../releases) page and run it. The installer isn't code-signed, so Windows SmartScreen may flag the publisher as unknown. Click **More info**, then **Run anyway**.

## How it works

Adding an account uses Epic's own OAuth login. The app keeps only the refresh token that login returns, encrypted locally. Launching an account trades that token for a one-time exchange code and starts the game with it, so the Epic launcher is never modified or closed.

Ranks are read from rocketleague.tracker.network through a hidden browser window, which is why no API key is required.

## FAQ

**Where is my password stored?**

It isn't. Signing in returns a token from Epic, and only that token is saved, encrypted on your machine.

**Why won't an account's ranks load?**

Its tracker profile is private, or the account has never been tracked. Ranks come from rocketleague.tracker.network, so if the site can't read the profile, neither can the app.

**An account says its login expired.**

Epic tokens don't last forever. Add the account again with a single sign-in and it's restored.

**Does the Epic launcher need to be installed or running?**

It doesn't need to be running. The game itself has to have been installed through Epic at some point, since that's where the game files come from.

**Can this get my account banned?**

The game starts through the normal Easy Anti-Cheat path, exactly like a standard launch, so nothing is injected or spoofed in-game. The unofficial part is automating the Epic sign-in, which isn't something Epic permits. There have been no issues in practice, but use it at your own risk.

## Building from source

You'll need the .NET 10 SDK, plus the WiX 5 CLI tool to build the installer.

```powershell
# run the app
dotnet run --project src/RLSwitcher

# build the installer (publishes, generates the license, compiles the MSI)
cd installer
./build-installer.ps1 -Author "yourname"
```

## Contributing

This is maintained in spare time and will keep getting updates if there's interest. Bug reports and feature requests are welcome through the [issue tracker](../../issues).

## License

Released under the [MIT License](LICENSE.txt). You are free to use, modify, and redistribute it, including in your own projects, provided the original copyright and license notice are retained.

## Credits

The account login is based on the OAuth approach from [Slipstream](https://github.com/jun-eau/Slipstream), and the Rocket League API details draw on [AeonLucid's RocketLeaguePublic](https://github.com/AeonLucid/RocketLeaguePublic).

---

<sub>Coded with Claude Opus 4.8.</sub>
