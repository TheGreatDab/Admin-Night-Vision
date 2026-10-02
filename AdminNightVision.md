## Features

**Admin Night Vision** lets admins see clearly at night without changing anything for anyone else.

- **Personal time lock** — `/nv` locks the time *you* see to midday (or any hour from 0 to 24) and clears fog, rain, clouds and wind on your screen only. Everyone else keeps the real time and weather.
- **Unlimited night vision goggles** — `/unvg` equips a pair of night vision goggles in a hidden extra clothing slot. They can't be dropped or kept: moving them out or dropping them removes them.
- **Auto-enable** — players with the auto permission get their time lock back automatically when they wake up after connecting, at the hour they last used.
- **Developer API** — other plugins can lock and unlock a player's time and check whether it's locked.

## Permissions

This plugin uses Oxide's permission system. To assign a permission, use `oxide.grant <user or group> <name or steam id> <permission>`. To remove a permission, use `oxide.revoke <user or group> <name or steam id> <permission>`.

| Permission | What it allows |
|---|---|
| `adminnightvision.allowed` | Toggle the personal time lock with `/nv` |
| `adminnightvision.unlimitednvg` | Equip and remove unlimited night vision goggles with `/unvg` |
| `adminnightvision.auto` | Get the time lock back automatically when waking up after connecting |

Example — give your admin group everything:

```
oxide.grant group admin adminnightvision.allowed
oxide.grant group admin adminnightvision.unlimitednvg
oxide.grant group admin adminnightvision.auto
```

## Chat Commands

| Command | Description |
|---|---|
| `/nv [0-24]` or `/adminnightvision [0-24]` | Toggle the time lock. Optionally give the hour to show, e.g. `/nv 13.5`; without one, the configured default hour is used |
| `/unvg` or `/unlimitednvg` | Equip or remove unlimited night vision goggles |
| `/nv help` | Show the command list in chat |

## Configuration

The settings and options can be configured in the `AdminNightVision` file under the `config` directory. The use of an editor and validator is recommended to avoid formatting issues and syntax errors.

```json
{
  "chatIconID": "0",
  "date": "1/25/2024",
  "time": 12.0
}
```

- **chatIconID** — a SteamID64 whose avatar is shown next to the plugin's chat messages; `"0"` for the default icon.
- **date** — the date shown while the time is locked (`M/d/yyyy`). It sets where the sun and moon sit in the sky. An invalid date falls back to `1/25/2024`.
- **time** — the default hour shown by `/nv` when no hour is given, from `0` to `24`. Values outside that range fall back to `12`.

## Localization

The default messages are in the `AdminNightVision` file under the `lang/en` directory. To add support for another language, create a new language folder (e.g. **de** for German) if not already created, copy the default language file to the new folder and then customize the messages.

```json
{
  "ChatPrefix": "<color=#00ff00>[Admin Night Vision]</color>",
  "NoPerms": "You do not have permission to use this command!",
  "TimeLocked": "Time locked to {0}",
  "TimeUnlocked": "Time unlocked",
  "HelpTitle": "<size=16><color=#00ff00>Admin Night Vision</color> Help</size>\n",
  "Help1": "<color=#00ff00>/adminnightvision <0-24>(/nv)</color> - Toggle time lock night vision with optional time 0-24",
  "Help2": "<color=#00ff00>/unlimitednvg (/unvg)</color> - Equip/remove unlimited night vision goggles",
  "EquipUNVG": "Equipped unlimited night vision goggles",
  "RemoveUNVG": "Removed unlimited night vision goggles"
}
```

In `TimeLocked`, `{0}` is the hour the time is locked to.

## Data

The hour each auto-enable player last used is saved in `AdminNightVision/playerTimes` under the `data` directory, so it survives restarts.

## Developer API

```csharp
[PluginReference]
private Plugin AdminNightVision;
```

### LockPlayerTime

Locks a player's time to the given hour (0–24).

```csharp
AdminNightVision?.Call("LockPlayerTime", player, 12f);
```

### UnlockPlayerTime

Unlocks a player's time, back to the real time and weather.

```csharp
AdminNightVision?.Call("UnlockPlayerTime", player);
```

### IsPlayerTimeLocked

Returns `true` when the player's time is locked.

```csharp
bool locked = AdminNightVision?.Call("IsPlayerTimeLocked", player) is true;
```

### BlockEnvUpdates

While `true`, players without a time lock stop receiving environment updates from this plugin, so another plugin can manage their time and weather.

```csharp
AdminNightVision?.Call("BlockEnvUpdates", true);
```

## Compatibility with NightVision plugins

Plugins written for the original NightVision, such as **Clear Night**, look it up by name. Admin Night Vision links itself to them automatically, so they keep skipping time-locked players instead of sending them the real sky. The console shows one line per linked plugin, for example:

```
[Admin Night Vision] Clear Night looks for NightVision; linked it to Admin Night Vision
```

Don't run Admin Night Vision and NightVision together. Both send every player the sky, so a locked sky flickers between day and night. Unload one with `o.unload NightVision`.

## Credits & License

- **Dab / DabTheGreat** — current author and maintainer.
- A fork of **NightVision** by Clearshot, which is no longer maintained, used under its MIT License.

Admin Night Vision is released under the MIT License. Copyright (c) 2020 Clearshot; copyright (c) 2026 Dab / DabTheGreat. The full license text is included at the top of the plugin file.
