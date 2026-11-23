# ADR-0016 Amendment 1: the Moon accuracy budget against the series that ships

- Status: Proposed - awaiting architect ruling (see Decision required)
- Date: 2026-11-22
- Amends: [ADR-0016](0016-kundali-ephemeris-provider-and-capability-boundary.md) section 4, Moon row only
- Related: [#144](https://github.com/aprraju/myvedicstar/issues/144), [#140](https://github.com/aprraju/myvedicstar/issues/140),
  [#30](https://github.com/aprraju/myvedicstar/issues/30)
- Does not change: ADR-0016's body, its status, its capability boundary, or any engine code

## Why an amendment and not an edit

ADR-0016 records an open defect in its own text: it ships a Moon series, sets a budget for that series, and rejects the
series by that budget. The defect is stated honestly at
`docs/day/adr/0016-kundali-ephemeris-provider-and-capability-boundary.md:71-77`, and the resolution space is named at
:78-82 without being taken, deliberately: `:83-85` says so and marks the status **unresolved**.

The blocked work is calibration. #140 cannot fill the Moon tolerance in the parity fixture while the budget's scope is
undecided, because a tolerance is the number a row is checked against. This amendment supplies the decision material and
takes nothing: it does not edit section 4, does not fill #140's tolerance, and does not touch
`src/MyVedicDay.Astronomy.Calculation/LunarPosition.cs`.

## The conflict, stated at file:line

Three statements in one document disagree. Each is quoted with its line, checked against `origin/main`.

1. **What ships.** ADR-0016 `:61-62` describes the Moon as the "Meeus chapter 47 truncated series" with accuracy "stated
   in `LunarPosition.cs:10-11`: about 10 arcseconds in longitude, 4 in latitude". The code agrees with the description:
   `src/MyVedicDay.Astronomy.Calculation/LunarPosition.cs:9-11` documents "about 10 arc-seconds in longitude and 4 in
   latitude".

2. **What is demanded.** ADR-0016 `:164` sets `| Sidereal longitude, Moon | <= 3 arcseconds |`, under the heading at
   `:156`, "Accuracy budgets (**acceptance tolerances, not aspirations**)", and `:158-159` states "They are validation
   gates in KUN-20; exceeding them fails the build."

3. **What is rejected.** ADR-0016 `:104` rejects the same series: `| ADR-0002's truncated Meeus + Standish Keplerian
   set | ... | Demonstrably insufficient: Moon 10 arcseconds, ... |`.

So the shipped configuration is the one the document rejects, measured against the budget the document set, in a
document that calls that budget a build-failing gate. The gap is roughly a factor of three, and the factor is not the
point - the point is that the same document calls one number both insufficient and required.

### On the ~10" figure itself

The ~10" figure is **our restatement, not a citation**. `LunarPosition.cs:9-11` states it in prose under a summary that
does name Meeus chapter 47 as the source of the *series*; it does not cite a Meeus passage for the *accuracy claim*, and
Meeus's own stated figure for the chapter-47 truncated series is not reproduced there. ADR-0016 `:67-69` already records
this honestly ("This ADR does not attribute that figure to a specific Meeus passage, so it is recorded here as the
implementation's own stated accuracy until a citation is added").

This matters for one option specifically. Any resolution that writes a *new* number into section 4 inherits the same
problem in a worse form: the new number would also be our restatement, and it would now be a gate. That is why option
(b) below is not the cheap option it appears to be.

## Decision required

One question, and it is the owner's, not the architect's to answer on their own authority:

> **Is section 4's <= 3" Moon budget the acceptance gate for the *finished* AST engine, or the gate for what *ships
> now*?**

The answer decides between two different shapes of resolution, and it does so without anyone defending a number:

- **Finished engine** -> the analytical Moon is *interim by definition*; the budget does not move, it gains a second,
  narrower scope. This is **(a)**.
- **What ships now** -> the budget applies to the current implementation, so the implementation must meet it. This is
  **(c)**, or a **(b)** chosen knowingly and in writing.

## Resolutions

Each option is written to be ruled on, not drafted further. Cost is stated in the currency the repo actually spends:
ADR revisions, engine versions, and which gates stop meaning what.

### (a) Scope the budget: the analytical Moon is interim until AST-12

**What it says.** Section 4's `<= 3"` applies to the *provider that replaces the analytical series*, i.e. the AST-12
end state. The analytical provider carries its own declared budget (its stated ~10") until AST-12 lands. #140 then
calibrates the Moon row against the analytical series at ~10" and records that the row is measuring the interim provider.

**Consequences, in full.**

- *Scope.* Section 4 gains a scope column or a scoped second table. The `<= 3"` number does not move, which preserves the
  owner's #40 stance that budgets are not traded. This is the only option under which no existing number changes meaning.
- *Gate integrity.* Two budgets now exist for one quantity (interim ~10", target 3"). This is precisely the
  `(body, claim)` problem #130 already solved for the capability boundary; done properly it reuses that mechanism rather
  than adding a parallel number. Done improperly it re-creates the two-sources defect.
- *Calibration.* #140's Moon tolerance becomes a number for the *analytical* series. When AST-12 lands, that tolerance
  must be replaced, not widened - a new row, a new budget, a new fixture. Cost: one fixture regeneration at AST-12.
- *Engine versioning.* Consistent with ADR-0016's own cost note (the ephemeris version is customer-visible provenance).
  The interim/target split is already implied by the provider being replaceable behind one port.
- *Honesty.* The rejection table's "Demonstrably insufficient" verdict stays true *as a verdict about the target*, and
  stops being a contradiction because it is no longer a statement about what ships against its own gate.
- *What stays unproven.* Until AST-12, no fixture demonstrates the engine can meet `<= 3"`; the gate is unexercised for
  its target value. That is a real gap and must be recorded as such, not implied away as green.
- *Cost.* Medium. One ADR revision, one fixture regeneration at AST-12, one provenance field distinguishing interim
  from target.

### (b) Widen the Moon budget for this phase

**What it says.** Section 4's Moon row changes from `<= 3"` to `<= 10"` (or whatever the analytical series actually
measures), with the basis recorded as the analytical series' own stated accuracy.

**Consequences, in full.**

- *Lowest effort.* One table cell, one basis note, no code, no fixture change beyond #140's own row.
- *Highest structural cost, and the reason it must be a knowing choice.* The number written down does not exist yet as a
  validated fact: `LunarPosition.cs:9-11` states ~10" **without citing Meeus**, as recorded above. So (b) writes a gate
  from our own restatement.
- *The failure mode that matters.* A budget derived from the implementation it validates cannot fail. Once the gate is
  set from the series' measured error, the fixture will pass by construction, and any future regression smaller than the
  gate is invisible. Section 4's own words - "acceptance tolerances, not aspirations" - are the exact thing lost. The
  gate keeps its form and loses its function.
- *Mitigation, if chosen.* It can be made honest only if the new number is **cited to an external authority** rather than
  to our own prose, and recorded as a **knowingly reduced gate with a date and a re-open condition** (re-open at AST-12).
  Without both, (b) is a silent weakening wearing an owner decision's clothes.
- *Precedent.* This is the option that, taken quietly, would license every future "just move the budget" without a ruling.
  Taken openly and dated, it is defensible.
- *Cost.* Low effort, high precedent cost, and it requires the citation work the current figure does not have.

### (c) Keep section 4, extend the series to meet it

**What it says.** The budget stays at `<= 3"`, section 4 keeps its stated meaning, and the Moon series is extended (full
or denser Meeus term set, or the AST-12 provider brought forward) until it meets the gate.

**Consequences, in full.**

- *Only option under which section 4 keeps its stated meaning* with no scope split and no number change.
- *Highest implementation cost.* Real engine work: larger term tables, their own truncation budgeting, their own
  independent fixtures, a new engine version. It is not a documentation change and must not be scheduled as one.
- *Blocks AST-12 alignment.* The analytical series extension competes with the provider migration for the same effort;
  doing both is double work unless the extension *is* the migration.
- *Honesty gain.* Under (c), #140 records the Moon row as **not yet meeting the gate** rather than fitting a tolerance to
  it. The fixture states a distance, not a pass. That is the most truthful artifact of the three.
- *Risk.* Extending a series to hit a specific number invites fitting the series to the fixture. The extension must be
  chosen by published truncation criteria, not by closing the gap, or (c) degenerates into (b) with more code.
- *Cost.* High: engine work, new version, new fixtures, and it is the only option that delays Kundali accuracy work.

## What this amendment does not do

- **Does not edit ADR-0016's body.** The contradiction at `:71-77` stays as written until a ruling; it is an accurate
  record of an unresolved state.
- **Does not fill #140's Moon tolerance.** The tolerance remains unfilled until the scope question is answered; filling it
  now would pre-empt the ruling.
- **Does not touch `LunarPosition.cs`.** The implementation is correct for what it implements.
- **Does not pick an option.** The ruling is the architect's and the owner's; this document exists so that ruling is a
  choice between costed consequences rather than a draft to be written.

## Unproven properties, stated rather than implied

- No fixture currently demonstrates the engine meets `<= 3"` for the Moon. Under (a) and (b) it will not, by design.
- The ~10" figure is an uncited restatement. Under (b) it would become an uncited gate.
- The 4" latitude figure in `LunarPosition.cs:10-11` shares the same status and is not separately budgeted in section 4.
- Whether a denser Meeus term set reaches `<= 3"` under published truncation criteria is not established here; (c) assumes
  it is possible, and that assumption is unverified in this document.
