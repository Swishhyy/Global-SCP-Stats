# Global SCP Stats

An EXILED plugin for server and global SCP: Secret Laboratory stats, action XP, and milestone badges. Version **0.2.8** targets **EXILED 9.15.2** on **.NET Framework 4.8 / x64**.

[Stats website](https://globalscpstats.com) · [Support and privacy requests](https://discord.gg/BtCq4kfdcV) · [Discord bot source](https://github.com/Swishhyy/Global-SCP-Stats-Discord-Bot)

## Install

1. Put `GlobalSCPStats.dll` in `EXILED/Plugins`. Replace the previous copy when upgrading. If you still have `GlobalSCPStats.Core.dll` from an older build, remove it from `EXILED/Plugins/dependencies`.
2. Start or restart the server. By default, the plugin registers with the hosted API over HTTPS and starts tracking automatically.
3. Run `gstats` in Remote Admin to check the assigned server ID, upload status, and queued batches. Temporary connection failures retry automatically.

EXILED generates the YAML settings on first startup. Set `is_enabled: false` to disable the plugin. See the [example configuration](examples/example-config.yml).

## Stats and rewards

The plugin tracks playtime, rounds, kills, deaths, escapes, damage, healing, sessions, distance moved, time per role and zone, kills and deaths by role and weapon, items, and [supported gameplay actions](docs/ACTIONS.md). Players show on the site under their in-game name. XP is configurable per action, with cooldowns and a rolling cap. Set `experience.enabled: false` to keep tracking stats without awarding XP.

Configure `reward_badges` to award in-game badges from this server's lifetime stats. Each server chooses its own metrics, thresholds, and labels. Existing staff and supporter badges are preserved by default.

## Commands

| Command | Where | Result |
| --- | --- | --- |
| `.mystats` | Player console | Current connection's stats and global XP when available |
| `.mystats actions` | Player console | Current connection's action counters |
| `.statsprivacy info` / `status` | Player console | Collection status, privacy notice, and controls |
| `.statsprivacy off` | Player console | Stops local collection and removes the player's pending local stats |
| `.statsprivacy allow` | Player console | Clears the local opt-out or approves a server's opt-in mode. Game DNT takes priority. |
| `gstats` / `globalstats` | Remote Admin | Server ID, tracked players, upload status, and queue. Requires `globalstats.admin`. |

## Build

1. Install the SCP:SL Dedicated Server through Steam. In Visual Studio, install the .NET desktop development workload and the .NET Framework 4.8 targeting pack.
2. Open `GlobalSCPStats.sln`, select **Release**, and choose **Build > Build Solution**.
3. Copy `src/GlobalSCPStats/bin/Release/net48/GlobalSCPStats.dll` into `EXILED/Plugins`.

For a non-default Steam library, set `SL_REFERENCES` to the dedicated server's `SCPSL_Data/Managed` folder and restart Visual Studio. For a terminal build, run `dotnet build GlobalSCPStats.sln -c Release`.

## Data and privacy

The plugin respects the game's Do Not Track flag and saved local opt-outs. Set `privacy.require_player_opt_in: true` to require additional approval. That mode uses `privacy.notice_url` and `privacy.notice_version` to track which notice a player approved.

Keep the `GlobalSCPStats/<port>` folder in EXILED's config directory across restarts. A fresh install generates `storage.key` and saves the server ID and upload token in encrypted `enrollment.state`. Preserve any existing `GSS_STORAGE_KEY` environment variable if it was used to encrypt this server's files.

The encrypted queue keeps failed uploads for up to 72 hours by default. Tune it with `checkpoint_interval_seconds`, `max_queued_batches`, and `max_queue_age_hours`. The optional `data_directory` overrides the default path. The local privacy controls govern this server's collection and pending data. Use the support Discord for hosted account requests. See [privacy and security](docs/PRIVACY.md).

## Manual connection

For self-hosted integrations, set `server_id` and `upload_endpoint` under `global_scp_stats` in the YAML. Supply the server token in the protected `GSS_API_TOKEN` environment variable. An optional `profile_endpoint` must share the HTTPS upload host and port. A manual server ID with a blank upload endpoint runs in local-only mode. See the [API contract](docs/API.md).

## License

[AGPL-3.0-only](LICENSE). This implementation was written for this repository after reviewing the earlier plugin's behavior.
