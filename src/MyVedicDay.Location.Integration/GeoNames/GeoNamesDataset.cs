using System.Globalization;
using System.IO.Compression;

namespace MyVedicDay.Location.Integration.GeoNames;

/// <summary>
/// In-memory index over the embedded GeoNames extract (cities with population ≥ 5,000, countries, states).
/// Built by <c>tools/build-geonames-places.cs</c>. Data © GeoNames, CC BY 4.0.
/// </summary>
internal sealed class GeoNamesDataset
{
    public const string ResourceName = "MyVedicDay.GeoNames.Places";

    // Common country names and abbreviations people type that are not in the GeoNames country list.
    private static readonly Dictionary<string, string> CountryAliases = new(StringComparer.Ordinal)
    {
        ["usa"] = "US",
        ["us"] = "US",
        ["u s a"] = "US",
        ["united states of america"] = "US",
        ["america"] = "US",
        ["uk"] = "GB",
        ["great britain"] = "GB",
        ["britain"] = "GB",
        ["england"] = "GB",
        ["scotland"] = "GB",
        ["wales"] = "GB",
        ["northern ireland"] = "GB",
        ["uae"] = "AE",
    };

    private readonly Dictionary<string, string> _countryCodes;
    private readonly Dictionary<string, string> _countryNames;
    private readonly Dictionary<string, (string Name, string Normalized, string NormalizedAscii)> _states;
    private readonly GeoNamesPlace[] _places;
    private readonly Dictionary<string, List<(int Index, bool IsPrimaryName)>> _nameIndex;

    // Spatial grid: 1° × 1° cells, keyed by (floor(lat), floor(lon)).
    // Reduces FindNearest from O(70 000) linear scan to O(candidate cells × places per cell).
    private readonly Dictionary<(int Lat, int Lon), List<int>> _spatialIndex;

    private GeoNamesDataset(
        Dictionary<string, string> countryCodes,
        Dictionary<string, string> countryNames,
        Dictionary<string, (string Name, string Normalized, string NormalizedAscii)> states,
        GeoNamesPlace[] places,
        Dictionary<string, List<(int Index, bool IsPrimaryName)>> nameIndex,
        Dictionary<(int Lat, int Lon), List<int>> spatialIndex)
    {
        _countryCodes = countryCodes;
        _countryNames = countryNames;
        _states = states;
        _places = places;
        _nameIndex = nameIndex;
        _spatialIndex = spatialIndex;
    }

    public static GeoNamesDataset Load()
    {
        using var resource = typeof(GeoNamesDataset).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        var countryCodes = new Dictionary<string, string>(StringComparer.Ordinal);
        var countryNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var states = new Dictionary<string, (string, string, string)>(StringComparer.Ordinal);
        var places = new List<GeoNamesPlace>(70_000);
        var nameIndex = new Dictionary<string, List<(int, bool)>>(StringComparer.Ordinal);
        var spatialIndex = new Dictionary<(int Lat, int Lon), List<int>>();

        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split('\t');
            switch (fields[0])
            {
                case "C":
                    countryNames[fields[1]] = fields[3];
                    countryCodes[PlaceNameNormalizer.Normalize(fields[1])] = fields[1];
                    countryCodes[PlaceNameNormalizer.Normalize(fields[2])] = fields[1];
                    countryCodes.TryAdd(PlaceNameNormalizer.Normalize(fields[3]), fields[1]);
                    break;

                case "A":
                    states[fields[1]] = (fields[2], PlaceNameNormalizer.Normalize(fields[2]), PlaceNameNormalizer.Normalize(fields[3]));
                    break;

                case "P":
                    var index = places.Count;
                    var lat = double.Parse(fields[6], CultureInfo.InvariantCulture);
                    var lon = double.Parse(fields[7], CultureInfo.InvariantCulture);
                    places.Add(new GeoNamesPlace(
                        fields[1],
                        fields[4],
                        fields[5],
                        lat,
                        lon,
                        string.IsNullOrEmpty(fields[9]) ? null : fields[9]));

                    AddToIndex(nameIndex, fields[1], index, isPrimaryName: true);
                    AddToIndex(nameIndex, fields[2], index, isPrimaryName: true);
                    foreach (var alternate in fields[3].Split('|', StringSplitOptions.RemoveEmptyEntries))
                    {
                        AddToIndex(nameIndex, alternate, index, isPrimaryName: false);
                    }

                    AddToSpatialIndex(spatialIndex, lat, lon, index);
                    break;
            }
        }

        foreach (var (alias, code) in CountryAliases)
        {
            countryCodes.TryAdd(alias, code);
        }

