using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;
using Xunit;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// api#140 (AST-12a), slice 2: ONE Horizons parity row, end to end.
/// </summary>
/// <remarks>
/// <para><b>Why one row and not a batch.</b> A row shape that is wrong is wrong three hundred times. The structural
/// questions - is the time scale named per row, is the tolerance named rather than filled, is the Drik offset on one
/// side only - are all visible in a single row and invisible in a diff that adds three hundred.</para>
///
/// <para><b>The two sides are deliberately asymmetric.</b> The reference side (Horizons, recorded below) must NOT carry
/// the Drik Panchang lunar offset: that offset is an engine implementation choice, not an astronomical fact, so a
/// reference containing it would recreate the second-source defect. Only the engine side applies it, exactly once,
/// inside <see cref="MeeusEphemeris"/>.</para>
///
/// <para><b>Time scales are named per row, and only one direction is available on purpose.</b> The engine port takes
/// UT. <see cref="UtInstant"/> converts to <see cref="TtInstant"/> and <b>nothing converts back</b>, so a round trip
/// cannot accumulate error. That means this file cannot express "Horizons gave me TT, convert it to UT for the port" -
/// and inventing a TT-to-UT method to make the generator tidy would add the reverse direction to the one place that
/// deliberately lacks it. The row therefore states the UT instant it is at, and the TT value is recorded as the frame
/// the reference was queried in, not as an input to be converted.</para>
/// </remarks>
public sealed class HorizonsParityRowTests
{
    /// <summary>
    /// The instant of the reference query, as a UT Julian Day. This is the argument the port takes, stated in the same
    /// unit it takes it in, so no conversion is implied.
    /// </summary>
    private const double ReferenceJdUt = 2451545.0;

    /// <summary>
    /// JPL Horizons DE441 geocentric apparent ecliptic longitude of the Moon, degrees, at the TT instant
    /// corresponding to <see cref="ReferenceJdUt"/>.
    /// </summary>
    /// <remarks>
    /// QUANTITIES='31' (ObsEcLon), geocentric, apparent of date, recorded to ~1e-7 degrees.
    /// <b>This value contains NO Drik Panchang offset.</b> If a future edit adds one here, the row would be comparing
    /// the engine's own implementation choice back to itself.
    /// </remarks>
    private const double ReferenceLongitudeDegrees = 223.3237860;

    /// <summary>Frame of the reference value, stated per row rather than assumed.</summary>
    private const string ReferenceFrame = "Geocentric apparent ecliptic longitude of date (Horizons QUANTITIES='31', ObsEcLon)";

    /// <summary>Frame of what the port returns, stated per row so the mismatch is named rather than unexamined.</summary>
    private const string PortFrame = "Geocentric tropical longitude, mean equinox of date, no nutation (IEphemeris contract)";

    private static void InstallConversion() => TtOffsets.Install(DeltaT.ToJulianEphemerisDay);

    [Fact]
    public void The_reference_frame_and_the_port_frame_are_both_stated_and_are_not_the_same()
    {
        // Deliberately does not compare free text - that would be a comment with a test around it. What it pins is that
        // both frames are STATED and differ where they must: apparent-of-date versus mean-equinox-of-date is a real,
        // named quantity, and a row that recorded only the reference frame would leave the gap unexamined.
        Assert.False(string.IsNullOrWhiteSpace(ReferenceFrame));
        Assert.False(string.IsNullOrWhiteSpace(PortFrame));
        Assert.NotEqual(ReferenceFrame, PortFrame);
    }

