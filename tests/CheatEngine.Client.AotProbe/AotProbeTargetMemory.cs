using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.AotProbe;

/// <summary>
///     A 256-byte in-process target behind the Fluent memory builders, so that each builder route runs under Native AOT
///     without a Cheat Engine host.
/// </summary>
/// <remarks>
///     Strings are NUL-terminated within their explicit maximum length, in bytes. A pointer chain reads a 64-bit
///     pointer at each step and adds the step's offset. The detailed outcome members are not used by Fluent and throw.
/// </remarks>
internal sealed class AotProbeTargetMemory : IMemoryClient, IMemoryReadContext, IMemoryWriteContext
{
	private const string Operation = "AotProbe.TargetMemory";
	private const int Size = 256;
	private readonly byte[] _bytes = new byte[Size];

	/// <summary>Gets the first address of the target.</summary>
	internal static Address BaseAddress => new(0x1000);

	/// <inheritdoc />
	public PointerSize Bitness => PointerSize.Bit64;

	/// <inheritdoc />
	public PointerSize ConfiguredPointerSize => PointerSize.Bit64;

	/// <inheritdoc />
	public int? ConfiguredPointerSizeBytes => PointerSize.Bit64.Bytes;

	/// <inheritdoc />
	public bool? ConfiguredPointerSizeDiffersFromBitness => false;

	/// <inheritdoc />
	public bool TryReadBytes(Address address, Span<byte> destination, out CheatEngineFailure failure)
	{
		if (!TryLocate(address, destination.Length, out int offset, out failure))
		{
			return false;
		}

		_bytes.AsSpan(offset, destination.Length).CopyTo(destination);
		return true;
	}

