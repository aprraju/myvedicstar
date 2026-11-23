using MyVedicStar.Kundali.Shared;
using Xunit;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// KUN-17 / api#57 gap 3. The download URL is the only place a caller can reach a stored artifact, so its expiry rules
/// are where "a stale artifact must not be readable" is either true or merely intended.
/// </summary>
public sealed class KundaliReportDownloadLeaseTests
{
    private static readonly DateTimeOffset Issued = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_published_expiry_is_the_issue_window_when_the_report_outlives_it()
    {
        // The ordinary case: the report is long-lived, so the short window binds. Publishing the window and enforcing
        // the window are the same number here.
        var expiry = KundaliReportDownloadLease.EffectiveExpiry(Issued, Issued.AddDays(30));

        Assert.Equal(Issued + KundaliReportDownloadLease.IssueWindow, expiry);
    }

    [Fact]
    public void The_published_expiry_is_the_REPORT_lifetime_when_that_is_earlier()
    {
        // THE constraint: the effective expiry is the MINIMUM, not the ceiling. A report expiring in two minutes must
        // publish two minutes, not the fifteen-minute window - otherwise the published field is a promise the server
        // then breaks by refusing the URL two minutes in. Publishing the ceiling while enforcing something shorter is
        // exactly the lie this field exists to prevent.
        var reportExpiry = Issued.AddMinutes(2);

        var expiry = KundaliReportDownloadLease.EffectiveExpiry(Issued, reportExpiry);

        Assert.Equal(reportExpiry, expiry);
        Assert.NotEqual(Issued + KundaliReportDownloadLease.IssueWindow, expiry);
    }

    [Fact]
    public void A_deleted_report_kills_the_url_immediately_whatever_the_window_says()
    {
        // Stateless revocation: no report lifetime means the URL is dead now, even though the issue window has barely
        // started. This is what avoids a denylist.
        Assert.Null(KundaliReportDownloadLease.EffectiveExpiry(Issued, null));
        Assert.False(KundaliReportDownloadLease.IsReadable(Issued, null, Issued.AddSeconds(1)));
    }

    [Fact]
    public void The_url_is_readable_before_the_earlier_boundary_and_not_at_it()
    {
        // Half-open, matching the dasha interval rule: at the boundary the URL has stopped working.
        var reportExpiry = Issued.AddMinutes(2);

        Assert.True(KundaliReportDownloadLease.IsReadable(Issued, reportExpiry, Issued.AddMinutes(1)));
        Assert.False(KundaliReportDownloadLease.IsReadable(Issued, reportExpiry, Issued.AddMinutes(2)));
    }

    [Fact]
    public void Shrinking_the_report_lifetime_revokes_a_url_that_was_previously_readable()
    {
        // The re-check at download, expressed as a fact rather than a comment: the same URL, the same clock, and only
        // the live report lifetime changed. If validity were captured at issue time this would still be readable.
        var now = Issued.AddMinutes(5);

        Assert.True(KundaliReportDownloadLease.IsReadable(Issued, Issued.AddDays(30), now));
        Assert.False(KundaliReportDownloadLease.IsReadable(Issued, Issued.AddMinutes(3), now));
    }

    [Fact]
    public void The_report_status_set_has_no_stale_member()
    {
        // A superseded report is not a state, it is a queued/running/ready report whose Revision no longer matches the
        // profile. Pinning the absence keeps the freshness envelope the single way to express it.
        Assert.Equal(4, KundaliReportStatus.All.Count);
        Assert.DoesNotContain("stale", KundaliReportStatus.All);
        Assert.Contains(KundaliReportStatus.Queued, KundaliReportStatus.All);
    }

    [Fact]
    public void A_failure_names_where_it_happened_and_a_code_from_the_closed_set()
    {
        // api#57 B1: this assertion is STRONGER than the one it replaces, not adapted to pass. The old test only required
        // a non-empty string, which any server sentence satisfied - the defect the code set exists to remove.
        var failure = new KundaliReportFailure(KundaliSections.Yogas, KundaliReportFailureCodes.SectionEmpty);

        Assert.Contains(failure.Section, KundaliSections.All);
        Assert.Contains(failure.Code, KundaliReportFailureCodes.All);
        Assert.True(failure.IsKnownCode);
    }

    [Fact]
    public void An_unlisted_reason_is_representable_but_never_a_known_code()
    {
        // The type does not crash on an unknown code - a store read from an older row must still deserialise. It reports
        // itself as unknown, which is what lets a caller refuse to render it rather than showing server prose.
        var unknown = new KundaliReportFailure(KundaliSections.Yogas, "rule set unavailable");

        Assert.False(unknown.IsKnownCode);
        Assert.DoesNotContain(unknown.Code, KundaliReportFailureCodes.All);
    }
}