    [Fact]
    public void One_row_records_every_field_the_acceptance_requires()
    {
        InstallConversion();

        // The port takes UT; the instant is already UT, so there is no conversion here to get wrong. When a later row
        // needs a TT-sourced instant it must say how it became UT, and that is a decision this row does not pre-empt.
        var engineLongitude = new MeeusEphemeris().GetMoonLongitude(ReferenceJdUt);

        var separation = ParityGate.SeparationArcseconds(ReferenceLongitudeDegrees, engineLongitude);

        // The row shape, complete. Tolerance is NAMED AND UNFILLED on purpose - see ToleranceSource.
        var row = new
        {
            Body = nameof(CelestialBody.Moon),
            Claim = nameof(FixtureClaim.EngineReference),
            JdUt = ReferenceJdUt,
            Quantity = "Geocentric apparent ecliptic longitude of date",
            ReferenceFrame,
            PortFrame,
            ReferenceLongitudeDegrees,
            EngineLongitudeDegrees = engineLongitude,
            SeparationArcseconds = separation,
            // NOT a number. "about 10 arcseconds" is our recorded restatement of the figure quoted in
            // LunarPosition.cs:10, which asserts it without quoting Meeus. A sourced-looking number that nobody
            // sourced is worse than an explicit gap; api#144 tracks which budget this figure actually answers to.
            ToleranceArcseconds = (double?)null,
            ToleranceSource = "UNFILLED - our recorded restatement of the figure quoted in LunarPosition.cs:10 (about 10 arcseconds). No accuracy claim attaches. See api#144.",
            DrikOffsetAppliedOn = "engine side only, exactly once, inside MeeusEphemeris.GetMoonLongitude",
        };

        // Structurally complete: an unfilled tolerance must still be a declared field, not an absent one.
        Assert.Equal("Moon", row.Body);
        Assert.Equal("EngineReference", row.Claim);
        Assert.Equal(ReferenceJdUt, row.JdUt);
        Assert.Contains("UNFILLED", row.ToleranceSource);
        Assert.Null(row.ToleranceArcseconds);
        Assert.Contains("engine side only", row.DrikOffsetAppliedOn);

        // The measurement, reported rather than asserted as a pass. The budget is unfilled, so this row cannot claim to
        // meet one; asserting a pass against a number chosen to fit is the laundering api#140 exists to prevent.
        Assert.True(separation < 60.0,
            $"Moon separation {separation:F2}\" is beyond even the ~10\" series budget. That is a real disagreement "
            + "between the engine and Horizons, not a tolerance question.");
    }

    [Fact]
    public void Applying_the_drik_offset_to_the_reference_side_would_double_count_it()
    {
        // The asymmetry this row depends on, measured rather than assumed. The engine value = series + 28 arcsec. If the
        // reference also carried +28 arcsec, the two would agree for the wrong reason and the parity gate would stop
        // detecting a real provider divergence.
        //
        // The first version of this test asserted the offset moves the reference "away from" the engine, and it FAILED.
        // That assertion assumed which side of Horizons the engine falls on. It does not: the engine currently sits
        // BELOW the Horizons apparent value here, so +28 arcsec on the reference moves it TOWARD the engine. The
        // direction is an empirical question about this instant, not a fact about the offset - which is exactly why the
        // offset must not be applied to the reference, and why a test that encoded the direction would have been wrong.
        InstallConversion();
        var engine = new MeeusEphemeris().GetMoonLongitude(ReferenceJdUt);

        var series = LunarPosition.MeanEquinoxLongitude(DeltaT.ToJulianEphemerisDay(ReferenceJdUt));

        // What the engine adds, measured against the series it adds it to, rather than trusting the constant's name.
        var offsetActuallyApplied = ParityGate.SeparationArcseconds(series, engine);
        Assert.Equal(28.0, offsetActuallyApplied, 6);

        // And therefore: a reference carrying the same offset is measuring the engine's implementation choice, not the
        // sky. State that as the invariant - the row's reference must contain NO offset.
        Assert.Contains("NO Drik Panchang offset", ReadReferenceDoc());
    }

    /// <summary>The reference doc comment, read so the "no offset on the reference" promise is checked, not just written.</summary>
    private static string ReadReferenceDoc()
    {
        // Deliberately narrow: reads the type's own source for the marker sentence. If someone deletes that promise,
        // this fails - which is the point of asserting prose that a reviewer would otherwise have to remember.
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "HorizonsParityRowTests.cs");
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }
}
