using MyVedicDay.Muhurta.Calculation.Models;
using MyVedicDay.Panchang.Calculation.Models;

namespace MyVedicDay.Muhurta.Calculation;

/// <summary>Approved birth-Nakshatra policy: inclusive forward count in the 27-star cycle, repeated in nines.</summary>
public static class TaraBala
{
    public static int Position(Nakshatra birth, Nakshatra active)
    {
        if (!Enum.IsDefined(birth) || !Enum.IsDefined(active))
        {
            throw new ArgumentOutOfRangeException(nameof(birth), "Both Nakshatras must belong to the 27-star catalog.");
        }

        return (((int)active - (int)birth + 27) % 27) % 9 + 1;
    }

    public static MuhurtaStatus? MaximumStatus(int position) => position switch
    {
        1 => MuhurtaStatus.Mixed,
        3 or 5 or 7 => MuhurtaStatus.Caution,
        2 or 4 or 6 or 8 or 9 => null,
        _ => throw new ArgumentOutOfRangeException(nameof(position)),
    };
}
