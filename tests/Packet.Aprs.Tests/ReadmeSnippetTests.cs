using System.Text.RegularExpressions;

namespace Packet.Aprs.Tests;

/// <summary>
/// Stops the README drifting from <see cref="ReadmeExamples"/>, the tested copy of its code. Every
/// <c>```csharp</c> block in the package README is read at test time; each code line must appear
/// (whitespace and trailing comments ignored) in ReadmeExamples.cs, and for a
/// <c>Console.WriteLine(x)</c> line, <c>x</c> must appear there, since the tests assert instead of
/// printing. A renamed property that only one side knows about fails here.
/// </summary>
public class ReadmeSnippetTests
{
    private static readonly string[] Readmes =
    [
        Path.Combine("src", "Packet.Aprs", "README.md"),
        "README.md",
    ];

    public static TheoryData<string, int, string> Lines()
    {
        var data = new TheoryData<string, int, string>();
        foreach (string readme in Readmes)
        {
            string[] all = File.ReadAllLines(TestPaths.InRepo(readme));
            bool inBlock = false;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].StartsWith("```csharp", StringComparison.Ordinal))
                {
                    inBlock = true;
                }
                else if (all[i].StartsWith("```", StringComparison.Ordinal))
                {
                    inBlock = false;
                }
                else if (inBlock && Code(all[i]) is { Length: > 1 } code)
                {
                    data.Add(readme, i + 1, all[i]);
                }
            }
        }

        return data;
    }

    [Fact]
    public void The_readmes_have_csharp_examples_to_check()
        => Lines().Count.Should().BeGreaterThan(50);

    [Theory]
    [MemberData(nameof(Lines))]
    public void Every_readme_code_line_is_in_the_tested_examples(string readme, int line, string text)
    {
        string tested = Normalise(File.ReadAllText(TestPaths.InProject("ReadmeExamples.cs")));
        string code = Code(text);

        Match write = Regex.Match(code, @"^Console\.WriteLine\((.*)\);?$");
        string needle = Normalise(write.Success ? write.Groups[1].Value : code);

        tested.Should().Contain(needle, $"{readme} line {line} (`{text.Trim()}`) must match the code in ReadmeExamples.cs");
    }

    /// <summary>The line without its trailing <c>// comment</c> (outside a string), or "" if it is only a comment or a brace.</summary>
    private static string Code(string line)
    {
        bool inString = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '\\')
            {
                i++;
            }
            else if (line[i] == '"')
            {
                inString = !inString;
            }
            else if (!inString && line[i] == '/' && line[i + 1] == '/')
            {
                line = line[..i];
                break;
            }
        }

        line = line.Trim();

        // Needs a live KISS TNC, so it cannot run in a test; the line before it is the tested part.
        if (line.StartsWith("await kiss.", StringComparison.Ordinal))
        {
            return "";
        }

        return line is "{" or "}" or "};" or "break;" || line.StartsWith("using ", StringComparison.Ordinal) ? "" : line;
    }

    private static string Normalise(string s) => Regex.Replace(s, @"\s+", "");
}
