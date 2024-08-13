> [!CAUTION]
> **Not approved by or associated with Mojang or Microsoft.**<br>
> **This project doesn't allow you to pirate Minecraft: Bedrock Edition, you must own it.**

# Bedrock Updater
Download, update & install Minecraft: Bedrock Edition without the Microsoft Store.

## Features
- Instantly download, update & install Minecraft: Bedrock Edition:
    - Minecraft
        
    - Minecraft Preview

- Decouples the game from the Microsoft Store & Windows Update.<br>Making it possible to deploy Minecraft: Bedrock Edition on systems where these components aren't accessible.

- Installs any dependencies required for Minecraft: Bedrock Edition.

## Prerequisites
- A Microsoft account that owns Minecraft: Bedrock Edition.
    - To sign in:

        - Open Windows Settings.

        - Go to Accounts → Email & accounts.

        - Click <kbd>Add an Account</kbd>.

- Hardware & software that fulfill the [system requirements](https://www.minecraft.net/en-us/store/minecraft-java-bedrock-edition-pc#accordionv1-0afde1e050-item-203d6a0d57) for Minecraft: Bedrock Edition.

## Usage
- [Install](#installation) Bedrock Updater with your preferred method.

- From the Windows Start Menu:
    - Start `Bedrock Updater` for Minecraft.

    - Start `Bedrock Updater Preview` for Minecraft Preview.

> [!NOTE]
> If you have downloaded Bedrock Updater manually, pass `/Preview` to the executable for Minecraft Preview.

## Installation
#### Manual
Download the latest release from [GitHub Releases](https://github.com/Aetopia/BedrockUpdater/releases/latest).

### [Scoop](https://scoop.sh/)
#### Install
```
scoop bucket add games
scoop install bedrockupdater
```

### Minecraft: Bedrock Edition
#### Uninstall

Run the following script in PowerShell to uninstall Minecraft: Bedrock Edition & Xbox Identity Provider.
```powershell
$ProgressPreference = $ErrorActionPreference = "SilentlyContinue"

Get-AppxPackage | ForEach-Object { if ($_.Name -in @("Microsoft.MinecraftUWP", "Microsoft.MinecraftWindowsBeta", "Microsoft.XboxIdentityProvider")) { Remove-AppxPackage $_ } }

$ProgressPreference = $ErrorActionPreference = "Continue"
```

## Building
1. Download the following:
    - [.NET SDK](https://dotnet.microsoft.com/en-us/download)
    - [.NET Framework 4.8.1 Developer Pack](https://dotnet.microsoft.com/en-us/download/dotnet-framework/thank-you/net481-developer-pack-offline-installer)

2. Run the following command to compile:

    ```cmd
    dotnet publish "src\BedrockUpdater.csproj"
    ```
