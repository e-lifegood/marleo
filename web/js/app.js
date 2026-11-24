// Marleo web UI — wires the page to the native host (MainForm.cs) and to the feature modules.
import { hydrateIcons, icon } from "./icons.js";
import { renderMarkdown, fixMedia, renderMermaid } from "./render.js";
import { Marks, Sets } from "./marks.js";
import { Toc } from "./toc.js";
import { Tree } from "./tree.js";

const $ = (id) => document.getElementById(id);
const host = window.chrome?.webview;
const post = (msg) => host?.postMessage(msg);
const html = document.documentElement;

window.addEventListener("error", (e) => post({ type: "jserror", message: `${e.message} @ ${e.filename}:${e.lineno}` }));
window.addEventListener("unhandledrejection", (e) => post({ type: "jserror", message: String(e.reason?.stack || e.reason) }));

const store = {
  get(key, fallback) {
    try { const v = localStorage.getItem("marleo." + key); return v == null ? fallback : JSON.parse(v); } catch { return fallback; }
  },
  set(key, value) {
    try { localStorage.setItem("marleo." + key, JSON.stringify(value)); } catch { /* storage unavailable */ }
  },
};

const els = {
  layout: $("layout"), scroller: $("scroller"), article: $("doc"), banner: $("banner"), crumbs: $("crumbs"),
  lane: $("lane"), markbar: $("markbar"), chips: $("chips"), markInput: $("mark-input"),
  findbar: $("findbar"), findInput: $("find-input"), findCount: $("find-count"),
  menu: $("menu"), toast: $("toast"), help: $("help"), treeFilter: $("tree-filter"),
};

const state = {
  doc: null,
  history: [],
  hIndex: -1,
  navigating: false,
  themePref: store.get("theme", "auto"),
  themeOverride: null,
  zoom: store.get("zoom", 1),
  sidebar: false,
  toc: store.get("toc", true),
  version: "",
  startupMs: null,
  settingsPath: "",
};

hydrateIcons();

const marks = new Marks({ article: els.article, scroller: els.scroller, lane: els.lane, bar: els.markbar, chips: els.chips });
const toc = new Toc({ list: $("toc-list"), panel: $("toc"), scroller: els.scroller, article: els.article });
const tree = new Tree({
  el: $("tree"), filter: els.treeFilter, rootName: $("root-name"),
  onOpen: (path, newWindow) => post({ type: "open", path, newWindow }),
});

// ================================================================== host messages

host?.addEventListener("message", (e) => onHost(e.data));

function onHost(m) {
  switch (m.type) {
    case "init":
      state.version = m.version;
      state.settingsPath = m.settingsPath;
      if (m.theme === "dark" || m.theme === "light") { state.themeOverride = m.theme; applyTheme(); }
      if (m.highlights?.length) for (const h of m.highlights) marks.add(h);
      setSidebar(m.folderMode);
      if (m.error) { showEmpty(null); showBanner(m.error); }
      break;
    case "doc": showDoc(m); break;
    case "tree":
      tree.setData(m.root, m.tree);
      if (m.truncated) toast("Large folder — the file tree was truncated");
      break;
    case "missing": showBanner("This file was deleted or moved. Showing the last version."); break;
    case "empty": showEmpty(m.root); break;
    case "toast": state.navigating = false; toast(m.text); break;
    case "stats": state.startupMs = m.startupMs; break;
  }
}

// ================================================================== documents

async function showDoc(m) {
  const sc = els.scroller;
  const same = state.doc && state.doc.path.toLowerCase() === m.path.toLowerCase();
  const reload = m.reload && same;
  const prevTop = sc.scrollTop;
  const atBottom = sc.scrollTop > 0 && sc.scrollTop + sc.clientHeight >= sc.scrollHeight - 40;
  if (state.doc && !same) savePosition();

  state.doc = m;
  $("btn-edit").disabled = false;
  if (!reload && !state.navigating) pushHistory(m.path);
  state.navigating = false;

  const { html: body, hasMermaid } = renderMarkdown(m.content);
  els.article.innerHTML = body;
  els.banner.hidden = true;
  fixMedia(els.article, m.dir);
  marks.apply();
  toc.build();
  tree.setCurrent(m.path);
  renderCrumbs(m);
  document.title = m.name;

  const restore = () => {
    if (reload) sc.scrollTop = atBottom ? sc.scrollHeight : prevTop;
    else if (m.hash) scrollToId(m.hash);
    else sc.scrollTop = store.get("pos", {})[m.path.toLowerCase()] ?? 0;
  };
  restore();
  if (reload) pulseLive();
  else sc.focus({ preventScroll: true });
  post({ type: "rendered" }); // text is on screen

  if (hasMermaid) {
    await renderMermaid(els.article, html.dataset.theme === "dark");
    restore();
    marks.renderLane();
  }
  post({ type: "settled" }); // diagrams done too
}

