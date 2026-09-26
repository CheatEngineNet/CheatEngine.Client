using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Text;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The DBWIN buffer decoder: the writer's process id, then an ANSI string ended by NUL, kept only for the sandboxed
///     Cheat Engine so the debug output of other processes is never stored.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DebugOutputBufferTests
{
	private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

	[Fact]
	public void TheProcessIdAndTheAnsiTextAreDecoded()
	{
		byte[] buffer = Buffer(4242, [.. "[HostLog] caf"u8, 0xE9, (byte) '\r', (byte) '\n', 0, .. "stale bytes"u8]);

		Assert.True(DebugOutputBuffer.TryDecode(buffer, Windows1252, out DebugOutputMessage message));
		Assert.Equal(new DebugOutputMessage(4242, "[HostLog] café"), message);
	}

	[Fact]
	public void AMessageWithoutTerminatorUsesTheWholeBufferAndAShortBufferIsRejected()
	{
		Assert.True(DebugOutputBuffer.TryDecode(Buffer(7, "tail"u8.ToArray()), Encoding.ASCII, out DebugOutputMessage message));
		Assert.Equal("tail", message.Text);
		Assert.False(DebugOutputBuffer.TryDecode([1, 2, 3], Encoding.ASCII, out _));
	}

	[Fact]
	public void OnlyTheTrackedProcessesAreKept()
	{
		DebugOutputBuffer output = new(Encoding.ASCII);

		bool beforeTracking = output.Accept(Buffer(10, "early"u8.ToArray()));
		output.Track(10);
		bool tracked = output.Accept(Buffer(10, "identify"u8.ToArray()));
		bool other = output.Accept(Buffer(11, "someone else"u8.ToArray()));

		Assert.False(beforeTracking);
		Assert.True(tracked);
		Assert.False(other);
		Assert.Equal([new DebugOutputMessage(10, "identify")], output.Messages);
		Assert.Equal(2, output.Ignored);
		Assert.Equal(0, output.Dropped);
	}

	[Fact]
	public void TheKeptMessagesAreBounded()
	{
		DebugOutputBuffer output = new(Encoding.ASCII);
		output.Track(10);
		byte[] buffer = Buffer(10, "line"u8.ToArray());

		for (int index = 0; index <= DebugOutputBuffer.MaximumMessages; index++)
		{
			output.Accept(buffer);
		}

		Assert.Equal(DebugOutputBuffer.MaximumMessages, output.Messages.Count);
		Assert.Equal(1, output.Dropped);
	}

	private static byte[] Buffer(int processId, byte[] text)
	{
		byte[] buffer = new byte[sizeof(int) + text.Length];
		BinaryPrimitives.WriteInt32LittleEndian(buffer, processId);
		text.CopyTo(buffer, sizeof(int));
		return buffer;
	}
}
