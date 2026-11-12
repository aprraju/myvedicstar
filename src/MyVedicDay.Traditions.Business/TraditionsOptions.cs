using System.ComponentModel.DataAnnotations;
namespace MyVedicDay.Traditions;

public sealed class TraditionsOptions
{
    public const string SectionName = "Traditions";

    /// <summary>Tradition used when a request does not specify one.</summary>
    [Required]
    public string DefaultTradition { get; set; } = "all";
}
