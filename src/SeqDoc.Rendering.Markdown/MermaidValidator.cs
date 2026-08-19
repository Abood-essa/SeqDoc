using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace SeqDoc.Rendering.Markdown;

/// <summary>
/// Structural Mermaid sequence-diagram validation. The validator checks the header, participant
/// declarations, message arrows, and balanced nested
/// <c>alt</c>/<c>else</c>, <c>opt</c>, <c>break</c>, <c>loop</c>, and <c>end</c> blocks using a
/// stack. It deterministically rejects misplaced <c>else</c> (outside an open <c>alt</c>), a
/// <c>break</c> outside any open block, unsupported <c>par</c>, unknown tokens, blank lines,
/// nesting deeper than the maximum supported fragment depth (3), and unbalanced blocks, without
/// interpreting any label meaning.
/// </summary>
public static partial class MermaidValidator
{
    /// <summary>The maximum supported nested fragment depth; deeper plans must fail deterministically.</summary>
    private const int MaxFragmentDepth = 3;

    public static ImmutableArray<string> Validate(string mermaid)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(mermaid))
        {
            return ["The Mermaid diagram is empty."];
        }

        string[] lines = mermaid.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "sequenceDiagram")
        {
            errors.Add("The first line must be 'sequenceDiagram'.");
        }

        // The stack holds the kind of every currently open block; the top decides where 'else'
        // may appear and depth is the stack height.
        var blockStack = new List<string>();
        for (int index = 1; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line.Length == 0)
            {
                errors.Add($"Line {index + 1} is empty; canonical output must not contain blank lines.");
                continue;
            }

            if (ObsoleteControlLabelRegex().IsMatch(line))
            {
                errors.Add($"Line {index + 1}: generic control labels are not accepted in fragment or note positions.");
                continue;
            }

            if (line == "end")
            {
                if (blockStack.Count == 0)
                {
                    errors.Add($"Line {index + 1}: 'end' appears without a matching open block.");
                }
                else
                {
                    blockStack.RemoveAt(blockStack.Count - 1);
                }

                continue;
            }

            if (line.StartsWith("else ", StringComparison.Ordinal))
            {
                // 'else' continues the open 'alt' block; it must never appear outside an alt or
                // inside a non-alt block.
                if (blockStack.Count == 0 || !string.Equals(blockStack[^1], "alt", StringComparison.Ordinal))
                {
                    errors.Add($"Line {index + 1}: 'else' appears outside an open 'alt' block.");
                }

                continue;
            }

            if (line.StartsWith("alt ", StringComparison.Ordinal))
            {
                PushBlock(blockStack, "alt", errors, index);
                continue;
            }

            if (line.StartsWith("opt ", StringComparison.Ordinal))
            {
                PushBlock(blockStack, "opt", errors, index);
                continue;
            }

            if (line.StartsWith("break ", StringComparison.Ordinal))
            {
                if (blockStack.Count == 0)
                {
                    errors.Add($"Line {index + 1}: 'break' appears outside any open block.");
                }
                else
                {
                    PushBlock(blockStack, "break", errors, index);
                }

                continue;
            }

            if (line.StartsWith("loop ", StringComparison.Ordinal))
            {
                PushBlock(blockStack, "loop", errors, index);
                continue;
            }

            if (line.StartsWith("par ", StringComparison.Ordinal))
            {
                // 'par' expresses concurrency that SeqDoc does not claim; it is rejected instead of
                // being silently treated as an unknown token.
                errors.Add($"Line {index + 1}: 'par' fragments are unsupported.");
                continue;
            }

            if (ParticipantRegex().IsMatch(line))
            {
                continue;
            }

            if (MessageRegex().IsMatch(line))
            {
                continue;
            }

            errors.Add($"Line {index + 1}: unrecognized Mermaid sequence-diagram syntax.");
        }

        if (blockStack.Count > 0)
        {
            errors.Add($"The '{blockStack[^1]}' block is not closed with 'end'.");
        }

        return errors.ToImmutableArray();
    }

    private static void PushBlock(List<string> blockStack, string kind, List<string> errors, int index)
    {
        if (blockStack.Count >= MaxFragmentDepth)
        {
            errors.Add($"Line {index + 1}: fragment nesting depth exceeds the maximum supported depth of {MaxFragmentDepth}.");
        }

        blockStack.Add(kind);
    }

    [GeneratedRegex(@"^participant\s+\S+\s+as\s+(?:#(?:quot|59|96);|[^""`;\p{Cc}])+$", RegexOptions.CultureInvariant)]
    private static partial Regex ParticipantRegex();

    [GeneratedRegex(@"^(?:alt|else|opt|break|loop)\s+(?:Condition|Continue|Continue evaluating condition|Path terminates)$", RegexOptions.CultureInvariant)]
    private static partial Regex ObsoleteControlLabelRegex();

    [GeneratedRegex(@"^\S+(-{2}>>|->>)\S+:\s*\S.*$", RegexOptions.CultureInvariant)]
    private static partial Regex MessageRegex();
}
