// =============================================================================
// CommentExtractor — Splits source code into stripped body + extracted comments
// =============================================================================
// Used by the code indexer to produce:
//   1. BodyHash — SHA-256 of code with comments stripped (structural dedup)
//   2. FullBodyHash — SHA-256 of full code including comments (content dedup)
//   3. DocText — extracted comment text for semantic embedding
//
// Supports: C-family (// /* */), Python (#, """), Shell (#), SQL (--),
//           XML doc (///), Razor (@* *@), XAML (<!-- -->), HTML (<!-- -->)
//
// Example:
//   var (stripped, docText) = CommentExtractor.Extract(code, "csharp");
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace TheWatch.Cli.Services;

public static class CommentExtractor
{
    /// <summary>
    /// Extract comments from source code. Returns (strippedCode, docText).
    /// strippedCode has all comments removed. docText has all comment text concatenated.
    /// </summary>
    public static (string StrippedCode, string DocText) Extract(string code, string language)
    {
        return language switch
        {
            "csharp" or "java" or "kotlin" or "dart" or "rust" or "go"
                or "typescript" or "javascript" or "cpp" or "c" or "swift"
                or "protobuf" => ExtractCStyle(code),
            "python" => ExtractPython(code),
            "ruby" => ExtractRuby(code),
            "sql" => ExtractSql(code),
            "powershell" => ExtractPowerShell(code),
            "shell" => ExtractShell(code),
            "razor" => ExtractRazorXml(code),
            "xaml" or "html" => ExtractRazorXml(code),
            _ => (code, "")
        };
    }

    // C-family: // single line, /* multi line */, /// XML doc
    private static (string, string) ExtractCStyle(string code)
    {
        var stripped = new StringBuilder(code.Length);
        var docs = new StringBuilder();
        var i = 0;
        var inString = false;
        char stringChar = '"';

        while (i < code.Length)
        {
            // Skip string literals
            if (!inString && (code[i] == '"' || code[i] == '\''))
            {
                stringChar = code[i];
                inString = true;
                stripped.Append(code[i++]);
                continue;
            }
            if (inString)
            {
                if (code[i] == '\\' && i + 1 < code.Length) { stripped.Append(code[i++]); stripped.Append(code[i++]); continue; }
                if (code[i] == stringChar) inString = false;
                stripped.Append(code[i++]);
                continue;
            }

            // /// XML doc comment or // single line
            if (i + 1 < code.Length && code[i] == '/' && code[i + 1] == '/')
            {
                var end = code.IndexOf('\n', i);
                if (end < 0) end = code.Length;
                var comment = code[(i + 2)..end].TrimStart('/').Trim();
                if (comment.Length > 0) docs.AppendLine(comment);
                stripped.Append('\n');
                i = end + 1;
                continue;
            }

            // /* block comment */
            if (i + 1 < code.Length && code[i] == '/' && code[i + 1] == '*')
            {
                var end = code.IndexOf("*/", i + 2);
                if (end < 0) end = code.Length - 2;
                var comment = code[(i + 2)..end];
                // Clean up block comment stars
                foreach (var line in comment.Split('\n'))
                {
                    var trimmed = line.Trim().TrimStart('*').Trim();
                    if (trimmed.Length > 0) docs.AppendLine(trimmed);
                }
                stripped.Append(' ');
                i = end + 2;
                continue;
            }

            stripped.Append(code[i++]);
        }

        return (stripped.ToString(), docs.ToString().Trim());
    }

    // Python: # single line, """ or ''' docstrings
    private static (string, string) ExtractPython(string code)
    {
        var stripped = new StringBuilder(code.Length);
        var docs = new StringBuilder();
        var i = 0;

        while (i < code.Length)
        {
            // Triple-quoted docstring
            if (i + 2 < code.Length && ((code[i] == '"' && code[i + 1] == '"' && code[i + 2] == '"') ||
                                         (code[i] == '\'' && code[i + 1] == '\'' && code[i + 2] == '\'')))
            {
                var quote = code[i..(i + 3)];
                var end = code.IndexOf(quote, i + 3);
                if (end < 0) end = code.Length - 3;
                var docstring = code[(i + 3)..end].Trim();
                if (docstring.Length > 0) docs.AppendLine(docstring);
                stripped.Append("\"\"\"\"\"\"");
                i = end + 3;
                continue;
            }

            // # comment
            if (code[i] == '#')
            {
                var end = code.IndexOf('\n', i);
                if (end < 0) end = code.Length;
                var comment = code[(i + 1)..end].Trim();
                if (comment.Length > 0) docs.AppendLine(comment);
                stripped.Append('\n');
                i = end + 1;
                continue;
            }

            stripped.Append(code[i++]);
        }

        return (stripped.ToString(), docs.ToString().Trim());
    }

