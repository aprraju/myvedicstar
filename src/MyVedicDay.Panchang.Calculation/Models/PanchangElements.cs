namespace MyVedicDay.Panchang.Calculation.Models;

/// <summary>Lunar fortnight.</summary>
public enum Paksha
{
    /// <summary>Waxing fortnight.</summary>
    Shukla = 1,

    /// <summary>Waning fortnight.</summary>
    Krishna = 2,
}

/// <summary>
/// Lunar day: each Tithi spans 12° of Moon–Sun elongation. Values 1–15 are Shukla Paksha, 16–30 Krishna Paksha.
/// </summary>
public enum Tithi
{
    ShuklaPratipada = 1,
    ShuklaDwitiya,
    ShuklaTritiya,
    ShuklaChaturthi,
    ShuklaPanchami,
    ShuklaShashthi,
    ShuklaSaptami,
    ShuklaAshtami,
    ShuklaNavami,
    ShuklaDashami,
    ShuklaEkadashi,
    ShuklaDwadashi,
    ShuklaTrayodashi,
    ShuklaChaturdashi,
    Purnima,
    KrishnaPratipada,
    KrishnaDwitiya,
    KrishnaTritiya,
    KrishnaChaturthi,
    KrishnaPanchami,
    KrishnaShashthi,
    KrishnaSaptami,
    KrishnaAshtami,
    KrishnaNavami,
    KrishnaDashami,
    KrishnaEkadashi,
    KrishnaDwadashi,
    KrishnaTrayodashi,
    KrishnaChaturdashi,
    Amavasya,
}

/// <summary>Lunar mansion: each Nakshatra spans 13°20′ of sidereal lunar longitude.</summary>
public enum Nakshatra
{
    Ashwini = 1,
    Bharani,
    Krittika,
    Rohini,
    Mrigashira,
    Ardra,
    Punarvasu,
    Pushya,
    Ashlesha,
    Magha,
    PurvaPhalguni,
    UttaraPhalguni,
    Hasta,
    Chitra,
    Swati,
    Vishakha,
    Anuradha,
    Jyeshtha,
    Mula,
    PurvaAshadha,
    UttaraAshadha,
    Shravana,
    Dhanishta,
    Shatabhisha,
    PurvaBhadrapada,
    UttaraBhadrapada,
    Revati,
}

/// <summary>Nitya Yoga: each Yoga spans 13°20′ of the sum of sidereal solar and lunar longitudes.</summary>
public enum Yoga
{
    Vishkambha = 1,
    Priti,
    Ayushman,
    Saubhagya,
    Shobhana,
    Atiganda,
    Sukarma,
    Dhriti,
    Shula,
    Ganda,
    Vriddhi,
    Dhruva,
    Vyaghata,
    Harshana,
    Vajra,
    Siddhi,
    Vyatipata,
    Variyana,
    Parigha,
    Shiva,
    Siddha,
    Sadhya,
    Shubha,
    Shukla,
    Brahma,
    Indra,
    Vaidhriti,
}

/// <summary>Half-Tithi: 60 Karanas per lunar month drawn from 4 fixed and 7 repeating names.</summary>
public enum Karana
{
    Kimstughna = 1,
    Bava,
    Balava,
    Kaulava,
    Taitila,
    Gara,
    Vanija,
    Vishti,
    Shakuni,
    Chatushpada,
    Naga,
}
