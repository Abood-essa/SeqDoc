using Microsoft.Extensions.Configuration;

namespace AdvancedAnalysis.ConfigurationReads
{
    /// <summary>
    /// accepted contract fixture shapes for the analyzed-application configuration facts. The supported positives
    /// are exact Microsoft <c>ConfigurationBinder.GetValue&lt;bool&gt;</c> reads with compile-time
    /// constant non-sensitive keys, assigned once to a bool local used directly by an <c>if</c> boolean
    /// condition. Every other shape (same-simple-name lookalike, dynamic key, non-boolean generic,
    /// section call, custom receiver, reassigned local, and a local that flows through a helper call)
    /// must fail closed and never project a configuration fact beyond the exact read itself.
    /// </summary>
    public static class ConfigurationReadShapes
    {
        // Supported positive: exact GetValue<bool>(constant key) assigned once to a bool local used
        // directly by an if boolean condition. The lookalike helpers live in the nested Lookalikes
        // namespace so extension resolution binds these calls to the exact Microsoft ConfigurationBinder
        // (the closest enclosing namespace of an extension method wins, and no Lookalikes using is
        // imported here).
        public static bool UseSqlDatabase(IConfiguration configuration)
        {
            bool useSqlDatabase = configuration.GetValue<bool>("FeatureToggles:UseSqlDatabase");
            if (useSqlDatabase)
            {
                return true;
            }

            return false;
        }

        // Supported positive with an explicit compiler-proven boolean default.
        public static bool UseCacheWithDefault(IConfiguration configuration)
        {
            bool useCache = configuration.GetValue<bool>("FeatureToggles:UseCache", defaultValue: true);
            if (useCache)
            {
                return true;
            }

            return false;
        }

        // Supported positive: ConfigurationManager receiver (builder.Configuration is a
        // ConfigurationManager, which is assignable to the exact IConfiguration).
        public static bool UseAudit(ConfigurationManager configuration)
        {
            bool useAudit = configuration.GetValue<bool>("FeatureToggles:UseAudit");
            if (useAudit)
            {
                return true;
            }

            return false;
        }

        // Negative: a same-simple-name GetValue helper is not the exact Microsoft ConfigurationBinder
        // and must never project a configuration read fact. The static call keeps the negative
        // explicit; the collector must fail closed on the Lookalikes.FakeBinder containing type.
        public static bool LookalikeHelper(IConfiguration configuration)
        {
            bool lookalike = Lookalikes.FakeBinder.GetValue<bool>(configuration, "FeatureToggles:UseSqlDatabase");
            if (lookalike)
            {
                return true;
            }

            return false;
        }

        // Negative: a dynamic (non-compile-time-constant) key cannot project a canonical key.
        public static bool DynamicKey(IConfiguration configuration, string key)
        {
            bool dynamicRead = configuration.GetValue<bool>(key);
            if (dynamicRead)
            {
                return true;
            }

            return false;
        }

        // Negative: unsupported non-boolean generic value types never project.
        public static string? UnsupportedString(IConfiguration configuration)
            => configuration.GetValue<string>("FeatureToggles:UseSqlDatabase");

        public static int UnsupportedInt(IConfiguration configuration)
            => configuration.GetValue<int>("FeatureToggles:UseSqlDatabase");

        // Negative: GetSection is not an admitted boolean read.
        public static IConfigurationSection UnsupportedSection(IConfiguration configuration)
            => configuration.GetSection("FeatureToggles");

        // Negative: a same-shape GetValue call on a receiver that is not assignable to the exact
        // IConfiguration never projects.
        public static bool UnsupportedCustomReceiver(string source)
            => Lookalikes.CustomReader.GetValue<bool>(source, "FeatureToggles:UseSqlDatabase");

        // Negative: the read's local is reassigned before the if, so the direct local-to-condition
        // association fails closed (no condition fact) even though the read itself is exact.
        public static bool ReassignedLocal(IConfiguration configuration)
        {
            bool toggle = configuration.GetValue<bool>("FeatureToggles:UseSqlDatabase");
            toggle = ComputeFallback();
            if (toggle)
            {
                return true;
            }

            return false;
        }

