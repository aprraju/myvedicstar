using System.Text;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// The AST-09 case-count and coverage report, rendered from an actual evaluation run (issue #74, AC-1).
/// </summary>
/// <remarks>
/// <para><b>Derived, never maintained by hand.</b> AC-1 asks for case counts per category; a table typed into a markdown
/// file goes stale the moment a spec number changes and nobody notices. This renders from the rows the suite actually
/// produced, so the report and the run cannot disagree.</para>
/// <para><b>It leads with what was measured, not with how many cases exist.</b> "10,000 cases" is a corpus size, not an
/// accuracy result. The first line that matters is how many were compared against an independent reference, because a
/// reader who sees 10,000 and stops has been told nothing true about accuracy.</para>
/// </remarks>
public static class CorpusEvaluationReport
{
    public static string Render(IReadOnlyList<CorpusEvaluation> evaluation)
    {
        var builder = new StringBuilder();

        var compared = evaluation.Count(entry => entry.WasCompared);
        builder.AppendLine("AST-09 accuracy corpus report (issue #74)");
        builder.AppendLine("==========================================");
        builder.AppendLine($"Total cases: {evaluation.Count}");
        builder.AppendLine($"Compared against an independent reference: {compared}");
        builder.AppendLine($"Not measured: {evaluation.Count - compared}");
        builder.AppendLine();

        builder.AppendLine("By category:");
        foreach (var group in evaluation.GroupBy(entry => entry.Case.Category).OrderBy(group => group.Key.ToString()))
        {
            var scored = group.Count(entry => entry.WasCompared);
            builder.AppendLine($"  {group.Key}: {group.Count()} cases, {scored} compared");
        }

        builder.AppendLine();
        builder.AppendLine("By outcome:");
        foreach (var group in evaluation.GroupBy(entry => entry.Status).OrderBy(group => group.Key.ToString()))
            builder.AppendLine($"  {group.Key}: {group.Count()}");

        builder.AppendLine();
        if (compared == 0)
            builder.AppendLine("No case was compared: the committed reference set does not cover the corpus's instants, " +
                               "so this run makes no accuracy claim. The corpus is not failing - it is not measured.");
        else
            builder.AppendLine($"{compared} case(s) were measured against committed Horizons rows; see each case's detail " +
                               "for its residual and budget.");

        return builder.ToString();
    }
}
