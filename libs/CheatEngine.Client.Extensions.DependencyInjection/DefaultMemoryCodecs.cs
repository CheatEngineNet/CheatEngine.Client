using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.Client.Memory;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CheatEngine.Client.Extensions.DependencyInjection;

internal static class DefaultMemoryCodecs
{
	internal static void Add(IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.TryAdd(ServiceDescriptor.Singleton<IMemoryCodec<byte>>(UnmanagedMemoryCodec<byte>.Instance));
		services.TryAdd(ServiceDescriptor.Singleton<IMemoryCodec<sbyte>>(UnmanagedMemoryCodec<sbyte>.Instance));
		services.TryAdd(ServiceDescriptor.Singleton<IMemoryCodec<ushort>>(UnmanagedMemoryCodec<ushort>.Instance));
		services.TryAdd(ServiceDescriptor.Singleton<IMemoryCodec<short>>(UnmanagedMemoryCodec<short>.Instance));
		services.TryAdd(ServiceDescriptor.Singleton<IMemoryCodec<uint>>(UnmanagedMemoryCodec<uint>.Instance));
		services.TryAdd(ServiceDescriptor.Singleton<IMemoryCodec<int>>(UnmanagedMemoryCodec<int>.Instance));
		services.TryAdd(ServiceDescriptor.Singleton<IMemoryCodec<ulong>>(UnmanagedMemoryCodec<ulong>.Instance));
		services.TryAdd(ServiceDescriptor.Singleton<IMemoryCodec<long>>(UnmanagedMemoryCodec<long>.Instance));
		services.TryAdd(ServiceDescriptor.Singleton<IMemoryCodec<float>>(UnmanagedMemoryCodec<float>.Instance));
		services.TryAdd(ServiceDescriptor.Singleton<IMemoryCodec<double>>(UnmanagedMemoryCodec<double>.Instance));
		services.TryAdd(ServiceDescriptor.Singleton<IMemoryCodec<Address>>(AddressMemoryCodec.Instance));
	}

	private sealed class UnmanagedMemoryCodec<T> : IMemoryCodec<T>
		where T : unmanaged
	{
		internal static UnmanagedMemoryCodec<T> Instance
		{
			get;
		} = new();

		public bool TryRead(IMemoryReadContext context, Address address, out T value)
		{
			ArgumentNullException.ThrowIfNull(context);
			Span<byte> bytes = stackalloc byte[Unsafe.SizeOf<T>()];
			if (context.TryReadBytes(address, bytes))
			{
				value = MemoryMarshal.Read<T>(bytes);
				return true;
			}

			value = default;
			return false;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in T value)
		{
			ArgumentNullException.ThrowIfNull(context);
			ReadOnlySpan<T> values = MemoryMarshal.CreateReadOnlySpan(in value, 1);
			return context.TryWriteBytes(address, MemoryMarshal.AsBytes(values));
		}
	}

	private sealed class AddressMemoryCodec : IMemoryCodec<Address>
	{
		internal static AddressMemoryCodec Instance
		{
			get;
		} = new();

		public bool TryRead(IMemoryReadContext context, Address address, out Address value)
		{
			ArgumentNullException.ThrowIfNull(context);
			if (!IsSupportedPointerSize(context.PointerSize))
			{
				value = default;
				return false;
			}

			Span<byte> bytes = stackalloc byte[sizeof(ulong)];
			Span<byte> target = bytes[..context.PointerSize];
			if (!context.TryReadBytes(address, target))
			{
				value = default;
				return false;
			}

			value = Address.FromUInt64(context.PointerSize == sizeof(ulong)
				? BinaryPrimitives.ReadUInt64LittleEndian(target)
				: BinaryPrimitives.ReadUInt32LittleEndian(target));
			return true;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in Address value)
		{
			ArgumentNullException.ThrowIfNull(context);
			if (!IsSupportedPointerSize(context.PointerSize) ||
			    (context.PointerSize == sizeof(uint) && value.Value > uint.MaxValue))
			{
				return false;
			}

			Span<byte> bytes = stackalloc byte[sizeof(ulong)];
			Span<byte> target = bytes[..context.PointerSize];
			if (context.PointerSize == sizeof(ulong))
			{
				BinaryPrimitives.WriteUInt64LittleEndian(target, value.Value);
			}
			else
			{
				BinaryPrimitives.WriteUInt32LittleEndian(target, (uint) value.Value);
			}

			return context.TryWriteBytes(address, target);
		}

		private static bool IsSupportedPointerSize(int pointerSize)
		{
			return pointerSize is sizeof(uint) or sizeof(ulong);
		}
	}
}
