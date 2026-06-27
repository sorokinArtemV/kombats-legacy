using Kombats.Players.Application.Abstractions;
using Kombats.Players.Domain.Progression;
using Microsoft.Extensions.Options;

namespace Kombats.Players.Infrastructure.Configuration;

internal sealed class LevelingConfigProvider : ILevelingConfigProvider
{
    private readonly IOptions<LevelingOptions> _options;

    public LevelingConfigProvider(IOptions<LevelingOptions> options)
    {
        _options = options;
    }

    public LevelingConfig Get() => new(_options.Value.BaseFactor);

    public int GetCurrentVersion() => _options.Value.CurrentVersion;
}

