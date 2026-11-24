# Working with agents

Markdown is the native format of AI agents: it is plain text, diff-friendly and structured with headings.
That is why we keep our context, specs and reviews as `.md` files instead of PDF or DOCX.

## Writing a good context file

- Start with the **goal** in one sentence.
- List the **constraints** the agent must respect.
- Link to related files instead of copying them: see the [release checklist](release.md).
- Keep a short **decisions** section so the next agent knows what was already settled.

## Review loop

1. The agent updates `context.md` while it works.
2. Marleo shows the change instantly (watch the green dot next to the file name).
3. You read, highlight the terms that matter (for example *agent*, *review*, *decision*) and press
   <kbd>Ctrl</kbd>+<kbd>E</kbd> to correct something in VS Code.

> [!NOTE]
> Save your favourite highlight terms as a **set** (bookmark icon) and load them on any document.

Back to the [handbook](../README.md).
