namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// Tests that touch the process-wide default UT-&gt;TT conversion, directly or through a seam that captures and restores it.
/// </summary>
/// <remarks>
/// api#75. The defect this prevents was found by running the whole suite, not the class: a seam disposed in one test class
/// restored a default that another class had since replaced, so an unrelated install looked like it had failed. xUnit runs
/// one collection at a time, so serialising these classes removes the interleaving without putting a lock in production
/// code. <see cref="DisableParallelization"/> is set because the hazard is cross-class, not cross-test.
/// </remarks>
[CollectionDefinition("astronomy-tt-offsets", DisableParallelization = true)]
public sealed class TtOffsetsCollection;
