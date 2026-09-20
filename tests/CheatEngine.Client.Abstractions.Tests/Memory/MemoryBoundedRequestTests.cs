using CheatEngine.Client.Memory;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Tests.Memory;

public sealed class MemoryBoundedRequestTests
{
	[Fact]
	public void BytesWriteRequestCopiesCallerDataIntoAnImmutablePayload()
	{
		byte[] source = [0x90, 0x90];
		Address address = 0x401000;

		MemoryBytesWriteRequest request = new(address, source);
		source[0] = 0xCC;

		Assert.Equal(address, request.Address);
		Assert.Equal([0x90, 0x90], request.Bytes);
	}

	[Fact]
	public void BytesWriteRequestRejectsAnEmptyPayload()
	{
		Assert.Throws<ArgumentException>(() => new MemoryBytesWriteRequest(0x401000, Array.Empty<byte>()));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void BytesReadRequestRejectsANonPositiveLength(int length)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryBytesReadRequest(0x401000, length));
	}

	[Fact]
	public void BytesReadRequestPreservesTheExactPositiveLength()
	{
		Address address = 0x401020;

		MemoryBytesReadRequest request = new(address, 64);

		Assert.Equal(address, request.Address);
		Assert.Equal(64, request.Length);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void StringReadRequestRejectsANonPositiveMaximumLength(int maximumLength)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryStringReadRequest(
			0x401000, maximumLength, true));
	}

	[Fact]
	public void StringReadRequestPreservesTheExplicitMaximumAndEncodingChoice()
	{
		Address address = 0x402000;

		MemoryStringReadRequest request = new(address, 128, true);

		Assert.Equal(address, request.Address);
		Assert.Equal(128, request.MaximumLength);
		Assert.True(request.WideCharacter);
	}

	[Fact]
	public void StringWriteRequestPreservesTextAndTheExplicitEncodingChoice()
	{
		Address address = 0x402100;

		MemoryStringWriteRequest request = new(address, "Player one", true);

		Assert.Equal(address, request.Address);
		Assert.Equal("Player one", request.Value);
		Assert.True(request.WideCharacter);
	}

	[Fact]
	public void StringWriteRequestRejectsNullText()
	{
		Assert.Throws<ArgumentNullException>(() => new MemoryStringWriteRequest(0x402200, null!));
	}

	[Fact]
	public void PointerChainRequestCopiesOffsetsAndPreservesTheirOrder()
	{
		long[] offsets = [0x10, -0x20, 0x30];
		Address baseAddress = 0x500000;

		PointerChainRequest request = new(baseAddress, offsets);
		offsets[0] = 0x7F;

		Assert.Equal(baseAddress, request.BaseAddress);
		Assert.Equal([0x10L, -0x20L, 0x30L], request.Offsets);
	}

	[Fact]
	public void PointerChainRequestRejectsEmptyOrOverlongChains()
	{
		long[] overlongOffsets = new long[65];

		Assert.Throws<ArgumentException>(() => new PointerChainRequest(0x500000, Array.Empty<long>()));
		Assert.Throws<ArgumentOutOfRangeException>(() => new PointerChainRequest(0x500000, overlongOffsets));
	}

	[Fact]
	public void PointerChainRequestAcceptsTheDocumentedMaximumHopCount()
	{
		long[] offsets = new long[64];
		offsets[63] = 0x40;

		PointerChainRequest request = new(0x501000, offsets);

		Assert.Equal(64, request.Offsets.Length);
		Assert.Equal(0x40, request.Offsets[^1]);
	}

	[Fact]
	public void TypedRequestsRejectANullCodecBeforeTheyCanReachCheatEngine()
	{
		Assert.Throws<ArgumentNullException>(() => new MemoryReadRequest<int>(0x502000, null!));
		Assert.Throws<ArgumentNullException>(() => new MemoryWriteRequest<int>(0x502000, 123, null!));
	}
}