function showEmpty(root) {
  state.doc = null;
  $("btn-edit").disabled = true;
  els.article.innerHTML = `<div class="empty">
    <div class="empty-icon">${icon("file", 44)}</div>
    <h2>${root ? "No Markdown files here" : "Nothing to show"}</h2>
    ${root ? `<p class="empty-path">${esc(root)}</p>` : ""}
    <div class="empty-actions">
      <button class="edit-btn" data-act="open-file">${icon("file", 15)}<span>Open file…</span></button>
      <button class="ghost-btn" data-act="open-folder">${icon("folderOpen", 15)}<span>Open folder…</span></button>
    </div></div>`;
  toc.build();
  els.crumbs.innerHTML = root ? `<span class="crumb">${esc(baseName(root))}</span>` : "";
  post({ type: "rendered" });
  post({ type: "settled" });
}

function renderCrumbs(m) {
  const root = tree.root || m.root || m.dir;
  let rel = m.dir;
  if (root && m.dir.toLowerCase().startsWith(root.toLowerCase())) rel = m.dir.slice(root.length);
  const parts = [baseName(root), ...rel.split("\\").filter(Boolean)];
  els.crumbs.innerHTML =
    parts.map((p) => `<span class="crumb">${esc(p)}</span><span class="crumb-sep">${icon("chevron", 13)}</span>`).join("") +
    `<span class="crumb crumb-file" title="${esc(m.path)}">${esc(m.name)}</span>` +
    `<span class="live-dot" id="live-dot" title="Live: reloads automatically when the file changes"></span>`;
}

function pulseLive() {
  const dot = $("live-dot");
  if (!dot) return;
  dot.classList.remove("pulse");
  void dot.offsetWidth;
  dot.classList.add("pulse");
  dot.title = `Live — updated ${new Date().toLocaleTimeString()}`;
}

function showBanner(text) {
  els.banner.innerHTML = `${icon("alert", 15)}<span>${esc(text)}</span>`;
  els.banner.hidden = false;
}

function scrollToId(id) {
  const target = document.getElementById(id) || document.getElementById(id.toLowerCase());
  target?.scrollIntoView({ block: "start" });
}

/** Line of the block at the top of the viewport (or of the selection) → "Edit" opens VS Code right there. */
function currentLine() {
  const sel = getSelection();
  if (sel && !sel.isCollapsed && els.article.contains(sel.anchorNode)) {
    const n = sel.anchorNode.nodeType === 1 ? sel.anchorNode : sel.anchorNode.parentElement;
    const el = n?.closest("[data-line]");
    if (el) return Number(el.dataset.line);
  }
  if (els.scroller.scrollTop < 8) return 1;
  const top = els.scroller.getBoundingClientRect().top + 24;
  let line = 1;
  for (const el of els.article.querySelectorAll("[data-line]")) {
    if (el.getBoundingClientRect().top <= top) line = Math.max(line, Number(el.dataset.line));
  }
  return line;
}

// remember the reading position of each file
let posTimer = 0;
els.scroller.addEventListener("scroll", () => {
  clearTimeout(posTimer);
  posTimer = setTimeout(savePosition, 400);
}, { passive: true });

function savePosition() {
  if (!state.doc) return;
  const all = store.get("pos", {});
  const key = state.doc.path.toLowerCase();
  delete all[key];
  all[key] = Math.round(els.scroller.scrollTop);
  const keys = Object.keys(all);
  for (const k of keys.slice(0, Math.max(0, keys.length - 300))) delete all[k];
  store.set("pos", all);
}

