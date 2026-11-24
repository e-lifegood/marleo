// UI smoke test for Marleo. Run against the samples and look at the PNG:
//   marleo samples\demo.md --run-script tests\ui-smoke.js --screenshot smoke.png
// It drives the UI through real keyboard events and prints a pass/fail panel on top of the page.
(async () => {
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  const key = (k, opts = {}) => document.dispatchEvent(new KeyboardEvent("keydown", { key: k, bubbles: true, cancelable: true, ...opts }));
  const results = [];
  const check = (name, ok, detail = "") => results.push({ name, ok: !!ok, detail });
  const article = document.getElementById("doc");

  // 1. select a word and press H → new highlight chip
  const textNode = [...article.querySelectorAll("p")].map((p) => p.firstChild).find((n) => n?.nodeType === 3 && /\w{4,}/.test(n.nodeValue));
  const word = /\w{4,}/.exec(textNode.nodeValue)[0];
  const range = document.createRange();
  range.setStart(textNode, textNode.nodeValue.indexOf(word));
  range.setEnd(textNode, textNode.nodeValue.indexOf(word) + word.length);
  getSelection().removeAllRanges();
  getSelection().addRange(range);
  key("h");
  await sleep(50);
  const chips = document.querySelectorAll(".chip");
  check("H highlights selection", chips.length === 1, `“${word}” → ${article.querySelectorAll("mark.hl-0").length} marks`);

  // 2. a second term via the input gets a different color
  const input = document.getElementById("mark-input");
  input.value = "highlight";
  input.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));
  await sleep(50);
  check("Second term, second color", article.querySelectorAll("mark.hl-1").length > 0, `${article.querySelectorAll("mark.hl-1").length} marks`);

  // 3. Alt+1 jumps to an occurrence
  key("1", { altKey: true, code: "Digit1" });
  await sleep(50);
  check("Alt+1 navigates", article.querySelector("mark.hl-current"), document.querySelector(".chip .chip-count")?.textContent);

  // 4. overview ruler ticks
  check("Scrollbar markers", document.querySelectorAll("#lane .tick").length > 0, `${document.querySelectorAll("#lane .tick").length} ticks`);

  // 5. Ctrl+F find
  key("f", { ctrlKey: true });
  const find = document.getElementById("find-input");
  find.value = "marleo";
  find.dispatchEvent(new Event("input", { bubbles: true }));
  await sleep(250);
  check("Ctrl+F find", article.querySelectorAll("mark.hl-find").length > 0, document.getElementById("find-count").textContent);
  key("Escape");

  // 6. outline, tree, code highlighting, alerts, tasks
  check("Outline built", document.querySelectorAll("#toc-list a").length > 3, `${document.querySelectorAll("#toc-list a").length} headings`);
  check("Syntax highlighting", article.querySelector(".hljs-keyword"));
  check("Alerts", article.querySelectorAll(".alert").length >= 4);
  check("Task lists", article.querySelectorAll("input.task").length >= 2);
  check("data-line for Edit", article.querySelectorAll("[data-line]").length > 20);
  const imgs = [...article.querySelectorAll("img")];
  const loaded = await Promise.all(imgs.map((i) => new Promise((r) => { const t = new Image(); t.onload = () => r(t.naturalWidth > 0); t.onerror = () => r(false); t.src = i.src; })));
  check("Images served from disk", imgs.length && loaded.every(Boolean), `${imgs.length} img`);
  await sleep(300);
  check("Mermaid diagrams", article.querySelectorAll(".mermaid svg").length >= 1, `${article.querySelectorAll(".mermaid svg").length} svg`);

  // 7. theme toggle
  const before = document.documentElement.dataset.theme;
  // three presses = a full cycle (auto → light → dark), so the user's setting is left as it was
  const seen = [];
  for (let i = 0; i < 3; i++) { key("d", { ctrlKey: true }); seen.push(document.documentElement.dataset.theme); }
  check("Ctrl+D theme", seen.includes("dark") && seen.includes("light"), `${before} → ${seen.join(" → ")}`);

  const panel = document.createElement("div");
  panel.style.cssText = "position:fixed;left:16px;bottom:16px;z-index:999;padding:12px 16px;border-radius:10px;font:13px/1.6 Segoe UI;background:#111;color:#eee;box-shadow:0 8px 30px #0008";
  panel.innerHTML = results.map((r) => `<div>${r.ok ? "✅" : "❌"} ${r.name} <span style="opacity:.6">${r.detail}</span></div>`).join("");
  document.body.append(panel);
  window.chrome?.webview?.postMessage({ type: "log", message: "SMOKE " + JSON.stringify(results) });
})();
