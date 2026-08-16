using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace AdvancedAnalysis.ConditionalDependencyInjection.Services;

public interface IStorageService
{
    Task<string> GetItemAsync(int id);
}

public sealed class MemoryStorageService : IStorageService
{
    public Task<string> GetItemAsync(int id) => Task.FromResult($"memory:{id}");
}

public sealed class FileStorageService : IStorageService
{
    public Task<string> GetItemAsync(int id) => Task.FromResult($"file:{id}");
}

public interface IAuditService
{
    void Record(string entry);
}

public sealed class ConsoleAuditService : IAuditService
{
    public void Record(string entry)
    {
    }
}

public sealed class FileAuditService : IAuditService
{
    public void Record(string entry)
    {
    }
}

public interface ICacheService
{
    object? Read(string key);
}

public sealed class MemoryCacheService : ICacheService
{
    public object? Read(string key) => null;
}

public interface ISmsService
{
    void Send(string message);
}

public sealed class TwilioSmsService : ISmsService
{
    public void Send(string message)
    {
    }
}

public sealed class VonageSmsService : ISmsService
{
    public void Send(string message)
    {
    }
}

public interface IBackupService
{
    void Backup();
}

public sealed class LocalBackupService : IBackupService
{
    public void Backup()
    {
    }
}

public sealed class CloudBackupService : IBackupService
{
    public void Backup()
    {
    }
}

public interface INotificationService
{
    void Notify(string message);
}

public sealed class SmsNotificationService : INotificationService
{
    public void Notify(string message)
    {
    }
}

public sealed class PriorityNotificationService : INotificationService
{
    public void Notify(string message)
    {
    }
}

public sealed class BatchNotificationService : INotificationService
{
    public void Notify(string message)
    {
    }
}

public interface IKeyedService
{
    string Describe();
}

public sealed class AlphaKeyedService : IKeyedService
{
    public string Describe() => "alpha";
}

public sealed class BetaKeyedService : IKeyedService
{
    public string Describe() => "beta";
}

public interface ITryService
{
    string Describe();
}

public sealed class TryServiceImplementation : ITryService
{
    public string Describe() => "try";
}

public interface IWidgetService
{
    string Describe();
}

public sealed class WidgetService : IWidgetService
{
    public string Describe() => "widget";
}

public sealed class OtherWidgetService : IWidgetService
{
    public string Describe() => "other";
}

public interface IPolicyService
{
    bool Allows(string operation);
}

public sealed class StrictPolicyService : IPolicyService
{
    public bool Allows(string operation) => false;
}

public sealed class RelaxedPolicyService : IPolicyService
{
    public bool Allows(string operation) => true;
}

public interface ISinkService
{
    void Write(string entry);
}

public sealed class JsonSinkService : ISinkService
{
    public void Write(string entry)
    {
    }
}

public sealed class ConsoleSinkService : ISinkService
{
    public void Write(string entry)
    {
    }
}

public interface IFallbackService
{
    string Describe();
}

public sealed class SecondaryAwareService : IFallbackService
{
    public string Describe() => "secondary-aware";
}

public sealed class PrimaryBlindService : IFallbackService
{
    public string Describe() => "primary-blind";
}

public interface ILoopService
{
    string Describe();
}

public sealed class LoopScopedService : ILoopService
{
    public string Describe() => "loop";
}

public sealed class NonLoopService : ILoopService
{
    public string Describe() => "non-loop";
}

public interface IReassignedService
{
    string Describe();
}

public sealed class ReassignedTrueService : IReassignedService
{
    public string Describe() => "reassigned-true";
}

public sealed class ReassignedFalseService : IReassignedService
{
    public string Describe() => "reassigned-false";
}

public interface IRefEscapedService
{
    string Describe();
}

public sealed class RefEscapedTrueService : IRefEscapedService
{
    public string Describe() => "ref-true";
}

public sealed class RefEscapedFalseService : IRefEscapedService
{
    public string Describe() => "ref-false";
}

public interface IOutEscapedService
{
    string Describe();
}

public sealed class OutEscapedTrueService : IOutEscapedService
{
    public string Describe() => "out-true";
}

public sealed class OutEscapedFalseService : IOutEscapedService
{
    public string Describe() => "out-false";
}

/// <summary>
/// accepted contract negative helper shapes. <see cref="ComputeChoice"/> is a non-admitted condition source used
/// by the unresolved-condition partition, <see cref="Mutate"/> and <see cref="Replace"/> escape a
/// read local by ref/out before its condition (regression), and
/// <see cref="RegisterAlternativeSinks"/> proves that an extracted method containing an admitted
/// read and an exact if/else never produces top-level companion arm facts: Method Flow remains the
/// sole control authority inside extracted methods.
/// </summary>
public static class DiShapes
{
    public static bool ComputeChoice() => true;

    public static void Mutate(ref bool value) => value = false;

    public static void Replace(out bool value) => value = false;

    public static void RegisterAlternativeSinks(IServiceCollection services, IConfiguration configuration)
    {
        bool useJsonSink = configuration.GetValue<bool>("FeatureToggles:UseJsonSink");
        if (useJsonSink)
        {
            services.AddScoped<ISinkService, JsonSinkService>();
        }
        else
        {
            services.AddScoped<ISinkService, ConsoleSinkService>();
        }
    }
}