// ================================================================== history

function pushHistory(path) {
  state.history = state.history.slice(0, state.hIndex + 1);
  if (state.history.at(-1)?.toLowerCase() !== path.toLowerCase()) state.history.push(path);
  state.hIndex = state.history.length - 1;
  updateNav();
}

function go(delta) {
  const i = state.hIndex + delta;
  if (i < 0 || i >= state.history.length) return;
  savePosition();
  state.hIndex = i;
  state.navigating = true;
  post({ type: "open", path: state.history[i] });
  updateNav();
}

function updateNav() {
  $("btn-back").disabled = state.hIndex <= 0;
  $("btn-forward").disabled = state.hIndex >= state.history.length - 1;
}

// ================================================================== links, copy buttons, empty-state actions

els.article.addEventListener("click", (e) => {
  const copy = e.target.closest(".code-copy");
  if (copy) {
    const code = copy.closest(".code-block")?.querySelector("pre code")?.textContent ?? "";
    copyText(code).then(() => {
      copy.classList.add("done");
      copy.querySelector("span").textContent = "Copied";
      setTimeout(() => { copy.classList.remove("done"); copy.querySelector("span").textContent = "Copy"; }, 1400);
    });
    return;
  }
  const act = e.target.closest("[data-act]")?.dataset.act;
  if (act === "open-file") return post({ type: "openFileDialog" });
  if (act === "open-folder") return post({ type: "openFolderDialog" });

  const a = e.target.closest("a[href]");
  if (a) { e.preventDefault(); followLink(a.getAttribute("href"), e.ctrlKey || e.shiftKey); }
});

els.article.addEventListener("auxclick", (e) => {
  const a = e.target.closest("a[href]");
  if (a && e.button === 1) { e.preventDefault(); followLink(a.getAttribute("href"), true); }
});

function followLink(href, newWindow) {
  if (!href) return;
  if (href.startsWith("#")) return scrollToId(decodeURIComponent(href.slice(1)));
  if (/^[a-z]:[\\/]/i.test(href) || /^file:/i.test(href) || !/^[a-z][a-z0-9+.-]*:/i.test(href)) {
    post({ type: "link", href, newWindow });
  } else post({ type: "external", url: href });
}

function copyText(text) {
  return navigator.clipboard.writeText(text).catch(() => post({ type: "copy", text }));
}

// ================================================================== highlight input, find bar, sets

els.markInput.addEventListener("keydown", (e) => {
  if (e.key === "Enter") {
    marks.add(els.markInput.value);
    els.markInput.value = "";
  } else if (e.key === "Escape") {
    els.markInput.value = "";
    els.markInput.blur();
  }
});
$("btn-clear-marks").onclick = () => marks.clear();

let findTimer = 0;
function openFind() {
  const sel = getSelection().toString().trim();
  els.findbar.hidden = false;
  if (sel && sel.length < 200) els.findInput.value = sel;
  els.findInput.focus();
  els.findInput.select();
  runFind();
}
function runFind() {
  const n = marks.setFind(els.findInput.value.trim());
  els.findCount.textContent = els.findInput.value.trim() ? (n ? `${n} found` : "No results") : "";
  els.findCount.classList.toggle("none", !n && !!els.findInput.value.trim());
  if (n) findStep(1);
}
function findStep(dir) {
  const r = marks.next("find", dir);
  if (r) els.findCount.textContent = `${r.index + 1} of ${r.total}`;
}
function closeFind() {
  els.findbar.hidden = true;
  marks.setFind(null);
  els.scroller.focus({ preventScroll: true });
}
els.findInput.addEventListener("input", () => { clearTimeout(findTimer); findTimer = setTimeout(runFind, 120); });
els.findInput.addEventListener("keydown", (e) => {
  if (e.key === "Enter") { e.preventDefault(); findStep(e.shiftKey ? -1 : 1); }
  else if (e.key === "Escape") closeFind();
});
$("find-next").onclick = () => findStep(1);
$("find-prev").onclick = () => findStep(-1);
$("find-close").onclick = closeFind;
$("find-pin").onclick = () => { marks.add(els.findInput.value); closeFind(); };