	/// <inheritdoc />
	public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out CheatEngineFailure failure)
	{
		if (!TryLocate(address, source.Length, out int offset, out failure))
		{
			return false;
		}

		source.CopyTo(_bytes.AsSpan(offset));
		return true;
	}

	/// <inheritdoc />
	public bool TryReadPrimitive<T>(Address address, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		Span<byte> buffer = stackalloc byte[Unsafe.SizeOf<T>()];
		if (!TryReadBytes(address, buffer, out failure))
		{
			value = default;
			return false;
		}

		value = MemoryMarshal.Read<T>(buffer);
		return true;
	}

	/// <inheritdoc />
	public T ReadPrimitive<T>(Address address, CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		if (!TryReadPrimitive(address, out T value, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
		}

		return value;
	}

	/// <inheritdoc />
	public bool TryWritePrimitive<T>(Address address, T value, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		Span<byte> buffer = stackalloc byte[Unsafe.SizeOf<T>()];
		MemoryMarshal.Write(buffer, in value);
		return TryWriteBytes(address, buffer, out failure);
	}

	/// <inheritdoc />
	public void WritePrimitive<T>(Address address, T value, CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		if (!TryWritePrimitive(address, value, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
		}
	}

	/// <inheritdoc />
	public bool TryReadPrimitiveBatch<T>(MemoryPrimitiveBatchReadRequest<T> request, out ImmutableArray<T> values,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		ImmutableArray<T>.Builder read = ImmutableArray.CreateBuilder<T>(request.Addresses.Length);
		foreach (Address address in request.Addresses)
		{
			if (!TryReadPrimitive(address, out T value, out failure, cancellationToken))
			{
				values = default;
				return false;
			}

			read.Add(value);
		}

		values = read.MoveToImmutable();
		failure = default;
		return true;
	}

	/// <inheritdoc />
	public ImmutableArray<T> ReadPrimitiveBatch<T>(MemoryPrimitiveBatchReadRequest<T> request,
		CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		if (!TryReadPrimitiveBatch(request, out ImmutableArray<T> values, out CheatEngineFailure failure,
				cancellationToken))
		{
			failure.Throw(cancellationToken);
		}

		return values;
	}

	/// <inheritdoc />
	public MemoryPrimitiveBatchReadOutcome<T> ReadPrimitiveBatchDetailed<T>(
		MemoryPrimitiveBatchReadRequest<T> request, CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		throw new NotSupportedException("The Fluent batch builder never reads a detailed outcome.");
	}

	/// <inheritdoc />
	public bool TryWritePrimitiveBatch<T>(MemoryPrimitiveBatchWriteRequest<T> request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		foreach (MemoryAddressValue<T> write in request.Values)
		{
			if (!TryWritePrimitive(write.Address, write.Value, out failure, cancellationToken))
			{
				return false;
			}
		}

		failure = default;
		return true;
	}

	/// <inheritdoc />
	public void WritePrimitiveBatch<T>(MemoryPrimitiveBatchWriteRequest<T> request,
		CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		if (!TryWritePrimitiveBatch(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
		}
	}

	/// <inheritdoc />
	public MemoryPrimitiveBatchWriteOutcome WritePrimitiveBatchDetailed<T>(
		MemoryPrimitiveBatchWriteRequest<T> request, CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		throw new NotSupportedException("The Fluent batch builder never reads a detailed outcome.");
	}

	/// <inheritdoc />
	public bool TryReadBytes(MemoryBytesReadRequest request, out ImmutableArray<byte> bytes,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		byte[] buffer = new byte[request.Length];
		if (!TryReadBytes(request.Address, buffer, out failure))
		{
			bytes = default;
			return false;
		}

		bytes = [.. buffer];
		return true;
	}

	/// <inheritdoc />
	public ImmutableArray<byte> ReadBytes(MemoryBytesReadRequest request, CancellationToken cancellationToken = default)
	{
		if (!TryReadBytes(request, out ImmutableArray<byte> bytes, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
		}

		return bytes;
	}

	/// <inheritdoc />
	public MemoryBytesReadOutcome ReadBytesDetailed(MemoryBytesReadRequest request,
		CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException("The Fluent address builder never reads a detailed outcome.");
	}

	/// <inheritdoc />
	public bool TryWriteBytes(MemoryBytesWriteRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return TryWriteBytes(request.Address, request.Bytes.AsSpan(), out failure);
	}

	/// <inheritdoc />
	public void WriteBytes(MemoryBytesWriteRequest request, CancellationToken cancellationToken = default)
	{
		if (!TryWriteBytes(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
		}
	}

	/// <inheritdoc />
	public bool TryReadString(MemoryStringReadRequest request, [NotNullWhen(true)] out string? value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		byte[] buffer = new byte[request.MaximumLength];
		if (!TryReadBytes(request.Address, buffer, out failure))
		{
			value = null;
			return false;
		}

		value = request.Encoding == MemoryStringEncoding.Utf16
			? new string(BeforeNul(MemoryMarshal.Cast<byte, char>(buffer.AsSpan(0, buffer.Length & ~1))))
			: Encoding.UTF8.GetString(BeforeNul<byte>(buffer));
		return true;
	}

	/// <inheritdoc />
	public string ReadString(MemoryStringReadRequest request, CancellationToken cancellationToken = default)
	{
		if (!TryReadString(request, out string? value, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
		}

		return value;
	}

	/// <inheritdoc />
	public bool TryWriteString(MemoryStringWriteRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		Encoding encoding = request.Encoding == MemoryStringEncoding.Utf16 ? Encoding.Unicode : Encoding.UTF8;
		int terminator = request.Encoding == MemoryStringEncoding.Utf16 ? sizeof(char) : sizeof(byte);
		byte[] encoded = encoding.GetBytes(request.Value);
		byte[] buffer = new byte[Math.Min(encoded.Length + terminator, request.MaximumLength)];
		encoded.AsSpan(0, Math.Min(encoded.Length, buffer.Length)).CopyTo(buffer);
		return TryWriteBytes(request.Address, buffer, out failure);
	}

	/// <inheritdoc />
	public void WriteString(MemoryStringWriteRequest request, CancellationToken cancellationToken = default)
	{
		if (!TryWriteString(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
		}
	}

	/// <inheritdoc />
	public bool TryResolvePointerChain(PointerChainRequest request, out Address address,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		Address current = request.BaseAddress;
		foreach (long offset in request.Offsets)
		{
			if (!TryReadPrimitive(current, out ulong pointer, out failure, cancellationToken))
			{
				address = default;
				return false;
			}

			current = new Address(pointer) + offset;
		}

		address = current;
		failure = default;
		return true;
	}

	/// <inheritdoc />
	public Address ResolvePointerChain(PointerChainRequest request, CancellationToken cancellationToken = default)
	{
		if (!TryResolvePointerChain(request, out Address address, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
		}

		return address;
	}

	/// <inheritdoc />
	public bool TryRead<T>(MemoryReadRequest<T> request, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		return request.Codec.TryRead(this, request.Address, out value, out failure);
	}

	/// <inheritdoc />
	public T Read<T>(MemoryReadRequest<T> request, CancellationToken cancellationToken = default)
	{
		if (!TryRead(request, out T? value, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
		}

		return value;
	}

	/// <inheritdoc />
	public bool TryWrite<T>(MemoryWriteRequest<T> request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return request.Codec.TryWrite(this, request.Address, request.Value, out failure);
	}

	/// <inheritdoc />
	public void Write<T>(MemoryWriteRequest<T> request, CancellationToken cancellationToken = default)
	{
		if (!TryWrite(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
		}
	}

	private static ReadOnlySpan<T> BeforeNul<T>(ReadOnlySpan<T> text)
		where T : unmanaged, IEquatable<T>
	{
		int end = text.IndexOf(default(T));
		return end < 0 ? text : text[..end];
	}

	private static bool TryLocate(Address address, int length, out int offset, out CheatEngineFailure failure)
	{
		ulong start = address.Value - BaseAddress.Value;
		if (address < BaseAddress || start > Size || (ulong) length > Size - start)
		{
			offset = 0;
			failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, Operation,
				"The range lies outside the probe's target memory.", null, CheatEngineHostEffect.NotStarted);
			return false;
		}

		offset = (int) start;
		failure = default;
		return true;
	}
}
