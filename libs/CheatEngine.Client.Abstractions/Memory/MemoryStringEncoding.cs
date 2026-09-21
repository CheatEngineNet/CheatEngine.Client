namespace CheatEngine.Client.Memory;

/// <summary>Identifies the target representation used by a bounded string operation.</summary>
public enum MemoryStringEncoding
{
	/// <summary>Uses the UTF-8 representation accepted by Cheat Engine's narrow string globals.</summary>
	Utf8 = 0,

	/// <summary>Uses the UTF-16 representation accepted by Cheat Engine's wide string globals.</summary>
	Utf16 = 1
}
