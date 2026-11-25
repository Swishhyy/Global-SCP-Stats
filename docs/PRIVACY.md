# Privacy and security

This document describes what the connector handles and the work required for public deployment. An enabled connector starts collecting after startup, subject to player Do Not Track and saved local preferences.

## Data handled by this connector

| Data | Purpose and handling |
| --- | --- |
| Steam/Discord platform user ID | Links a player's contributions across sessions and servers; encrypted in the local outbox and transmitted only over HTTPS |
| Public server ID, batch ID, batch creation timestamp | Attributes contributions and prevents duplicate processing |
| In-game nickname (normally the Steam name) | Shown as the player's name on the public leaderboards and profile; trimmed to 32 characters without control characters. The latest reported name replaces the previous one |
| Connected playtime, rounds, kills, deaths, escapes, longest session | Aggregate statistics; no per-kill victim/attacker relationship is stored |
| Action-name counters, damage/healing amounts, XP delta | Progression; raw event objects are never serialized |
| Stat-only counters: roles played, kills and deaths by role, weapon or cause (game enum names), escapes by scenario, rounds won/survived, sessions, items used/picked up/thrown by type | Public profile statistics; counts only, no XP and no per-event records |
| Distance moved (meters, by sprinting/walking/crouching and by SCP), seconds as each role and in each zone | Public profile statistics. Positions are sampled in memory about once a second to compute distance and are never stored or transmitted; only the running totals are uploaded |
| Server name, plugin version, framework and framework version | Shown on the public server page; the server name has hidden rich-text tags removed |
| Returned global XP, level, server lifetime stats, profile revision | Temporary in-memory data for progression and server-owned rewards |
| Keyed preference marker, local approval/withdrawal, observed game DNT, notice version, change timestamp | Encrypted `privacy.state` file; markers use domain-separated HMAC-SHA-256 instead of raw platform IDs |
| Local collection ID and contribution ID | Retires pending data after withdrawal and prevents privacy rewrites from duplicating other players' accepted stats |

