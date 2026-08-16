using SeqDoc.Application.Analysis;

namespace SeqDoc.Cli;

internal enum CliCommand
{
    Help,
    Analyze,
    Catalog,
    InspectSolution,
}

internal sealed record CliArguments(
    CliCommand Command,
    string? TargetPath,
    string? RepositoryRoot,
    string? ConfigurationFile,
    string? Profile,
    string? Configuration,
    string? TargetFramework,
    string? RuntimeIdentifier,
    string? CachePath,
    string? OutputPath,
    bool AllFrameworks,
    bool Json,
    CatalogKind CatalogKind,
    string? Query,
    string? IdPrefix,
    string? Entry)
{
    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "--repository-root", "--config", "--profile", "--configuration", "--framework",
        "--runtime", "--cache", "--output", "--kind", "--query", "--id", "--entry",
    };

    public static bool TryParse(string[] args, out CliArguments? parsed, out string? error)
    {
        parsed = null;
        error = null;
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            parsed = Empty(CliCommand.Help);
            return true;
        }

        int optionStart;
        CliCommand command;
        string? target;
        if (args[0] == "analyze" || args[0] == "catalog")
        {
            command = args[0] == "analyze" ? CliCommand.Analyze : CliCommand.Catalog;
            if (args.Length < 2 || args[1].StartsWith('-'))
            {
                error = $"Command '{args[0]}' requires a solution or project path.";
                return false;
            }

            target = args[1];
            optionStart = 2;
        }
        else if (args[0] == "inspect" && args.Length >= 2 && args[1] == "solution")
        {
            command = CliCommand.InspectSolution;
            if (args.Length < 3 || args[2].StartsWith('-'))
            {
                error = "Command 'inspect solution' requires a solution or project path.";
                return false;
            }

            target = args[2];
            optionStart = 3;
        }
        else
        {
            error = $"Unknown command '{args[0]}'.";
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool json = false;
        bool allFrameworks = false;
        for (int index = optionStart; index < args.Length; index++)
        {
            string option = args[index];
            if (option == "--json")
            {
                json = true;
                continue;
            }

            if (option == "--all-frameworks")
            {
                allFrameworks = true;
                continue;
            }

            if (!ValueOptions.Contains(option))
            {
                error = $"Unknown option '{option}'.";
                return false;
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Option '{option}' requires a value.";
                return false;
            }

            if (!values.TryAdd(option, args[++index]))
            {
                error = $"Option '{option}' was supplied more than once.";
                return false;
            }
        }

        if (allFrameworks && values.ContainsKey("--framework"))
        {
            error = "Options '--framework' and '--all-frameworks' cannot be used together.";
            return false;
        }

        if (allFrameworks && values.ContainsKey("--output"))
        {
            error = "Option '--output' is not valid together with '--all-frameworks' in accepted contract; documentation generation is single-profile only.";
            return false;
        }

        var kind = CatalogKind.All;
        if (values.TryGetValue("--kind", out string? kindText)
            && !Enum.TryParse(kindText, ignoreCase: true, out kind))
        {
            error = "Option '--kind' must be one of: all, project, document, type, method, reference, invocation.";
            return false;
        }

        if (command != CliCommand.Catalog
            && (values.ContainsKey("--kind") || values.ContainsKey("--query") || values.ContainsKey("--id")))
        {
            error = "Options '--kind', '--query', and '--id' are valid only for 'catalog'.";
            return false;
        }

        if (command != CliCommand.Analyze && values.ContainsKey("--output"))
        {
            error = "Option '--output' is valid only for 'analyze'.";
            return false;
        }

        if (command != CliCommand.Analyze && values.ContainsKey("--entry"))
        {
            error = "Option '--entry' is valid only for 'analyze'.";
            return false;
        }

        if (values.ContainsKey("--entry") && !values.ContainsKey("--output"))
        {
            error = "Option '--entry' requires '--output' to select one documentation entry.";
            return false;
        }

        if (values.ContainsKey("--entry") && values.ContainsKey("--all-frameworks"))
        {
            error = "Option '--entry' is not valid together with '--all-frameworks'; documentation generation is single-profile only.";
            return false;
        }

        parsed = new CliArguments(
            command,
            target,
            Get("--repository-root"),
            Get("--config"),
            Get("--profile"),
            Get("--configuration"),
            Get("--framework"),
            Get("--runtime"),
            Get("--cache"),
            Get("--output"),
            allFrameworks,
            json,
            kind,
            Get("--query"),
            Get("--id"),
            Get("--entry"));
        return true;

        string? Get(string name) => values.GetValueOrDefault(name);
    }

    private static CliArguments Empty(CliCommand command) => new(
        command, null, null, null, null, null, null, null, null, null, false, false, CatalogKind.All, null, null, null);
}
