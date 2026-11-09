using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;

namespace xpTURN.Feedback.Tests.Editor.Docs
{
    // the /// comments of the package's Runtime sources, read line by line: each declaration with the comment above it.
    // A declaration is named the way reflection names it — its type, its member name (a constructor by its type, an
    // operator by op_…) and its parameter names, then their types when two overloads share the names
    internal static class DocComments
    {
        internal sealed class Entry
        {
            public string File;
            public int Line;
            public string Type;
            public string Kind;
            public string Name;
            public string[] ParamNames = new string[0];
            public string[] ParamTypes = new string[0];
            public bool Public;
            public string Xml;
            public readonly List<string> Problems = new List<string>();

            public override string ToString() => $"{Type}.{Name}({string.Join(", ", ParamNames)}) {Path.GetFileName(File)}:{Line}";
        }

        sealed class Scope
        {
            public string Name;
            public string Kind;
            public bool Public;
            // a sealed class's protected members are no one's to override — not the public surface
            public bool Sealed;
            public int Depth;
            public bool Opened;
        }

        static readonly Regex TypeDecl = new Regex(@"^(?<mods>(?:(?:public|internal|private|protected|static|sealed|abstract|readonly|partial|ref|unsafe|new)\s+)*)(?<kind>class|struct|interface|enum)\s+(?<name>\w+)");
        static readonly Regex Attribute = new Regex(@"^\[[^\]]*\]\s*");
        static readonly Regex Event = new Regex(@"\bevent\s+.+?\s+(?<name>\w+)\s*(?:;|\{|=)");
        static readonly Regex Operator = new Regex(@"\boperator\s*(?<op>==|!=|<=|>=|<|>|\+|-|\*|/|%|!|true|false)\s*\(");
        static readonly Regex Conversion = new Regex(@"\b(?<conv>implicit|explicit)\s+operator\s+[\w.<>\[\], ]+\s*\(");
        static readonly Regex Call = new Regex(@"(?<name>[A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(");
        static readonly Regex EnumValue = new Regex(@"^(?<name>[A-Za-z_]\w*)\s*(?:=\s*[^,]+)?,?$");
        static readonly Regex Access = new Regex(@"\b(?:public|internal|private|protected)\b");

        static List<Entry> s_All;

        internal static List<Entry> All => s_All ?? (s_All = Read(Path.Combine(DocRegions.PackageRoot, "Runtime")));

        internal static List<Entry> Read(string root)
        {
            var all = new List<Entry>();
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                all.AddRange(ReadFile(file));
            return all;
        }

        // the code of a line without its strings, chars and trailing // comment — for the braces
        static string Code(string line)
        {
            string s = Regex.Replace(line, "@\"(?:[^\"]|\"\")*\"|\"(?:[^\"\\\\]|\\\\.)*\"|'(?:[^'\\\\]|\\\\.)*'", "\"\"");
            int c = s.IndexOf("//", StringComparison.Ordinal);
            return c < 0 ? s : s.Substring(0, c);
        }

