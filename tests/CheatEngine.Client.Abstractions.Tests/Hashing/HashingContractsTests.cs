using CheatEngine.Client.Hashing;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Abstractions.Tests.Hashing;

public sealed class HashingContractsTests
{
	[Fact]
	public void MemoryHashRequestPreservesTheBoundedRangeAndAlgorithm()
	{
		MemoryHashRequest request = new(0x500000, 64, TargetHashAlgorithm.Sha1);

		Assert.Equal((Address) 0x500000, request.Address);
		Assert.Equal(64, request.Length);
		Assert.Equal(TargetHashAlgorithm.Sha1, request.Algorithm);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void MemoryHashRequestRejectsANonPositiveLength(int length)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryHashRequest(0x500000, length));
	}

	[Fact]
	public void HashContractsRejectAnUndefinedAlgorithm()
	{
		TargetHashAlgorithm invalid = (TargetHashAlgorithm) 99;

		Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryHashRequest(0x500000, 1, invalid));
		Assert.Throws<ArgumentOutOfRangeException>(() => new FileHashRequest(
			Path.Combine(Path.GetTempPath(), "client-hash.bin"), invalid));
		Assert.Throws<ArgumentOutOfRangeException>(() => new HashDigest(invalid, "digest"));
	}

	[Fact]
	public void FileHashRequestRequiresAnAbsolutePathButNotFileSystemAccessAtConstruction()
	{
		string path = Path.Combine(Path.GetTempPath(), "not-created.bin");
		FileHashRequest request = new(path);

		Assert.Equal(path, request.FilePath);
		Assert.Equal(TargetHashAlgorithm.Sha256, request.Algorithm);
		Assert.False(File.Exists(path));
		Assert.Throws<ArgumentException>(() => new FileHashRequest("relative.bin"));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void DigestRejectsBlankValues(string value)
	{
		Assert.Throws<ArgumentException>(() => new HashDigest(TargetHashAlgorithm.Sha256, value));
	}

	[Fact]
	public void DigestPreservesAlgorithmAndCopiedText()
	{
		HashDigest digest = new(TargetHashAlgorithm.Md5, "a94a8fe5");

		Assert.Equal(TargetHashAlgorithm.Md5, digest.Algorithm);
		Assert.Equal("a94a8fe5", digest.Value);
	}
}
