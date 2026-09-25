using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.AotProbe;

/// <summary>Encodes an <see cref="AotProbePoint" /> as two little-endian 32-bit integers.</summary>
internal sealed class AotProbePointCodec : IMemoryCodec<AotProbePoint>
{
	/// <inheritdoc />
	public bool TryRead(IMemoryReadContext context, Address address, [MaybeNullWhen(false)] out AotProbePoint value,
		out CheatEngineFailure failure)
	{
		ArgumentNullException.ThrowIfNull(context);
		Span<byte> buffer = stackalloc byte[2 * sizeof(int)];
		if (!context.TryReadBytes(address, buffer, out failure))
		{
			value = default;
			return false;
		}

		value = new AotProbePoint(BinaryPrimitives.ReadInt32LittleEndian(buffer),
			BinaryPrimitives.ReadInt32LittleEndian(buffer[sizeof(int)..]));
		return true;
	}

	/// <inheritdoc />
	public bool TryWrite(IMemoryWriteContext context, Address address, in AotProbePoint value,
		out CheatEngineFailure failure)
	{
		ArgumentNullException.ThrowIfNull(context);
		Span<byte> buffer = stackalloc byte[2 * sizeof(int)];
		BinaryPrimitives.WriteInt32LittleEndian(buffer, value.X);
		BinaryPrimitives.WriteInt32LittleEndian(buffer[sizeof(int)..], value.Y);
		return context.TryWriteBytes(address, buffer, out failure);
	}
}

/// <summary>A custom value type that only a codec can read or write.</summary>
/// <param name="X">The first coordinate.</param>
/// <param name="Y">The second coordinate.</param>
internal readonly record struct AotProbePoint(int X, int Y);
