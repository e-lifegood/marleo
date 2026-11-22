// Markdown → sanitized HTML.
// markdown-it (CommonMark + GFM tables/strikethrough/autolinks) with a few small built-in rules:
//   • data-line on every block  → "Edit in VS Code" jumps to the line you are reading
//   • GitHub-style heading ids  → #anchors and the table of contents
//   • task lists  - [ ] / - [x]
//   • GitHub alerts  > [!NOTE]  > [!TIP]  > [!IMPORTANT]  > [!WARNING]  > [!CAUTION]
//   • YAML front matter shown as a collapsible block
//   • fenced code highlighted with highlight.js; ```mermaid kept for the diagram renderer
import { icon } from "./icons.js";

const md = window.markdownit({ html: true, linkify: true, typographer: false });
const esc = md.utils.escapeHtml;

// --- data-line on block tokens -------------------------------------------------------------
md.core.ruler.push("mdv_lines", (state) => {
  const offset = state.env.lineOffset || 0;
  for (const t of state.tokens) {
    if (t.map && t.nesting >= 0 && t.type !== "inline") t.attrSet("data-line", String(t.map[0] + 1 + offset));
  }
});

// --- heading ids (GitHub style) + collect headings -----------------------------------------
export function slugify(text) {
  return text.trim().toLowerCase().replace(/[^\p{L}\p{N}\s_-]/gu, "").replace(/\s/g, "-");
}

md.core.ruler.push("mdv_headings", (state) => {
  const used = new Map();
  const tokens = state.tokens;
  for (let i = 0; i < tokens.length; i++) {
    if (tokens[i].type !== "heading_open") continue;
    const inline = tokens[i + 1];
    const text = (inline.children || [])
      .filter((c) => c.type === "text" || c.type === "code_inline")
      .map((c) => c.content)
      .join("");
    let slug = slugify(text) || "section";
    if (used.has(slug)) {
      const n = used.get(slug) + 1;
      used.set(slug, n);
      slug = `${slug}-${n}`;
    } else used.set(slug, 0);
    tokens[i].attrSet("id", slug);
  }
});

// --- task lists ----------------------------------------------------------------------------
md.core.ruler.push("mdv_tasks", (state) => {
  const tokens = state.tokens;
  for (let i = 2; i < tokens.length; i++) {
    const t = tokens[i];
    if (t.type !== "inline" || tokens[i - 1].type !== "paragraph_open" || tokens[i - 2].type !== "list_item_open") continue;
    const first = t.children?.[0];
    const m = first?.type === "text" && /^\[([ xX])\][ \t]/.exec(first.content);
    if (!m) continue;
    first.content = first.content.slice(m[0].length);
    const box = new state.Token("html_inline", "", 0);
    box.content = `<input type="checkbox" class="task" disabled${m[1] === " " ? "" : " checked"}>`;
    t.children.unshift(box);
    tokens[i - 2].attrJoin("class", "task-item");
  }
});

// --- GitHub alerts -------------------------------------------------------------------------
const ALERTS = {
  note: ["Note", "info"],
  tip: ["Tip", "lightbulb"],
  important: ["Important", "message"],
  warning: ["Warning", "alert"],
  caution: ["Caution", "octagon"],
};
md.core.ruler.push("mdv_alerts", (state) => {
  const tokens = state.tokens;
  for (let i = 0; i < tokens.length - 2; i++) {
    if (tokens[i].type !== "blockquote_open" || tokens[i + 1].type !== "paragraph_open") continue;
    const inline = tokens[i + 2];
    const first = inline.children?.[0];
    const m = first?.type === "text" && /^\[!(note|tip|important|warning|caution)\][ \t]*/i.exec(first.content);
    if (!m) continue;
    const kind = m[1].toLowerCase();
    first.content = first.content.slice(m[0].length);
    if (!first.content) {
      inline.children.shift();
      if (inline.children[0]?.type === "softbreak") inline.children.shift();
    }
    const [title, ico] = ALERTS[kind];
    const head = new state.Token("html_inline", "", 0);
    head.content = `<span class="alert-title">${icon(ico, 15)}${title}</span>`;
    inline.children.unshift(head);
    tokens[i].attrJoin("class", `alert alert-${kind}`);
  }
});

