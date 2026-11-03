using Microsoft.CodeAnalysis.VisualBasic;

namespace AspxVb.Server.Site;

public static class VbNames
{
    /// <summary>The name as a VB identifier: reserved words get brackets (an ID="return" control is declared as [return]).</summary>
    public static string Escape(string name) =>
        SyntaxFacts.IsReservedKeyword(SyntaxFacts.GetKeywordKind(name)) ? $"[{name}]" : name;
}
