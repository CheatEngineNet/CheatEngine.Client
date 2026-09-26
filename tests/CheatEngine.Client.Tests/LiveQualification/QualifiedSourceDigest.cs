using System.Security.Cryptography;
using System.Text;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>One input of the digest: its repository-relative path and the SHA-256 of its normalized content.</summary>
/// <param name="Path">The forward-slash path from the repository root.</param>
/// <param name="Sha256">The lower-case SHA-256 of the content with CRLF normalized to LF (binary files as is).</param>
internal readonly record struct DigestInput(string Path, string Sha256);

/// <summary>
///     The SHA-256 that binds qualification evidence to the shipping sources: every tracked file of <c>libs/</c>,
///     <c>src/</c>, <c>source-generators/</c> and <c>templates/</c> (their lock files included), plus
///     <c>Directory.Build.*</c>, <c>Directory.Packages.props</c>, <c>eng/*.props</c> and <c>global.json</c>, but no
///     Markdown (<c>*.md</c>, <c>AnalyzerReleases.*.md</c> included), no <c>PublicAPI.*.txt</c> and no
///     <c>HostQualificationEvidence.cs</c>, which records the evidence itself. Text is normalized from CRLF to LF, so a
///     checkout's line endings never change the digest. The digest is the SHA-256 of a <c>sha256sum</c>-style manifest,
///     one <c>&lt;sha256&gt;  &lt;path&gt;\n</c> line per input in ordinal path order. Files are enumerated like
///     <c>git ls-files</c> sees the tree: build output and tool folders (<c>bin</c>, <c>obj</c>, <c>artifacts</c>,
///     <c>.git</c>, <c>.idea</c>, <c>.vs</c>, <c>TestResults</c>) and the other <c>.gitignore</c> patterns never count.
///     Compiled into CheatEngine.Client.Tests (the live runner records it) and CheatEngine.Client.Repository.Tests (the
///     evidence tests recompute it).
/// </summary>
internal static class QualifiedSourceDigest
{
	/// <summary>The folders whose every tracked file is an input.</summary>
	internal static readonly string[] IncludedDirectories = ["libs/", "src/", "source-generators/", "templates/"];

	/// <summary>The repository-root files that are inputs.</summary>
	internal static readonly string[] IncludedRootFiles =
		["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json"];

	/// <summary>The folder whose <c>*.props</c> files, directly inside it, are inputs.</summary>
	internal const string IncludedPropsDirectory = "eng/";

	/// <summary>The file name patterns that are never inputs.</summary>
	internal static readonly string[] ExcludedFilePatterns = ["*.md", "PublicAPI.*.txt", "AnalyzerReleases.*.md", "HostQualificationEvidence.cs"];

	/// <summary>Path segments that <c>.gitignore</c> excludes: build output and tool state.</summary>
	internal static readonly string[] IgnoredSegments = ["bin", "obj", "artifacts", ".git", ".idea", ".vs", "TestResults"];

	/// <summary>File extensions that <c>.gitignore</c> excludes.</summary>
	internal static readonly string[] IgnoredExtensions = [".binlog", ".user", ".suo"];

	/// <summary>The extensions <c>.gitattributes</c> marks binary: hashed as is, never normalized.</summary>
	internal static readonly string[] BinaryExtensions = [".dll", ".exe", ".nupkg", ".snupkg", ".png", ".ico", ".snk"];

	/// <summary>Whether a repository-relative, forward-slash path is an input of the digest.</summary>
	internal static bool IsInput(string relativePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
		string[] segments = relativePath.Split('/');
		string name = segments[^1];
		if (segments.Any(static segment => IgnoredSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)) ||
			IgnoredExtensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) ||
			IsExcludedName(name))
		{
			return false;
		}

		return IncludedDirectories.Any(directory => relativePath.StartsWith(directory, StringComparison.Ordinal)) ||
			   IncludedRootFiles.Contains(relativePath, StringComparer.Ordinal) ||
			   (segments.Length == 2 && relativePath.StartsWith(IncludedPropsDirectory, StringComparison.Ordinal) &&
				name.EndsWith(".props", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Every input below <paramref name="root" />, as forward-slash relative paths in ordinal order.</summary>
	internal static IReadOnlyList<string> EnumerateInputs(string root)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(root);
		List<string> inputs = [];
		foreach (string directory in IncludedDirectories.Append(IncludedPropsDirectory))
		{
			string folder = Path.Combine(root, directory);
			if (!Directory.Exists(folder))
			{
				continue;
			}

			inputs.AddRange(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
				.Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
				.Where(IsInput));
		}

		inputs.AddRange(IncludedRootFiles.Where(file => File.Exists(Path.Combine(root, file))));
		inputs.Sort(StringComparer.Ordinal);
		return inputs;
	}

	/// <summary>Every input with the SHA-256 of its normalized content.</summary>
	internal static IReadOnlyList<DigestInput> Describe(string root)
	{
		return EnumerateInputs(root)
			.Select(path => new DigestInput(path, Convert.ToHexStringLower(SHA256.HashData(Normalize(path, File.ReadAllBytes(Path.Combine(root, path)))))))
			.ToArray();
	}

	/// <summary>The lower-case SHA-256 of the manifest of <see cref="Describe" />.</summary>
	internal static string Compute(string root)
	{
		StringBuilder manifest = new();
		foreach (DigestInput input in Describe(root))
		{
			manifest.Append(input.Sha256).Append("  ").Append(input.Path).Append('\n');
		}

		return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.ToString())));
	}

	/// <summary>The content with every CRLF turned into LF, unless the path is binary.</summary>
	internal static byte[] Normalize(string path, byte[] content)
	{
		ArgumentNullException.ThrowIfNull(path);
		ArgumentNullException.ThrowIfNull(content);
		if (BinaryExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
		{
			return content;
		}

		using MemoryStream normalized = new(content.Length);
		for (int index = 0; index < content.Length; index++)
		{
			if (content[index] == (byte) '\r' && index + 1 < content.Length && content[index + 1] == (byte) '\n')
			{
				continue;
			}

			normalized.WriteByte(content[index]);
		}

		return normalized.ToArray();
	}

	private static bool IsExcludedName(string name)
	{
		return name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
			   (name.StartsWith("PublicAPI.", StringComparison.Ordinal) && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) ||
			   (name.StartsWith("AnalyzerReleases.", StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) ||
			   string.Equals(name, "HostQualificationEvidence.cs", StringComparison.Ordinal);
	}
}
