# Shadbala threshold basis — owner-confirmed (api#116)

**Status: the required-rupas table IS owner-confirmed, 2026-11-22.** It ships as versioned config
(`Shadbala.RulesVersion = "shadbala-classical-owner-confirmed-1"`). The values did not change when they were confirmed;
what changed is the recorded decision and the rules version.

> This file previously read "owner-pending". The history below is kept because it explains why the confirmation gate has
> the shape it does.

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

Each value carries **its own** ownership rather than one table-level flag, because a correction may settle one planet
and leave the other six open — a single bit would force the first confirmation to be a false statement about the rest,
and would make a partial correction impossible to express.

## Why this was safe while unconfirmed, and what changed

Shadbala reports `complete: false` for every planet: Kala needs the birth moment's diurnal division and Chesta needs a
true velocity series, and both are withheld rather than approximated. A partial total **understates** strength, so a
planet that looks short of its threshold may simply be missing two components. Four independent signals say so —
section `complete`, per-planet `isComplete`, per-planet `componentsWithheld`, and
`componentsInTotal`/`componentsExcludedFromTotal`. No user can obtain a confirmed-looking answer.

**The day Kala and Chesta are implemented and `complete` can be `true`, that stops being true.** The ratio moves from
visibly provisional to apparently authoritative — and it would be authoritative against a number nobody approved. That
is why the threshold was confirmed before completeness became reachable, rather than after.

## The gate today

The gate asserts two premises explicitly, because a single disjunction over the live state would now pass on the
"confirmed" branch and never evaluate the other:

1. **Unconfirmed basis + a complete result must fail.** Driven by a synthetic unconfirmed state, so the branch is still
   exercised.
2. **Confirmed basis + a complete result is permitted.** This is the state the product is now in.

The gate is **kept, not retired**. A confirmation is reversible: a withdrawn or corrected value returns the product to
an unconfirmed basis, and retiring the gate would mean nobody notices. `ThresholdBasis.BlockingMessage` is likewise kept
and unit-tested directly against a synthetic unconfirmed state, so it cannot rot while it is unreachable.

## What "resolved" required (done 2026-11-22)



1. ✅ The owner confirmed the values verbatim: Sun 5, Moon 6, Mars 5, Mercury 7, Jupiter 6.5, Venus 5.5, Saturn 5. The
   numbers did not change.
2. ✅ `Shadbala.RulesVersion` moved to `shadbala-classical-owner-confirmed-1`; the old string stays citable in the
   commit history and is named in `RulesVersion`'s remarks.
3. ✅ `Shadbala.ThresholdBasis` records `IsOwnerConfirmed: true`, `Status: owner-confirmed`, and `DecidedOn: 2026-11-22`
   per threshold.
4. ✅ The gate is kept and both branches are exercised.

**What this confirmation does NOT settle:** the tolerance budget in **#144** (a separate decision), and the withheld
components — **Kala and Chesta are still not implemented**, so Shadbala remains component-partial for its own reasons.

## Ownership

- **api#116** — this item: threshold basis.
- **#144** — the tolerance decision, same owner, related but distinct.

Where the numbers came from is recorded in `Shadbala.RequiredRupas` and the class remarks; the numbers are classical
required rupas, transcribed as engine config. The question this file leaves open is not where they came from but
whether they are the values the product should ship.
