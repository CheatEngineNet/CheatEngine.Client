# CheatEngine.Client.LivePlugin.Coexistence.PluginCollision

This plugin intentionally exports `cheatengine_client_coexistence_a_collision`, the same Lua name as Plugin A. It is
not a third positive coexistence participant. In the controlled protocol, enable A first, confirm its marker, then
attempt to enable this plugin. The host-visible result must be recorded and Plugin A's marker must remain callable.

Its client module registers the generated Lua module itself, through `ILuaClient.TryRegisterModule`, so the expected
enable failure states what the Client reported:
`Collision=Refused; Kind=OperationRejected; HostEffect=NotApplied; Operation=Lua.RegisterModule`. `NotApplied` means the
CheatEngine.SDK registration ran its `RejectExisting` preflight, found A's global and published nothing. Any other
classification, or an enable that succeeds, is a failed observation.

Qualification scenario Q16 has two host parts. This plugin covers the first one: a collision is refused before any
write, so the established owner keeps its global. The second one, "a third-party replacement survives disable", needs no
extra plugin: an explicit operator script replaces one of Plugin A's globals and A is then disabled (see the subsection
"Third-party replacement survives disable (Q16)" of the Coexistence README one folder up).

Do not load it before A, rename the collision global, or use a manual Lua assignment to repair a failed result. The
operator script of the second part is a scripted protocol step, not a repair.
