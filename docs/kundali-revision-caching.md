# Kundali revision-aware caching and profile-change propagation (KUN-21 / KUN-22)

Persisted results remain the source of truth (KUN-04). The cache is an optimisation and is safe to flush entirely;
nothing below makes the cache authoritative for correctness.

## Revision identity

A result is identified by a **revision key**: a SHA-256 over

| Input | Why it can change the answer |
|---|---|
| account id, profile id | an entry must never be readable across accounts |
| Vedic Profile revision | a birth-input or settings edit changes what the engine should compute |
| ayanamsa (existing Settings) | changes every sidereal longitude |
| engine, ephemeris, rule-set, timezone-data versions | an upgrade must not be served as current |

The key is hashed, so no birth date, time, coordinate, name or timezone appears in a cache key, a PostgreSQL cache row or
a log line. `KundaliCacheKeys` is the only producer; `IsWellFormed` rejects anything it did not build.

## Section reads

`GET /api/v1/kundali/{profileId}/overview` reads through `IKundaliResultCache` (the shared HybridCache behind a port,
policy `kundali`). A miss computes; a hit is served **only** when its freshness envelope is still valid:

- the key already excludes other revisions and other version tuples;
- `CurrentDashaExpiresAtUtc` is the end of the mahadasha containing the response instant - the only field in this
  payload that changes without a revision change - so the entry expires exactly at that boundary rather than after an
  arbitrary lifetime (12 hour ceiling as a backstop).

A cache read, write or remove failure is a miss, never an error. After computing, the profile is **re-read**: if its
revision moved while the payload was being built, the payload is discarded as 409 `ProfileChanged` and not cached.
That is the compare-and-swap for read paths.

## Freshness on the envelope

Every section payload and the aggregate view carry:

- `profileRevision` - the profile's input revision right now;
- `inputRevision` - the revision the result was computed from;
- `isCurrent` - true only for a succeeded, non-superseded revision computed from the present revision;
- `revisionKey` - the opaque identity above, for client query caches;
- `supersededByRevision` on a revision summary, when a newer revision replaced it.

Staleness is computed at read time from the profile revision and the supersession marker. It is never derived from a
cached copy, so a stale cache entry cannot make a stale revision look current.

## HTTP

Private payloads stay `Cache-Control: private, no-store`; the gain is a validator. `RevisionCacheHeaders` sets an `ETag`
derived from the freshness envelope and answers `If-None-Match` with `304`. A revision change always produces a new tag.

## Profile edits (KUN-22)

`POST`-free internal path: `IKundaliFacade.ApplyProfileChangeAsync` (the profile module calls it when a profile is
replaced). One transaction:

1. `KundaliAggregates.ProfileRevision` advances by compare-and-swap on the value read from the profile;
2. the aggregate revision is bumped (so a client's optimistic update sees the change);
3. an in-flight revision is set to `Failed` with `superseded_by_profile_edit` and `SupersededByProfileRevision`;
4. exactly one `KundaliProfileInvalidations` row is written (`Id`, `UserId`, `ProfileId`, `OldRevision`,
   `NewRevision`, `ChangeKind`, `CancelledRevisionId`, `PublishedAtUtc`) - ids and revisions only, never birth data.

Change kinds: `display` (name only), `birth-input`, `calculation-settings`. A `display` change advances the profile
revision but creates no Kundali revision, abandons nothing, invalidates no cache entry and calls the engine zero times - a
rename costs no calculation. Any unrecognised kind is treated as a real change: failing to recalculate is recoverable,
showing a result computed from different inputs is not.

Delivery is idempotent: `(UserId, ProfileId, NewRevision)` is unique, and a replay of an already-applied revision is a
no-op that cannot cancel work a later revision legitimately started.

## Obsolete background results

`IKundaliFacade.DiscardObsoleteResultAsync` is the admission check for a worker result. It is discarded with a reason
code when:

| Reason | Condition |
|---|---|
| `profile-revision-changed` | the result's profile revision is not the profile's present revision |
| `provenance-changed` | the version tuple or ayanamsa moved since the result was computed |
| `superseded` | the revision is no longer the aggregate's active revision |
| `not-found` | the revision is not this owner's |

Store-level enforcement is independent of the facade: `SaveSectionAsync` rejects a write for a revision carrying
`SupersededByProfileRevision` and rejects an intermediate write to a terminal revision, so a late payload can never
become readable even if a consumer skips the facade check.

## Measurements

The p95 section budgets from #31 (400 ms cold / 100 ms warm) are acceptance gates. No timing harness is committed here;
warm/cold measurements are recorded per deployment, not in this repository, and a miss is reported rather than waived.
