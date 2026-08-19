using System.Collections.Immutable;
using SeqDoc.Rendering.Markdown;
using Xunit;

namespace SeqDoc.Rendering.Tests;

/// <summary>
/// accepted contract contract coverage for stack-based Mermaid fragment validation (contract stage accepted contract "Required Model"
/// item 11). The validator must use a stack over nested <c>alt</c>/<c>else</c>, <c>opt</c>,
/// <c>break</c>, <c>loop</c>, and <c>end</c> tokens and deterministically reject:
///
/// - a missing <c>end</c> for an open block;
/// - an extra <c>end</c> without an open block;
/// - <c>else</c> outside an <c>alt</c> or inside a non-alt block (misplaced);
/// - unsupported <c>par</c>;
/// - a <c>break</c> outside any open block (misplaced);
/// - the canonical termination note outside a <c>break</c> block (misplaced);
/// - unrecognized syntax;
/// - nesting deeper than the maximum supported fragment depth (3).
///
/// Equivalent malformed variants are consolidated into one theory; the positive balanced-nesting
/// partition is a separate fact. Until the product validator grows the stack contract this file
/// cannot compile or fails its assertions; that is the intentionally absent accepted contract behavior, not test
/// setup.
/// </summary>
public sealed class MermaidValidatorFragmentTests
{
    [Theory]
    [InlineData("missing-end", "sequenceDiagram\n    alt Decision\n        x->>y: m")]
    [InlineData("extra-end", "sequenceDiagram\n    end")]
    [InlineData("else-outside-alt", "sequenceDiagram\n    else Other\n        x->>y: m\n    end")]
    [InlineData("else-inside-opt", "sequenceDiagram\n    opt Guard\n        else Other\n    end")]
    [InlineData("par-unsupported", "sequenceDiagram\n    par Parallel\n        x->>y: m\n    end")]
    [InlineData("break-outside-block", "sequenceDiagram\n    break Stop\n        x->>y: m\n    end")]
    [InlineData("unknown-token", "sequenceDiagram\n    frobnicate\n        x->>y: m\n    end")]
    [InlineData("generic-fragment-label", "sequenceDiagram\n    alt Condition\n        x->>y: m\n    end")]
    [InlineData("generic-else-label", "sequenceDiagram\n    alt Ready\n        x->>y: m\n    else Continue\n        x->>y: n\n    end")]
    [InlineData("deep-nesting", "sequenceDiagram\n    alt A\n        alt B\n            alt C\n                alt D\n                    x->>y: m\n                end\n            end\n        end\n    end")]
    public void StackValidatorRejectsMalformedFragmentBlocks(string partition, string mermaid)
    {
        var errors = MermaidValidator.Validate(mermaid);

        Assert.NotEmpty(errors);
        string expectedKeyword = partition switch
        {
            "missing-end" => "end",
            "extra-end" => "end",
            "else-outside-alt" => "else",
            "else-inside-opt" => "else",
            "par-unsupported" => "par",
            "break-outside-block" => "break",
            "unknown-token" => "unrecognized",
            "generic-fragment-label" => "generic",
            "generic-else-label" => "generic",
            "deep-nesting" => "depth",
            _ => throw new ArgumentOutOfRangeException(nameof(partition)),
        };
        Assert.Contains(errors, error => error.Contains(expectedKeyword, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidatorAllowsControlWordsInsideMessageText()
    {
        Assert.Empty(MermaidValidator.Validate(
            "sequenceDiagram\n    x->>y: Condition and Continue are message text"));
    }

    [Fact]
    public void StackValidatorAcceptsBalancedAltOptBreakLoopNesting()
    {
        const string mermaid =
            "sequenceDiagram\n" +
            "    participant client as Client\n" +
            "    alt Decision\n" +
            "        opt Guard\n" +
            "            loop Retry\n" +
            "                client->>client: ping\n" +
            "            end\n" +
            "        end\n" +
            "        break Stop\n" +
            "            client->>client: bye\n" +
            "        end\n" +
            "    end";

        Assert.Empty(MermaidValidator.Validate(mermaid));
    }

    [Theory]
    [InlineData("participant client as Safe;drop")]
    [InlineData("participant client as Safe`fence`")]
    [InlineData("participant client as Safe\u0001label")]
    [InlineData("participant client as \"quoted\"")]
    public void ValidatorRejectsHostileQuoteFreeAliasForms(string participant)
    {
        Assert.Contains(MermaidValidator.Validate("sequenceDiagram\n    " + participant),
            error => error.Contains("unrecognized", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    // Negative: obsolete termination notes and generic control labels are never accepted.
    [InlineData("top-level-canonical-single", "sequenceDiagram\n    Note over client: Path terminates")]
    [InlineData("top-level-canonical-range", "sequenceDiagram\n    Note over client,service: Path terminates")]
    [InlineData("note-inside-alt", "sequenceDiagram\n    participant client as \"Client\"\n    alt Decision\n        Note over client: Path terminates\n    end")]
    [InlineData("non-canonical-label", "sequenceDiagram\n    Note over client: something else")]
    [InlineData("non-canonical-form", "sequenceDiagram\n    Note right of client: Path terminates")]
    [InlineData("lowercase-keyword", "sequenceDiagram\n    note over client: Path terminates")]
    public void ValidatorRejectsObsoleteTerminationNotesAndGenericControlLabels(string partition, string mermaid)
    {
        // DQ-1 strict-note contract: the validator accepts exactly the canonical sequence note the
        // renderer emits inside an empty Break (single participant key or the first,last range with
        // the fixed "Path terminates" label) and only when the current stack frame is exactly a
        // 'break'. The same canonical text outside a Break (top level or inside another fragment)
        // is a context error and every other note form is unrecognized syntax, so a
        // documentation-set build can never silently admit generic or invented note vocabulary
        // outside a terminating region.
        ImmutableArray<string> errors = MermaidValidator.Validate(mermaid);
        Assert.NotEmpty(errors);
        string expectedKeyword = partition switch
        {
            "top-level-canonical-single" => "unrecognized",
            "top-level-canonical-range" => "unrecognized",
            "note-inside-alt" => "unrecognized",
            "non-canonical-label" => "unrecognized",
            "non-canonical-form" => "unrecognized",
            "lowercase-keyword" => "unrecognized",
            _ => throw new ArgumentOutOfRangeException(nameof(partition)),
        };
        Assert.Contains(errors, error => error.Contains(expectedKeyword, StringComparison.OrdinalIgnoreCase));
    }
}
