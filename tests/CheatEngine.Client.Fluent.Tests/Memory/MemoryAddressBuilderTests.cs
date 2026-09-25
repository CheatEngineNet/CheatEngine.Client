using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Fluent.Tests.Memory;

public sealed class MemoryAddressBuilderTests
{
	[Fact]
	public void DefaultBuilderRejectsABuiltInTerminalOperationAndNamesTheBoundEntryPoint()
	{
		MemoryAddressBuilder builder = default;

		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(() => builder.Read<int>(TestContext.Current.CancellationToken));

		Assert.Contains("memory.At(address)", exception.Message);
		Assert.DoesNotContain("Memory.At(memory, address)", exception.Message);
		Assert.DoesNotContain("Using(", exception.Message);
	}

	[Fact]
	public void AtBindsTheBuilderToTheMemoryServiceAndForwardsBuiltInReads()
	{
		Address address = 0x401000;
		FakeMemoryClient memory = new(1337);

		int value = memory.At(address).Read<int>(TestContext.Current.CancellationToken);

		Assert.Equal(1337, value);
		Assert.Equal(address, memory.LastPrimitiveReadAddress);
		Assert.Equal(typeof(int), memory.LastPrimitiveReadType);
	}

	[Fact]
	public void TheMemoryEntryPointsRejectANullService()
	{
		IMemoryClient memory = null!;

		Assert.Throws<ArgumentNullException>(() => memory.At(0x401000UL));
		Assert.Throws<ArgumentNullException>(() => memory.Batch<int>());
	}

