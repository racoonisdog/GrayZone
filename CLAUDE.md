# GrayZone Claude Entry

Read `AGENTS.md` in this directory first, then follow the same GrayZone startup reads, Deprecated-folder rule, Unity/SVN guardrails, and durable worklog rules.

## Language Rule

- Answer the user in Korean by default.
- Ask approval, permission, and confirmation questions in Korean.
- Keep code identifiers, command names, file paths, and quoted tool output in their original language.

## WorkList Rule

When the user asks to add an item to the worklist, use:

`\\100.85.118.17\GrayZone\User\03_Programming\WorkList`

Add a focused Markdown item under `진행예정`, `진행중`, or `완료`. Default to `진행예정` unless the user clearly specifies another status.
Do not infer or invent planned work from context alone; add `진행예정` items only when the user explicitly asks or answers what should be tracked next.
