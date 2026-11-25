# API contract — schema version 2

The connector implements the client side of this contract. The ingestion service, D1 database, enrollment and verified linking are in Global-SCP-Stats-Site; the lightweight Discord Worker is in Global-SCP-Stats-Discord-Bot.

## Server enrollment and authentication

1. An authenticated server owner registers a server with the private service. Enrollment needs its own ownership/abuse controls; no secret bootstrap key belongs in the public plugin.
2. The service issues a public `server_id` and a cryptographically random per-server token with at least 256 bits of entropy. Show the token only through the owner's protected setup flow. Store a one-way token digest server-side, using constant-time verification. Never reuse a player identifier as a token.
3. Give each token only `stats:write` for that server and, if enabled, the narrowly scoped profile lookup capability. The service must bind the token to `server_id`; a request field must never override its authenticated server identity.
4. Allow rotation and revocation per server. Master secrets, database credentials, Discord bot tokens, and encryption keys stay in private infrastructure. A server owner can access their own token and submit forged events; a public connector cannot prove gameplay authenticity on an owner-controlled machine. Rate-limit, validate, and quarantine untrusted contributions before they affect global progression.

All requests use `Authorization: Bearer <server-token>` over HTTPS. Redirects are not followed. Response bodies and authorization values are not logged by the connector. Redact request bodies, identifiers, and authorization headers in the hosting stack as well.

## POST /v1/stats/batches

Configure the complete HTTPS URL as `upload_endpoint`.

Headers:

```http
Authorization: Bearer <server-token>
Content-Type: application/json
Idempotency-Key: 63968788-0f8e-424b-bcdf-900bb2dfc96b
```

Example delta batch:

```json
{
  "schema_version": 2,
  "batch_id": "63968788-0f8e-424b-bcdf-900bb2dfc96b",
  "server_id": "example-server",
  "created_at": "2026-11-24T20:00:00.0000000+00:00",
  "players": [
    {
      "user_id": "76561198000000001@steam",
      "playtime_seconds": 30.25,
      "rounds_played": 1,
      "kills": 1,
      "deaths": 0,
      "escapes": 0,
      "actions": { "round_played": 1, "kill": 1, "player.item_used": 2 },
      "xp_earned": 72,
      "damage_dealt": 125.0,
      "damage_received": 5.0,
      "health_restored": 0.0,
      "contribution_id": "d6bd974b-a4de-407a-b27a-5ddbf5beac09",
      "collection_id": "f65dce64-6bc6-4055-a1d7-57cc41776d68"
    }
  ]
}
```

Values are **deltas**, not lifetime totals. The generic `actions.kill` counter describes the same kills as the top-level `kills` field: do not add both together. The XP field already includes configured rewards; do not independently award another copy for the same counters.

Each player delta has its own immutable `contribution_id`. `collection_id` identifies the connector's current local collection approval/policy revision; it is not an authentication credential, an account-wide preference, or independent proof of a valid legal basis. A player withdrawing and later approving collection gets a new ID so old queued data cannot be reactivated.

The backend must:

1. Verify token scope, server identity, schema, IDs, bounds, timestamps, finite non-negative numeric values, action keys, and its agreed XP policy. A local XP cap is an abuse reduction, not evidence of trustworthy data.
2. Require the header and body batch IDs to match. Atomically deduplicate both batches by `(server_id, batch_id)` and individual deltas by `(server_id, contribution_id)`, recording payload digests and checking identity. An identical retry returns success without applying deltas again. A different payload under a previously used ID returns `409` and requires investigation. A new batch containing already accepted contributions acknowledges them without adding their counters or XP again.
3. Apply per-server and global aggregates, accepted XP transactionally with both idempotency records. Retain deduplication records beyond the permitted retry/backup replay horizon, and reject batches older than that horizon rather than recounting them after ledger expiry.
4. Return `204` or another `2xx` only after durable acceptance. This includes any `202`: returning success makes the connector delete its local copy.
5. Leave reward rules and grants to each participating game server. The backend stores stats and XP only.