// --- code fences ---------------------------------------------------------------------------
md.renderer.rules.fence = (tokens, idx) => {
  const t = tokens[idx];
  const lang = (t.info || "").trim().split(/\s+/)[0].toLowerCase();
  const line = t.attrGet("data-line") || "";
  if (lang === "mermaid") {
    return `<div class="mermaid-block" data-line="${line}"><pre class="mermaid">${esc(t.content)}</pre></div>\n`;
  }
  let body;
  if (lang && window.hljs?.getLanguage(lang)) {
    try { body = window.hljs.highlight(t.content, { language: lang, ignoreIllegals: true }).value; }
    catch { body = esc(t.content); }
  } else body = esc(t.content);
  return `<div class="code-block" data-line="${line}"><div class="code-head"><span class="code-lang">${esc(lang)}</span>` +
    `<button class="code-copy" type="button" title="Copy code">${icon("copy", 14)}<span>Copy</span></button></div>` +
    `<pre class="hljs"><code>${body}</code></pre></div>\n`;
};

// Wide tables scroll horizontally instead of breaking the layout.
md.renderer.rules.table_open = (tokens, idx, opts, env, self) => `<div class="table-wrap">${self.renderToken(tokens, idx, opts)}`;
md.renderer.rules.table_close = (tokens, idx, opts, env, self) => `${self.renderToken(tokens, idx, opts)}</div>`;

// --- public API ----------------------------------------------------------------------------
const FRONT_MATTER = /^---[ \t]*\r?\n([\s\S]*?)\r?\n(?:---|\.\.\.)[ \t]*(?:\r?\n|$)/;

/** @returns {{ html: string, hasMermaid: boolean }} */
export function renderMarkdown(source) {
  let src = source.replace(/^﻿/, "");
  let front = "";
  let lineOffset = 0;
  const fm = FRONT_MATTER.exec(src);
  if (fm) {
    lineOffset = fm[0].split("\n").length - 1;
    src = src.slice(fm[0].length);
    let yaml = esc(fm[1]);
    try { if (window.hljs?.getLanguage("yaml")) yaml = window.hljs.highlight(fm[1], { language: "yaml" }).value; } catch { /* keep plain */ }
    front = `<details class="frontmatter" data-line="1"><summary>Front matter</summary><pre class="hljs"><code>${yaml}</code></pre></details>`;
  }

  const raw = front + md.render(src, { lineOffset });
  // Documents may contain raw HTML; scripts, event handlers, iframes and <style> are stripped.
  const html = window.DOMPurify.sanitize(raw, { FORBID_TAGS: ["style"], ADD_ATTR: ["target"] });
  return { html, hasMermaid: /class="mermaid"/.test(html) };
}

/**
 * Makes images/media inside the document load from disk:
 * relative paths are resolved against the document folder and served by the host as https://local.mdv/…
 */
export function fixMedia(article, docDir) {
  if (!docDir) return;
  const dir = docDir.replace(/\\/g, "/").replace(/\/?$/, "/");
  const base = "https://local.mdv/" + (dir.startsWith("//") ? "UNC/" + dir.slice(2) : dir);
  for (const el of article.querySelectorAll("img[src], video[src], audio[src], source[src]")) {
    const src = el.getAttribute("src");
    if (!src || /^(https?:|data:|blob:)/i.test(src)) continue;
    let url;
    if (/^file:/i.test(src)) url = "https://local.mdv/" + decodeURI(src.replace(/^file:\/+/i, ""));
    else if (/^[a-z]:[\\/]/i.test(src)) url = "https://local.mdv/" + src.replace(/\\/g, "/");
    else url = new URL(src.replace(/\\/g, "/"), encodeURI(base)).href;
    el.setAttribute("src", url);
    if (el.tagName === "IMG") el.loading = "lazy";
  }
}

let mermaidLoading = null;
function loadMermaid() {
  mermaidLoading ??= new Promise((resolve, reject) => {
    const s = document.createElement("script");
    s.src = "vendor/mermaid.min.js";
    s.onload = () => resolve(window.mermaid);
    s.onerror = reject;
    document.head.append(s);
  });
  return mermaidLoading;
}

/** Renders ```mermaid blocks. The script (≈5 MB) is only loaded when a document actually has diagrams. */
export async function renderMermaid(article, dark) {
  const blocks = [...article.querySelectorAll("pre.mermaid")];
  if (!blocks.length) return;
  const mermaid = await loadMermaid();
  mermaid.initialize({ startOnLoad: false, securityLevel: "strict", theme: dark ? "dark" : "default" });
  for (const b of blocks) {
    b.dataset.src ??= b.textContent;
    b.removeAttribute("data-processed");
    b.textContent = b.dataset.src;
  }
  try { await mermaid.run({ nodes: blocks, suppressErrors: true }); } catch (e) { console.warn(e); }
}
