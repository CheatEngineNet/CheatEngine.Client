using CheatEngine.Client.Hotkeys;

namespace CheatEngine.Client.Abstractions.Tests.Hotkeys;

public sealed class HotkeyContractsTests
{
	[Fact]
	public void GesturePreservesTheVirtualKeyAndModifierCombination()
	{
		HotkeyGesture gesture = new(0x70, HotkeyModifiers.Control | HotkeyModifiers.Shift);

		Assert.Equal(0x70, gesture.VirtualKey);
		Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, gesture.Modifiers);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(255)]
	public void GestureAcceptsBothDocumentedVirtualKeyBoundaries(int virtualKey)
	{
		HotkeyGesture gesture = new(virtualKey);

		Assert.Equal(virtualKey, gesture.VirtualKey);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(256)]
	public void GestureRejectsValuesOutsideTheVirtualKeyRange(int virtualKey)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new HotkeyGesture(virtualKey));
	}

	[Fact]
	public void GestureRejectsUnknownModifierBits()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new HotkeyGesture(0x70, (HotkeyModifiers) 16));
	}

	[Theory]
	[InlineData("")]
	[InlineData("  ")]
	public void RegistrationAndEventRejectBlankNames(string name)
	{
		HotkeyGesture gesture = new(0x70);

		Assert.Throws<ArgumentException>(() => new HotkeyRegistration(name, gesture));
		Assert.Throws<ArgumentException>(() => new HotkeyEvent(name, gesture, DateTimeOffset.UtcNow));
	}

	[Fact]
	public void RegistrationAndEventPreserveCopiedGestureAndTimestamp()
	{
		HotkeyGesture gesture = new(0x71, HotkeyModifiers.Alt);
		DateTimeOffset occurredAt = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
		HotkeyRegistration registration = new("plugin.toggle", gesture);
		HotkeyEvent hotkeyEvent = new("plugin.toggle", gesture, occurredAt);

		Assert.Equal("plugin.toggle", registration.Name);
		Assert.Equal(gesture, registration.Gesture);
		Assert.Equal("plugin.toggle", hotkeyEvent.Name);
		Assert.Equal(gesture, hotkeyEvent.Gesture);
		Assert.Equal(occurredAt, hotkeyEvent.OccurredAt);
	}
}
