using System.Text;

namespace AspxVb.Server.Site;

/// <summary>Where a generated field came from, so definitions can land on the markup.</summary>
public sealed record FieldOrigin(string Id, int VirtualStart, int VirtualEnd, int MarkupIdStart, int MarkupIdEnd);

public sealed record GeneratedPartial(string Text, IReadOnlyList<FieldOrigin> Fields);

/// <summary>
/// Builds the partial class ASP.NET generates at runtime for a page or user control: one
/// Protected WithEvents field per server control (ADR 0004). Exists only in memory.
/// </summary>
public static class VirtualPartial
{
    public static GeneratedPartial Generate(string markupFileName, string inherits, IEnumerable<ServerControl> controls, ControlTypes types, Func<string, string, string?> userControlType)
    {
        var parts = inherits.Split('.');
        var className = parts[^1];
        var ns = parts.Length > 1 ? string.Join('.', parts[..^1]) : null;

        var sb = new StringBuilder();
        var fields = new List<FieldOrigin>();
        sb.Append("' Generated in memory from ").Append(markupFileName).Append(": the control fields ASP.NET declares at runtime.\n");
        if (ns != null) sb.Append("Namespace ").Append(ns).Append('\n');
        sb.Append("Partial Class ").Append(className).Append('\n');
        foreach (var c in controls)
        {
            var type = types.TypeFor(c, userControlType);
            if (type == null) continue;
            sb.Append("    Protected WithEvents ");
            var start = sb.Length;
            sb.Append(VbNames.Escape(c.Id.Value));
            fields.Add(new FieldOrigin(c.Id.Value, start, sb.Length, c.Id.ValueStart, c.Id.ValueEnd));
            sb.Append(" As Global.").Append(type).Append('\n');
        }
        sb.Append("End Class\n");
        if (ns != null) sb.Append("End Namespace\n");
        return new GeneratedPartial(sb.ToString(), fields);
    }
}
