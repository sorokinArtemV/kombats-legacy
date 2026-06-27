using Kombats.LoadTests.Configuration;

namespace Kombats.LoadTests.VirtualPlayer;

internal sealed record VirtualPlayerOptions(
    UserCredentials User,
    TargetOptions Target,
    LoadOptions Load,
    IPlayerBehavior Behavior,
    int RandomSeed);
