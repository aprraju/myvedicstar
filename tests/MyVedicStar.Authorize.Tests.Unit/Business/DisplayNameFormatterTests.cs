using MyVedicStar.Authorize.Business.Users;

namespace MyVedicStar.Authorize.Tests.Unit.Business;

public sealed class DisplayNameFormatterTests
{
    [Theory]
    [InlineData("John", "Smith", "John Smith")]
    [InlineData("  John ", " Smith  ", "John Smith")]
    [InlineData("John", null, "John")]
    [InlineData("John", "", "John")]
    [InlineData("John", "   ", "John")]
    [InlineData(null, "Smith", "Smith")]
    [InlineData("", "Smith", "Smith")]
    [InlineData(null, null, null)]
    [InlineData("", "  ", null)]
    public void Format_HandlesMissingOrBlankParts(string? givenName, string? surname, string? expected)
    {
        Assert.Equal(expected, DisplayNameFormatter.Format(givenName, surname));
    }
}
