using Kombats.Players.Domain.Progression;

namespace Kombats.Players.Application.Abstractions;

internal interface ILevelingConfigProvider
{
   public LevelingConfig Get();
   public int GetCurrentVersion();
}

