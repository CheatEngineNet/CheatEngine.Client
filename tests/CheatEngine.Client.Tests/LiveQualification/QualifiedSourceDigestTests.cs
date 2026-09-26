using System.Text;

using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The shipping source digest: a CRLF checkout and an LF checkout hash the same, the input set is exactly the one the
///     qualification plan names, every content or path change moves the digest while excluded files never do, and the
///     enumeration agrees with what git tracks and ignores in this repository.
/// </summary>
public sealed class QualifiedSourceDigestTests : IDisposable
{
	private readonly TemporaryDirectory _temporary = new("QualifiedSourceDigest");

	public void Dispose()
	{
		_temporary.Dispose();
	}

	[Fact]
	public void TheInputAndExclusionListsAreThoseOfTheQualificationPlan()
	{
		Assert.Equal(["libs/", "src/", "source-generators/", "templates/"], QualifiedSourceDigest.IncludedDirectories);
		Assert.Equal(["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json"],
			QualifiedSourceDigest.IncludedRootFiles);
		Assert.Equal("eng/", QualifiedSourceDigest.IncludedPropsDirectory);
		Assert.Equal(["*.md", "PublicAPI.*.txt", "AnalyzerReleases.*.md", "HostQualificationEvidence.cs"],
			QualifiedSourceDigest.ExcludedFilePatterns);
	}

	[Fact]
	public void TheInputSetIsExact()
	{
		string root = Tree("exact", "\r\n");

		Assert.Equal(
		[
			"Directory.Build.props",
			"Directory.Build.targets",
			"Directory.Packages.props",
			"eng/CheatEngineSdk.props",
			"eng/Shipping.props",
			"global.json",
			"libs/Core/Core.csproj",
			"libs/Core/Domains/Runtime.cs",
			"libs/Core/packages.lock.json",
			"source-generators/Lua/Emitter.cs",
			"src/Client/Client.csproj",
			"templates/Templates/content/Plugin/.template.config/template.json",
			"templates/Templates/content/Plugin/Plugin.cs",
			"templates/Templates/packages.lock.json"
		], QualifiedSourceDigest.EnumerateInputs(root));
	}

	[Theory]
	[InlineData("libs/Core/README.md", false)]
	[InlineData("libs/Core/PublicAPI.Shipped.txt", false)]
	[InlineData("libs/Core/PublicAPI.Unshipped.txt", false)]
	[InlineData("source-generators/Lua/AnalyzerReleases.Unshipped.md", false)]
	[InlineData("libs/CheatEngine.Client.Core/Qualification/HostQualificationEvidence.cs", false)]
	[InlineData("libs/Core/bin/Debug/Core.dll", false)]
	[InlineData("libs/Core/obj/project.assets.json", false)]
	[InlineData("libs/Core/build.binlog", false)]
	[InlineData("libs/Core/Core.csproj.user", false)]
	[InlineData("eng/nested/Other.props", false)]
	[InlineData("eng/Build.targets", false)]
	[InlineData("tests/Core.Tests/CoreTests.cs", false)]
	[InlineData("README.md", false)]
	[InlineData("nuget.config", false)]
	[InlineData(".editorconfig", false)]
	[InlineData("libs/Core/PublicAPI.txt.cs", true)]
	[InlineData("libs/Core/Resources/icon.png", true)]
	[InlineData("eng/Tests.props", true)]
	public void EachPathIsClassifiedByThePlan(string path, bool input)
	{
		Assert.Equal(input, QualifiedSourceDigest.IsInput(path));
	}

	[Fact]
	public void TheEmbeddedEvidenceFileExistsAndIsNoInputWhileTheGateIs()
	{
		const string Evidence = "libs/CheatEngine.Client.Core/Qualification/HostQualificationEvidence.cs";
		IReadOnlyList<string> inputs = QualifiedSourceDigest.EnumerateInputs(RepositoryLayout.Root);

		Assert.True(File.Exists(RepositoryLayout.Combine(Evidence)), $"'{Evidence}' is the file the digest excludes; it must exist.");
		Assert.False(QualifiedSourceDigest.IsInput(Evidence));
		Assert.DoesNotContain(Evidence, inputs);
		Assert.Contains("libs/CheatEngine.Client.Core/Qualification/HostQualificationGate.cs", inputs);
	}

	[Fact]
	public void CrLfAndLfCheckoutsHaveTheSameDigest()
	{
		string crlf = Tree("crlf", "\r\n");
		string lf = Tree("lf", "\n");

		Assert.NotEqual(File.ReadAllBytes(Path.Combine(crlf, "global.json")), File.ReadAllBytes(Path.Combine(lf, "global.json")));
		Assert.Equal(QualifiedSourceDigest.Describe(lf), QualifiedSourceDigest.Describe(crlf));
		Assert.Equal(QualifiedSourceDigest.Compute(lf), QualifiedSourceDigest.Compute(crlf));
		Assert.Matches("^[0-9a-f]{64}$", QualifiedSourceDigest.Compute(lf));
	}

