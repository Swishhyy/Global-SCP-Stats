# Global SCP Stats

An open-source SCP: Secret Laboratory connector for server and global statistics, action XP, and milestone rewards. Version **0.2.4** targets **EXILED 9.15.2** and **.NET Framework 4.8 / x64**.

This is a development build. Server collection defaults to **off**. Once enabled, player collection follows the game's **Do Not Track** flag by default: DNT on blocks tracking, while DNT off permits it unless the player has additionally opted out locally. A separate allow command is not needed for normal participation. The website/API and lightweight Discord Worker are maintained in the separate Site and Discord-Bot repositories. Discord application activation is still required for the linked-account privacy commands.

## What works

1. Tracks connected playtime, rounds participated in, kills, deaths, escapes, damage, healing, and a broad catalog of item, interaction, movement, objective, and SCP ability counters.
2. Awards configurable XP for every tracked action by default. Disable XP globally or set individual rewards to zero while retaining all statistics. Cooldowns and a rolling per-player XP cap limit farming.
3. Saves contributions in an encrypted local outbox before uploading. Ordinary retries preserve their batch IDs. Privacy removal can rewrite a mixed batch while keeping surviving contribution IDs stable, allowing a schema-v2 backend to prevent double-counting.
4. Uses a separate, revocable API token per server. Tokens and storage keys come from environment variables, never from the DLL or source code.
5. Fetches authoritative global XP and server-specific lifetime stats from the API. Each server evaluates its own configured badge metrics and thresholds, preserving existing badges by default. The backend defines no rewards.
6. Includes server-local milestone evaluation for XP, levels, kills, escapes, rounds, playtime, and individual actions. Discord role policies belong to individual servers and are not implemented by the lightweight stats bot.
7. Respects `Player.DoNotTrack` before tracking gameplay or fetching reward profiles, with a main-thread refresh every second. DNT always overrides saved approvals and commands. `.statsprivacy off` provides an additional local opt-out. Preferences are encrypted and keyed by an HMAC identifier. Either block clears in-memory stats/reward profiles and schedules pending local contribution removal.

## Build

1. Install Visual Studio with the .NET Framework 4.8 targeting pack and a .NET 10 or newer SDK. Install the matching SCP:SL Dedicated Server through Steam.
2. Open `GlobalSCPStats.sln`, select **Release**, and choose **Build > Build Solution**.
3. Copy `src/GlobalSCPStats/bin/Release/net48/GlobalSCPStats.dll` into `EXILED/Plugins`. This DLL includes Core, which also builds separately for tests.

The project finds the dedicated server in the default Windows Steam library. For another library, set the Windows user environment variable `SL_REFERENCES` to that server's `SCPSL_Data/Managed` folder and restart Visual Studio. For a terminal build, run `dotnet build GlobalSCPStats.sln -c Release`.

## Connect a server

1. Install `GlobalSCPStats.dll` and let EXILED generate the config. On upgrade, remove the old `GlobalSCPStats.Core.dll` from `EXILED/Plugins/dependencies`. See [example-config.yml](examples/example-config.yml).
2. Ask the Global SCP Stats operator for a one-time code. Set `GSS_SETUP_CODE` in the server's protected environment. Leave `server_id` and the endpoints blank to use managed enrollment.
3. Set `GSS_STORAGE_KEY` to a base64-encoded **64-byte cryptographically random key** in the server environment. Preserve this key separately from the connector's data directory. It protects the saved connector credentials, queued stats, and privacy preferences.
4. Configure `privacy.notice_url` and `notice_version` for your player notice. Set `collection_enabled: true` when ready to collect, then restart the server.
5. Run `gstats` in Remote Admin to see the assigned server ID and upload status. Once enrolled, remove `GSS_SETUP_CODE` from the environment. Future restarts use the encrypted `enrollment.state` in the connector data directory.

Existing and self-hosted servers can still use manual setup. Keep their existing `server_id`, HTTPS endpoints and `GSS_API_TOKEN`. The optional profile endpoint must use the same host and port as uploads. For local-only testing, set a manual server ID and leave the endpoints blank.

Use the hosting panel's protected environment settings or a service secret store. The YAML contains the **names** of environment variables, not their secret values. Each server needs a separate token and storage key. Never ship a master token inside the connector.

`privacy.require_player_opt_in` now defaults to `false`. DNT is always respected, and `.statsprivacy allow` cannot override it. A local `.statsprivacy off` remains effective even after toggling DNT; `.statsprivacy allow` clears that extra local opt-out while DNT is off. Optional stricter servers can set `require_player_opt_in: true` to also require saved approval and a configured notice URL. Restart after privacy-policy changes. DNT being off is a collection preference, not by itself a determination of lawful basis or valid consent.

If upgrading a generated **0.2.0** config, change `privacy.require_player_opt_in` from `true` to `false` to use the new DNT-based default. Existing saved local opt-outs are preserved during preference migration.

