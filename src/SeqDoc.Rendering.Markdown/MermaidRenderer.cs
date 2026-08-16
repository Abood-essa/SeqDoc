using System.Text;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Identity;

namespace SeqDoc.Rendering.Markdown;

/// <summary>
/// Serializes a renderer-neutral <see cref="DiagramPlan"/> into Mermaid sequence-diagram text. The
/// renderer performs zero semantic ordering: participants, messages, and the ordered sequence tree
/// are emitted exactly in the plan's order (escaping and formatting only). When the plan carries a
/// non-empty sequence, the renderer walks the tree depth-first with deterministic two-space
/// indentation per fragment level (base 4 spaces: a fragment opener/closer at depth d sits at
/// 4 + 2*(d-1) and its contents at 4 + 2*d). When the sequence is empty (legacy topology-empty
/// plans), the accepted flat <see cref="DiagramBranch"/> alt/else output is emitted byte-stable. The
/// renderer never chooses fragment kind, arm polarity, or order. An empty typed Break (the Core
/// closed-shape contract admits no content) additionally receives the canonical deterministic
/// termination note derived solely from Break semantics and the plan's stable participant keys, so
/// terminating regions always carry renderable content; the note is an annotation, never an
/// invented interaction. Output always uses canonical newlines.
/// </summary>
public static class MermaidRenderer
{
    public static string Render(DiagramPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var builder = new StringBuilder();
        builder.Append("sequenceDiagram").Append('\n');
        foreach (var participant in plan.Participants)
        {
            builder.Append("    participant ").Append(participant.Key)
                .Append(" as \"").Append(EscapeLabel(participant.Label)).Append('"').Append('\n');
        }

        if (plan.Sequence is not null && plan.Sequence.Elements.Length > 0)
        {
            RenderSequence(builder, plan, plan.Sequence, depth: 0);
        }
        else
        {
            RenderLegacyBranches(builder, plan);
        }

        return builder.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// Depth-first serialization of the ordered sequence tree. Elements are emitted in the plan's
    /// exact chronology (message ref, fragment, message ref), so a continuation message after a
    /// rejoining decision always renders after the decision. Messages at a sequence level sit at
    /// 4 + 2*depth spaces; fragments open at 4 + 2*(depth-1), hold contents at 4 + 2*depth, and
    /// close at the opener indentation. Nothing here chooses placement or kind.
    /// </summary>
    private static void RenderSequence(StringBuilder builder, DiagramPlan plan, DiagramSequence sequence, int depth)
    {
        int messageIndent = 4 + (2 * depth);
        foreach (var element in sequence.Elements)
        {
            if (element.IsMessageRef)
            {
                var message = FindMessage(plan, element.MessageRefId!.Value);
                builder.Append(' ', messageIndent).Append(RenderMessage(message)).Append('\n');
            }
            else
            {
                RenderFragment(builder, plan, element.NestedFragment!, depth + 1);
            }
        }
    }

    private static void RenderFragment(StringBuilder builder, DiagramPlan plan, DiagramFragment fragment, int depth)
    {
        int openerIndent = 4 + (2 * (depth - 1));
        switch (fragment.Kind)
        {
            case DiagramFragmentKind.Alt:
                foreach (var arm in fragment.Arms)
                {
                    // The planner records the arm role explicitly (IsElse); the renderer serializes
                    // the recorded role and never infers alt/else from array index.
                    string opener = arm.IsElse
                        ? $"else {EscapeLabel(arm.Label)}"
                        : $"alt {EscapeLabel(fragment.Label)}";
                    builder.Append(' ', openerIndent).Append(opener).Append('\n');
                    RenderSequence(
                        builder,
                        plan,
                        new DiagramSequence(arm.MessageRefs, arm.Fragments),
                        depth);
                }

                builder.Append(' ', openerIndent).Append("end").Append('\n');
                break;
            case DiagramFragmentKind.Opt:
            case DiagramFragmentKind.Break:
            case DiagramFragmentKind.Loop:
                builder.Append(' ', openerIndent)
                    .Append(FragmentKeyword(fragment.Kind))
                    .Append(' ')
                    .Append(EscapeLabel(fragment.Label))
                    .Append('\n');
                if (fragment.Kind == DiagramFragmentKind.Break
                    && fragment.MessageRefs.IsDefaultOrEmpty
                    && fragment.Fragments.IsDefaultOrEmpty)
                {
                    // DQ-1: a typed Break is an empty marker by the Core closed-shape contract
                    // (no arms, message refs, or nested fragments), which serialized as an empty
                    // `break {label}` / `end` pair that collapsed layout in Visual Studio Code and
                    // Mermaid Live. Emit one deterministic non-interaction annotation derived
                    // solely from Break semantics and the plan's stable participant keys so every
                    // terminating region carries renderable content. The note never synthesizes a
                    // message arrow or invents project vocabulary. A future non-empty Break (were
                    // the closed-shape contract ever relaxed) keeps the shared content path below
                    // without an invented note.
                    builder.Append(' ', 4 + (2 * depth))
                        .Append(RenderBreakTerminationNote(plan))
                        .Append('\n');
                }
                else
                {
                    RenderSequence(
                        builder,
                        plan,
                        new DiagramSequence(fragment.MessageRefs, fragment.Fragments),
                        depth);
                }

                builder.Append(' ', openerIndent).Append("end").Append('\n');
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(fragment),
                    $"Undefined diagram fragment kind '{fragment.Kind}'.");
        }
    }

