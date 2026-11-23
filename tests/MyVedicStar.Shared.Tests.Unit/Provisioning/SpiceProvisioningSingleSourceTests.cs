namespace MyVedicStar.Shared.Tests.Unit.Provisioning;

using System.Text.RegularExpressions;

/// <summary>
/// api#173. The deploy image and the CI runner must provision the SAME pinned toolkit, and neither may keep its own copy
/// of the pinning rules.
/// </summary>
/// <remarks>
/// <para><b>The property.</b> `deploy/docker/Dockerfile` and `.github/workflows/spice-provisioning.yml` both provision
/// CSPICE. The statement "they both call `deploy/scripts/provision-spice.sh`" is true today and is exactly the kind of
/// claim that stops being true silently: someone debugging a CI-only failure pastes a `curl` and a version into the
/// workflow, CI goes green, and the deploy image is still on a different toolkit. The two paths then disagree about
/// which toolkit was ever proven, and nothing says so.</para>
/// <para><b>What is asserted, and why it is a scan rather than a runtime check.</b> None of these files can be executed
/// by a unit test - one is a Dockerfile, one is a GitHub Actions workflow. What CAN be asserted is the structural
/// property that makes drift impossible: the consuming files contain no version, URL or checksum of their own, and they
/// delegate to the one script that holds them. That is a real property with a real failure mode, and it fails when the
/// thing it forbids is added - which is the only test of a rule worth having.</para>
/// <para><b>What is NOT proven here.</b> That the script itself succeeds, that the fetched artefacts match the recorded
/// hashes, or that a provisioned deploy image loads the library. Those need network access and an image build; the first
/// is evidenced by CI run 45078530912 for the CI path, and the deploy-image path is blocked on api#193. This test covers
/// agreement between the two paths, not the correctness of the provisioning itself.</para>
/// </remarks>
public sealed class SpiceProvisioningSingleSourceTests
{
    private const string ScriptPath = "deploy/scripts/provision-spice.sh";
    private const string DockerfilePath = "deploy/docker/Dockerfile";
    private const string WorkflowPath = ".github/workflows/spice-provisioning.yml";

    /// <summary>The one place the toolkit version, URLs and hashes are allowed to live.</summary>
    private static readonly Lazy<string> RepositoryRoot = new(() => FindRepositoryRoot());

    /// <summary>
    /// Things that only the script may say. Each is a way of restating the pinning rules in a consumer.
    /// </summary>
    private static readonly (string Description, Regex Pattern)[] ScriptOnlyContent =
    [
        // A toolkit version literal. "N0067" appearing in the Dockerfile means someone pinned it there too.
        ("a toolkit version literal", new Regex(@"\bN00\d{2}\b", RegexOptions.Compiled)),

        // A URL to the NAIF server. Any address of the artefact source in a consumer is a second source of artefacts.
        // Matched as a URL rather than as the bare hostname, because "naif0012.tls" is a filename on both paths and
        // flagging it would force a valid COPY line to be renamed.
        ("a NAIF artefact URL", new Regex(@"https?://naif\.jpl\.nasa\.gov", RegexOptions.Compiled)),

        // A SHA-256 literal, i.e. a checksum maintained outside the script.
        ("a SHA-256 checksum literal", new Regex(@"\b[0-9a-fA-F]{64}\b", RegexOptions.Compiled)),

        // A direct download INVOCATION, not merely the word. "apt-get install curl" installs a tool; "curl -o ..." is
        // the specific paste that would make CI green on an unpinned toolkit. Matching the bare word would force the
        // build stage to stop installing the client it needs to run the script at all.
        ("a direct download invocation",
            new Regex(@"\b(curl|wget)\b\s+(-|--|https?://)", RegexOptions.Compiled))
    ];

    [Fact]
    public void Both_provisioning_paths_delegate_to_the_same_pinned_script()
    {
        var root = RepositoryRoot.Value;
        var dockerfile = File.ReadAllText(Path.Combine(root, DockerfilePath));
        var workflow = File.ReadAllText(Path.Combine(root, WorkflowPath));

        // The positive half: each path must actually reach the script. A test that only forbade duplication would pass
        // if someone deleted the provisioning step entirely, which is the opposite failure.
        Assert.Contains("provision-spice.sh", dockerfile, StringComparison.Ordinal);
        Assert.Contains(ScriptPath, workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_deploy_image_does_not_restate_the_pinning_rules()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot.Value, DockerfilePath));

