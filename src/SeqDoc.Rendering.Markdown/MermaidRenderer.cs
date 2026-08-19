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
/// renderer never chooses fragment kind, arm polarity, or order. Fragments and arms are emitted only
/// when their recursively filtered content remains renderable. Output always uses canonical newlines.
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
                .Append(" as ").Append(EscapeAlias(participant.Label)).Append('\n');
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
        if (!HasRenderableContent(plan, fragment))
        {
            return;
        }

        int openerIndent = 4 + (2 * (depth - 1));
        switch (fragment.Kind)
        {
            case DiagramFragmentKind.Alt:
                var survivingArms = fragment.Arms.Where(arm => HasRenderableContent(plan, arm)).ToArray();
                if (survivingArms.Length == 1)
                {
                    var arm = survivingArms[0];
                    builder.Append(' ', openerIndent)
                        .Append("opt ")
                        .Append(EscapeLabel(arm.Label))
                        .Append('\n');
                    RenderSequence(
                        builder,
                        plan,
                        new DiagramSequence(arm.MessageRefs, arm.Fragments),
                        depth);
                    builder.Append(' ', openerIndent).Append("end").Append('\n');
                    break;
                }

                bool wroteArm = false;
                foreach (var arm in survivingArms)
                {
                    // The planner records the arm role explicitly (IsElse); the renderer serializes
                    // the recorded role and never infers alt/else from array index.
                    if (!wroteArm)
                    {
                        builder.Append(' ', openerIndent)
                            .Append("alt ")
                            .Append(EscapeLabel(fragment.Label))
                            .Append('\n');
                        if (arm.IsElse && !string.Equals(arm.Label, fragment.Label, StringComparison.Ordinal))
                        {
                            builder.Append(' ', openerIndent)
                                .Append("else ")
                                .Append(EscapeLabel(arm.Label))
                                .Append('\n');
                        }
                    }
                    else
                    {
                        builder.Append(' ', openerIndent)
                            .Append("else ")
                            .Append(EscapeLabel(arm.Label))
                            .Append('\n');
                    }
                    RenderSequence(
                        builder,
                        plan,
                        new DiagramSequence(arm.MessageRefs, arm.Fragments),
                        depth);
                    wroteArm = true;
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
                RenderSequence(
                    builder,
                    plan,
                    new DiagramSequence(fragment.MessageRefs, fragment.Fragments),
                    depth);

                builder.Append(' ', openerIndent).Append("end").Append('\n');
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(fragment),
                    $"Undefined diagram fragment kind '{fragment.Kind}'.");
        }
    }

    private static bool HasRenderableContent(DiagramPlan plan, DiagramFragment fragment)
    {
        if (fragment.Kind == DiagramFragmentKind.Break)
        {
            return false;
        }

        if (fragment.Kind == DiagramFragmentKind.Alt)
        {
            return fragment.Arms.Any(arm => HasRenderableContent(plan, arm));
        }

        return fragment.MessageRefs.Any(reference => HasMessage(plan, reference))
            || fragment.Fragments.Any(nested => HasRenderableContent(plan, nested));
    }

    private static bool HasRenderableContent(DiagramPlan plan, DiagramAltArm arm)
        => arm.MessageRefs.Any(reference => HasMessage(plan, reference))
            || arm.Fragments.Any(fragment => HasRenderableContent(plan, fragment));

    private static bool HasMessage(DiagramPlan plan, DiagramPlanElementId id)
        => plan.Messages.Any(message => message.Id == id);

    /// <summary>Canonical lowercase Mermaid keyword for a fragment kind.</summary>
    private static string FragmentKeyword(DiagramFragmentKind kind) => kind switch
    {
        DiagramFragmentKind.Opt => "opt",
        DiagramFragmentKind.Break => "break",
        DiagramFragmentKind.Loop => "loop",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), $"Undefined diagram fragment kind '{kind}'."),
    };

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
    /// quotes use the Mermaid entity so labels stay balanced.
    /// </summary>
    private static string EscapeLabel(string label)
    {
        string normalized = label.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Replace("\"", "#quot;");
    }

    /// <summary>
    /// Participant aliases are emitted without Mermaid's optional wrapping quotes. Encode characters
    /// that could escape the declaration, terminate a statement, or close the Markdown fence while
    /// retaining ordinary readable text.
    /// </summary>
    private static string EscapeAlias(string label)
    {
        var builder = new StringBuilder(label.Length);
        foreach (char character in label)
        {
            if (char.IsControl(character) || character is ';' or '`')
            {
                builder.Append(character switch
                {
                    ';' => "#59;",
                    '`' => "#96;",
                    _ => " ",
                });
            }
            else if (character == '"')
            {
                builder.Append("#quot;");
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
