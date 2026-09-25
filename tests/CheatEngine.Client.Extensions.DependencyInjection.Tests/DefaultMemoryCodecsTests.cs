using System.Buffers.Binary;
using System.Globalization;

using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Runtime;
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
			ValidateOnBuild = true,
			ValidateScopes = true
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

		Assert.True(codec.TryRead(context, address, out int value, out _));
		Assert.Equal(0x12345678, value);
		Assert.Equal(address, context.LastReadAddress);

		Assert.True(codec.TryWrite(context, address, 0x0A0B0C0D, out _));
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

		Assert.False(codec.TryRead(context, address, out long value, out _));
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

		Assert.True(codec.TryRead(context, address, out Address read, out _));
		Assert.Equal(Address.FromUInt64(expectedValue), read);

		Assert.True(codec.TryWrite(context, address, Address.FromUInt64(rawValue), out _));
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

		Assert.False(codec.TryRead(malformedWidth, address, out Address malformedRead, out _));
		Assert.Equal(Address.Zero, malformedRead);
		Assert.Null(malformedWidth.LastReadAddress);

		Assert.False(codec.TryWrite(malformedWidth, address, Address.FromUInt64(0x1234), out _));
		Assert.Null(malformedWidth.LastWriteAddress);

		Assert.False(codec.TryWrite(narrowTarget, address, Address.FromUInt64(0x1_0000_0000), out _));
		Assert.Null(narrowTarget.LastWriteAddress);
	}

	/// <summary>
	///     C1 codec matrix (audit A12-09, Q21): every built-in integer codec writes the exact little-endian bytes of its
	///     boundary values and reads them back unchanged; 64-bit values above 2^53 never pass through a double.
	/// </summary>
	[Theory]
	[Trait("Qualification", "Q21")]
	[InlineData("int8", "-128", "80")]
	[InlineData("int8", "127", "7F")]
	[InlineData("int8", "0", "00")]
	[InlineData("int8", "-1", "FF")]
	[InlineData("uint8", "0", "00")]
	[InlineData("uint8", "255", "FF")]
	[InlineData("int16", "-32768", "0080")]
	[InlineData("int16", "32767", "FF7F")]
	[InlineData("int16", "-1", "FFFF")]
	[InlineData("uint16", "0", "0000")]
	[InlineData("uint16", "65535", "FFFF")]
	[InlineData("int32", "-2147483648", "00000080")]
	[InlineData("int32", "2147483647", "FFFFFF7F")]
	[InlineData("int32", "0", "00000000")]
	[InlineData("int32", "-1", "FFFFFFFF")]
	[InlineData("uint32", "0", "00000000")]
	[InlineData("uint32", "2147483648", "00000080")]
	[InlineData("uint32", "4294967295", "FFFFFFFF")]
	[InlineData("int64", "-9223372036854775808", "0000000000000080")]
	[InlineData("int64", "9223372036854775807", "FFFFFFFFFFFFFF7F")]
	[InlineData("int64", "0", "0000000000000000")]
	[InlineData("int64", "-1", "FFFFFFFFFFFFFFFF")]
	[InlineData("int64", "2147483648", "0000008000000000")]
	[InlineData("int64", "9007199254740992", "0000000000002000")]
	[InlineData("int64", "9007199254740993", "0100000000002000")]
	[InlineData("uint64", "0", "0000000000000000")]
	[InlineData("uint64", "2147483648", "0000008000000000")]
	[InlineData("uint64", "9007199254740993", "0100000000002000")]
	[InlineData("uint64", "9223372036854775808", "0000000000000080")]
	[InlineData("uint64", "18446744073709551615", "FFFFFFFFFFFFFFFF")]
	public void DefaultCodecsRoundTripEverySignedAndUnsignedWidthAtItsBoundaries(string type, string value,
		string littleEndianHex)
	{
		using ServiceProvider provider = CreateProvider();
		byte[] expected = Convert.FromHexString(littleEndianHex);

		switch (type)
		{
			case "int8":
				AssertRoundTrip(provider, sbyte.Parse(value, CultureInfo.InvariantCulture), expected);
				break;
			case "uint8":
				AssertRoundTrip(provider, byte.Parse(value, CultureInfo.InvariantCulture), expected);
				break;
			case "int16":
				AssertRoundTrip(provider, short.Parse(value, CultureInfo.InvariantCulture), expected);
				break;
			case "uint16":
				AssertRoundTrip(provider, ushort.Parse(value, CultureInfo.InvariantCulture), expected);
				break;
			case "int32":
				AssertRoundTrip(provider, int.Parse(value, CultureInfo.InvariantCulture), expected);
				break;
			case "uint32":
				AssertRoundTrip(provider, uint.Parse(value, CultureInfo.InvariantCulture), expected);
				break;
			case "int64":
				AssertRoundTrip(provider, long.Parse(value, CultureInfo.InvariantCulture), expected);
				break;
			case "uint64":
				AssertRoundTrip(provider, ulong.Parse(value, CultureInfo.InvariantCulture), expected);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(type), type, null);
		}
	}

	[Fact]
	[Trait("Qualification", "Q21")]
	public void ReadsSignedMinusOneFromAThirtyTwoBitPrimitive()
	{
		// Audit ch.12 scenario #1: the same four bytes are -1 as a signed value and 4294967295 as an unsigned value.
		using ServiceProvider provider = CreateProvider();
		BufferMemoryContext context = new(8, [0xFF, 0xFF, 0xFF, 0xFF]);

		Assert.True(provider.GetRequiredService<IMemoryCodec<int>>().TryRead(context, 0x401000, out int signed, out _));
		Assert.True(provider.GetRequiredService<IMemoryCodec<uint>>().TryRead(context, 0x401000, out uint unsigned, out _));

		Assert.Equal(-1, signed);
		Assert.Equal(4294967295u, unsigned);
	}

	[Theory]
	[Trait("Qualification", "Q21")]
	[InlineData(0x7FC00001u)]
	[InlineData(0xFFC00123u)]
	[InlineData(0x80000000u)]
	[InlineData(0x00000001u)]
	[InlineData(0x7F800000u)]
	[InlineData(0xFF800000u)]
	public void FloatingPointCodecsPreserveEveryBitPattern(uint singleBits)
	{
		// NaN payloads, negative zero, subnormals and infinities survive a write and a read bit for bit.
		using ServiceProvider provider = CreateProvider();
		ulong doubleBits = singleBits switch
		{
			0x7FC00001u => 0x7FF8000000000001ul,
			0xFFC00123u => 0xFFF8000000000123ul,
			0x80000000u => 0x8000000000000000ul,
			0x00000001u => 0x0000000000000001ul,
			0x7F800000u => 0x7FF0000000000000ul,
			_ => 0xFFF0000000000000ul
		};

		AssertRoundTrip(provider, BitConverter.UInt32BitsToSingle(singleBits), LittleEndian(singleBits, 4));
		AssertRoundTrip(provider, BitConverter.UInt64BitsToDouble(doubleBits), LittleEndian(doubleBits, 8));
		BufferMemoryContext singleContext = new(8, LittleEndian(singleBits, 4));
		BufferMemoryContext doubleContext = new(8, LittleEndian(doubleBits, 8));
		Assert.True(provider.GetRequiredService<IMemoryCodec<float>>().TryRead(singleContext, 0x401000,
			out float single, out _));
		Assert.True(provider.GetRequiredService<IMemoryCodec<double>>().TryRead(doubleContext, 0x401000,
			out double @double, out _));
		Assert.Equal(singleBits, BitConverter.SingleToUInt32Bits(single));
		Assert.Equal(doubleBits, BitConverter.DoubleToUInt64Bits(@double));
	}

	[Fact]
	[Trait("Qualification", "Q21")]
	public void AddressCodecReadsAnAddressAboveFourGibibytesOnASixtyFourBitTarget()
	{
		// 0x100000000 is the image base of the x64 tutorial target observed by the spike (C3, P1).
		using ServiceProvider provider = CreateProvider();
		IMemoryCodec<Address> codec = provider.GetRequiredService<IMemoryCodec<Address>>();
		BufferMemoryContext context = new(8, [0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00]);

		Assert.True(codec.TryRead(context, 0x401000, out Address read, out _));
		Assert.True(codec.TryWrite(context, 0x401000, Address.FromUInt64(0x1_0000_0000), out _));

		Assert.Equal(Address.FromUInt64(0x1_0000_0000), read);
		Assert.Equal([0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00], context.LastWrittenBytes);
	}

	[Fact]
	[Trait("Qualification", "Q21")]
	public void AddressCodecRefusesWritingAnAddressAboveFourGibibytesToAThirtyTwoBitTarget()
	{
		using ServiceProvider provider = CreateProvider();
		IMemoryCodec<Address> codec = provider.GetRequiredService<IMemoryCodec<Address>>();
		BufferMemoryContext context = new(4, [0, 0, 0, 0]);

		Assert.False(codec.TryWrite(context, 0x401000, Address.FromUInt64(0x1_0000_0000), out _));
		Assert.True(codec.TryWrite(context, 0x401000, Address.FromUInt64(uint.MaxValue), out _));

		Assert.Equal([0xFF, 0xFF, 0xFF, 0xFF], context.LastWrittenBytes);
	}

	[Fact]
	[Trait("Qualification", "Q31")]
	public void BuiltInAddressCodecIsRefusedWhenTheConfiguredPointerSizeDiffersFromTheProcessWidth()
	{
		// Spike C3 D3: Cheat Engine's readPointer follows the process width; the Client never picks a width silently.
		using ServiceProvider provider = CreateProvider();
		IMemoryCodec<Address> codec = provider.GetRequiredService<IMemoryCodec<Address>>();
		WidthMemoryContext mismatch = new(8, 4);
		WidthMemoryContext matching = new(8, 8);

		Assert.False(codec.TryRead(mismatch, 0x401000, out Address refused, out _));
		Assert.False(codec.TryWrite(mismatch, 0x401000, Address.FromUInt64(0x401000), out _));
		Assert.True(codec.TryRead(matching, 0x401000, out _, out _));

		Assert.Equal(Address.Zero, refused);
		Assert.Equal(0, mismatch.Accesses);
		Assert.Equal(1, matching.Accesses);
	}

	private static void AssertRoundTrip<T>(ServiceProvider provider, T value, byte[] expected)
		where T : unmanaged
	{
		IMemoryCodec<T> codec = provider.GetRequiredService<IMemoryCodec<T>>();
		BufferMemoryContext writeContext = new(8, []);

		Assert.True(codec.TryWrite(writeContext, 0x401000, value, out _));
		Assert.Equal(expected, writeContext.LastWrittenBytes);

		BufferMemoryContext readContext = new(8, writeContext.LastWrittenBytes);
		Assert.True(codec.TryRead(readContext, 0x401000, out T read, out _));
		Assert.Equal(value, read);
	}

	private static byte[] LittleEndian(ulong bits, int length)
	{
		byte[] bytes = new byte[sizeof(ulong)];
		BinaryPrimitives.WriteUInt64LittleEndian(bytes, bits);
		return bytes[..length];
	}

	private static ServiceProvider CreateProvider()
	{
		ServiceCollection services = new();
		DefaultMemoryCodecs.Add(services);
		return services.BuildServiceProvider();
	}

	/// <summary>A context that reports a configured pointer size apart from the bitness.</summary>
	private sealed class WidthMemoryContext(int processBytes, int configuredBytes)
		: IMemoryReadContext, IMemoryWriteContext
	{
		internal int Accesses
		{
			get;
			private set;
		}

		public PointerSize Bitness => new(processBytes);

		public int? ConfiguredPointerSizeBytes => configuredBytes;

		public PointerSize ConfiguredPointerSize => new(configuredBytes);

		public bool? ConfiguredPointerSizeDiffersFromBitness => configuredBytes != processBytes;

		public bool TryReadBytes(Address address, Span<byte> destination, out CheatEngineFailure failure)
		{
			failure = default;
			Accesses++;
			destination.Clear();
			return true;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out CheatEngineFailure failure)
		{
			failure = default;
			Accesses++;
			return true;
		}
	}

	/// <summary>A context over a byte buffer; a pointer size other than 4 or 8 bytes is an unknown bitness.</summary>
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

		public PointerSize Bitness
		{
			get;
		} = pointerSize is sizeof(uint) or sizeof(ulong) ? new PointerSize(pointerSize) : PointerSize.Unknown;

		public PointerSize ConfiguredPointerSize => Bitness;

		public int? ConfiguredPointerSizeBytes => Bitness.IsKnown ? Bitness.Bytes : null;

		public bool? ConfiguredPointerSizeDiffersFromBitness => false;

		public bool TryReadBytes(Address address, Span<byte> destination, out CheatEngineFailure failure)
		{
			failure = default;
			LastReadAddress = address;
			if (_bytes.Length < destination.Length)
			{
				return false;
			}

			_bytes.AsSpan(0, destination.Length).CopyTo(destination);
			return true;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out CheatEngineFailure failure)
		{
			failure = default;
			LastWriteAddress = address;
			LastWrittenBytes = source.ToArray();
			return true;
		}
	}
}
