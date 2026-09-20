using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

using MemoryFluent = CheatEngine.Client.Memory.Memory;

namespace CheatEngine.Client.Tests.Memory;

public sealed class MemoryAddressBuilderTests
{
	[Fact]
	public void AtWithoutServiceRejectsABuiltInTerminalOperation()
	{
		MemoryAddressBuilder builder = MemoryFluent.At(0x401000UL);

		Assert.Throws<InvalidOperationException>(() => builder.Read<int>(TestContext.Current.CancellationToken));
	}

	[Fact]
	public void UsingCreatesABoundBuilderAndForwardsBuiltInReads()
	{
		Address address = 0x401000;
		FakeMemoryClient memory = new(1337);
		MemoryAddressBuilder unbound = MemoryFluent.At(address);

		int value = unbound.Using(memory).Read<int>(TestContext.Current.CancellationToken);

		Assert.Equal(1337, value);
		Assert.Equal(address, memory.LastPrimitiveReadAddress);
		Assert.Equal(typeof(int), memory.LastPrimitiveReadType);
		Assert.Throws<InvalidOperationException>(() => unbound.Read<int>(TestContext.Current.CancellationToken));
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

		int value = MemoryFluent.At(address).Using(memory).ReadWith(codec, TestContext.Current.CancellationToken);

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

		bool succeeded = MemoryFluent.At(address).Using(memory).TryWriteWith(
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

		bool succeeded = MemoryFluent.At(address).Using(memory).TryRead(
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

		MemoryFluent.At(address).Using(memory).Write(77, TestContext.Current.CancellationToken);

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

		bool succeeded = MemoryFluent.At(address).Using(memory).TryReadWith(
			codec, out int value, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(42, value);
		Assert.Equal(default, failure);
		Assert.Equal(address, memory.LastCustomReadAddress);
		Assert.Same(codec, memory.LastCustomReadCodec);
	}

	[Fact]
	public void TryReadWithForwardsTheExplicitMemoryServiceAndCustomCodec()
	{
		Address address = 0x408000;
		Int32Codec codec = new();
		FakeMemoryClient memory = new(42);

		bool succeeded = MemoryFluent.At(address).TryReadWith(
			memory, codec, out int value, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(42, value);
		Assert.Equal(default, failure);
		Assert.Equal(address, memory.LastCustomReadAddress);
		Assert.Same(codec, memory.LastCustomReadCodec);
	}

	[Fact]
	public void TryReadWithRejectsMissingBoundMemoryAndNullExplicitArguments()
	{
		MemoryAddressBuilder unbound = MemoryFluent.At(0x409000UL);
		Int32Codec codec = new();
		FakeMemoryClient memory = new(0);

		Assert.Throws<InvalidOperationException>(() => unbound.TryReadWith(
			codec, out _, out _, TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentNullException>(() => unbound.TryReadWith<int>(
			null!, codec, out _, out _, TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentNullException>(() => unbound.TryReadWith(
			memory, null!, out int _, out _, TestContext.Current.CancellationToken));
	}

	[Fact]
	public void WriteWithForwardsTheBoundCustomCodecWithoutReplacingIt()
	{
		Address address = 0x40A000;
		Int32Codec codec = new();
		FakeMemoryClient memory = new(0);

		MemoryFluent.At(address).Using(memory).WriteWith(77, codec, TestContext.Current.CancellationToken);

		Assert.Equal(address, memory.LastCustomWriteAddress);
		Assert.Equal(77, memory.LastCustomWriteValue);
		Assert.Same(codec, memory.LastCustomWriteCodec);
	}

	[Fact]
	public void WriteWithForwardsTheExplicitMemoryServiceAndCustomCodec()
	{
		Address address = 0x40B000;
		Int32Codec codec = new();
		FakeMemoryClient memory = new(0);

		MemoryFluent.At(address).WriteWith(memory, 77, codec, TestContext.Current.CancellationToken);

		Assert.Equal(address, memory.LastCustomWriteAddress);
		Assert.Equal(77, memory.LastCustomWriteValue);
		Assert.Same(codec, memory.LastCustomWriteCodec);
	}

	[Fact]
	public void WriteWithRejectsMissingBoundMemoryAndNullExplicitArguments()
	{
		MemoryAddressBuilder unbound = MemoryFluent.At(0x40C000UL);
		Int32Codec codec = new();
		FakeMemoryClient memory = new(0);

		Assert.Throws<InvalidOperationException>(() => unbound.WriteWith(77, codec, TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentNullException>(() => unbound.WriteWith<int>(
			null!, 77, codec, TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentNullException>(() => unbound.WriteWith(
			memory, 77, null!, TestContext.Current.CancellationToken));
	}

	private sealed class Int32Codec : IMemoryCodec<int>
	{
		public bool TryRead(IMemoryReadContext context, Address address, out int value)
		{
			value = default;
			return false;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value)
		{
			return false;
		}
	}

	private sealed class FakeMemoryClient(int primitiveReadValue) : IMemoryClient
	{
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
		{
			LastPrimitiveReadAddress = address;
			LastPrimitiveReadType = typeof(T);
			value = (T) (object) primitiveReadValue;
			failure = default;
			return true;
		}

		public T ReadPrimitive<T>(Address address, CancellationToken cancellationToken = default)
		{
			_ = TryReadPrimitive(address, out T? value, out _, cancellationToken);
			return value!;
		}

		public bool TryWritePrimitive<T>(Address address, T value, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			LastPrimitiveWriteAddress = address;
			LastPrimitiveWriteType = typeof(T);
			LastPrimitiveWriteValue = value;
			failure = default;
			return true;
		}

		public void WritePrimitive<T>(Address address, T value, CancellationToken cancellationToken = default)
		{
			_ = TryWritePrimitive(address, value, out _, cancellationToken);
		}

		public bool TryReadBytes(MemoryBytesReadRequest request, out ImmutableArray<byte> bytes,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			bytes = [];
			failure = default;
			return true;
		}

		public ImmutableArray<byte> ReadBytes(MemoryBytesReadRequest request,
			CancellationToken cancellationToken = default)
		{
			_ = TryReadBytes(request, out ImmutableArray<byte> bytes, out _, cancellationToken);
			return bytes;
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