function highlightSelection() {
  const text = getSelection().toString();
  if (text.trim()) {
    marks.add(text);
    getSelection().removeAllRanges();
  } else els.markInput.focus();
}

// ---------- popover menus

function openMenu(anchor, build) {
  const m = els.menu;
  if (!m.hidden && m.dataset.owner === anchor.id) return closeMenu();
  m.dataset.owner = anchor.id;
  m.innerHTML = build();
  m.hidden = false;
  const r = anchor.getBoundingClientRect();
  m.style.top = `${r.bottom + 6}px`;
  m.style.right = `${Math.max(8, innerWidth - r.right)}px`;
  m.querySelector("input")?.focus();
}
function closeMenu() { els.menu.hidden = true; els.menu.dataset.owner = ""; }

document.addEventListener("mousedown", (e) => {
  if (!els.menu.hidden && !els.menu.contains(e.target) && !e.target.closest(`#${els.menu.dataset.owner}`)) closeMenu();
});

function setsMenu() {
  const sets = Sets.all();
  const names = Object.keys(sets).sort((a, b) => a.localeCompare(b));
  const list = names.length
    ? names.map((n) => `<div class="menu-item set-item" data-set="${esc(n)}">
        <span class="set-swatches">${sets[n].slice(0, 5).map((t) => `<i class="sw hl-${t.color}"></i>`).join("")}</span>
        <span class="menu-label">${esc(n)}</span><span class="menu-hint">${sets[n].length}</span>
        <button class="icon-btn small" data-del="${esc(n)}" title="Delete set">${icon("trash", 13)}</button></div>`).join("")
    : `<div class="menu-empty">No saved sets yet.<br>Highlight some words, then save them here.</div>`;
  return `<div class="menu-title">Highlight sets</div>${list}<div class="menu-sep"></div>
    <div class="menu-save"><input id="set-name" type="text" placeholder="Save current highlights as…" ${marks.terms.length ? "" : "disabled"} spellcheck="false">
    <button class="edit-btn small" id="set-save" ${marks.terms.length ? "" : "disabled"}>Save</button></div>`;
}

$("btn-sets").onclick = (e) => openMenu(e.currentTarget, setsMenu);

const moreItems = [
  ["open-file", "file", "Open file…", "Ctrl+O"],
  ["open-folder", "folderOpen", "Open folder…", "Ctrl+Shift+O"],
  ["reveal", "explorer", "Show in Explorer", ""],
  ["copy-path", "copy", "Copy file path", "Ctrl+Shift+C"],
  ["reload", "refresh", "Reload", "F5"],
  ["print", "print", "Print / Save as PDF…", ""],
  "-",
  ["settings", "settings", "Settings", "Ctrl+,"],
  ["help", "keyboard", "Keyboard shortcuts", "F1"],
];
$("btn-more").onclick = (e) => openMenu(e.currentTarget, () =>
  moreItems.map((it) => it === "-" ? `<div class="menu-sep"></div>` :
    `<button class="menu-item" data-cmd="${it[0]}">${icon(it[1], 15)}<span class="menu-label">${it[2]}</span><span class="menu-hint">${it[3]}</span></button>`).join(""));

els.menu.addEventListener("click", (e) => {
  const del = e.target.closest("[data-del]");
  if (del) { Sets.remove(del.dataset.del); els.menu.innerHTML = setsMenu(); return; }
  const set = e.target.closest("[data-set]");
  if (set) { marks.load(Sets.all()[set.dataset.set] ?? []); closeMenu(); toast(`Loaded “${set.dataset.set}”`); return; }
  if (e.target.closest("#set-save")) return saveSet();
  const cmd = e.target.closest("[data-cmd]")?.dataset.cmd;
  if (cmd) { closeMenu(); command(cmd); }
});
els.menu.addEventListener("keydown", (e) => {
  if (e.key === "Enter" && e.target.id === "set-name") saveSet();
  if (e.key === "Escape") closeMenu();
});
function saveSet() {
  const name = $("set-name")?.value.trim();
  if (!name || !marks.terms.length) return;
  Sets.save(name, marks.serialize());
  closeMenu();
  toast(`Saved “${name}”`);
}

// ================================================================== layout: sidebar, outline, theme, zoom

