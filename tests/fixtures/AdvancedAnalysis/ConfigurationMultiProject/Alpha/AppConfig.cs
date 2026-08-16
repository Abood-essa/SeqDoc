using Microsoft.Extensions.Configuration;

namespace Alpha;

/// <summary>
/// Alpha reads the shared boolean toggle with the exact Microsoft ConfigurationBinder shape. The
/// method name is deliberately distinct from Beta so per-project read attribution stays observable.
/// </summary>
public static class AppConfig
{
    public static bool ReadSharedToggleAlpha(IConfiguration configuration)
    {
        bool useSharedToggle = configuration.GetValue<bool>("FeatureToggles:UseSharedToggle");
        if (useSharedToggle)
        {
            return true;
        }

        return false;
    }
}
