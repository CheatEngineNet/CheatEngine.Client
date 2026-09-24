using CheatEngine.Client.Memory;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Abstractions.Tests.Memory;

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
		Assert.Throws<ArgumentOutOfRangeException>(() => MemoryStringReadRequest.Create(
			0x401000, maximumLength, MemoryStringEncoding.Utf16));
	}

	[Fact]
	public void StringReadRequestPreservesTheExplicitMaximumAndEncodingChoice()
	{
		Address address = 0x402000;

		MemoryStringReadRequest request = MemoryStringReadRequest.Create(address, 128, MemoryStringEncoding.Utf16);

		Assert.Equal(address, request.Address);
		Assert.Equal(128, request.MaximumLength);
		Assert.Equal(MemoryStringEncoding.Utf16, request.Encoding);
	}

	[Fact]
	public void StringWriteRequestPreservesTextAndTheExplicitEncodingChoice()
	{
		Address address = 0x402100;

		MemoryStringWriteRequest request =
			MemoryStringWriteRequest.CreateBounded(address, "Player one", 10, MemoryStringEncoding.Utf16);

		Assert.Equal(address, request.Address);
		Assert.Equal("Player one", request.Value);
		Assert.Equal(10, request.MaximumLength);
		Assert.Equal(MemoryStringEncoding.Utf16, request.Encoding);
	}

	[Fact]
	public void StringWriteRequestRejectsNullText()
	{
		Assert.Throws<ArgumentNullException>(() =>
			MemoryStringWriteRequest.CreateBounded(0x402200, null!, 1, MemoryStringEncoding.Utf8));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			MemoryStringWriteRequest.CreateBounded(0x402200, "A", 0, MemoryStringEncoding.Utf8));
	}

	[Fact]
	public void ExplicitStringFactoriesPreserveTheRequestedEncodingAndBound()
	{
		MemoryStringReadRequest read = MemoryStringReadRequest.Create(0x402210, 64, MemoryStringEncoding.Utf16);
		MemoryStringWriteRequest write = MemoryStringWriteRequest.CreateBounded(0x402220, "é", 2,
			MemoryStringEncoding.Utf8);

		Assert.Equal(MemoryStringEncoding.Utf16, read.Encoding);
		Assert.Equal(64, read.MaximumLength);
		Assert.Equal(MemoryStringEncoding.Utf8, write.Encoding);
		Assert.Equal(2, write.MaximumLength);
	}

	[Fact]
	public void ExplicitBoundedStringFactoryRejectsAnOverlongUtf8Payload()
	{
		Assert.Throws<ArgumentException>(() => MemoryStringWriteRequest.CreateBounded(0x402230, "é", 1,
			MemoryStringEncoding.Utf8));
		Assert.Throws<ArgumentOutOfRangeException>(() => MemoryStringReadRequest.Create(0x402240, 10,
			(MemoryStringEncoding) 42));
		Assert.Throws<ArgumentOutOfRangeException>(() => MemoryStringWriteRequest.CreateBounded(0x402240, "A", 10,
			(MemoryStringEncoding) 42));
	}

	[Fact]
	public void PrimitiveBatchRequestsCopyCallerInputsAndEnforceTheSharedBound()
	{
		Address[] addresses = [0x403000, 0x403010];
		MemoryAddressValue<int>[] writes = [new(0x403020, 12), new(0x403024, 24)];

		MemoryPrimitiveBatchReadRequest<int> reads = new(addresses);
		MemoryPrimitiveBatchWriteRequest<int> batchWrites = new(writes);
		addresses[0] = 0xDEAD;
		writes[0] = new MemoryAddressValue<int>(0xDEAD, 99);

		Assert.Equal([0x403000UL, 0x403010UL], reads.Addresses);
		Assert.Equal(0x403020UL, batchWrites.Values[0].Address);
		Assert.Equal(12, batchWrites.Values[0].Value);
		Assert.Equal(MemoryBatchLimits.MaximumOperations, 1024);
		Assert.Throws<ArgumentException>(() => new MemoryPrimitiveBatchReadRequest<int>(Array.Empty<Address>()));
		Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryPrimitiveBatchWriteRequest<int>(
			new MemoryAddressValue<int>[MemoryBatchLimits.MaximumOperations + 1]));
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
