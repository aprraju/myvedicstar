# FR-06 provider migration: mapped call inventory

AST-06 (#71). What the existing consumers call, and how each call reaches the provider.

## The port

`IEphemeris` in `src/MyVedicDay.Astronomy.Calculation/Ephemeris.cs` - four methods, unchanged:

| Method | Returns | Adapter delegates to |
| --- | --- | --- |
| `GetSunLongitude(double jdUt)` | tropical longitude, mean equinox of date | `IEphemerisEngine.GetTropicalLongitude(Sun, jdUt)` |
| `GetMoonLongitude(double jdUt)` | tropical longitude, mean equinox of date | `IEphemerisEngine.GetTropicalLongitude(Moon, jdUt)` |
| `GetSunEquatorial(double jdUt)` | apparent RA/Dec | `IEphemerisEngine.GetEquatorial(Sun, jdUt)` |
| `GetMoonEquatorial(double jdUt)` | apparent RA/Dec + horizontal parallax | `IEphemerisEngine.GetEquatorial(Moon, jdUt)` |

`AstronomyEngineEphemerisAdapter` contains no astronomy. Every value comes from the engine, which is what makes the
delegation test meaningful.

## Consumers (unchanged production code)

| Consumer | How it uses the port |
| --- | --- |
| `MyVedicDay.Panchang.Calculation` (`PanchangElementCalculator`) | Sun and Moon longitude for Tithi, Nakshatra, Yoga, Karana |
| `MyVedicDay.Astronomy.Calculation` (`SunEventCalculator`) | Sun equatorial for sunrise/sunset |
| `MyVedicDay.Astronomy.Calculation` (`MoonEventCalculator`) | Moon equatorial and parallax for moonrise/moonset |
| `MyVedicDay.Astronomy.Calculation` (`LunisolarCalendar`) | Sun and Moon longitude for month boundaries |
| `MyVedicDay.Astronomy.Calculation` (`KeplerianPlanetEphemeris`) | separate `IPlanetEphemeris` port, not migrated here |

**No consumer project requires a production-code edit for this story.** They resolve `IEphemeris` through DI and see
whichever provider the configuration selects.

## Provider selection

| Key | Registered `IEphemeris` | Notes |
| --- | --- | --- |
| `analytical` | `MeeusEphemeris` | Default until AST-12 (#77) |
| `spice` | `AstronomyEngineEphemerisAdapter` | Over `IEphemerisEngine` |
| `compare` | `ComparingEphemeris` | Runs both, returns the primary, emits deltas |

Unknown, malformed or absent keys throw at registration. There is no silent fallback.

## Parity fixtures

`astronomy/parity-fixtures.json`. Tolerance is stored in the fixture data, never in a code comment, so raising it is a
visible diff.

**Honest limitation:** the current fixture values are independent references (Meeus worked examples) for Sun and
Moon, which pin the analytical provider against a published source. They do **not** constitute an independent
accuracy claim for SPICE: comparing SPICE to the analytical series proves the two *agree*, not that either is right.
Absolute accuracy against JPL Horizons remains AST-09 (#74) work, and is listed as outstanding rather than implied.
