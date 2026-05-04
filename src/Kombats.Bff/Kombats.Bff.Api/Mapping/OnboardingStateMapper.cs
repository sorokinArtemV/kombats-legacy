namespace Kombats.Bff.Api.Mapping;

internal static class OnboardingStateMapper
{
    public static string ToDisplayString(int state) => state switch
    {
        0 => "Draft",
        1 => "Named",
        2 => "Ready",
        _ => "Unknown"
    };
}
