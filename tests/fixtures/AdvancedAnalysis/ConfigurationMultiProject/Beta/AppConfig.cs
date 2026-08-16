using Microsoft.Extensions.Configuration;

namespace Beta;

/// <summary>
/// Beta reads the shared boolean toggle with the exact Microsoft ConfigurationBinder shape. The
/// method name is deliberately distinct from Alpha so per-project read attribution stays observable.
/// </summary>
public static class AppConfig
{
    public static bool ReadSharedToggleBeta(IConfiguration configuration)
    {
        bool useSharedToggle = configuration.GetValue<bool>("FeatureToggles:UseSharedToggle");
        if (useSharedToggle)
        {
            return true;
        }

        return false;
    }
}
