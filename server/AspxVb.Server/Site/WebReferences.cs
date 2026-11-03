using System.Text;
using System.Xml.Linq;

namespace AspxVb.Server.Site;

/// <summary>
/// Generates the client proxy classes ASP.NET builds from App_WebReferences/**/*.wsdl, in
/// the shape wsdl.exe produces: a SoapHttpClientProtocol subclass with sync and async
/// methods, plus the schema's complex types and enums. Namespace = the folder path under
/// App_WebReferences. Covers document/literal services (what ASMX emits); anything it
/// can't map becomes Object rather than failing. Exists only in memory.
/// </summary>
public static class WebReferences
{
    private static readonly XNamespace Wsdl = "http://schemas.xmlsoap.org/wsdl/";
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";
    private static readonly XNamespace Soap11 = "http://schemas.xmlsoap.org/wsdl/soap/";

    public sealed record Proxy(string WsdlPath, string Code);

    public static IEnumerable<Proxy> Generate(string siteRoot)
    {
        var dir = FileText.OnDisk(Path.Combine(siteRoot, "App_WebReferences"));
        if (dir == null || !Directory.Exists(dir)) yield break;
        foreach (var wsdl in Directory.EnumerateFiles(dir, "*.wsdl", SearchOption.AllDirectories))
        {
            string? code;
            try
            {
                var folder = Path.GetRelativePath(dir, Path.GetDirectoryName(wsdl)!);
                code = new Generator(wsdl, folder == "." ? "" : folder.Replace('/', '.')).Run();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"web reference {wsdl}: {e.Message}");
                code = null;
            }
            if (code != null) yield return new Proxy(wsdl, code);
        }
    }

    private sealed class Generator(string wsdlPath, string ns)
    {
        private readonly Dictionary<string, XElement> _complexTypes = [];
        private readonly Dictionary<string, XElement> _simpleTypes = [];
        private readonly Dictionary<string, XElement> _elements = [];
        private readonly StringBuilder _sb = new();

        public string? Run()
        {
            var doc = XDocument.Parse(FileText.Read(wsdlPath));
            var defs = doc.Root!;
            foreach (var schema in defs.Element(Wsdl + "types")?.Elements(Xs + "schema") ?? []) LoadSchema(schema, Path.GetDirectoryName(wsdlPath)!, []);

            var service = defs.Element(Wsdl + "service");
            if (service == null) return null;
            var binding = defs.Elements(Wsdl + "binding").FirstOrDefault(b => b.Element(Soap11 + "binding") != null) ?? defs.Elements(Wsdl + "binding").FirstOrDefault();
            var portType = defs.Elements(Wsdl + "portType").FirstOrDefault(p => p.Attribute("name")?.Value == Local(binding?.Attribute("type")?.Value))
                           ?? defs.Elements(Wsdl + "portType").FirstOrDefault();
            var messages = defs.Elements(Wsdl + "message").ToDictionary(m => (string)m.Attribute("name")!, m => m);

            Line($"' Generated in memory from {Path.GetFileName(wsdlPath)} (App_WebReferences), as ASP.NET does at build time.");
            if (ns.Length > 0) Line($"Namespace {string.Join('.', ns.Split('.').Select(Id))}");
            var serviceName = Id((string)service.Attribute("name")!);
            Line($"Partial Public Class {serviceName}");
            Line("    Inherits System.Web.Services.Protocols.SoapHttpClientProtocol");
            Line("    Public Sub New()");
            Line("    End Sub");
            var asyncTypes = new StringBuilder();
            foreach (var op in portType?.Elements(Wsdl + "operation") ?? [])
            {
                var name = Id((string)op.Attribute("name")!);
                var input = Parts(op.Element(Wsdl + "input"), messages, request: true);
                var output = Parts(op.Element(Wsdl + "output"), messages, request: false);
                var parameters = string.Join(", ", input.Select(p => $"ByVal {p.Name} As {p.Type}")
                    .Concat(output.Skip(1).Select(p => $"ByRef {p.Name} As {p.Type}")));
                var result = output.FirstOrDefault();
                Line(result == null ? $"    Public Sub {name}({parameters})" : $"    Public Function {name}({parameters}) As {result.Type}");
                Line(result == null ? "    End Sub" : "    End Function");
                var asyncParams = string.Join(", ", input.Select(p => $"ByVal {p.Name} As {p.Type}"));
                Line($"    Public Overloads Sub {name}Async({asyncParams})");
                Line("    End Sub");
                Line($"    Public Overloads Sub {name}Async({(asyncParams.Length > 0 ? asyncParams + ", " : "")}ByVal userState As Object)");
                Line("    End Sub");
                Line($"    Public Event {name}Completed As {name}CompletedEventHandler");
                asyncTypes.Append($"Public Delegate Sub {name}CompletedEventHandler(ByVal sender As Object, ByVal e As {(result == null ? "System.ComponentModel.AsyncCompletedEventArgs" : name + "CompletedEventArgs")})\n");
                if (result != null)
                {
                    asyncTypes.Append($"Partial Public Class {name}CompletedEventArgs\n    Inherits System.ComponentModel.AsyncCompletedEventArgs\n");
                    asyncTypes.Append("    Private Sub New()\n        MyBase.New(Nothing, False, Nothing)\n    End Sub\n");
                    asyncTypes.Append($"    Public ReadOnly Property Result() As {result.Type}\n        Get\n            Return Nothing\n        End Get\n    End Property\nEnd Class\n");
                }
            }
            Line("End Class");
            _sb.Append(asyncTypes);
            foreach (var (name, type) in _complexTypes) if (ArrayItem(type) == null) EmitClass(name, type);
            foreach (var (name, type) in _simpleTypes) EmitEnum(name, type);
            if (ns.Length > 0) Line("End Namespace");
            return _sb.ToString();
        }

        private void LoadSchema(XElement schema, string dir, HashSet<string> seen)
        {
            foreach (var c in schema.Elements(Xs + "complexType")) _complexTypes.TryAdd((string)c.Attribute("name")!, c);
            foreach (var s in schema.Elements(Xs + "simpleType").Where(s => s.Descendants(Xs + "enumeration").Any())) _simpleTypes.TryAdd((string)s.Attribute("name")!, s);
            foreach (var e in schema.Elements(Xs + "element")) _elements.TryAdd((string)e.Attribute("name")!, e);
            // Imported/included schemas saved next to the .wsdl (Visual Studio downloads them).
            foreach (var imp in schema.Elements().Where(e => e.Name == Xs + "import" || e.Name == Xs + "include"))
            {
                var location = (string?)imp.Attribute("schemaLocation");
                if (location == null) continue;
                var file = FileText.OnDisk(Path.Combine(dir, Path.GetFileName(location.Split('?')[0])))
                           ?? Directory.EnumerateFiles(dir, "*.xsd").FirstOrDefault(f => !seen.Contains(f) && XDocument.Load(f).Root?.Attribute("targetNamespace")?.Value == (string?)imp.Attribute("namespace"));
                if (file == null || !seen.Add(file)) continue;
                LoadSchema(XDocument.Parse(FileText.Read(file)).Root!, dir, seen);
            }
        }

        private sealed record Param(string Name, string Type);

        /// <summary>Document/literal wrapped: the message's element is a sequence of parameters. Otherwise one parameter per part.</summary>
        private List<Param> Parts(XElement? io, Dictionary<string, XElement> messages, bool request)
        {
            if (io == null || !messages.TryGetValue(Local((string?)io.Attribute("message")) ?? "", out var message)) return [];
            var result = new List<Param>();
            foreach (var part in message.Elements(Wsdl + "part"))
            {
                if (Local((string?)part.Attribute("element")) is { } elementName && _elements.TryGetValue(elementName, out var element)
                    && element.Element(Xs + "complexType") is { } wrapper)
                {
                    result.AddRange(Members(wrapper).Select(m => new Param(Id(m.Name), m.Type)));
                    continue;
                }
                var type = Local((string?)part.Attribute("type")) is { } t ? TypeName(t) : "Object";
                if (request || part.Attribute("name")?.Value != "parameters") result.Add(new Param(Id((string)part.Attribute("name")!), type));
            }
            return result;
        }

        private sealed record Member(string Name, string Type);

        private IEnumerable<Member> Members(XElement complexType)
        {
            var sequence = complexType.Element(Xs + "sequence") ?? complexType.Element(Xs + "all")
                           ?? complexType.Element(Xs + "complexContent")?.Element(Xs + "extension")?.Element(Xs + "sequence");
            if (sequence == null) yield break;
            if (sequence.Elements(Xs + "element").Any(e => Local((string?)e.Attribute("ref")) == "schema"))
            {
                yield return new Member("Result", "System.Data.DataSet");
                yield break;
            }
            foreach (var e in sequence.Elements(Xs + "element"))
            {
                var name = (string?)e.Attribute("name") ?? Local((string?)e.Attribute("ref")) ?? "Item";
                var type = e.Attribute("type") is { } t ? TypeName(Local(t.Value)!)
                    : e.Element(Xs + "complexType") is { } inline && Members(inline).Any(m => m.Type == "System.Data.DataSet") ? "System.Data.DataSet"
                    : "Object";
                if ((string?)e.Attribute("maxOccurs") is { } max && max != "1" && max != "0") type += "()";
                yield return new Member(name, type);
            }
            foreach (var any in sequence.Elements(Xs + "any")) yield return new Member("Any", "System.Xml.XmlElement");
        }

        /// <summary>ArrayOfX (one unbounded element) is flattened to X(), as wsdl.exe does.</summary>
        private string? ArrayItem(XElement complexType)
        {
            var elements = complexType.Element(Xs + "sequence")?.Elements(Xs + "element").ToList();
            if (elements is not [var only] || (string?)only.Attribute("maxOccurs") is not ("unbounded" or not "1" and not null)) return null;
            return only.Attribute("type") is { } t ? TypeName(Local(t.Value)!) : null;
        }

        private string TypeName(string xsType)
        {
            if (_complexTypes.TryGetValue(xsType, out var complex)) return ArrayItem(complex) is { } item ? item + "()" : Id(xsType);
            if (_simpleTypes.ContainsKey(xsType)) return Id(xsType);
            return xsType switch
            {
                "string" or "anyURI" or "normalizedString" or "token" => "String",
                "int" => "Integer", "long" => "Long", "short" => "Short", "byte" => "SByte",
                "unsignedInt" => "UInteger", "unsignedLong" => "ULong", "unsignedShort" => "UShort", "unsignedByte" => "Byte",
                "boolean" => "Boolean", "float" => "Single", "double" => "Double", "decimal" => "Decimal",
                "dateTime" or "date" or "time" => "Date", "duration" => "System.TimeSpan",
                "base64Binary" or "hexBinary" => "Byte()", "guid" => "System.Guid", "char" => "Char",
                "QName" => "System.Xml.XmlQualifiedName",
                _ => "Object",
            };
        }

        private void EmitClass(string name, XElement type)
        {
            Line($"Partial Public Class {Id(name)}");
            if (type.Element(Xs + "complexContent")?.Element(Xs + "extension")?.Attribute("base") is { } b) Line($"    Inherits {TypeName(Local(b.Value)!)}");
            foreach (var m in Members(type)) Line($"    Public Property {Id(m.Name)} As {m.Type}");
            foreach (var a in type.Elements(Xs + "attribute").Concat(type.Descendants(Xs + "extension").Elements(Xs + "attribute")))
                if ((string?)a.Attribute("name") is { } an) Line($"    Public Property {Id(an)} As {(a.Attribute("type") is { } t ? TypeName(Local(t.Value)!) : "String")}");
            Line("End Class");
        }

        private void EmitEnum(string name, XElement type)
        {
            Line($"Public Enum {Id(name)}");
            foreach (var v in type.Descendants(Xs + "enumeration").Select(e => (string?)e.Attribute("value")).OfType<string>().Distinct())
                Line($"    {Id(v)}");
            Line("End Enum");
        }

        private void Line(string s) => _sb.Append(s).Append('\n');
    }

    private static string? Local(string? qname) => qname?.Split(':')[^1];

    /// <summary>A valid VB identifier for a schema name: invalid characters become '_', keywords get brackets.</summary>
    private static string Id(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name) sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        var id = sb.Length == 0 || char.IsDigit(sb[0]) ? "_" + sb : sb.ToString();
        return VbNames.Escape(id);
    }
}
