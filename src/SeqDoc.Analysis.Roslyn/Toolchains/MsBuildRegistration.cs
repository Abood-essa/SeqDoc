using System.Diagnostics;
using Microsoft.Build.Locator;

namespace SeqDoc.Analysis.Roslyn.Toolchains;

internal static class MsBuildRegistration
{
    private static readonly SemaphoreSlim RegistrationLock = new(1, 1);
    private static string? registeredVersion;

    public static async Task<string> EnsureRegisteredAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var selectedVersion = await ReadSelectedSdkVersionAsync(repositoryRoot, cancellationToken)
            .ConfigureAwait(false);

        await RegistrationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (MSBuildLocator.IsRegistered)
            {
                if (registeredVersion is not null
                    && !string.Equals(registeredVersion, selectedVersion, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"MSBuild {registeredVersion} is already registered, but this repository selects SDK {selectedVersion}. "
                        + "Analyze repositories requiring different SDKs in separate processes.");
                }

                return registeredVersion ?? selectedVersion;
            }

            var selectedInstance = MSBuildLocator.QueryVisualStudioInstances()
                .Where(instance => string.Equals(
                    instance.Version.ToString(),
                    selectedVersion,
                    StringComparison.Ordinal))
                .OrderBy(instance => instance.MSBuildPath, StringComparer.Ordinal)
                .FirstOrDefault();

            if (selectedInstance is null)
            {
                throw new InvalidOperationException(
                    $"The repository selects .NET SDK {selectedVersion}, but MSBuild Locator did not discover that SDK.");
            }

            MSBuildLocator.RegisterInstance(selectedInstance);
            registeredVersion = selectedVersion;
            return selectedVersion;
        }
        finally
        {
            RegistrationLock.Release();
        }
    }

    private static async Task<string> ReadSelectedSdkVersionAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("The dotnet SDK process could not be started.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var version = (await standardOutput.ConfigureAwait(false)).Trim();
        var error = (await standardError.ConfigureAwait(false)).Trim();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? "The repository's selected dotnet SDK could not be resolved."
                    : error);
        }

        return version;
    }
}
