// Sidebar: folder tree of Markdown files + quick filter (Ctrl+P).
import { icon } from "./icons.js";

export class Tree {
  constructor({ el, filter, rootName, onOpen }) {
    Object.assign(this, { el, filter, rootName, onOpen });
    this.data = null;
    this.root = "";
    this.current = "";
    this.expanded = new Set();
    this.selected = 0;

    el.addEventListener("click", (e) => {
      const row = e.target.closest(".tree-row");
      if (!row) return;
      if (row.dataset.dir) {
        const p = row.dataset.path;
        this.expanded.has(p) ? this.expanded.delete(p) : this.expanded.add(p);
        this.render();
      } else onOpen(row.dataset.path, e.ctrlKey);
    });
    el.addEventListener("auxclick", (e) => {
      const row = e.target.closest(".tree-row:not([data-dir])");
      if (row && e.button === 1) onOpen(row.dataset.path, true);
    });

    filter.addEventListener("input", () => { this.selected = 0; this.render(); });
    filter.addEventListener("keydown", (e) => {
      const rows = [...el.querySelectorAll(".tree-row:not([data-dir])")];
      if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        e.preventDefault();
        this.selected = Math.max(0, Math.min(rows.length - 1, this.selected + (e.key === "ArrowDown" ? 1 : -1)));
        this.#markSelected(rows);
      } else if (e.key === "Enter") {
        const row = rows[this.selected] ?? rows[0];
        if (row) { onOpen(row.dataset.path, e.ctrlKey); filter.value = ""; this.render(); filter.blur(); }
      } else if (e.key === "Escape") {
        filter.value = "";
        this.render();
        filter.blur();
      }
    });
  }

  setData(root, tree) {
    if (root !== this.root) this.expanded.clear();
    this.root = root;
    this.data = tree;
    this.rootName.textContent = tree?.name ?? "";
    this.rootName.title = root;
    // small trees: expand everything
    if (countFiles(tree) <= 40) walk(tree, (n) => n.dir && this.expanded.add(n.path));
    this.revealCurrent();
    this.render();
  }

  setCurrent(path) {
    this.current = path;
    this.revealCurrent();
    this.render();
  }

  revealCurrent() {
    if (!this.current || !this.data) return;
    const lower = this.current.toLowerCase();
    walk(this.data, (n) => {
      if (n.dir && lower.startsWith(n.path.toLowerCase().replace(/\\?$/, "\\"))) this.expanded.add(n.path);
    });
  }

  render() {
    if (!this.data) { this.el.innerHTML = ""; return; }
    const q = this.filter.value.trim().toLowerCase();
    if (q) {
      const hits = [];
      walk(this.data, (n) => {
        if (!n.dir && rel(this.root, n.path).toLowerCase().includes(q)) hits.push(n);
      });
      this.el.innerHTML = hits.length
        ? hits.slice(0, 300).map((n) => this.#fileRow(n, 0, rel(this.root, n.path))).join("")
        : `<div class="tree-empty">No matches</div>`;
      this.#markSelected([...this.el.querySelectorAll(".tree-row:not([data-dir])")]);
      return;
    }
    const kids = this.data.children ?? [];
    this.el.innerHTML = kids.length ? this.#rows(kids, 0) : `<div class="tree-empty">No Markdown files</div>`;
    this.el.querySelector(".tree-row.active")?.scrollIntoView({ block: "nearest" });
  }

  #rows(nodes, depth) {
    let html = "";
    for (const n of nodes) {
      if (n.dir) {
        const open = this.expanded.has(n.path);
        html += `<div class="tree-row tree-dir${open ? " open" : ""}" data-dir="1" data-path="${attr(n.path)}" style="--d:${depth}" title="${attr(n.path)}">
          <span class="tree-chev">${icon("chevron", 14)}</span>${icon(open ? "folderOpen" : "folder", 15)}<span class="tree-name">${esc(n.name)}</span></div>`;
        if (open) html += this.#rows(n.children ?? [], depth + 1);
      } else html += this.#fileRow(n, depth);
    }
    return html;
  }

  #fileRow(n, depth, hint) {
    const active = this.current && n.path.toLowerCase() === this.current.toLowerCase();
    const name = n.name.replace(/\.(md|markdown|mdown|mkd|mdx)$/i, "");
    return `<div class="tree-row tree-file${active ? " active" : ""}" data-path="${attr(n.path)}" style="--d:${depth}" title="${attr(n.path)}">
      <span class="tree-chev"></span>${icon("file", 15)}<span class="tree-name">${esc(name)}</span>${hint && hint !== n.name ? `<span class="tree-hint">${esc(hint)}</span>` : ""}</div>`;
  }

  #markSelected(rows) {
    rows.forEach((r, i) => r.classList.toggle("selected", i === this.selected));
    rows[this.selected]?.scrollIntoView({ block: "nearest" });
  }
}

function walk(n, fn) {
  if (!n) return;
  fn(n);
  for (const c of n.children ?? []) walk(c, fn);
}
function countFiles(n) {
  let c = 0;
  walk(n, (x) => { if (!x.dir) c++; });
  return c;
}
function rel(root, p) {
  const r = root.replace(/\\?$/, "\\");
  return p.toLowerCase().startsWith(r.toLowerCase()) ? p.slice(r.length) : p;
}
const esc = (s) => s.replace(/[&<>]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" })[c]);
const attr = (s) => esc(s).replace(/"/g, "&quot;");
