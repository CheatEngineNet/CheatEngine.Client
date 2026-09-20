using CheatEngine.Client.Memory;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.DependencyInjection;

namespace CheatEngine.Client.Extensions.DependencyInjection.Tests;

public sealed class DefaultMemoryCodecsTests
{
	[Fact]
	public void AddRegistersEveryBuiltInScalarAndPointerCodecExactlyOnce()
	{
		ServiceCollection services = new();

		DefaultMemoryCodecs.Add(services);
		DefaultMemoryCodecs.Add(services);

		using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true, ValidateScopes = true
		});

		Assert.IsType<IMemoryCodec<byte>>(provider.GetRequiredService<IMemoryCodec<byte>>(), false);
		Assert.IsType<IMemoryCodec<sbyte>>(provider.GetRequiredService<IMemoryCodec<sbyte>>(), false);
		Assert.IsType<IMemoryCodec<ushort>>(provider.GetRequiredService<IMemoryCodec<ushort>>(), false);
		Assert.IsType<IMemoryCodec<short>>(provider.GetRequiredService<IMemoryCodec<short>>(), false);
		Assert.IsType<IMemoryCodec<uint>>(provider.GetRequiredService<IMemoryCodec<uint>>(), false);
		Assert.IsType<IMemoryCodec<int>>(provider.GetRequiredService<IMemoryCodec<int>>(), false);
		Assert.IsType<IMemoryCodec<ulong>>(provider.GetRequiredService<IMemoryCodec<ulong>>(), false);
		Assert.IsType<IMemoryCodec<long>>(provider.GetRequiredService<IMemoryCodec<long>>(), false);
		Assert.IsType<IMemoryCodec<float>>(provider.GetRequiredService<IMemoryCodec<float>>(), false);
		Assert.IsType<IMemoryCodec<double>>(provider.GetRequiredService<IMemoryCodec<double>>(), false);
		Assert.IsType<IMemoryCodec<Address>>(provider.GetRequiredService<IMemoryCodec<Address>>(), false);
		Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IMemoryCodec<int>));
		Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IMemoryCodec<Address>));
	}

	[Fact]
	public void ScalarCodecReadsAndWritesTheExactLittleEndianTargetBytes()
	{
		using ServiceProvider provider = CreateProvider();
		IMemoryCodec<int> codec = provider.GetRequiredService<IMemoryCodec<int>>();
		BufferMemoryContext context = new(8, [0x78, 0x56, 0x34, 0x12]);
		Address address = 0x401000;

		Assert.True(codec.TryRead(context, address, out int value));
		Assert.Equal(0x12345678, value);
		Assert.Equal(address, context.LastReadAddress);

		Assert.True(codec.TryWrite(context, address, 0x0A0B0C0D));
		Assert.Equal(address, context.LastWriteAddress);
		Assert.Equal([0x0D, 0x0C, 0x0B, 0x0A], context.LastWrittenBytes);
	}

	[Fact]
	public void ScalarCodecReturnsFalseAndTheDefaultValueWhenTheContextCannotFillItsFixedWidthBuffer()
	{
		using ServiceProvider provider = CreateProvider();
		IMemoryCodec<long> codec = provider.GetRequiredService<IMemoryCodec<long>>();
		BufferMemoryContext context = new(8, [0x01, 0x02, 0x03, 0x04]);
		Address address = 0x401080;

		Assert.False(codec.TryRead(context, address, out long value));
		Assert.Equal(0L, value);
		Assert.Equal(address, context.LastReadAddress);
	}

	[Theory]
	[InlineData(4, 0xDEADBEEFul, 0xDEADBEEFu)]
	[InlineData(8, 0x1122334455667788ul, 0x1122334455667788ul)]
	public void AddressCodecUsesTheTargetPointerWidth(int pointerSize, ulong rawValue, ulong expectedValue)
	{
		using ServiceProvider provider = CreateProvider();
		IMemoryCodec<Address> codec = provider.GetRequiredService<IMemoryCodec<Address>>();
		BufferMemoryContext context = new(pointerSize, pointerSize == 4
			? [0xEF, 0xBE, 0xAD, 0xDE]
			: [0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11]);
		Address address = 0x401100;

		Assert.True(codec.TryRead(context, address, out Address read));
		Assert.Equal(Address.FromUInt64(expectedValue), read);

		Assert.True(codec.TryWrite(context, address, Address.FromUInt64(rawValue)));
		Assert.Equal(pointerSize, context.LastWrittenBytes.Length);
		Assert.Equal(pointerSize == 4
			? [0xEF, 0xBE, 0xAD, 0xDE]
			: [0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11], context.LastWrittenBytes);
	}

	[Fact]
	public void AddressCodecRejectsUnsupportedPointerWidthsAndNarrowingWritesWithoutTouchingMemory()
	{
		using ServiceProvider provider = CreateProvider();
		IMemoryCodec<Address> codec = provider.GetRequiredService<IMemoryCodec<Address>>();
		Address address = 0x401200;
		BufferMemoryContext malformedWidth = new(6, [0, 0, 0, 0, 0, 0]);
		BufferMemoryContext narrowTarget = new(4, [0, 0, 0, 0]);

		Assert.False(codec.TryRead(malformedWidth, address, out Address malformedRead));
		Assert.Equal(Address.Zero, malformedRead);
		Assert.Null(malformedWidth.LastReadAddress);

		Assert.False(codec.TryWrite(malformedWidth, address, Address.FromUInt64(0x1234)));
		Assert.Null(malformedWidth.LastWriteAddress);

		Assert.False(codec.TryWrite(narrowTarget, address, Address.FromUInt64(0x1_0000_0000)));
		Assert.Null(narrowTarget.LastWriteAddress);
	}

	private static ServiceProvider CreateProvider()
	{
		ServiceCollection services = new();
		DefaultMemoryCodecs.Add(services);
		return services.BuildServiceProvider();
	}

	private sealed class BufferMemoryContext(int pointerSize, byte[] bytes) : IMemoryReadContext, IMemoryWriteContext
	{
		private readonly byte[] _bytes = bytes;

		internal Address? LastReadAddress
		{
			get;
			private set;
		}

		internal Address? LastWriteAddress
		{
			get;
			private set;
		}

		internal byte[] LastWrittenBytes
		{
			get;
			private set;
		} = [];

		public int PointerSize
		{
			get;
		} = pointerSize;

		public bool TryReadBytes(Address address, Span<byte> destination)
		{
			LastReadAddress = address;
			if (_bytes.Length < destination.Length)
			{
				return false;
			}

			_bytes.AsSpan(0, destination.Length).CopyTo(destination);
			return true;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source)
		{
			LastWriteAddress = address;
			LastWrittenBytes = source.ToArray();
			return true;
		}
	}
}