The plugin uses AES-256-CBC with a fresh random IV and an independent HMAC-SHA-256 key, verified before decryption. HTTPS uses the platform certificate checks; redirects are refused. Names, player IP addresses, chat, audio, positions, and raw event objects are not collected.

## XP and rewards

```yaml
experience:
  enabled: true
  default_reward: 1
  default_cooldown_seconds: 2
  max_xp_per_minute: 1000
  rewards:
    kill: 50
    escape: 100
    player.door_interacting: 1
```

Set `experience.enabled: false` for statistics without XP. Every supported action still increments its counter when its XP reward is zero or its XP cooldown/cap applies. Gameplay actions and rewards are counted during active rounds; connected playtime includes waiting and spectator time.

In-game badges use this server's `reward_badges` metrics and thresholds against its lifetime server stats. The backend supplies counters, never reward decisions. A player cannot grant themselves a badge through a command. See [example-config.yml](examples/example-config.yml) and the [API contract](docs/API.md).

## Commands

| Command | Where | Result |
| --- | --- | --- |
| `.mystats` | Player console | This connection's stats and XP; global level/XP when a profile is available |
| `.mystats actions` | Player console | Individual action counters for this connection |
| `.statsprivacy info` / `status` | Player console | This server's collection status, data summary, notice, and controls |
| `.statsprivacy allow` | Player console | Clear an extra local opt-out, or approve optional stricter opt-in mode; always blocked while game DNT is on |
| `.statsprivacy off` | Player console | Stop collection, clear memory, persist withdrawal, and schedule queued local data removal |
| `gstats` / `globalstats` | Remote Admin | Tracked players, queued batches, and upload status; requires `globalstats.admin` |

## Verification

The dependency-free regression runner covers tracking, concurrent events, optional XP, reward validation, encryption/tampering, retention, durable retries, authentication, persistent preferences, DNT precedence/transitions, preference migration, queue erasure, and withdrawal during an in-flight upload. It uses simulated HTTP responses, so it does not require a real API or credentials.

```powershell
dotnet run --project tests/GlobalSCPStats.Tests -c Release -f net10.0
```

On Windows, the same suite can also run against the deployed core's framework:

```powershell
dotnet build tests/GlobalSCPStats.Tests -c Release -f net48
& ./tests/GlobalSCPStats.Tests/bin/Release/net48/GlobalSCPStats.Tests.exe
```

An actual EXILED server smoke test is still required. Compilation and unit/integration checks do not verify live event timing, badge conflicts with other installed plugins, or a hosted backend.

## Data and failure behavior

Queued batches expire after 72 hours by default, even if delivery fails. Retention also removes old incomplete encrypted writes. The queue holds at most 10,000 batches by default; once full, new deltas remain in memory and a warning is logged. Normal shutdown attempts a final checkpoint. An abrupt crash can lose changes since the last completed checkpoint, and memory-only changes cannot survive a full queue or disk failure.

API timeouts, HTTP 408/429, and server errors retry with backoff. Other non-success statuses pause uploads until reload while preserving the queue. A changed storage key or tampered file is rejected. Only an API success response permits deletion of a delivered batch; the API must make acceptance and idempotency durable before returning success.

Privacy cleanup independently removes disallowed local contributions even when uploads are disabled or paused. Approval is never enabled if its save fails. A failed withdrawal save still blocks that player in memory and pauses uploads while persistence retries; resolve storage errors before reconnecting/restarting. An upload already in progress may have reached the API. `.statsprivacy off` applies to this server's local collection and queue, not previously uploaded history or other servers.

Observed game DNT blocks are saved as minimal keyed preference markers so disconnected players' queued data stays excluded. Turning DNT off rotates the local collection ID before resuming, preventing older disallowed pending data from reappearing. No gameplay counts, playtime, XP, or new reward profile queries are collected for blocked players; other players' own aggregate counters do not store opponent identities.

New uploads use **schema v2** with per-player contribution IDs. A mixed schema-v1 batch cannot be partially erased safely without a backend migration; the connector preserves it and pauses uploads rather than risking duplicated stats for other players. See [the migration notes](docs/API.md#upgrading-from-010).

This repository contains the public connector and shared models, not the private hosting configuration. See [tracked actions](docs/ACTIONS.md), [API contract](docs/API.md), and [Privacy and security](docs/PRIVACY.md).

## License

AGPL-3.0-only. See [LICENSE](LICENSE). This implementation was written for this repository after reviewing the earlier plugin's behavior; no legacy source files were imported.

## Hosted services

Website: https://globalscpstats.com. Server upload: https://api.globalscpstats.com/v1/stats/batches. Profile lookup: https://api.globalscpstats.com/v1/profiles/lookup. Both require your own server token. Privacy support: https://discord.gg/BtCq4kfdcV. The Discord bot will offer /link and /privacy after its application is configured.