        // Negative: the read's local flows through a helper call instead of directly into an if
        // boolean condition, so no condition fact is associated.
        public static bool AmbiguousLocal(IConfiguration configuration)
        {
            bool toggle = configuration.GetValue<bool>("FeatureToggles:UseSqlDatabase");
            if (Decide(toggle))
            {
                return true;
            }

            return false;
        }

        // Supported positive: named instance-syntax arguments appear reordered in source; the read
        // resolves by the compiler-bound parameter ordinal, never by source position.
        public static bool UseNamedReordered(IConfiguration configuration)
        {
            bool useNamed = configuration.GetValue<bool>(defaultValue: true, key: "FeatureToggles:UseNamedReordered");
            if (useNamed)
            {
                return true;
            }

            return false;
        }

        // Supported positive: named static-syntax arguments appear reordered in source; the read
        // resolves by the compiler-bound parameter ordinal, never by source position.
        public static bool UseStaticNamedReordered(IConfiguration configuration)
        {
            bool useStaticNamed = ConfigurationBinder.GetValue<bool>(
                configuration,
                defaultValue: false,
                key: "FeatureToggles:UseStaticNamed");
            if (useStaticNamed)
            {
                return true;
            }

            return false;
        }

        // Negative: a while condition is not an actual if and never associates a condition fact.
        public static bool WhileLocal(IConfiguration configuration)
        {
            bool toggle = configuration.GetValue<bool>("FeatureToggles:UseSqlDatabase");
            while (toggle)
            {
                break;
            }

            return false;
        }

        // Negative: a by-reference escape before the if fails the direct single-write local shape.
        public static bool RefEscapeLocal(IConfiguration configuration)
        {
            bool toggle = configuration.GetValue<bool>("FeatureToggles:UseSqlDatabase");
            Mutate(ref toggle);
            if (toggle)
            {
                return true;
            }

            return false;
        }

        // Negative: an out escape before the if fails the direct single-write local shape.
        public static bool OutEscapeLocal(IConfiguration configuration)
        {
            bool toggle = configuration.GetValue<bool>("FeatureToggles:UseSqlDatabase");
            Replace(out toggle);
            if (toggle)
            {
                return true;
            }

            return false;
        }

        // Negative: sensitive keys spelled with hierarchical separators must never admit a read.
        public static bool SensitiveApiColonKey(IConfiguration configuration)
            => configuration.GetValue<bool>("Api:Key");

        public static bool SensitivePrivateDotKey(IConfiguration configuration)
            => configuration.GetValue<bool>("Private.Key");

        public static bool SensitiveAccessSlashKey(IConfiguration configuration)
            => configuration.GetValue<bool>("Access/Key");

        public static bool SensitivePasswordColonKey(IConfiguration configuration)
            => configuration.GetValue<bool>("Pass:Word");

        private static bool ComputeFallback() => true;

        private static bool Decide(bool value) => value;

        private static void Mutate(ref bool value) => value = false;

        private static void Replace(out bool value) => value = false;
    }

    namespace Lookalikes
    {
        /// <summary>
        /// Same simple method name and generic shape as <c>Microsoft.Extensions.Configuration
        /// .ConfigurationBinder.GetValue</c>, but a different containing type. The accepted contract collector must
        /// fail closed on this lookalike. The helper lives in a nested namespace so it never shadows
        /// the exact Microsoft extension method for the supported positive reads in
        /// ConfigurationReadShapes; callers invoke it as an explicit static call, never through
        /// extension resolution.
        /// </summary>
        public static class FakeBinder
        {
            public static T GetValue<T>(this IConfiguration configuration, string key) => default!;
        }

        /// <summary>
        /// A same-shape GetValue call whose receiver is a string, never assignable to the exact
        /// IConfiguration.
        /// </summary>
        public static class CustomReader
        {
            public static T GetValue<T>(string source, string key) => default!;
        }
    }
}