        internal static List<Entry> ReadFile(string file)
        {
            var entries = new List<Entry>();
            string[] lines = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n');
            var scopes = new List<Scope>();
            int depth = 0;
            List<string> doc = null;
            int docLine = 0;
            bool attributeBeforeDoc = false, attributeSeen = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string t = raw.Trim();
                if (t.StartsWith("///", StringComparison.Ordinal))
                {
                    if (doc == null)
                    {
                        doc = new List<string>();
                        docLine = i + 1;
                        attributeBeforeDoc = attributeSeen;
                    }
                    doc.Add(t.Substring(3).TrimStart());
                    continue;
                }
                if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("#", StringComparison.Ordinal))
                    continue;
                string code = Code(t);
                if (code.StartsWith("[", StringComparison.Ordinal) && Attribute.Replace(code, "").Trim().Length == 0)
                {
                    attributeSeen = true;
                    Braces(code, scopes, ref depth);
                    continue;
                }
                string head = Attribute.Replace(code, "").Trim();
                Scope inner = scopes.Count > 0 ? scopes[scopes.Count - 1] : null;
                bool atMember = inner != null && inner.Opened && depth == inner.Depth + 1;
                Entry e = null;
                Match type = TypeDecl.Match(head);
                if (type.Success && (inner == null || atMember))
                {
                    bool isPublic = type.Groups["mods"].Value.Contains("public");
                    e = new Entry { Kind = "type", Name = type.Groups["name"].Value, Type = type.Groups["name"].Value, Public = isPublic };
                    scopes.Add(new Scope { Name = e.Name, Kind = type.Groups["kind"].Value, Public = isPublic, Sealed = Regex.IsMatch(type.Groups["mods"].Value, @"\b(?:sealed|static)\b"), Depth = depth });
                }
                else if (atMember && !head.StartsWith("{", StringComparison.Ordinal) && !head.StartsWith("}", StringComparison.Ordinal))
                    e = Member(head, inner);
                if (e != null)
                {
                    e.File = file;
                    e.Line = i + 1;
                    if (doc != null)
                    {
                        e.Xml = string.Join("\n", doc);
                        if (attributeBeforeDoc)
                            e.Problems.Add("the /// is below an attribute — it documents nothing (CS1587)");
                    }
                    entries.Add(e);
                }
                else if (doc != null)
                    entries.Add(new Entry { File = file, Line = docLine, Kind = "orphan", Name = "", Type = inner?.Name ?? "", Xml = string.Join("\n", doc), Problems = { "a /// on no declaration this reader knows: " + t } });
                doc = null;
                attributeSeen = false;
                Braces(code, scopes, ref depth);
            }
            return entries;
        }

        static void Braces(string code, List<Scope> scopes, ref int depth)
        {
            foreach (char ch in code)
            {
                if (ch == '{')
                {
                    depth++;
                    if (scopes.Count > 0 && !scopes[scopes.Count - 1].Opened && depth == scopes[scopes.Count - 1].Depth + 1)
                        scopes[scopes.Count - 1].Opened = true;
                }
                else if (ch == '}')
                {
                    depth--;
                    while (scopes.Count > 0 && scopes[scopes.Count - 1].Opened && depth <= scopes[scopes.Count - 1].Depth)
                        scopes.RemoveAt(scopes.Count - 1);
                }
            }
        }

        static Entry Member(string head, Scope scope)
        {
            var e = new Entry { Type = scope.Name };
            if (scope.Kind == "enum")
            {
                Match v = EnumValue.Match(head);
                if (!v.Success)
                    return null;
                e.Kind = "value";
                e.Name = v.Groups["name"].Value;
                e.Public = scope.Public;
                return e;
            }
            // a protected member of a class one can derive from is the public surface too (the extension bases' calls)
            e.Public = scope.Public && (scope.Kind == "interface" ? !Access.IsMatch(head) || head.Contains("public")
                : Regex.IsMatch(head, @"\bpublic\b") || (scope.Kind == "class" && !scope.Sealed && Regex.IsMatch(head, @"\bprotected\b")));
            // the declaration part: before a body, an expression body or an initializer outside the parentheses
            string decl = DeclarationPart(head);
            Match ev = Event.Match(head);
            Match op = Operator.Match(decl);
            Match conv = Conversion.Match(decl);
            if (ev.Success)
            {
                e.Kind = "event";
                e.Name = ev.Groups["name"].Value;
                return e;
            }
            int paren = -1;
            if (op.Success)
            {
                e.Kind = "operator";
                e.Name = OperatorName(op.Groups["op"].Value);
                paren = op.Index + op.Length - 1;
            }
            else if (conv.Success)
            {
                e.Kind = "operator";
                e.Name = conv.Groups["conv"].Value == "implicit" ? "op_Implicit" : "op_Explicit";
                paren = conv.Index + conv.Length - 1;
            }
            else
            {
                Match call = Call.Match(decl);
                if (call.Success)
                {
                    e.Name = call.Groups["name"].Value;
                    string before = decl.Substring(0, call.Index);
                    bool onlyModifiers = Regex.Replace(before, @"\b(?:public|internal|private|protected|static|extern|unsafe)\b", "").Trim().Length == 0;
                    e.Kind = e.Name == scope.Name && onlyModifiers ? "ctor" : "method";
                    paren = call.Index + call.Length - 1;
                }
            }
            if (paren >= 0)
            {
                int close = Close(decl, paren);
                if (close < 0)
                {
                    e.Problems.Add("a parameter list that does not end on its line");
                    return e;
                }
                Params(decl.Substring(paren + 1, close - paren - 1), e);
                return e;
            }
            Match last = Regex.Match(decl, @"(?<name>[A-Za-z_]\w*)\s*$");
            if (!last.Success)
                return null;
            e.Name = last.Groups["name"].Value;
            string rest = head.Substring(Math.Min(head.Length, head.IndexOf(e.Name, StringComparison.Ordinal) + e.Name.Length)).TrimStart();
            e.Kind = rest.Length == 0 || rest.StartsWith("{", StringComparison.Ordinal) || rest.StartsWith("=>", StringComparison.Ordinal) ? "property" : "field";
            return e;
        }

        static string DeclarationPart(string head)
        {
            int d = 0;
            for (int i = 0; i < head.Length; i++)
            {
                char ch = head[i];
                if (ch == '(' || ch == '[' || ch == '<')
                    d++;
                else if (ch == ')' || ch == ']' || (ch == '>' && (i == 0 || head[i - 1] != '=')))
                    d--;
                else if (d == 0 && (ch == '{' || ch == ';' || (ch == '=' && (i + 1 >= head.Length || head[i + 1] != '='))) && !(ch == '=' && i > 0 && "=!<>".IndexOf(head[i - 1]) >= 0))
                    return head.Substring(0, i).Trim();
            }
            return head.Trim();
        }

        static int Close(string s, int open)
        {
            int d = 0;
            for (int i = open; i < s.Length; i++)
            {
                if (s[i] == '(')
                    d++;
                else if (s[i] == ')' && --d == 0)
                    return i;
            }
            return -1;
        }

        static void Params(string list, Entry e)
        {
            var names = new List<string>();
            var types = new List<string>();
            int d = 0, start = 0;
            var parts = new List<string>();
            for (int i = 0; i <= list.Length; i++)
            {
                if (i == list.Length || (list[i] == ',' && d == 0))
                {
                    parts.Add(list.Substring(start, i - start));
                    start = i + 1;
                }
                else if (list[i] == '<' || list[i] == '(' || list[i] == '[')
                    d++;
                else if (list[i] == '>' || list[i] == ')' || list[i] == ']')
                    d--;
            }
            foreach (string p0 in parts.Where(p => p.Trim().Length > 0))
            {
                string p = Attribute.Replace(p0.Trim(), "");
                int eq = p.IndexOf('=');
                if (eq >= 0)
                    p = p.Substring(0, eq);
                p = Regex.Replace(p.Trim(), @"^(?:(?:this|in|ref|out|params)\s+)+", "");
                int sp = p.LastIndexOf(' ');
                names.Add(p.Substring(sp + 1));
                types.Add(sp < 0 ? "" : p.Substring(0, sp).Trim());
            }
            e.ParamNames = names.ToArray();
            e.ParamTypes = types.ToArray();
        }

        static string OperatorName(string op)
        {
            switch (op)
            {
                case "==": return "op_Equality";
                case "!=": return "op_Inequality";
                case "<": return "op_LessThan";
                case ">": return "op_GreaterThan";
                case "<=": return "op_LessThanOrEqual";
                case ">=": return "op_GreaterThanOrEqual";
                case "+": return "op_Addition";
                case "-": return "op_Subtraction";
                case "*": return "op_Multiply";
                case "/": return "op_Division";
                default: return "op_" + op;
            }
        }

        // ---- reflection ↔ source

        internal static string ShortName(Type t) => t.Name.IndexOf('`') < 0 ? t.Name : t.Name.Substring(0, t.Name.IndexOf('`'));

        internal static List<Entry> Of(Type t) => All.Where(e => e.Type == ShortName(t) && e.Public).ToList();

        // the source declaration of a member — null when there is none, or more than one
        internal static Entry Find(Type t, MemberInfo m, out string problem)
        {
            problem = null;
            string name = m is ConstructorInfo ? ShortName(t) : m.Name;
            string kind = m is ConstructorInfo ? "ctor" : m is FieldInfo && t.IsEnum ? "value" : null;
            ParameterInfo[] ps = m is MethodBase mb ? mb.GetParameters() : null;
            List<Entry> named = Of(t).Where(e => e.Kind != "type" && e.Name == name && (kind == null || e.Kind == kind)).ToList();
            if (ps != null)
                named = named.Where(e => e.ParamNames.SequenceEqual(ps.Select(p => p.Name))).ToList();
            if (named.Count > 1 && ps != null)
                named = named.Where(e => e.ParamTypes.Select(Last).SequenceEqual(ps.Select(p => DocSources.Name(p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType)).Select(Last))).ToList();
            if (named.Count == 1)
                return named[0];
            problem = named.Count == 0 ? "no source declaration" : "more than one source declaration";
            return null;
        }

        static string Last(string typeText) => typeText.Substring(typeText.LastIndexOf('.') + 1).Replace(" ", "");

        // ---- the comment's form and its rendering

        static readonly HashSet<string> Inline = new HashSet<string> { "c", "see", "paramref" };

        // the problems of a comment: one <summary> and nothing else · closed · the three inline tags · a sentence
        internal static List<string> Form(Entry e)
        {
            var problems = new List<string>(e.Problems);
            if (e.Xml == null)
                return problems;
            var x = new XmlDocument();
            try
            {
                x.LoadXml("<doc>" + e.Xml + "</doc>");
            }
            catch (XmlException ex)
            {
                problems.Add("badly formed: " + ex.Message);
                return problems;
            }
            XmlNodeList top = x.DocumentElement.ChildNodes;
            List<XmlNode> elements = top.Cast<XmlNode>().Where(n => n.NodeType == XmlNodeType.Element).ToList();
            if (top.Cast<XmlNode>().Any(n => n.NodeType == XmlNodeType.Text && n.Value.Trim().Length > 0))
                problems.Add("text outside <summary>");
            if (elements.Count != 1 || elements[0].Name != "summary")
                problems.Add("not one <summary>: " + string.Join(", ", elements.Select(n => "<" + n.Name + ">")));
            foreach (XmlNode s in elements.Where(n => n.Name == "summary"))
            {
                if (s.InnerText.Trim().Length == 0)
                    problems.Add("an empty <summary>");
                foreach (XmlNode n in s.SelectNodes(".//*"))
                {
                    if (!Inline.Contains(n.Name))
                        problems.Add("<" + n.Name + "> in a summary");
                    else if (n.Name == "see" && n.Attributes["cref"] == null)
                        problems.Add("<see> without cref");
                    else if (n.Name == "paramref" && !e.ParamNames.Contains(n.Attributes["name"]?.Value))
                        problems.Add("<paramref name=\"" + n.Attributes["name"]?.Value + "\"> is not a parameter");
                }
            }
            return problems;
        }

        internal static IEnumerable<string> Crefs(Entry e)
        {
            if (e.Xml == null)
                yield break;
            foreach (Match m in Regex.Matches(e.Xml, "cref=\"(?<c>[^\"]*)\""))
                yield return m.Groups["c"].Value;
        }

        // the summary as one Markdown table cell
        internal static string Render(Entry e)
        {
            if (e?.Xml == null)
                return "";
            Match m = Regex.Match(e.Xml, "<summary>(?<s>.*?)</summary>", RegexOptions.Singleline);
            if (!m.Success)
                return "";
            string s = m.Groups["s"].Value;
            s = Regex.Replace(s, "<c>(?<x>.*?)</c>", "`${x}`");
            s = Regex.Replace(s, "<see cref=\"(?<x>[^\"]*)\"\\s*/>", "`${x}`");
            s = Regex.Replace(s, "<paramref name=\"(?<x>[^\"]*)\"\\s*/>", "`${x}`");
            s = Regex.Replace(s, "\\s+", " ").Trim();
            return s.Replace("|", "\\|").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
        }
    }
}