	[Fact]
	public void MemoryExtensionForwardsBuiltInWritesWithoutACodec()
	{
		Address address = 0x402000;
		FakeMemoryClient memory = new(0);

		bool succeeded = memory.At(address)
			.TryWrite(77, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(address, memory.LastPrimitiveWriteAddress);
		Assert.Equal(typeof(int), memory.LastPrimitiveWriteType);
		Assert.Equal(77, memory.LastPrimitiveWriteValue);
	}

	[Fact]
	public void ReadWithForwardsTheExplicitCustomCodecWithoutReplacingIt()
	{
		Address address = 0x403000;
		Int32Codec codec = new();
		FakeMemoryClient memory = new(42);

		int value = memory.At(address).ReadWith(codec, TestContext.Current.CancellationToken);

		Assert.Equal(42, value);
		Assert.Equal(address, memory.LastCustomReadAddress);
		Assert.Same(codec, memory.LastCustomReadCodec);
	}

	[Fact]
	public void TryWriteWithForwardsValueAddressAndTheExplicitCustomCodec()
	{
		Address address = 0x404000;
		Int32Codec codec = new();
		FakeMemoryClient memory = new(0);

		bool succeeded = memory.At(address).TryWriteWith(
			77, codec, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(address, memory.LastCustomWriteAddress);
		Assert.Equal(77, memory.LastCustomWriteValue);
		Assert.Same(codec, memory.LastCustomWriteCodec);
	}

	[Fact]
	public void TryReadForwardsBuiltInReadToTheBoundService()
	{
		Address address = 0x405000;
		FakeMemoryClient memory = new(1337);

		bool succeeded = memory.At(address).TryRead(
			out int value, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(1337, value);
		Assert.Equal(default, failure);
		Assert.Equal(address, memory.LastPrimitiveReadAddress);
		Assert.Equal(typeof(int), memory.LastPrimitiveReadType);
	}

	[Fact]
	public void WriteForwardsBuiltInWriteToTheBoundService()
	{
		Address address = 0x406000;
		FakeMemoryClient memory = new(0);

		memory.At(address).Write(77, TestContext.Current.CancellationToken);

		Assert.Equal(address, memory.LastPrimitiveWriteAddress);
		Assert.Equal(typeof(int), memory.LastPrimitiveWriteType);
		Assert.Equal(77, memory.LastPrimitiveWriteValue);
	}

	[Fact]
	public void TryReadWithForwardsTheBoundCustomCodecWithoutReplacingIt()
	{
		Address address = 0x407000;
		Int32Codec codec = new();
		FakeMemoryClient memory = new(42);

		bool succeeded = memory.At(address).TryReadWith(
			codec, out int value, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(42, value);
		Assert.Equal(default, failure);
		Assert.Equal(address, memory.LastCustomReadAddress);
		Assert.Same(codec, memory.LastCustomReadCodec);
	}

	[Fact]
	public void TryReadWithRejectsADefaultBuilderAndANullCodec()
	{
		MemoryAddressBuilder unbound = default;
		MemoryAddressBuilder bound = new FakeMemoryClient(0).At(0x409000UL);
		Int32Codec codec = new();

		Assert.Throws<InvalidOperationException>(() => unbound.TryReadWith(
			codec, out _, out _, TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentNullException>(() => bound.TryReadWith(
			(IMemoryCodec<int>) null!, out _, out _, TestContext.Current.CancellationToken));
	}

	[Fact]
	public void WriteWithForwardsTheBoundCustomCodecWithoutReplacingIt()
	{
		Address address = 0x40A000;
		Int32Codec codec = new();
		FakeMemoryClient memory = new(0);

		memory.At(address).WriteWith(77, codec, TestContext.Current.CancellationToken);

		Assert.Equal(address, memory.LastCustomWriteAddress);
		Assert.Equal(77, memory.LastCustomWriteValue);
		Assert.Same(codec, memory.LastCustomWriteCodec);
	}

	[Fact]
	public void WriteWithRejectsADefaultBuilderAndANullCodec()
	{
		MemoryAddressBuilder unbound = default;
		MemoryAddressBuilder bound = new FakeMemoryClient(0).At(0x40C000UL);
		Int32Codec codec = new();

		Assert.Throws<InvalidOperationException>(() =>
			unbound.WriteWith(77, codec, TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentNullException>(() =>
			bound.WriteWith(77, (IMemoryCodec<int>) null!, TestContext.Current.CancellationToken));
	}

	[Fact]
	public void BoundedBytesAndStringsProduceCopiedRequestsWithExplicitEncoding()
	{
		FakeMemoryClient memory = new(0);
		MemoryAddressBuilder builder = memory.At(0x40D000);

		ImmutableArray<byte> bytes = builder.ReadBytes(2, TestContext.Current.CancellationToken);
		builder.WriteUtf8("é", 2, TestContext.Current.CancellationToken);
		string text = builder.ReadUtf16(32, TestContext.Current.CancellationToken);

		Assert.Equal([0x10, 0x20], bytes);
		Assert.Equal(2, memory.LastBytesReadRequest.Length);
		Assert.Equal("é", memory.LastStringWriteRequest.Value);
		Assert.Equal(2, memory.LastStringWriteRequest.MaximumLength);
		Assert.Equal(MemoryStringEncoding.Utf8, memory.LastStringWriteRequest.Encoding);
		Assert.Equal(MemoryStringEncoding.Utf16, memory.LastStringReadRequest.Encoding);
		Assert.Equal(string.Empty, text);
	}

	[Fact]
	public void PointerChainsAndPrimitiveBatchesRemainBoundedAndUseOneTerminalContract()
	{
		FakeMemoryClient memory = new(1337);
		Address baseAddress = 0x40E000;
		MemoryPointerChainBuilder chain = memory.At(baseAddress).Follow([0x10L, -0x20L]);

		Address resolved = chain.Resolve(TestContext.Current.CancellationToken);
		ImmutableArray<int> values = memory.Batch<int>().Read([baseAddress, baseAddress + 4],
			TestContext.Current.CancellationToken);

		Assert.Equal(baseAddress, resolved);
		Assert.Equal([0x10L, -0x20L], chain.Request.Offsets);
		Assert.Equal([1337, 1337], values);
		Assert.Equal(2, memory.LastPrimitiveBatchReadCount);
	}

	private sealed class Int32Codec : IMemoryCodec<int>
	{
		public bool TryRead(IMemoryReadContext context, Address address, out int value, out CheatEngineFailure failure)
		{
			failure = default;
			value = default;
			return false;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value, out CheatEngineFailure failure)
		{
			failure = default;
			return false;
		}
	}

	private sealed class FakeMemoryClient(int primitiveReadValue) : IMemoryClient
	{
		internal MemoryBytesReadRequest LastBytesReadRequest
		{
			get;
			private set;
		}

		internal MemoryStringReadRequest LastStringReadRequest
		{
			get;
			private set;
		}

		internal MemoryStringWriteRequest LastStringWriteRequest
		{
			get;
			private set;
		}

		internal int LastPrimitiveBatchReadCount
		{
			get;
			private set;
		}

		internal Address LastPrimitiveReadAddress
		{
			get;
			private set;
		}

		internal Type? LastPrimitiveReadType
		{
			get;
			private set;
		}

		internal Address LastPrimitiveWriteAddress
		{
			get;
			private set;
		}

		internal Type? LastPrimitiveWriteType
		{
			get;
			private set;
		}

		internal object? LastPrimitiveWriteValue
		{
			get;
			private set;
		}

		internal Address LastCustomReadAddress
		{
			get;
			private set;
		}

		internal object? LastCustomReadCodec
		{
			get;
			private set;
		}

		internal Address LastCustomWriteAddress
		{
			get;
			private set;
		}

		internal object? LastCustomWriteCodec
		{
			get;
			private set;
		}

		internal object? LastCustomWriteValue
		{
			get;
			private set;
		}

		public bool TryReadPrimitive<T>(Address address, [MaybeNullWhen(false)] out T value,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
			where T : unmanaged
		{
			LastPrimitiveReadAddress = address;
			LastPrimitiveReadType = typeof(T);
			value = (T) (object) primitiveReadValue;
			failure = default;
			return true;
		}

		public T ReadPrimitive<T>(Address address, CancellationToken cancellationToken = default)
			where T : unmanaged
		{
			_ = TryReadPrimitive(address, out T value, out _, cancellationToken);
			return value;
		}

		public bool TryWritePrimitive<T>(Address address, T value, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
			where T : unmanaged
		{
			LastPrimitiveWriteAddress = address;
			LastPrimitiveWriteType = typeof(T);
			LastPrimitiveWriteValue = value;
			failure = default;
			return true;
		}

		public void WritePrimitive<T>(Address address, T value, CancellationToken cancellationToken = default)
			where T : unmanaged
		{
			_ = TryWritePrimitive(address, value, out _, cancellationToken);
		}

		public bool TryReadPrimitiveBatch<T>(MemoryPrimitiveBatchReadRequest<T> request,
			out ImmutableArray<T> values, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
			where T : unmanaged
		{
			LastPrimitiveBatchReadCount = request.Addresses.Length;
			T[] result = new T[request.Addresses.Length];
			for (int index = 0; index < result.Length; index++)
			{
				if (!TryReadPrimitive(request.Addresses[index], out T value, out failure, cancellationToken))
				{
					values = [];
					return false;
				}

				result[index] = value;
			}

			values = ImmutableArray.Create(result);
			failure = default;
			return true;
		}

		public ImmutableArray<T> ReadPrimitiveBatch<T>(MemoryPrimitiveBatchReadRequest<T> request,
			CancellationToken cancellationToken = default)
			where T : unmanaged
		{
			_ = TryReadPrimitiveBatch(request, out ImmutableArray<T> values, out _, cancellationToken);
			return values;
		}

		public bool TryWritePrimitiveBatch<T>(MemoryPrimitiveBatchWriteRequest<T> request,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
			where T : unmanaged
		{
			for (int index = 0; index < request.Values.Length; index++)
			{
				MemoryAddressValue<T> current = request.Values[index];
				if (!TryWritePrimitive(current.Address, current.Value, out failure, cancellationToken))
				{
					return false;
				}
			}

			failure = default;
			return true;
		}

		public void WritePrimitiveBatch<T>(MemoryPrimitiveBatchWriteRequest<T> request,
			CancellationToken cancellationToken = default)
			where T : unmanaged
		{
			_ = TryWritePrimitiveBatch(request, out _, cancellationToken);
		}

		public MemoryPrimitiveBatchReadOutcome<T> ReadPrimitiveBatchDetailed<T>(
			MemoryPrimitiveBatchReadRequest<T> request, CancellationToken cancellationToken = default)
			where T : unmanaged
		{
			_ = TryReadPrimitiveBatch(request, out ImmutableArray<T> values, out _, cancellationToken);
			return new MemoryPrimitiveBatchReadOutcome<T>(values.Length, values.AsSpan(), null, null);
		}

		public MemoryPrimitiveBatchWriteOutcome WritePrimitiveBatchDetailed<T>(
			MemoryPrimitiveBatchWriteRequest<T> request, CancellationToken cancellationToken = default)
			where T : unmanaged
		{
			_ = TryWritePrimitiveBatch(request, out _, cancellationToken);
			return new MemoryPrimitiveBatchWriteOutcome(request.Values.Length, request.Values.Length, null, null,
				MemoryBatchWriteEffectState.Completed);
		}

		public bool TryReadBytes(MemoryBytesReadRequest request, out ImmutableArray<byte> bytes,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			LastBytesReadRequest = request;
			bytes = [0x10, 0x20];
			failure = default;
			return true;
		}

		public ImmutableArray<byte> ReadBytes(MemoryBytesReadRequest request,
			CancellationToken cancellationToken = default)
		{
			_ = TryReadBytes(request, out ImmutableArray<byte> bytes, out _, cancellationToken);
			return bytes;
		}

		public MemoryBytesReadOutcome ReadBytesDetailed(MemoryBytesReadRequest request,
			CancellationToken cancellationToken = default)
		{
			_ = TryReadBytes(request, out ImmutableArray<byte> bytes, out _, cancellationToken);
			return new MemoryBytesReadOutcome(bytes.Length, bytes, null);
		}

		public bool TryWriteBytes(MemoryBytesWriteRequest request, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			failure = default;
			return true;
		}

		public void WriteBytes(MemoryBytesWriteRequest request, CancellationToken cancellationToken = default)
		{
			_ = TryWriteBytes(request, out _, cancellationToken);
		}

		public bool TryReadString(MemoryStringReadRequest request, [NotNullWhen(true)] out string? value,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			LastStringReadRequest = request;
			value = string.Empty;
			failure = default;
			return true;
		}

		public string ReadString(MemoryStringReadRequest request, CancellationToken cancellationToken = default)
		{
			_ = TryReadString(request, out string? value, out _, cancellationToken);
			return value!;
		}

		public bool TryWriteString(MemoryStringWriteRequest request, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			LastStringWriteRequest = request;
			failure = default;
			return true;
		}

		public void WriteString(MemoryStringWriteRequest request, CancellationToken cancellationToken = default)
		{
			_ = TryWriteString(request, out _, cancellationToken);
		}

		public bool TryResolvePointerChain(PointerChainRequest request, out Address address,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			address = request.BaseAddress;
			failure = default;
			return true;
		}

		public Address ResolvePointerChain(PointerChainRequest request, CancellationToken cancellationToken = default)
		{
			_ = TryResolvePointerChain(request, out Address address, out _, cancellationToken);
			return address;
		}

		public bool TryRead<T>(MemoryReadRequest<T> request, [MaybeNullWhen(false)] out T value,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			LastCustomReadAddress = request.Address;
			LastCustomReadCodec = request.Codec;
			value = (T) (object) primitiveReadValue;
			failure = default;
			return true;
		}

		public T Read<T>(MemoryReadRequest<T> request, CancellationToken cancellationToken = default)
		{
			_ = TryRead(request, out T? value, out _, cancellationToken);
			return value!;
		}

		public bool TryWrite<T>(MemoryWriteRequest<T> request, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			LastCustomWriteAddress = request.Address;
			LastCustomWriteValue = request.Value;
			LastCustomWriteCodec = request.Codec;
			failure = default;
			return true;
		}

		public void Write<T>(MemoryWriteRequest<T> request, CancellationToken cancellationToken = default)
		{
			_ = TryWrite(request, out _, cancellationToken);
		}
	}
}
