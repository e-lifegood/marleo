// Multi-color word highlighting — like Notepad++ "Style all occurrences of token", but with
// 10 colors, live counts, next/previous navigation, scrollbar markers and saved sets.
import { icon } from "./icons.js";

export const COLORS = 10;
const SETS_KEY = "marleo.sets";
const SKIP = "mark.hl, svg, .mermaid, script, style, .code-head, .frontmatter > summary";

const escapeRe = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

/** @typedef {{ text: string, color: number, cs: boolean, ww: boolean, marks?: HTMLElement[], cur?: number }} Term */

export class Marks {
  /**
   * @param {{ article: HTMLElement, scroller: HTMLElement, lane: HTMLElement, bar: HTMLElement, chips: HTMLElement, onChange?: () => void }} els
   */
  constructor(els) {
    Object.assign(this, els);
    /** @type {Term[]} */
    this.terms = [];
    /** @type {Term | null} temporary Ctrl+F term */
    this.find = null;
    this.chips.addEventListener("click", (e) => this.#onChipClick(e));
    new ResizeObserver(() => this.renderLane()).observe(this.scroller);
  }

  // ---------------------------------------------------------------- terms

  add(text, opts = {}) {
    text = text.replace(/\s+/g, " ").trim().slice(0, 200);
    if (!text) return;
    const existing = this.terms.findIndex((t) => t.text.toLowerCase() === text.toLowerCase());
    if (existing >= 0) { this.next(existing); return; }
    const used = new Set(this.terms.map((t) => t.color));
    let color = opts.color;
    if (color == null) {
      color = 0;
      while (used.has(color) && color < COLORS - 1) color++;
      if (used.has(color)) color = this.terms.length % COLORS;
    }
    this.terms.push({ text, color, cs: !!opts.cs, ww: !!opts.ww });
    this.apply();
  }

  remove(i) { this.terms.splice(i, 1); this.apply(); }

  clear() { this.terms = []; this.apply(); }

  load(terms) {
    this.terms = terms.map((t, i) => ({ text: t.text, color: t.color ?? i % COLORS, cs: !!t.cs, ww: !!t.ww }));
    this.apply();
  }

  serialize() { return this.terms.map(({ text, color, cs, ww }) => ({ text, color, cs, ww })); }

  setFind(text) {
    this.find = text ? { text, color: -1, cs: false, ww: false } : null;
    this.apply();
    return this.find?.marks?.length ?? 0;
  }

  // ---------------------------------------------------------------- DOM work

  /** Removes all marks and re-applies every term. Call after each render. */
  apply() {
    const root = this.article;
    for (const m of root.querySelectorAll("mark.hl")) m.replaceWith(m.textContent);
    root.normalize();

    for (const t of this.terms) {
      t.marks = [];
      t.cur = -1;
      wrap(root, regexFor(t), `hl hl-${t.color}`, t.marks, SKIP);
    }
    if (this.find) {
      this.find.marks = [];
      this.find.cur = -1;
      wrap(root, regexFor(this.find), "hl hl-find", this.find.marks, "mark.hl-find, svg, .mermaid, script, style");
    }
    this.renderChips();
    this.renderLane();
    this.onChange?.();
  }

  /** Jumps to the next (dir = 1) or previous (dir = -1) occurrence of term i ("find" for the find term). */
  next(i, dir = 1) {
    const t = i === "find" ? this.find : this.terms[i];
    if (!t?.marks?.length) return;
    const n = t.marks.length;
    if (t.cur < 0 || !t.marks[t.cur]?.isConnected) {
      // start from what is on screen
      const top = this.scroller.getBoundingClientRect().top + 60;
      const idx = t.marks.findIndex((m) => m.getBoundingClientRect().top > top);
      t.cur = dir > 0 ? (idx < 0 ? 0 : idx) : (idx <= 0 ? n - 1 : idx - 1);
    } else {
      t.cur = (t.cur + dir + n) % n;
    }
    for (const m of this.article.querySelectorAll("mark.hl-current")) m.classList.remove("hl-current");
    const m = t.marks[t.cur];
    m.classList.add("hl-current");
    m.scrollIntoView({ block: "center", behavior: "smooth" });
    this.renderChips();
    return { index: t.cur, total: n };
  }

  // ---------------------------------------------------------------- UI

  renderChips() {
    this.bar.hidden = this.terms.length === 0;
    this.chips.innerHTML = this.terms.map((t, i) => {
      const count = t.marks?.length ?? 0;
      const pos = t.cur >= 0 && count ? `${t.cur + 1}/${count}` : String(count);
      return `<span class="chip hl-${t.color}${count ? "" : " chip-zero"}" data-i="${i}">
        <button class="chip-dot" data-act="color" title="Change color"></button>
        <button class="chip-label" data-act="next" title="Next occurrence${i < 9 ? ` (Alt+${i + 1})` : ""}">${escapeHtml(t.text)}</button>
        <span class="chip-count" title="Occurrences">${pos}</span>
        <button class="chip-btn" data-act="prev" title="Previous${i < 9 ? ` (Alt+Shift+${i + 1})` : ""}">${icon("up", 13)}</button>
        <button class="chip-btn" data-act="next" title="Next">${icon("down", 13)}</button>
        <button class="chip-btn chip-toggle${t.cs ? " on" : ""}" data-act="cs" title="Match case">Aa</button>
        <button class="chip-btn chip-toggle${t.ww ? " on" : ""}" data-act="ww" title="Whole word"><u>ab</u></button>
        <button class="chip-btn" data-act="remove" title="Remove">${icon("close", 13)}</button>
      </span>`;
    }).join("");
  }

  #onChipClick(e) {
    const btn = e.target.closest("button[data-act]");
    const chip = e.target.closest(".chip");
    if (!btn || !chip) return;
    const i = Number(chip.dataset.i);
    const t = this.terms[i];
    switch (btn.dataset.act) {
      case "next": this.next(i, 1); break;
      case "prev": this.next(i, -1); break;
      case "remove": this.remove(i); break;
      case "cs": t.cs = !t.cs; this.apply(); break;
      case "ww": t.ww = !t.ww; this.apply(); break;
      case "color": t.color = (t.color + 1) % COLORS; this.apply(); break;
    }
  }