function setSidebar(on) {
  state.sidebar = !!on;
  els.layout.classList.toggle("no-sidebar", !state.sidebar);
  $("btn-sidebar").classList.toggle("on", state.sidebar);
  requestAnimationFrame(() => marks.renderLane());
}
function setToc(on) {
  state.toc = !!on;
  store.set("toc", state.toc);
  els.layout.classList.toggle("no-toc", !state.toc);
  $("btn-toc").classList.toggle("on", state.toc);
  requestAnimationFrame(() => marks.renderLane());
}

const sidebarEl = $("sidebar");
sidebarEl.style.setProperty("--sidebar-w", `${store.get("sidebarW", 264)}px`);
$("resizer").addEventListener("pointerdown", (e) => {
  e.preventDefault();
  const startX = e.clientX;
  const startW = sidebarEl.getBoundingClientRect().width;
  const move = (ev) => sidebarEl.style.setProperty("--sidebar-w", `${Math.max(180, Math.min(560, startW + ev.clientX - startX))}px`);
  const up = () => {
    removeEventListener("pointermove", move);
    removeEventListener("pointerup", up);
    store.set("sidebarW", Math.round(sidebarEl.getBoundingClientRect().width));
    marks.renderLane();
  };
  addEventListener("pointermove", move);
  addEventListener("pointerup", up);
});

const darkQuery = matchMedia("(prefers-color-scheme: dark)");
function applyTheme() {
  const pref = state.themeOverride ?? state.themePref;
  const dark = pref === "dark" || (pref === "auto" && darkQuery.matches);
  const changed = html.dataset.theme !== (dark ? "dark" : "light");
  html.dataset.theme = dark ? "dark" : "light";
  const btn = $("btn-theme");
  btn.innerHTML = icon(pref === "auto" ? "auto" : dark ? "moon" : "sun");
  btn.title = `Theme: ${pref} (Ctrl+D)`;
  post({ type: "theme", dark });
  if (changed && els.article.querySelector(".mermaid")) renderMermaid(els.article, dark).then(() => marks.renderLane());
}
darkQuery.addEventListener("change", applyTheme);
function cycleTheme() {
  const order = ["auto", "light", "dark"];
  state.themeOverride = null;
  state.themePref = order[(order.indexOf(state.themePref) + 1) % order.length];
  store.set("theme", state.themePref);
  applyTheme();
  toast(`Theme: ${state.themePref}`);
}

function setZoom(z) {
  state.zoom = Math.round(Math.max(0.6, Math.min(2.2, z)) * 10) / 10;
  html.style.setProperty("--zoom", state.zoom);
  store.set("zoom", state.zoom);
  toast(`${Math.round(state.zoom * 100)}%`);
  requestAnimationFrame(() => marks.renderLane());
}
els.scroller.addEventListener("wheel", (e) => {
  if (!e.ctrlKey) return;
  e.preventDefault();
  setZoom(state.zoom + (e.deltaY < 0 ? 0.1 : -0.1));
}, { passive: false });

// ================================================================== commands & keyboard

function command(cmd) {
  switch (cmd) {
    case "edit": if (state.doc) post({ type: "edit", line: currentLine() }); break;
    case "open-file": post({ type: "openFileDialog" }); break;
    case "open-folder": post({ type: "openFolderDialog" }); break;
    case "reveal": post({ type: "reveal" }); break;
    case "copy-path": if (state.doc) copyText(state.doc.path).then(() => toast("Path copied")); break;
    case "reload": post({ type: "reload" }); break;
    case "print": window.print(); break;
    case "settings": post({ type: "openSettings" }); break;
    case "help": showHelp(); break;
    case "quick-open": setSidebar(true); els.treeFilter.focus(); els.treeFilter.select(); break;
  }
}

function showHelp() {
  $("help-foot").textContent =
    `Marleo ${state.version}` + (state.startupMs ? ` · opened in ${state.startupMs} ms` : "") + (state.settingsPath ? ` · ${state.settingsPath}` : "");
  els.help.hidden = false;
}
els.help.addEventListener("click", (e) => { if (e.target === els.help || e.target.closest("[data-close]")) els.help.hidden = true; });

