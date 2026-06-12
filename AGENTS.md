# AGENTS.md

## Unity Project

This repository is a Unity project located at:

`C:\Users\user\GrayZone`

Use the Unity-specific CLI tools installed on this machine when working on Unity-related tasks.

## Unity Asset Inspection

Prefer `unity-scanner` before manually reading large Unity YAML files.

Use it for:
- finding scenes, prefabs, assets, and script references
- inspecting `.unity`, `.prefab`, and `.asset` files
- checking which GameObjects/components exist in a scene or prefab
- finding references to scripts or assets

Examples:

```powershell
unity-scanner list -p C:\Users\user\GrayZone Assets --depth 2
unity-scanner search -p C:\Users\user\GrayZone Assets --name Player --type prefab,scene
unity-scanner search -p C:\Users\user\GrayZone Assets --component GameManager --type scene,prefab
unity-scanner read -p C:\Users\user\GrayZone "Assets\0.Scenes\JungHyeon\Scenes\GameScene.unity" --depth 3
unity-scanner refs -p C:\Users\user\GrayZone "Assets\1.Scripts\Manager\GameManager.cs" Assets
```

## Unity Editor Control

Use `unity-cli` for Unity Editor validation when the Editor is open and the Connector package is available.

Always pass the project path explicitly:

```powershell
unity-cli --project C:\Users\user\GrayZone status
```

After C# script changes, prefer this validation flow:

```powershell
unity-cli --project C:\Users\user\GrayZone editor refresh --compile
unity-cli --project C:\Users\user\GrayZone console --type error,warning --stacktrace user
```

Useful commands:

```powershell
unity-cli --project C:\Users\user\GrayZone console --lines 50 --type error,warning,log
unity-cli --project C:\Users\user\GrayZone editor play --wait
unity-cli --project C:\Users\user\GrayZone editor stop
unity-cli --project C:\Users\user\GrayZone test
unity-cli --project C:\Users\user\GrayZone test --mode PlayMode
```

If `unity-cli` reports `no Unity instances found`, do not treat that as a code failure. It means Unity Editor is not currently connected. Continue with static checks and report that Editor validation could not be completed.

## PATH Note

If `unity-cli` or `unity-scanner` is not found in PowerShell, temporarily add the install paths:

```powershell
$env:Path = "$env:LOCALAPPDATA\unity-cli;$env:LOCALAPPDATA\unity-scanner;$env:Path"
```

## Unity File Safety

Do not edit generated Unity folders unless explicitly asked:

- `Library/`
- `Temp/`
- `obj/`
- `Logs/`
- `UserSettings/`

Prefer editing source files under:

- `Assets/1.Scripts/`
- `Assets/0.Scenes/`
- `Assets/2.Prefabs/`
- `Packages/manifest.json`
- `ProjectSettings/`

Avoid touching third-party assets under `Assets/3.Resources/` and `Assets/4.ThirdParty/` unless the task specifically requires it.

## Session Wrap-Up Log

When the user indicates that the current session is ending, create a concise Markdown summary in:

`C:\Users\user\GrayZone\SessionNotes`

Treat Korean phrases such as `세션 종료`, `이 대화는 여기까지`, `오늘은 여기까지`, `여기까지 하자`, `마무리`, `종료`, or similar end-of-session wording as a request to write this log before finishing.

If the `SessionNotes` folder does not exist, create it. Use `SessionNotes` for agent-written session summaries; do not use Unity's generated `Logs` folder for this purpose.

Use this filename format:

`YYYY-MM-DD-short-topic-title.md`

Examples:

- `2026-06-11-unity-cli-setup.md`
- `2026-06-11-player-health-debug.md`

Keep the title short, descriptive, lowercase when practical, and use hyphens instead of spaces. The date should use the current local date.

The log should be brief and useful for continuing later. Include:

- Summary
- Files changed
- Commands or checks run
- Current status
- Next steps

Do not create a session log for casual thanks or ordinary short replies unless the user clearly sounds like they are ending or wrapping up the session.
