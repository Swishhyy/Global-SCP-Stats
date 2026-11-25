# Global SCP Stats

An EXILED plugin for server and global SCP: Secret Laboratory stats, action XP, and milestone badges. Version **0.2.8** targets **EXILED 9.15.2** on **.NET Framework 4.8 / x64**.

[Stats website](https://globalscpstats.com) · [Support and privacy requests](https://discord.gg/BtCq4kfdcV) 

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


## Data and privacy

The plugin respects the game's Do Not Track flag and saved local opt-outs. Set `privacy.require_player_opt_in: true` to require additional approval. That mode uses `privacy.notice_url` and `privacy.notice_version` to track which notice a player approved.

## License

[AGPL-3.0-only](LICENSE). This implementation was written for this repository after reviewing the earlier plugin's behavior.