$("btn-sidebar").onclick = () => setSidebar(!state.sidebar);
$("btn-toc").onclick = () => setToc(!state.toc);
$("btn-theme").onclick = cycleTheme;
$("btn-edit").onclick = () => command("edit");
$("btn-back").onclick = () => go(-1);
$("btn-forward").onclick = () => go(1);
$("btn-open-folder").onclick = () => command("open-folder");

const isTyping = (t) => t instanceof HTMLInputElement || t instanceof HTMLTextAreaElement || t?.isContentEditable;

document.addEventListener("keydown", (e) => {
  const ctrl = e.ctrlKey || e.metaKey;
  const key = e.key.toLowerCase();
  let handled = true;

  if (e.key === "Escape") {
    if (!els.help.hidden) els.help.hidden = true;
    else if (!els.menu.hidden) closeMenu();
    else if (!els.findbar.hidden) closeFind();
    else handled = false;
  } else if (ctrl && !e.altKey) {
    switch (key) {
      case "e": command("edit"); break;
      case "f": openFind(); break;
      case "p": command("quick-open"); break;
      case "b": setSidebar(!state.sidebar); break;
      case "t": setToc(!state.toc); break;
      case "o": command(e.shiftKey ? "open-folder" : "open-file"); break;
      case "d": cycleTheme(); break;
      case "r": command("reload"); break;
      case "w": post({ type: "close" }); break;
      case ",": command("settings"); break;
      case "=": case "+": setZoom(state.zoom + 0.1); break;
      case "-": setZoom(state.zoom - 0.1); break;
      case "0": setZoom(1); break;
      case "h": if (e.shiftKey) marks.clear(); else handled = false; break;
      case "c": if (e.shiftKey) command("copy-path"); else handled = false; break;
      case "a":
        if (isTyping(e.target)) handled = false;
        else getSelection().selectAllChildren(els.article);
        break;
      default: handled = false;
    }
  } else if (e.key === "F5") command("reload");
  else if (e.key === "F1") showHelp();
  else if (e.key === "F3") { if (els.findbar.hidden) openFind(); else findStep(e.shiftKey ? -1 : 1); }
  else if (e.key === "F12") post({ type: "devtools" });
  else if (e.altKey && e.key === "ArrowLeft") go(-1);
  else if (e.altKey && e.key === "ArrowRight") go(1);
  else if (e.altKey && /^Digit[1-9]$/.test(e.code)) marks.next(Number(e.code.slice(5)) - 1, e.shiftKey ? -1 : 1);
  else if (!ctrl && !e.altKey && !isTyping(e.target)) {
    if (key === "h") highlightSelection();
    else if (e.key === "?") showHelp();
    else if (e.key === "/") openFind();
    else handled = false;
  } else handled = false;

  if (handled) e.preventDefault();
}, true);

// mouse back/forward buttons
addEventListener("mouseup", (e) => {
  if (e.button === 3) { e.preventDefault(); go(-1); }
  if (e.button === 4) { e.preventDefault(); go(1); }
});

// drag & drop a file or folder onto the window
addEventListener("dragover", (e) => { e.preventDefault(); e.dataTransfer.dropEffect = "copy"; html.classList.add("dragging"); });
addEventListener("dragleave", (e) => { if (!e.relatedTarget) html.classList.remove("dragging"); });
addEventListener("drop", (e) => {
  e.preventDefault();
  html.classList.remove("dragging");
  const files = [...(e.dataTransfer?.files ?? [])];
  if (files.length && host?.postMessageWithAdditionalObjects) host.postMessageWithAdditionalObjects({ type: "drop" }, files);
});

// ================================================================== misc

let toastTimer = 0;
function toast(text) {
  els.toast.textContent = text;
  els.toast.classList.add("show");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => els.toast.classList.remove("show"), 1800);
}

function baseName(p) { return (p || "").replace(/[\\/]+$/, "").split(/[\\/]/).pop() || p; }
function esc(s) { return String(s).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]); }

// ================================================================== start

setToc(state.toc);
applyTheme();
if (host) post({ type: "ready" });
else els.article.innerHTML = `<div class="empty"><h2>Marleo</h2><p>This page is the UI of marleo.exe — run the app to view documents.</p></div>`;
