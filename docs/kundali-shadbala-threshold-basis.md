# Shadbala threshold basis — owner-pending (api#116)

**Status: the required-rupas table is NOT owner-confirmed.** It ships as versioned config
(`Shadbala.RulesVersion = "shadbala-classical-partial-1"`) and the values are published, citable and revisable, but
nobody has signed them off.

## The values, as they stand today

| Planet | Required rupas |
|---|---|
| Sun | 5.0 |
| Moon | 6.0 |
| Mars | 5.0 |
| Mercury | 7.0 |
| Jupiter | 6.5 |
| Venus | 5.5 |
| Saturn | 5.0 |

Pinned by `ShadbalaThresholdBasisTests.Every_published_threshold_is_pinned_at_its_current_unconfirmed_value`, so a
change to any of them is a visible diff rather than a silent redefinition.

## Why this is safe today, and what changes when it is not

Shadbala reports `complete: false` for every planet: Kala needs the birth moment's diurnal division and Chesta needs a
true velocity series, and both are withheld rather than approximated. A partial total **understates** strength, so a
planet that looks short of its threshold may simply be missing two components. Four independent signals say so —
section `complete`, per-planet `isComplete`, per-planet `componentsWithheld`, and
`componentsInTotal`/`componentsExcludedFromTotal`. No user can obtain a confirmed-looking answer.

**The day Kala and Chesta are implemented and `complete` can be `true`, that stops being true.** The ratio moves from
visibly provisional to apparently authoritative, and it would be authoritative against a number nobody approved.

## The gate

`ShadbalaThresholdBasisTests.Completeness_is_impossible_or_the_threshold_basis_is_owner_confirmed` asserts:

> either a complete result is unreachable, **or** `Shadbala.ThresholdBasis.IsOwnerConfirmed` is true.

Today the first half holds. When it stops holding the suite fails, and the only way to make it pass is to record a real
owner confirmation — a decision, not a formatting fix.

## What "resolved" requires

1. The owner confirms or corrects the values above.
2. `Shadbala.RulesVersion` moves to a new version; the old values stay citable.
3. `Shadbala.ThresholdBasis` is updated to `IsOwnerConfirmed: true` with the confirming owner named.
4. The gate is restated in the PR that makes `complete: true` reachable.

Documenting an unconfirmed number does not confirm it. This file and the test exist so the switch is not flipped
silently.

## Ownership

- **api#116** — this item: threshold basis.
- **#144** — the tolerance decision, same owner, related but distinct.

Where the numbers came from is recorded in `Shadbala.RequiredRupas` and the class remarks; the numbers are classical
required rupas, transcribed as engine config. The question this file leaves open is not where they came from but
whether they are the values the product should ship.