The connector does not collect player IP addresses, chat, audio recordings, raw positions, or free-form damage-handler text (only the game's damage-type enum name). Player nicknames are sent so the site can show them; a player who doesn't want their name shown can turn on Do Not Track, which stops collection entirely. It counts some voice-related SCP ability activations without recording audio. A hosted API necessarily receives the connecting server's network address; hosting access logs, application logs, analytics, and backups need their own controls.

Identifiers and encrypted data must still be treated as personal data. Encryption and pseudonymisation do not automatically make the information anonymous or establish a lawful basis.

## Implemented safeguards

1. The default player policy follows EXILED's `Player.DoNotTrack`. DNT blocks collection even when local approval exists. The connector checks it on verification and spawn, before gameplay recording and profile access, and every second on the game thread. Game authentication may require reconnecting for a changed setting to appear.
2. Automatic enrollment generates a per-server upload token and a random 64-byte storage key. The connector stores the key with restricted file permissions and encrypts the saved token. Manual deployments can supply credentials through environment variables. The configured profile endpoint must share the upload host and port.
3. Local payloads use AES-256-CBC with PKCS#7 padding, a fresh random 128-bit IV for every file, and an independent 256-bit HMAC-SHA-256 key. The envelope is `GSS1 || IV || ciphertext || HMAC(header+IV+ciphertext)`. The MAC is checked before decryption. Base64 is used only to transport the key through configuration; it is not the data encryption algorithm.
4. Writes flush an encrypted temporary file before renaming it into the outbox. The supplied key must authenticate an existing queue before collection can resume. Corruption, wrong keys, and oversized encrypted files are rejected; failed uploads do not silently clear stats.
5. HTTPS uses the runtime's certificate validation, with no trust bypass. Automatic redirects are refused. Profile IDs are carried in request bodies rather than URLs. Authentication values, response bodies, and raw exception messages are excluded from connector logs.
6. Queued data expires after 72 hours by default. Retention is applied before uploading an old queue after restart and during checkpoints. Old incomplete encrypted writes expire too. Profile approvals expire from use after two minutes without refresh and are removed from memory on disconnect.
7. `.statsprivacy off` immediately stops tracking that player, removes their in-memory statistics/XP budgets and reward profile, and persists a withdrawal marker. Background cleanup removes their queued contributions. Surviving schema-v2 contribution IDs stay unchanged across rewritten batches. Re-approval rotates the collection ID, so it cannot resurrect the previously withdrawn queue contents.
8. Preferences are loaded fail-closed on corruption or a wrong key. Approval starts only after it is saved. A failed withdrawal save still blocks that player in memory; uploads pause while persistence retries. Incomplete or unsafe privacy cleanup pauses delivery instead of sending a disallowed batch.
9. DNT cannot be overridden by an allow command or by optional additional opt-in settings. Observing DNT clears the player's tracking and cached reward profile and schedules queued data removal. Its keyed exclusion marker prevents old pending data from being sent after disconnect. A transition back to DNT-off rotates the collection ID, while preserving any separate local opt-out. Preference v1 records migrate without losing existing local withdrawals.

## Operator responsibilities and boundaries

1. Run the server and API under restricted service accounts. Protect process environments and use host/service secret storage. A server owner or compromised server process can read its own environment and plaintext in memory; this design does not hide secrets from that owner. Encrypting files cannot protect against an attacker who also obtains their key.
2. Restrict access to the connector directory and protect backups separately. Preserve the original storage key for saved credentials, queued data, and privacy preferences. Restoring the original key also preserves recorded withdrawals. Rotate and revoke API tokens independently.
3. Use supported server/Mono/OS versions, a valid API certificate, and a TLS 1.2-or-newer deployment policy. Apply patching, access auditing, request-size/rate limits, and abuse monitoring. Redact hosting logs and prevent crash dumps or diagnostics from being retained indiscriminately.
4. Set queue retention and capacity for the real purpose. The 72-hour default is an engineering choice, not a statutory retention period. A full queue keeps newer deltas in memory; a disk failure, full queue at shutdown, or abrupt crash can lose unsaved changes. The queue cap is a batch-count cap, not a fixed byte budget.
5. Retention runs while the connector is active. Stopped/decommissioned servers need an operational cleanup process for outbox files, secrets, backups, and other copies. Use encrypted disks/backups where deleted blocks or snapshots could remain recoverable.
6. A local withdrawal does not erase previously uploaded history, other servers' data, or an upload already in progress. Resolve a reported preference-write failure before reconnecting/restarting; a failed write cannot be promised to survive a crash. Reconcile backups with newer withdrawals instead of restoring old approval state blindly.
7. Withdrawal markers remain to honor future collection preferences after local statistics are removed. Keyed markers remain potentially linkable personal data while the key is available. Define their retention and account-wide deletion handling with the service's privacy workflow. Capturing an in-game approval is a technical control, not a certification that every requirement for valid consent has been met.
8. DNT-off is not automatically legal consent or a lawful basis. The controller still needs the notice, basis, scope, and rights workflow described below. An optional `require_player_opt_in: true` adds a separate approval step; the default `false` follows the game's flag. Additional opt-in mode requires a notice URL and renewed approval when its notice version changes. Old 0.2.0 generated configs retain their `true` value until updated.

## Required before public deployment

1. Identify the controller/processor roles for the service and participating server owners. Determine territorial scope, the lawful basis for collection and cross-server sharing, and any required agreements. Do not assume that a server owner installing a plugin is player consent.
2. Publish a clear player-facing privacy notice covering purposes, identifiers, recipients, retention, contact details, and rights. If relying on consent, implement valid choice and withdrawal; other lawful bases have their own requirements.
3. Implement verified account-wide access/export, correction, history deletion, and applicable objection/restriction workflows. Local controls are implemented, but they do not replace backend suppression, propagation to other servers, or protection against restored backups recreating erased progress. Clear server-owned rewards and Discord links under the defined policy. A per-server token must not authorize arbitrary global account deletion.
4. Implement backend storage protection with managed, separately controlled keys, restricted lookup indexes, audited administrative access, and protected backups. Define retention for linked lifetime progression, idempotency records, logs, Discord links, and server integrations. Local queue expiry does not erase the hosted database.
5. Verify Discord linking, minimize account data returned to the connector, and document role grants/revocation. Establish processor/subprocessor and international transfer arrangements where required for hosting and other providers.
6. Review whether a DPIA or other assessment is required for the actual scale, profiling, audience, and risks. Establish incident response, appropriate breach notification processes, periodic security testing, and restoration tests.

## Primary references

- [GDPR text on EUR-Lex](https://eur-lex.europa.eu/eli/reg/2016/679/oj/eng/): Articles 5 and 6 (principles/lawfulness), 12–22 (individual rights), 25 (design/defaults), 28 (processors), 32–34 (security/breaches), 35 (DPIA), and Chapter V (transfers).
- [EDPB: Secure personal data](https://www.edpb.europa.eu/sme/be-compliant/secure-personal-data_en): encryption, pseudonymisation, and why encrypted data can remain personal data.
