using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.SourcePolicy;

/// <summary>
///     Client failure kinds must not depend on error text (A07-22, A24-24): a Cheat Engine or Lua message changes with the
///     host language and version, so Client libraries classify by type and status only.
/// </summary>
public sealed partial class ErrorTextClassificationPolicyTests
{
	[Fact]
	public void ClientNeverClassifiesFailuresByErrorText()
	{
		List<string> violations = [];
		int inspectedFiles = 0;
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("*.cs")
					 .Where(static path => path.StartsWith("libs/", StringComparison.Ordinal)))
		{
			inspectedFiles++;
			string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, file));
			for (int index = 0; index < lines.Length; index++)
			{
				if (ClassifiesByMessage(lines[index]))
				{
					violations.Add($"{file}:{index + 1}: {lines[index].Trim()}");
				}
			}
		}

		Assert.True(inspectedFiles > 0, "No Client library source was inspected.");
		Assert.True(violations.Count == 0,
			"Classify failures by exception type or SDK status, never by message text:" + Environment.NewLine +
			string.Join(Environment.NewLine, violations));
	}

	[Theory]
	[InlineData("if (exception.Message.Contains(\"not found\")) {", true)]
	[InlineData("bool missing = error.Message.StartsWith(\"attempt to\", StringComparison.Ordinal);", true)]
	[InlineData("int at = failure.Message.IndexOf(\"nil\");", true)]
	[InlineData("if (luaError.Message == \"timeout\")", true)]
	[InlineData("_ = e.Message.EndsWith(\".\") || e.Message.Equals(\"x\");", true)]
	[InlineData("luaMessage = LuaError.FromStack(state, status).Message;", false)]
	[InlineData("failure = new CheatEngineFailure(kind, operation, exception.Message, exception);", false)]
	[InlineData("string text = $\"{primary.Message} The cleanup also failed.\";", false)]
	public void MessageClassificationDetectorFindsComparisonsButNotMessageBuilding(string line, bool expected)
	{
		Assert.Equal(expected, ClassifiesByMessage(line));
	}

	private static bool ClassifiesByMessage(string line)
	{
		return MessageComparison().IsMatch(line);
	}

	[GeneratedRegex(@"\.Message\s*(\.\s*(Contains|StartsWith|EndsWith|IndexOf|Equals)\s*\(|[!=]=)")]
	private static partial Regex MessageComparison();
}
