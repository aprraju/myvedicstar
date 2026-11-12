namespace MyVedicDay.Api.Contracts.Traditions;

/// <summary>A regional tradition a user can choose.</summary>
/// <param name="Key">Identifier to pass as <c>tradition</c>, e.g. <c>telugu</c>.</param>
/// <param name="Name">Localized name, e.g. "Telugu".</param>
/// <param name="Description">Localized region description, e.g. "Andhra Pradesh and Telangana."</param>
public sealed record TraditionSummaryResponse(string Key, string Name, string? Description);

/// <summary>Available traditions. Each domain applies its own rules for a tradition (see e.g. <c>/api/v1/panchang/traditions</c>).</summary>
/// <param name="Language">Language of names and descriptions.</param>
/// <param name="Traditions">Traditions in display order.</param>
public sealed record TraditionsResponse(string Language, IReadOnlyList<TraditionSummaryResponse> Traditions);
