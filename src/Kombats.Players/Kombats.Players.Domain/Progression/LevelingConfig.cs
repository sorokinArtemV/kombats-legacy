namespace Kombats.Players.Domain.Progression;

public sealed class LevelingConfig
{
    public long BaseFactor { get; }

    public LevelingConfig(long baseFactor)
    {
        if (baseFactor <= 0)
            throw new ArgumentException("BaseFactor must be positive.");

        BaseFactor = baseFactor;
    }
}

