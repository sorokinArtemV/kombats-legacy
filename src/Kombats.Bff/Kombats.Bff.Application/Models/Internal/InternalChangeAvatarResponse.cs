namespace Kombats.Bff.Application.Models.Internal;

public sealed record InternalChangeAvatarResponse(
    // Nullable to tolerate pre-feature Players payloads during rollout;
    // the BFF endpoint coalesces to the default avatar id.
    string? AvatarId,
    int Revision);
