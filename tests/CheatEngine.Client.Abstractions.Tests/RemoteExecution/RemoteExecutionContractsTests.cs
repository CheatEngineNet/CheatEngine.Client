using CheatEngine.Client.RemoteExecution;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Abstractions.Tests.RemoteExecution;

public sealed class RemoteExecutionContractsTests
{
	[Fact]
	public void DllInjectionRequestPreservesAnAbsoluteDllPath()
	{
		string path = Path.Combine(Path.GetTempPath(), "client-plugin.dll");

		RemoteDllInjectionRequest request = new(path);

		Assert.Equal(path, request.LibraryPath);
	}

	[Theory]
	[InlineData("")]
	[InlineData("relative.dll")]
	[InlineData("C:\\plugins\\client.txt")]
	public void DllInjectionRequestRejectsRelativeOrNonDllPaths(string path)
	{
		Assert.Throws<ArgumentException>(() => new RemoteDllInjectionRequest(path));
	}

	[Fact]
	public void RemoteCallRequestCopiesParameterBytesAndPreservesAPositiveTimeout()
	{
		byte[] parameters = [1, 2, 3];
		RemoteCallRequest request = new(0x401000, parameters, TimeSpan.FromMilliseconds(25));
		parameters[0] = 99;

		Assert.Equal((Address) 0x401000, request.EntryPoint);
		Assert.Equal([1, 2, 3], request.Parameters);
		Assert.Equal(TimeSpan.FromMilliseconds(25), request.Timeout);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void RemoteCallRequestRejectsANonPositiveTimeout(int milliseconds)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteCallRequest(
			0x401000, [], TimeSpan.FromMilliseconds(milliseconds)));
	}

	[Fact]
	public void RemoteCallResultCopiesOutputBytes()
	{
		byte[] output = [0x10, 0x20];
		RemoteCallResult result = new(42, output);
		output[0] = 0xCC;

		Assert.Equal(42UL, result.ReturnValue);
		Assert.Equal([0x10, 0x20], result.Output);
	}
}
