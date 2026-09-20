using CheatEngine.Client.Core.Infrastructure;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

public sealed class CoreClientPolicyFailureCoverageTests
{
	[Fact]
	public void TryAuthorizeTableFileRejectsAnEmptyPathBeforeItCanBeNormalized()
	{
		CoreClientPolicy policy = CoreClientPolicy.SafeDefaults;

		bool allowed = policy.TryAuthorizeTableFile("  ", false, out string reason);

		Assert.False(allowed);
		Assert.Equal("The table path must be non-empty and fully qualified.", reason);
	}

	[Fact]
	public void TryAuthorizeTableFileRejectsAPathThatCannotBeNormalizedSafely()
	{
		CoreClientPolicy policy = CoreClientPolicy.SafeDefaults;
		string pathContainingANullCharacter = new('\0', 1);

		bool allowed = policy.TryAuthorizeTableFile(pathContainingANullCharacter, false, out string reason);

		Assert.False(allowed);
		Assert.Equal("The table path cannot be normalized safely.", reason);
	}

	[Fact]
	public void TryAuthorizeTableFileRejectsAConfiguredRootThatNoLongerExists()
	{
		string root = Path.Combine(Path.GetTempPath(), "CheatEngine.Client.Tests", Guid.NewGuid().ToString("N"));
		CoreClientPolicy policy = new([root], false);
		string candidate = Path.Combine(root, "profile.ct");

		bool allowed = policy.TryAuthorizeTableFile(candidate, false, out string reason);

		Assert.False(allowed);
		Assert.Equal("The configured table root does not currently exist and cannot be verified.", reason);
	}

	[Fact]
	public void TryAuthorizeTableFileRejectsAnExportWhoseParentDirectoryDoesNotExist()
	{
		using TemporaryDirectory directory = new();
		CoreClientPolicy policy = new([directory.Path], false);
		string candidate = Path.Combine(directory.Path, "missing", "profile.ct");

		bool allowed = policy.TryAuthorizeTableFile(candidate, false, out string reason);

		Assert.False(allowed);
		Assert.Equal("A trusted table export destination must have an existing parent directory.", reason);
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
