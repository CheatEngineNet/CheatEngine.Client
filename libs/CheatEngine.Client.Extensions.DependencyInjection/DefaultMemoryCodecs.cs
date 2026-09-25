using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Runtime;
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

		public bool TryRead(IMemoryReadContext context, Address address, out T value, out CheatEngineFailure failure)
		{
			ArgumentNullException.ThrowIfNull(context);
			// The default failure lets the Client classify a refusal from what the context observed.
			failure = default;
			Span<byte> bytes = stackalloc byte[Unsafe.SizeOf<T>()];
			if (context.TryReadBytes(address, bytes, out _))
			{
				value = MemoryMarshal.Read<T>(bytes);
				return true;
			}

			value = default;
			return false;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in T value, out CheatEngineFailure failure)
		{
			ArgumentNullException.ThrowIfNull(context);
			failure = default;
			ReadOnlySpan<T> values = MemoryMarshal.CreateReadOnlySpan(in value, 1);
			return context.TryWriteBytes(address, MemoryMarshal.AsBytes(values), out _);
		}
	}

	/// <summary>
	///     The built-in pointer codec: it reads and writes the target's bitness in little-endian order (the local x86/x64
	///     profile), and asks the Core codec context to admit it first so that an unknown bitness, or a Cheat Engine
	///     configured pointer size that differs from it, refuses the operation before any memory access.
	/// </summary>
	private sealed class AddressMemoryCodec : IMemoryCodec<Address>
	{
		internal static AddressMemoryCodec Instance
		{
			get;
		} = new();

		public bool TryRead(IMemoryReadContext context, Address address, out Address value,
			out CheatEngineFailure failure)
		{
			ArgumentNullException.ThrowIfNull(context);
			failure = default;
			PointerSize width = context is ICorePointerCodecPolicy policy
				? policy.TryAdmitPointerCodec() ? context.Bitness : PointerSize.Unknown
				: context.ConfiguredPointerSizeDiffersFromBitness == true ? PointerSize.Unknown : context.Bitness;
			Span<byte> bytes = stackalloc byte[sizeof(ulong)];
			Span<byte> target = bytes[..width.Bytes];
			if (!width.IsKnown || !context.TryReadBytes(address, target, out _))
			{
				value = default;
				return false;
			}

			value = Address.FromUInt64(width == PointerSize.Bit64
				? BinaryPrimitives.ReadUInt64LittleEndian(target)
				: BinaryPrimitives.ReadUInt32LittleEndian(target));
			return true;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in Address value,
			out CheatEngineFailure failure)
		{
			ArgumentNullException.ThrowIfNull(context);
			failure = default;
			PointerSize width = context is ICorePointerCodecPolicy policy
				? policy.TryAdmitPointerCodec() ? context.Bitness : PointerSize.Unknown
				: context.ConfiguredPointerSizeDiffersFromBitness == true ? PointerSize.Unknown : context.Bitness;
			// A value wider than a 32-bit target is refused instead of truncated.
			if (!width.IsKnown || (width == PointerSize.Bit32 && value.Value > uint.MaxValue))
			{
				return false;
			}

			Span<byte> bytes = stackalloc byte[sizeof(ulong)];
			Span<byte> target = bytes[..width.Bytes];
			if (width == PointerSize.Bit64)
			{
				BinaryPrimitives.WriteUInt64LittleEndian(target, value.Value);
			}
			else
			{
				BinaryPrimitives.WriteUInt32LittleEndian(target, (uint) value.Value);
			}

			return context.TryWriteBytes(address, target, out _);
		}
	}
}
