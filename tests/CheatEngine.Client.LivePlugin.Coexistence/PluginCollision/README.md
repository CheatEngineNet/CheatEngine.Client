# CheatEngine.Client.LivePlugin.Coexistence.PluginCollision

This plugin intentionally exports `cheatengine_client_coexistence_a_collision`, the same Lua name as Plugin A. It is
not a third positive coexistence participant. In the controlled protocol, enable A first, confirm its marker, then
attempt to enable this plugin. The host-visible result must be recorded and Plugin A's marker must remain callable.

Do not load it before A, rename the collision global, or use a manual Lua assignment to repair a failed result.