| Status | Connector behavior |
| --- | --- |
| `2xx` | Acknowledge and remove the encrypted local batch |
| `408`, `429`, `5xx`, connection failure, timeout | Retain and retry with exponential backoff; respect `Retry-After` up to a one-day delay |
| Redirect, `400`, `401`, `403`, `409`, `422`, other non-success | Retain; pause uploads until reload after correction |

An API restart or a lost response must not duplicate XP or stats.

When local privacy controls remove one player from a mixed queued batch, the connector writes a new batch ID while preserving every surviving contribution ID and value. This also covers a lost receipt after the original batch was already accepted. **A backend that only deduplicates batch IDs is incompatible with schema v2's privacy rewrite flow.**

## POST /v1/profiles/lookup

Configure the complete same-origin HTTPS URL as `profile_endpoint`. The player ID stays in the JSON body rather than appearing in access-log URLs.

```json
{ "user_id": "76561198000000001@steam" }
```

Return `200` with private/no-store caching headers:

```json
{
  "user_id": "76561198000000001@steam",
  "total_xp": 2400,
  "level": 10,
  "revision": 42,
  "server_stats": { "xp_earned": 1200, "kills": 12, "deaths": 7, "escapes": 3, "rounds_played": 20, "playtime_seconds": 3600, "actions": { "player.item_used": 15 } }
}
```

The backend returns global XP/level/revision and this authenticated server's lifetime stats, including action counters. It does not return reward decisions. Server badges evaluate local metric/threshold rules against server_stats, using level = floor(sqrt(server_xp / 250)) + 1 for server-level rewards. Reward definitions and grants never enter the backend.

The connector requests profiles only for players eligible under the game's DNT flag and local collection policy. Responses remain bounded to 64 KiB, identity-checked and cached in memory for two minutes. DNT or withdrawal clears the cache and stops lookups. Stale or unavailable server stats cannot award a badge.

## Server rewards and Discord

Configure each badge's reward_id, metric, threshold, label/color and priority in that server's configuration. Existing staff badges are preserved by default. Reward IDs are local names, not API approvals. The bot displays stats and handles verified privacy requests; it does not assign rewards or roles. A server can add its own role integration separately.

## Privacy enforcement required before launch

The connector implements game DNT enforcement, additional local opt-out/approval, and removal of queued contributions. DNT always takes priority. DNT-off normally permits tracking without another allow command; optional additional opt-in can make the local policy stricter. DNT changes observed while connected retire older pending collection IDs. The service must still expose verified account-wide access/export/deletion and collection preference workflows. Authenticate ownership of the player account separately: a server token asserting a user ID must not be sufficient to delete that account's global history.

Propagate account-wide changes to participating connectors, suppress opted-out/deleted players before ingestion and rewards, and prevent old queue retries or restored backups from recreating erased profiles. Local `.statsprivacy off` cannot retract an upload already in progress or erase hosted data. Account-wide preference propagation and history deletion remain launch blockers; this version is for controlled testing with server collection off by default.

## Upgrading from 0.1.0

The encrypted file format remains GSS1. Existing schema-v1 batches remain unchanged during ordinary retries. Under the default DNT-based collection policy, legacy contributions can be permitted unless excluded by an observed DNT flag or saved local withdrawal. Under optional additional opt-in, unapproved queued contributions are removed. Game flags are known when observed by the server; no client can retroactively retract an upload already received by the API.

A fully disallowed legacy batch can be removed. Partially removing a player from a mixed schema-v1 batch would require new contribution IDs, which cannot safely deduplicate an earlier lost receipt. That situation pauses uploads and preserves the mixed file for an explicit backend migration; the connector does not guess whether it was accepted. Resolve legacy files and deploy contribution-level deduplication before enabling schema-v2 ingestion.