	[Fact]
	public void ContentAndPathChangesMoveTheDigestButExcludedFilesNeverDo()
	{
		string root = Tree("changes", "\r\n");
		string original = QualifiedSourceDigest.Compute(root);

		File.WriteAllText(Path.Combine(root, "libs/Core/README.md"), "rewritten");
		File.WriteAllText(Path.Combine(root, "libs/Core/PublicAPI.Unshipped.txt"), "#nullable enable\r\nNew.Member\r\n");
		File.WriteAllText(Path.Combine(root, "libs/CheatEngine.Client.Core/Qualification/HostQualificationEvidence.cs"), "// evidence");
		File.WriteAllText(Path.Combine(root, "tests/Core.Tests/CoreTests.cs"), "// changed test");
		Assert.Equal(original, QualifiedSourceDigest.Compute(root));

		File.WriteAllText(Path.Combine(root, "libs/Core/Domains/Runtime.cs"), "class Runtime { }\r\n// changed\r\n");
		string changed = QualifiedSourceDigest.Compute(root);
		File.Move(Path.Combine(root, "src/Client/Client.csproj"), Path.Combine(root, "src/Client/Renamed.csproj"));
		string renamed = QualifiedSourceDigest.Compute(root);
		File.WriteAllText(Path.Combine(root, "libs/Core/packages.lock.json"), "{ \"version\": 2, \"changed\": true }");

		Assert.Equal(4, new[] { original, changed, renamed, QualifiedSourceDigest.Compute(root) }.Distinct(StringComparer.Ordinal).Count());
	}

	[Fact]
	public void BinaryFilesAreHashedAsTheyAre()
	{
		byte[] content = [0x89, (byte) 'P', (byte) 'N', (byte) 'G', (byte) '\r', (byte) '\n', 0x1A, (byte) '\n'];

		Assert.Equal(content, QualifiedSourceDigest.Normalize("libs/Core/icon.png", content));
		Assert.Equal([0x89, (byte) 'P', (byte) 'N', (byte) 'G', (byte) '\n', 0x1A, (byte) '\n'],
			QualifiedSourceDigest.Normalize("libs/Core/data.bin", content));
		Assert.Equal("a\rb\nc\n"u8.ToArray(), QualifiedSourceDigest.Normalize("libs/Core/Text.cs", "a\rb\r\nc\n"u8.ToArray()));
	}

	[Fact]
	public async Task TheRepositoryEnumerationAgreesWithGitAsync()
	{
		string root = RepositoryLayout.Root;
		string[] scope =
		[
			.. QualifiedSourceDigest.IncludedDirectories.Select(static directory => directory.TrimEnd('/')),
			QualifiedSourceDigest.IncludedPropsDirectory.TrimEnd('/'),
			.. QualifiedSourceDigest.IncludedRootFiles
		];
		HashSet<string> inputs = new(QualifiedSourceDigest.EnumerateInputs(root), StringComparer.Ordinal);

		string[] tracked = await GitFilesAsync(root, ["ls-files", "-z", "--", .. scope]);
		string[] ignored = await GitFilesAsync(root, ["ls-files", "-z", "--others", "--ignored", "--exclude-standard", "--", .. scope]);

		string[] missing = [.. tracked.Where(QualifiedSourceDigest.IsInput).Where(path => !inputs.Contains(path))];
		string[] included = [.. ignored.Where(inputs.Contains)];
		Assert.True(tracked.Length > 100, $"git ls-files listed only {tracked.Length} files below the digest scope.");
		Assert.True(missing.Length == 0, $"Tracked inputs the digest does not enumerate: {string.Join(", ", missing)}");
		Assert.True(included.Length == 0, $"Files git ignores that the digest would hash: {string.Join(", ", included)}");
		Assert.Contains("eng/CheatEngineSdk.props", inputs);
		Assert.Contains("templates/CheatEngine.Client.Templates/packages.lock.json", inputs);
		Assert.DoesNotContain(inputs, static path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
	}

	private static async Task<string[]> GitFilesAsync(string root, string[] arguments)
	{
		DotNetProcessResult git = await DotNetProcess.RunToolAsync("git", root, new Dictionary<string, string>(StringComparer.Ordinal),
			arguments);
		Assert.True(git.ExitCode == 0, git.ToString());
		return git.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
	}

	/// <summary>A fake repository whose text files use <paramref name="newLine" />.</summary>
	private string Tree(string name, string newLine)
	{
		string root = _temporary.CreateDirectory(name);
		string[] files =
		[
			"Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "README.md",
			"CHANGELOG.md", "nuget.config", ".editorconfig", "eng/CheatEngineSdk.props", "eng/Shipping.props",
			"eng/nested/Other.props", "eng/Build.targets", "libs/Core/Core.csproj", "libs/Core/Domains/Runtime.cs",
			"libs/Core/packages.lock.json", "libs/Core/README.md", "libs/Core/PublicAPI.Shipped.txt",
			"libs/Core/PublicAPI.Unshipped.txt", "libs/Core/bin/Debug/Core.dll", "libs/Core/obj/project.assets.json",
			"libs/Core/build.binlog", "libs/CheatEngine.Client.Core/Qualification/HostQualificationEvidence.cs",
			"source-generators/Lua/Emitter.cs", "source-generators/Lua/AnalyzerReleases.Shipped.md", "src/Client/Client.csproj",
			"templates/Templates/packages.lock.json", "templates/Templates/content/Plugin/Plugin.cs",
			"templates/Templates/content/Plugin/.template.config/template.json", "tests/Core.Tests/CoreTests.cs",
			"artifacts/bin/Core/Core.dll"
		];
		foreach (string file in files)
		{
			string path = Path.Combine(root, file);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, $"// {file}{newLine}line two{newLine}", new UTF8Encoding(false));
		}

		return root;
	}
}
