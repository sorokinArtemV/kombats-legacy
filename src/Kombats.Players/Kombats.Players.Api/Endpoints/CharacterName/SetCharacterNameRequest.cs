namespace Kombats.Players.Api.Endpoints.CharacterName;

/// <summary>
/// Request body for setting the character display name (once, when in Draft).
/// </summary>
public record SetCharacterNameRequest(string Name);