    // Ruby: # single line, =begin/=end block
    private static (string, string) ExtractRuby(string code)
    {
        var stripped = new StringBuilder(code.Length);
        var docs = new StringBuilder();
        var lines = code.Split('\n');
        var inBlock = false;

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("=begin")) { inBlock = true; continue; }
            if (line.TrimStart().StartsWith("=end")) { inBlock = false; continue; }
            if (inBlock) { docs.AppendLine(line.Trim()); continue; }

            var hashIdx = line.IndexOf('#');
            if (hashIdx >= 0 && !IsInsideString(line, hashIdx))
            {
                stripped.AppendLine(line[..hashIdx]);
                var comment = line[(hashIdx + 1)..].Trim();
                if (comment.Length > 0) docs.AppendLine(comment);
            }
            else
            {
                stripped.AppendLine(line);
            }
        }

        return (stripped.ToString(), docs.ToString().Trim());
    }

    // SQL: -- single line, /* block */
    private static (string, string) ExtractSql(string code)
    {
        var stripped = new StringBuilder(code.Length);
        var docs = new StringBuilder();
        var i = 0;

        while (i < code.Length)
        {
            if (i + 1 < code.Length && code[i] == '-' && code[i + 1] == '-')
            {
                var end = code.IndexOf('\n', i);
                if (end < 0) end = code.Length;
                var comment = code[(i + 2)..end].Trim();
                if (comment.Length > 0) docs.AppendLine(comment);
                stripped.Append('\n');
                i = end + 1;
                continue;
            }
            if (i + 1 < code.Length && code[i] == '/' && code[i + 1] == '*')
            {
                var end = code.IndexOf("*/", i + 2);
                if (end < 0) end = code.Length - 2;
                var comment = code[(i + 2)..end].Trim();
                if (comment.Length > 0) docs.AppendLine(comment);
                i = end + 2;
                continue;
            }
            stripped.Append(code[i++]);
        }

        return (stripped.ToString(), docs.ToString().Trim());
    }

    // PowerShell: # single line, <# block #>
    private static (string, string) ExtractPowerShell(string code)
    {
        var stripped = new StringBuilder(code.Length);
        var docs = new StringBuilder();
        var i = 0;

        while (i < code.Length)
        {
            if (i + 1 < code.Length && code[i] == '<' && code[i + 1] == '#')
            {
                var end = code.IndexOf("#>", i + 2);
                if (end < 0) end = code.Length - 2;
                var comment = code[(i + 2)..end].Trim();
                if (comment.Length > 0) docs.AppendLine(comment);
                i = end + 2;
                continue;
            }
            if (code[i] == '#' && (i == 0 || code[i - 1] == '\n'))
            {
                var end = code.IndexOf('\n', i);
                if (end < 0) end = code.Length;
                var comment = code[(i + 1)..end].Trim();
                if (comment.Length > 0) docs.AppendLine(comment);
                stripped.Append('\n');
                i = end + 1;
                continue;
            }
            stripped.Append(code[i++]);
        }

        return (stripped.ToString(), docs.ToString().Trim());
    }

    // Shell: # single line
    private static (string, string) ExtractShell(string code)
    {
        var stripped = new StringBuilder(code.Length);
        var docs = new StringBuilder();

        foreach (var line in code.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#') && !trimmed.StartsWith("#!")) // Skip shebang
            {
                var comment = trimmed[1..].Trim();
                if (comment.Length > 0) docs.AppendLine(comment);
                stripped.AppendLine();
            }
            else
            {
                stripped.AppendLine(line);
            }
        }

        return (stripped.ToString(), docs.ToString().Trim());
    }

    // Razor/XAML/HTML: <!-- --> and @* *@
    private static (string, string) ExtractRazorXml(string code)
    {
        var docs = new StringBuilder();
        var stripped = code;

        // @* razor comments *@
        stripped = Regex.Replace(stripped, @"@\*.*?\*@", m =>
        {
            docs.AppendLine(m.Value[2..^2].Trim());
            return "";
        }, RegexOptions.Singleline);

        // <!-- XML/HTML comments -->
        stripped = Regex.Replace(stripped, @"<!--.*?-->", m =>
        {
            docs.AppendLine(m.Value[4..^3].Trim());
            return "";
        }, RegexOptions.Singleline);

        return (stripped, docs.ToString().Trim());
    }

    private static bool IsInsideString(string line, int pos)
    {
        var inSingle = false;
        var inDouble = false;
        for (var i = 0; i < pos; i++)
        {
            if (line[i] == '\'' && !inDouble) inSingle = !inSingle;
            if (line[i] == '"' && !inSingle) inDouble = !inDouble;
        }
        return inSingle || inDouble;
    }
}
