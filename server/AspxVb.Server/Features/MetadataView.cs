using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace AspxVb.Server.Features;

/// <summary>
/// A read-only, VB-flavored outline of a framework or Bin type, so go to definition on
/// Trace, Session and friends lands somewhere useful. Generated from metadata; not
/// decompiled source.
/// </summary>
public sealed record MetadataView(string Uri, string Text, int Line)
{
    private static readonly SymbolDisplayFormat MemberFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility | SymbolDisplayMemberOptions.IncludeModifiers
                       | SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeType
                       | SymbolDisplayMemberOptions.IncludeConstantValue,
        kindOptions: SymbolDisplayKindOptions.IncludeMemberKeyword,
        propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
        parameterOptions: SymbolDisplayParameterOptions.IncludeName | SymbolDisplayParameterOptions.IncludeType
                          | SymbolDisplayParameterOptions.IncludeParamsRefOut | SymbolDisplayParameterOptions.IncludeDefaultValue
                          | SymbolDisplayParameterOptions.IncludeOptionalBrackets,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static MetadataView? For(ISymbol symbol)
    {
        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        if (type == null) return null;
        while (type.ContainingType != null) type = type.ContainingType;

        var sb = new StringBuilder();
        var line = 0;
        var targetLine = 0;
        void Emit(string s, ISymbol? member = null)
        {
            if (member != null && SymbolEqualityComparer.Default.Equals(member, symbol)) targetLine = line;
            sb.Append(s).Append('\n');
            line++;
        }

        Emit($"' {type.ContainingAssembly?.Identity.GetDisplayName()}");
        Emit("' Generated from metadata (read-only). Member bodies are not available.");
        Emit("");
        var ns = type.ContainingNamespace is { IsGlobalNamespace: false } n ? n.ToDisplayString() : null;
        if (ns != null) Emit($"Namespace {ns}");
        var indent = ns != null ? "    " : "";
        EmitType(type, indent);
        if (ns != null) Emit("End Namespace");

        void EmitType(INamedTypeSymbol t, string pad)
        {
            Summary(t, pad);
            var keyword = t.TypeKind switch
            {
                TypeKind.Interface => "Interface", TypeKind.Struct => "Structure", TypeKind.Enum => "Enum",
                TypeKind.Delegate => "Delegate", TypeKind.Module => "Module", _ => "Class",
            };
            var modifiers = t.TypeKind == TypeKind.Class && t.IsAbstract ? "MustInherit " : t.TypeKind == TypeKind.Class && t.IsSealed && !t.IsStatic ? "NotInheritable " : "";
            Emit($"{pad}{Accessibility(t)} {modifiers}{keyword} {t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}", t);
            if (t.TypeKind == TypeKind.Delegate) return;
            if (t.BaseType is { SpecialType: not SpecialType.System_Object and not SpecialType.System_ValueType and not SpecialType.System_Enum } b)
                Emit($"{pad}    Inherits {b.ToDisplayString()}");
            foreach (var i in t.Interfaces) Emit($"{pad}    Implements {i.ToDisplayString()}");
            foreach (var m in t.GetMembers().Where(Visible).OrderBy(Order).ThenBy(m => m.Name))
            {
                if (m is INamedTypeSymbol nested) { Emit(""); EmitType(nested, pad + "    "); continue; }
                Summary(m, pad + "    ");
                Emit($"{pad}    {m.ToDisplayString(MemberFormat)}", m);
            }
            Emit($"{pad}End {keyword}");
        }

        void Summary(ISymbol s, string pad)
        {
            var xml = s.GetDocumentationCommentXml();
            if (string.IsNullOrWhiteSpace(xml)) return;
            try
            {
                var summary = XElement.Parse(xml).Element("summary")?.Value;
                foreach (var l in (summary ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)) Emit($"{pad}''' {l}");
            }
            catch { /* malformed doc XML: skip */ }
        }

        var uri = $"aspxvb-metadata:/{System.Uri.EscapeDataString(type.ContainingAssembly?.Name ?? "unknown")}/{System.Uri.EscapeDataString(type.ToDisplayString())}.vb";
        return new MetadataView(uri, sb.ToString(), targetLine);
    }

    private static bool Visible(ISymbol m) =>
        !m.IsImplicitlyDeclared
        && m.DeclaredAccessibility is Microsoft.CodeAnalysis.Accessibility.Public or Microsoft.CodeAnalysis.Accessibility.Protected or Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal
        && m is not IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise or MethodKind.StaticConstructor };

    private static int Order(ISymbol m) => m switch
    {
        IFieldSymbol => 0, IMethodSymbol { MethodKind: MethodKind.Constructor } => 1, IPropertySymbol => 2,
        IEventSymbol => 3, IMethodSymbol => 4, _ => 5,
    };

    private static string Accessibility(ISymbol s) => s.DeclaredAccessibility switch
    {
        Microsoft.CodeAnalysis.Accessibility.Public => "Public",
        Microsoft.CodeAnalysis.Accessibility.Protected => "Protected",
        Microsoft.CodeAnalysis.Accessibility.Internal => "Friend",
        _ => "Private",
    };
}
