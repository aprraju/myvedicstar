namespace MyVedicStar.Authorize.Integration;

/// <summary>Updates only explicitly changed name attributes on the verified Entra object.</summary>
public interface IGraphProfileClient
{
    Task UpdateNamesAsync(string issuer, string objectId, IReadOnlyDictionary<string, string?> changes, CancellationToken token);
}
