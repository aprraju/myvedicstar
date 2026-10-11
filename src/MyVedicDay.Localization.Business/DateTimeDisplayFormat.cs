namespace MyVedicDay.Localization;

/// <summary>
/// The display format applied to every <c>…Display</c> value in a response: <c>{date} {time} {zone abbreviation}</c>.
/// </summary>
/// <param name="DatePattern">Date pattern for the location's country, e.g. <c>MM/dd/yyyy</c> (US) or <c>dd/MM/yyyy</c> (India).</param>
/// <param name="TimePattern">Time pattern, e.g. <c>HH:mm:ss</c>.</param>
/// <param name="CountryCode">Country that selected the date pattern, or <see langword="null"/> when unknown.</param>
public sealed record DateTimeDisplayFormat(string DatePattern, string TimePattern, string? CountryCode);