        return new GeoNamesDataset(countryCodes, countryNames, states, [.. places], nameIndex, spatialIndex);
    }

    public IReadOnlyList<GeocodedPlace> Search(PlaceSearch search, int maxResults)
    {
        if (!_nameIndex.TryGetValue(PlaceNameNormalizer.Normalize(search.City), out var candidates))
        {
            return [];
        }

        string? countryCode = null;
        if (!string.IsNullOrWhiteSpace(search.Country)
            && !_countryCodes.TryGetValue(PlaceNameNormalizer.Normalize(search.Country), out countryCode))
        {
            return [];
        }

        var state = string.IsNullOrWhiteSpace(search.State) ? null : PlaceNameNormalizer.Normalize(search.State);

        // Places are stored most-populous first, so index order is population order.
        return candidates
            .Where(candidate => countryCode is null || _places[candidate.Index].CountryCode == countryCode)
            .Where(candidate => state is null || StateMatches(_places[candidate.Index], state))
            .OrderByDescending(candidate => candidate.IsPrimaryName)
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => candidate.Index)
            .Distinct()
            .Take(maxResults)
            .Select(ToGeocodedPlace)
            .ToArray();
    }

    /// <summary>
    /// Finds the nearest city within <paramref name="maxDistanceKm"/> km using the spatial grid index instead of a
    /// linear scan. Cells are 1° × 1°; the search radius in cells is ceil(maxDistanceKm / 111) + 1, which covers any
    /// point within <paramref name="maxDistanceKm"/> km regardless of latitude.
    /// </summary>
    public GeocodedPlace? FindNearest(double latitude, double longitude, double maxDistanceKm)
    {
        var centerCell = ToCell(latitude, longitude);
        var cellRadius  = (int)Math.Ceiling(maxDistanceKm / 111.0) + 1;

        var bestIndex    = -1;
        var bestDistance = double.MaxValue;

        for (var dLat = -cellRadius; dLat <= cellRadius; dLat++)
        {
            for (var dLon = -cellRadius; dLon <= cellRadius; dLon++)
            {
                if (!_spatialIndex.TryGetValue((centerCell.Lat + dLat, centerCell.Lon + dLon), out var candidates))
                {
                    continue;
                }

                foreach (var i in candidates)
                {
                    var distance = DistanceKm(latitude, longitude, _places[i].Latitude, _places[i].Longitude);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestIndex    = i;
                    }
                }
            }
        }

        return bestIndex >= 0 && bestDistance <= maxDistanceKm ? ToGeocodedPlace(bestIndex) : null;
    }

    private static (int Lat, int Lon) ToCell(double lat, double lon) =>
        ((int)Math.Floor(lat), (int)Math.Floor(lon));

    private static void AddToSpatialIndex(Dictionary<(int Lat, int Lon), List<int>> index, double lat, double lon, int placeIndex)
    {
        var cell = ToCell(lat, lon);
        if (!index.TryGetValue(cell, out var list))
        {
            list = [];
            index[cell] = list;
        }

        list.Add(placeIndex);
    }

    private static void AddToIndex(Dictionary<string, List<(int, bool)>> index, string name, int placeIndex, bool isPrimaryName)
    {
        var key = PlaceNameNormalizer.Normalize(name);
        if (key.Length == 0)
        {
            return;
        }

        if (!index.TryGetValue(key, out var entries))
        {
            entries = [];
            index[key] = entries;
        }

        entries.Add((placeIndex, isPrimaryName));
    }

    private static double DistanceKm(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double EarthRadiusKm = 6371.0088;
        const double ToRadians = Math.PI / 180d;

        var deltaLatitude = (latitude2 - latitude1) * ToRadians;
        var deltaLongitude = (longitude2 - longitude1) * ToRadians;
        var a = (Math.Sin(deltaLatitude / 2) * Math.Sin(deltaLatitude / 2))
            + (Math.Cos(latitude1 * ToRadians) * Math.Cos(latitude2 * ToRadians) * Math.Sin(deltaLongitude / 2) * Math.Sin(deltaLongitude / 2));
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private bool StateMatches(GeoNamesPlace place, string normalizedState)
    {
        if (place.Admin1Code.Equals(normalizedState, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return _states.TryGetValue($"{place.CountryCode}.{place.Admin1Code}", out var state)
            && (state.Normalized == normalizedState || state.NormalizedAscii == normalizedState);
    }

    private GeocodedPlace ToGeocodedPlace(int index)
    {
        var place = _places[index];
        var state = _states.TryGetValue($"{place.CountryCode}.{place.Admin1Code}", out var entry) ? entry.Name : null;
        var country = _countryNames.GetValueOrDefault(place.CountryCode, place.CountryCode);
        return new GeocodedPlace(place.Name, state, country, place.CountryCode, place.Latitude, place.Longitude, place.TimeZoneId);
    }

    private sealed record GeoNamesPlace(
        string Name, string CountryCode, string Admin1Code, double Latitude, double Longitude, string? TimeZoneId);
}
