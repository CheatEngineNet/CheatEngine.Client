using System.Text.RegularExpressions;

namespace CheatEngine.Client.LivePlugin.Qualification.Tests;

/// <summary>
///     Lightweight source-scan proof for the two safety properties the harness project and its README assert but that
///     no compiled test previously checked: it reaches Cheat Engine only through the public Client API (ADR-01), and
///     every Lua function the README marks mutating runs through <c>QualificationScenarios.RunMutating</c> before it can
///     touch the live target. The x64 plugin project cannot be referenced from this AnyCPU test module (see the project
///     README), so these tests read its own source files as text instead of compiling against it.
/// </summary>
public sealed class QualificationPluginSourceComplianceTests
{
	private static readonly string[] ForbiddenTokens =
	[
		"LuaState", "LuaFrame", "AcquireOperation", "LuaRuntime", "[LuaGlobal", "DllImport", "LibraryImport",
		"CheatEngine.SDK.Lua"
	];

	[Fact]
	public void QualificationPluginUsesOnlyTheClientApiForCheatEngineAccess()
	{
		List<string> violations = [];
		foreach (string file in PluginSourceFiles())
		{
			string text = File.ReadAllText(file);
			foreach (string token in ForbiddenTokens)
			{
				if (text.Contains(token, StringComparison.Ordinal))
				{
					violations.Add($"{Path.GetFileName(file)} contains '{token}'");
				}
			}
		}

		Assert.True(violations.Count == 0,
			"The qualification plugin must reach Cheat Engine only through the Client API (ADR-01); none of its own " +
			$"files may touch the SDK's native Lua stack or interop directly:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
	}

	[Fact]
	public void MutatingLuaFunctionsRouteThroughTheAuthorizationGate()
	{
		Dictionary<string, bool> mutatingByLuaName = ParseMutatingColumn(ReadmeText());
		Dictionary<string, string> targetByLuaName = ParseLuaFunctionTargets(LuaFunctionsText());
		string scenariosText = ScenariosText();

		Assert.True(mutatingByLuaName.Count >= 10,
			"The README 'Lua functions' table did not parse as expected (found " +
			$"{mutatingByLuaName.Count} rows); check the table's markdown shape against the parser in this test.");

		List<string> problems = [];
		foreach (KeyValuePair<string, bool> row in mutatingByLuaName)
		{
			if (!targetByLuaName.TryGetValue(row.Key, out string? target))
			{
				problems.Add(
					$"{row.Key}(): the README lists it, but no [LuaFunction(\"cheatengine_client_qualification_{row.Key}\")] method delegating to a QualificationScenarios method was found.");
				continue;
			}

			if (!row.Value)
			{
				continue;
			}

			string? body = MethodBody(scenariosText, target);
			if (body is null)
			{
				problems.Add($"{row.Key}() -> QualificationScenarios.{target}: the method was not found.");
			}
			else if (!body.Contains("RunMutating(", StringComparison.Ordinal))
			{
				problems.Add($"{row.Key}() -> QualificationScenarios.{target}: its body never calls RunMutating.");
			}
		}

		Assert.True(problems.Count == 0,
			"Every Lua function the README's 'Lua functions' table marks mutating must run through " +
			$"QualificationScenarios.RunMutating before it can touch the live target:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
	}

	private static IEnumerable<string> PluginSourceFiles()
	{
		string root = PluginProjectDirectory();
		foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
		{
			string relative = Path.GetRelativePath(root, file);
			if (relative.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
			{
				continue;
			}

			yield return file;
		}
	}

	private static Dictionary<string, bool> ParseMutatingColumn(string readme)
	{
		Dictionary<string, bool> mutatingByName = [];
		foreach (Match match in Regex.Matches(readme,
			@"^\|\s*`(?<name>[A-Za-z_][A-Za-z0-9_]*)\([^`]*\)`\s*\|\s*(?<mutating>[^|]+)\|", RegexOptions.Multiline))
		{
			string mutatingText = match.Groups["mutating"].Value.Trim();
			mutatingByName[match.Groups["name"].Value] = !mutatingText.Equals("no", StringComparison.OrdinalIgnoreCase);
		}

		return mutatingByName;
	}

	private static Dictionary<string, string> ParseLuaFunctionTargets(string luaFunctionsSource)
	{
		Dictionary<string, string> targetByLuaName = [];
		foreach (Match match in Regex.Matches(luaFunctionsSource,
			"\\[LuaFunction\\(\"cheatengine_client_qualification_(?<lua>[a-z0-9_]+)\"\\)\\][^{]*\\{\\s*return\\s+QualificationScenarios\\.(?<target>[A-Za-z0-9_]+)\\("))
		{
			targetByLuaName[match.Groups["lua"].Value] = match.Groups["target"].Value;
		}

		return targetByLuaName;
	}

	private static string? MethodBody(string scenariosSource, string methodName)
	{
		string startMarker = $"internal static string {methodName}(";
		int start = scenariosSource.IndexOf(startMarker, StringComparison.Ordinal);
		if (start < 0)
		{
			return null;
		}

		int next = scenariosSource.IndexOf("\n\tinternal static string ", start + startMarker.Length, StringComparison.Ordinal);
		int end = next >= 0 ? next : scenariosSource.Length;
		return scenariosSource[start..end];
	}

	private static string ReadmeText()
	{
		return File.ReadAllText(Path.Combine(PluginProjectDirectory(), "README.md"));
	}

	private static string LuaFunctionsText()
	{
		return File.ReadAllText(Path.Combine(PluginProjectDirectory(), "QualificationLuaFunctions.cs"));
	}

	/// <summary>Every file of the partial <c>QualificationScenarios</c> class, concatenated in ordinal order.</summary>
	private static string ScenariosText()
	{
		return string.Concat(Directory.EnumerateFiles(PluginProjectDirectory(), "QualificationScenarios*.cs")
			.Order(StringComparer.Ordinal)
			.Select(File.ReadAllText));
	}

	private static string PluginProjectDirectory()
	{
		return Path.Combine(RepositoryRootPath(), "tests", "CheatEngine.Client.LivePlugin.Qualification");
	}

	private static string RepositoryRootPath()
	{
		for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "CheatEngine.Client.slnx")))
			{
				return directory.FullName;
			}
		}

		throw new InvalidOperationException(
			$"'CheatEngine.Client.slnx' was not found above '{AppContext.BaseDirectory}': this test expects to run from the repository's artifacts directory.");
	}
}
