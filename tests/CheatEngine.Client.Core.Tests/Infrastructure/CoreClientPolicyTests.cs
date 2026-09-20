using CheatEngine.Client.Core.Infrastructure;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

public sealed class CoreClientPolicyTests
{
	[Fact]
	public void TryAuthorizeTableFileAllowsARegularExistingImportInsideItsConfiguredRoot()
	{
		using TemporaryDirectory directory = new();
		string source = Path.Combine(directory.Path, "profile.ct");
		File.WriteAllText(source, "<CheatTable />");
		CoreClientPolicy policy = new([directory.Path], false);

		bool allowed = policy.TryAuthorizeTableFile(source, true, out string reason);

		Assert.True(allowed);
		Assert.Equal(string.Empty, reason);
	}

	[Fact]
	public void TryAuthorizeTableFileRejectsAPathWithOnlyTheSameTextPrefixAsTheConfiguredRoot()
	{
		using TemporaryDirectory parent = new();
		string approved = Path.Combine(parent.Path, "tables");
		string similarlyNamed = Path.Combine(parent.Path, "tables-escape");
		Directory.CreateDirectory(approved);
		Directory.CreateDirectory(similarlyNamed);
		string candidate = Path.Combine(similarlyNamed, "profile.ct");
		File.WriteAllText(candidate, "<CheatTable />");
		CoreClientPolicy policy = new([approved], false);

		bool allowed = policy.TryAuthorizeTableFile(candidate, true, out string reason);

		Assert.False(allowed);
		Assert.Contains("outside", reason, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void TryAuthorizeTableFileRejectsMissingImportSourceButAllowsANewExportInAnExistingDirectory()
	{
		using TemporaryDirectory directory = new();
		string candidate = Path.Combine(directory.Path, "new-profile.ct");
		CoreClientPolicy policy = new([directory.Path], false);

		bool importAllowed = policy.TryAuthorizeTableFile(candidate, true, out string importReason);
		bool exportAllowed = policy.TryAuthorizeTableFile(candidate, false, out string exportReason);

		Assert.False(importAllowed);
		Assert.Contains("existing regular file", importReason, StringComparison.OrdinalIgnoreCase);
		Assert.True(exportAllowed);
		Assert.Equal(string.Empty, exportReason);
	}

	private sealed class TemporaryDirectory : IDisposable
	{
		internal TemporaryDirectory()
		{
			Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CheatEngine.Client.Tests",
				Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path);
		}

		internal string Path
		{
			get;
		}

		public void Dispose()
		{
			if (Directory.Exists(Path))
			{
				Directory.Delete(Path, true);
			}
		}
	}
}