    /// <summary>Canonical lowercase Mermaid keyword for a fragment kind.</summary>
    private static string FragmentKeyword(DiagramFragmentKind kind) => kind switch
    {
        DiagramFragmentKind.Opt => "opt",
        DiagramFragmentKind.Break => "break",
        DiagramFragmentKind.Loop => "loop",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), $"Undefined diagram fragment kind '{kind}'."),
    };

    /// <summary>
    /// Canonical DQ-1 termination note emitted inside an empty typed Break. The note spans the
    /// plan's stable participant keys in deterministic plan order: a single key for a
    /// one-participant plan, otherwise the first and last keys of the participant list. The label
    /// is a fixed Break-semantics phrase and never project vocabulary, timestamps, paths, or
    /// invented calls, and the note never becomes a message arrow. A plan with zero participants
    /// cannot name an anchor, so rendering fails closed instead of inventing a key.
    /// </summary>
    private static string RenderBreakTerminationNote(DiagramPlan plan)
    {
        if (plan.Participants.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException(
                "A Break fragment requires at least one planned participant to render its termination note; the plan has none.");
        }

        string first = plan.Participants[0].Key;
        string span = plan.Participants.Length == 1
            ? first
            : $"{first},{plan.Participants[^1].Key}";
        return $"Note over {span}: Path terminates";
    }

    /// <summary>
    /// Resolves a message reference or fails closed: an unresolved reference that somehow arrives at
    /// the renderer is an error, never a silent skip.
    /// </summary>
    private static DiagramMessage FindMessage(DiagramPlan plan, DiagramPlanElementId id)
    {
        foreach (var message in plan.Messages)
        {
            if (message.Id == id)
            {
                return message;
            }
        }

        throw new InvalidOperationException(
            $"A sequence element references diagram message '{id.Value}' that is not planned; rendering fails closed.");
    }

    /// <summary>
    /// Byte-stable legacy flat rendering for topology-empty plans: branch messages are collected by
    /// key and rendered inside one flat alt/else block with the historical 8-space message
    /// indentation. This path is preserved verbatim so accepted alpha output never changes.
    /// </summary>
    private static void RenderLegacyBranches(StringBuilder builder, DiagramPlan plan)
    {
        var branchKeys = plan.Branches
            .SelectMany(branch => branch.MessageKeys)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var message in plan.Messages)
        {
            if (branchKeys.Contains(message.Key))
            {
                continue;
            }

            builder.Append("    ").Append(RenderMessage(message)).Append('\n');
        }

        for (int index = 0; index < plan.Branches.Length; index++)
        {
            var branch = plan.Branches[index];
            string opener = index == 0 ? "alt" : "else";
            builder.Append("    ").Append(opener).Append(' ').Append(EscapeLabel(branch.Label)).Append('\n');
            foreach (var key in branch.MessageKeys)
            {
                var message = FindMessageByKey(plan, key);
                if (message is not null)
                {
                    builder.Append("        ").Append(RenderMessage(message)).Append('\n');
                }
            }

            if (index == plan.Branches.Length - 1)
            {
                builder.Append("    end").Append('\n');
            }
        }
    }

    private static DiagramMessage? FindMessageByKey(DiagramPlan plan, string key)
    {
        foreach (var message in plan.Messages)
        {
            if (string.Equals(message.Key, key, StringComparison.Ordinal))
            {
                return message;
            }
        }

        return null;
    }

    private static string RenderMessage(DiagramMessage message)
    {
        string arrow = message.Kind == DiagramMessageKind.Request ? "->>" : "-->>";
        return $"{message.Source}{arrow}{message.Target}: {EscapeLabel(message.Label)}";
    }

    /// <summary>
    /// Mermaid cannot represent newlines inside a label; they collapse to a single space. Embedded
    /// quotes use the Mermaid entity so quoted participant aliases and message labels stay balanced,
    /// and no label ever introduces a backtick that could close a Markdown fence.
    /// </summary>
    private static string EscapeLabel(string label)
    {
        string normalized = label.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Replace("\"", "#quot;");
    }
}