        AssertContainsNoPinningRules(DockerfilePath, content);
    }

    [Fact]
    public void The_ci_workflow_does_not_restate_the_pinning_rules()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot.Value, WorkflowPath));

        AssertContainsNoPinningRules(WorkflowPath, content);
    }

    [Fact]
    public void The_script_is_where_the_version_and_the_checksums_actually_are()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot.Value, ScriptPath));

        // The negative half of the property: if the consumers are forbidden from holding the pin, the script must hold
        // it. Without this, deleting the script's own constants would satisfy every other assertion here while leaving
        // nothing pinned at all.
        Assert.Matches(new Regex("^readonly SPICE_VERSION=\"N00\\d{2}\"$", RegexOptions.Multiline), script);
        Assert.Matches(new Regex("^TOOLKIT_SHA256=\"[0-9a-fA-F]{64}\"$", RegexOptions.Multiline), script);
        Assert.Contains("KERNEL_SHA256=(", script, StringComparison.Ordinal);

        // And it must refuse to bake an unverified artefact rather than proceeding without a hash. The UNVERIFIED branch
        // is the mechanism; asserting the string keeps it from being deleted as dead code.
        Assert.Contains("UNVERIFIED", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_paths_place_the_artefacts_at_the_same_locations()
    {
        var root = RepositoryRoot.Value;
        var dockerfile = File.ReadAllText(Path.Combine(root, DockerfilePath));
        var workflow = File.ReadAllText(Path.Combine(root, WorkflowPath));

        // Agreement about the version is worthless if the two paths put the library and kernels where different code
        // looks. `SpiceConfiguration.Default` is the single authority for where the engine looks, so both paths must
        // name these paths - this is the one place a literal is required in both, because the destination is not part
        // of the pinning rules.
        string[] destinations = ["libcspice.so", "de440.bsp", "naif0012.tls", "pck00011.tpc"];

        foreach (var artefact in destinations)
        {
            Assert.Contains(artefact, dockerfile, StringComparison.Ordinal);
            Assert.Contains(artefact, workflow, StringComparison.Ordinal);
        }

        // The kernels land under /var/spice on both paths, matching SpiceConfiguration.Default.
        Assert.Contains("/var/spice", dockerfile, StringComparison.Ordinal);
        Assert.Contains("/var/spice", workflow, StringComparison.Ordinal);
    }

    /// <summary>
    /// Fails if a consumer holds a pinning rule OUTSIDE a comment.
    /// </summary>
    /// <remarks>
    /// Comments are excluded deliberately, and this is the distinction the test turns on. Both consumers legitimately
    /// SAY "pinned (N0067)" in prose explaining why the script is their only source - a scanner that flagged that would
    /// force the comments to be deleted to make the suite green, which removes the explanation that stops the next
    /// person pasting a real pin. What must not exist is an executable line that downloads, pins or checksums
    /// anything: that is the copy that drifts. Scanning comments as well would be testing documentation grammar rather
    /// than the property.
    /// </remarks>
    private static void AssertContainsNoPinningRules(string file, string content)
    {
        // Strip whole-line comments for both formats the two consumers use (Dockerfile '#', YAML '#'). Inline trailing
        // comments are left in place: an executable line that also mentions a pin is still an executable line.
        var executable = string.Join("\n", content
            .Split("\n")
            .Where(line => !line.TrimStart().StartsWith("#", StringComparison.Ordinal)));

        foreach (var (description, pattern) in ScriptOnlyContent)
        {
            var match = pattern.Match(executable);
            Assert.False(match.Success,
                $"{file} contains {description} ('{match.Value}') outside a comment. The toolkit version, URLs and " +
                $"checksums live ONLY in {ScriptPath}, so the deploy image and CI cannot disagree about which toolkit " +
                "they provision. If this file needs the value, it needs the script to return it - not a copy that " +
                "will drift.");
        }
    }

    /// <summary>Walks up from the test binary to the repository root, found by the solution file.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MyVedicStar.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