  /** Overview ruler: one tick per occurrence, drawn over the scrollbar. */
  renderLane() {
    const lane = this.lane;
    const all = [...this.terms, ...(this.find ? [this.find] : [])];
    if (!all.some((t) => t.marks?.length)) { lane.innerHTML = ""; return; }
    const total = this.scroller.scrollHeight;
    const h = lane.clientHeight;
    if (!total || !h) return;
    const base = this.scroller.getBoundingClientRect().top - this.scroller.scrollTop;
    const seen = new Set();
    let html = "";
    for (const t of all) {
      const cls = t.color < 0 ? "tick-find" : `tick-${t.color}`;
      for (const m of t.marks ?? []) {
        const y = Math.min(h - 3, Math.round(((m.getBoundingClientRect().top - base) / total) * h));
        const key = cls + ":" + y;
        if (seen.has(key)) continue;
        seen.add(key);
        html += `<i class="tick ${cls}" style="top:${y}px"></i>`;
      }
    }
    lane.innerHTML = html;
  }
}

function regexFor(t) {
  let src = escapeRe(t.text);
  if (t.ww) src = `(?<![\\p{L}\\p{N}_])${src}(?![\\p{L}\\p{N}_])`;
  return new RegExp(src, t.cs ? "gu" : "giu");
}

/** Wraps every match of `re` in text nodes under `root` with <mark class=…>. */
function wrap(root, re, className, out, skipSelector) {
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
    acceptNode(n) {
      const p = n.parentElement;
      if (!p || !n.nodeValue.trim() || p.closest(skipSelector)) return NodeFilter.FILTER_REJECT;
      re.lastIndex = 0;
      return re.test(n.nodeValue) ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT;
    },
  });
  const nodes = [];
  while (walker.nextNode()) nodes.push(walker.currentNode);

  for (const node of nodes) {
    const text = node.nodeValue;
    const frag = document.createDocumentFragment();
    let last = 0;
    re.lastIndex = 0;
    for (let m; (m = re.exec(text)); ) {
      if (!m[0].length) { re.lastIndex++; continue; }
      if (m.index > last) frag.append(text.slice(last, m.index));
      const mark = document.createElement("mark");
      mark.className = className;
      mark.textContent = m[0];
      frag.append(mark);
      out.push(mark);
      last = m.index + m[0].length;
    }
    if (last < text.length) frag.append(text.slice(last));
    node.replaceWith(frag);
  }
}

function escapeHtml(s) {
  return s.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
}

// ---------------------------------------------------------------- saved sets (localStorage)

export const Sets = {
  all() {
    try { return JSON.parse(localStorage.getItem(SETS_KEY) || "{}"); } catch { return {}; }
  },
  save(name, terms) {
    const all = Sets.all();
    all[name] = terms;
    try { localStorage.setItem(SETS_KEY, JSON.stringify(all)); } catch { /* storage unavailable */ }
  },
  remove(name) {
    const all = Sets.all();
    delete all[name];
    try { localStorage.setItem(SETS_KEY, JSON.stringify(all)); } catch { /* storage unavailable */ }
  },
};
