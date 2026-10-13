using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
#if GEONAMES_COMPATIBILITY
namespace GeoNames;
#else
using GeoNames;

namespace MyVedicStar.API.Controllers;
#endif

/// <summary>Additive routes; existing /locations/resolve and calculation contracts are untouched.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("public-read")]
[Route("api/v1/locations")]
[Microsoft.AspNetCore.Http.Tags("Locations")]
[ProducesResponseType<ValidationProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(429)]
[ProducesResponseType<ProblemDetails>(503)]
public sealed class GeoNamesLocationsController(ILocationSearchProvider provider) : ControllerBase
{
#if !GEONAMES_COMPATIBILITY
    /// <summary>Lists countries for worldwide address editing through the Business facade.</summary>
    [HttpGet("countries"), ProducesResponseType<MyVedicStar.API.Contracts.AddressOptionsResponse>(200)]
    public async Task<IActionResult> Countries([FromServices] MyVedicStar.Authorize.Business.IAddressLookupFacade facade, CancellationToken token) =>
        Ok(new MyVedicStar.API.Contracts.AddressOptionsResponse((await facade.CountriesAsync(token)).Select(MyVedicStar.API.Contracts.AddressOptionResponse.From).ToArray()));

    /// <summary>Lists administrative regions belonging to the selected country.</summary>
    [HttpGet("regions"), ProducesResponseType<MyVedicStar.API.Contracts.AddressOptionsResponse>(200)]
    public async Task<IActionResult> Regions([FromQuery, Required, RegularExpression("^[A-Z]{2}$")] string country,
        [FromServices] MyVedicStar.Authorize.Business.IAddressLookupFacade facade, CancellationToken token) =>
        Ok(new MyVedicStar.API.Contracts.AddressOptionsResponse((await facade.RegionsAsync(country, token)).Select(MyVedicStar.API.Contracts.AddressOptionResponse.From).ToArray()));

    /// <summary>Suggests cities within the selected country and administrative region.</summary>
    [HttpGet("address-cities"), ProducesResponseType<MyVedicStar.API.Contracts.AddressPlacesResponse>(200)]
    public async Task<IActionResult> AddressCities([FromQuery, Required, StringLength(120, MinimumLength = 2)] string text,
        [FromQuery, Required, RegularExpression("^[A-Z]{2}$")] string country,
        [FromQuery, StringLength(20), RegularExpression("^[A-Za-z0-9-]+$")] string? region,
        [FromServices] MyVedicStar.Authorize.Business.IAddressLookupFacade facade, CancellationToken token) =>
        Ok(new MyVedicStar.API.Contracts.AddressPlacesResponse((await facade.CitiesAsync(text.Trim(), country, region, token)).Select(MyVedicStar.API.Contracts.AddressPlaceResponse.From).ToArray()));
#endif

    [HttpGet("search")]
    [ProducesResponseType<LocationSearchResponse>(200)]
    public Task<IActionResult> Search([FromQuery, Required, StringLength(120, MinimumLength = 2)] string text,
        [FromQuery, RegularExpression("^[A-Z]{2}$")] string? country, [FromQuery, RegularExpression("^[a-z]{2,3}(-[A-Za-z]{2,4})?$")] string? language,
        [FromQuery, Range(1, 8)] int maxRows = 8, CancellationToken cancellationToken = default) =>
        Execute(() => provider.SearchAsync(text.Trim().Normalize(), country, language, maxRows, cancellationToken));

    [HttpGet("postal")]
    [ProducesResponseType<LocationSearchResponse>(200)]
    public Task<IActionResult> Postal([FromQuery, Required, StringLength(10, MinimumLength = 2), RegularExpression("^[A-Za-z0-9 -]+$")] string text,
        [FromQuery, RegularExpression("^[A-Z]{2}$")] string? country, [FromQuery, Range(1, 8)] int maxRows = 8, CancellationToken cancellationToken = default) =>
        Execute(() => provider.PostalAsync(text.Trim().ToUpperInvariant(), country, maxRows, cancellationToken));

    [HttpGet("reverse")]
    [ProducesResponseType<LocationReverseResponse>(200)]
    public Task<IActionResult> Reverse([FromQuery, BindRequired, Range(-90, 90)] double latitude, [FromQuery, BindRequired, Range(-180, 180)] double longitude,
        [FromQuery, RegularExpression("^[a-z]{2,3}(-[A-Za-z]{2,4})?$")] string? language, CancellationToken cancellationToken) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) ? Execute(() => provider.ReverseAsync(latitude, longitude, language, cancellationToken)) : Task.FromResult<IActionResult>(BadRequest());

    [HttpGet("timezone")]
    [ProducesResponseType<LocationTimezoneResponse>(200)]
    public Task<IActionResult> Timezone([FromQuery, BindRequired, Range(-90, 90)] double latitude, [FromQuery, BindRequired, Range(-180, 180)] double longitude, CancellationToken cancellationToken) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) ? Execute(() => provider.TimezoneAsync(latitude, longitude, cancellationToken)) : Task.FromResult<IActionResult>(BadRequest());

    private async Task<IActionResult> Execute<T>(Func<Task<T>> operation)
    {
        try { return Ok(await operation()); }
        catch (GeoNamesUnavailableException error)
        {
            if (error.RetryAfterSeconds is { } seconds) Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            return Problem(statusCode: error.StatusCode, title: "Location lookup is temporarily unavailable.", detail: error.Detail);
        }
    }
}

