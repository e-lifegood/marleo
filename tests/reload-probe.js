// Logs every re-render of the document to %LOCALAPPDATA%\mdv\mdv.log (see tests/run-tests.ps1).
(() => {
  const log = (message) => window.chrome.webview.postMessage({ type: "log", message });
  const doc = document.getElementById("doc");
  const describe = () => `h1=${doc.querySelector("h1")?.textContent} paragraphs=${doc.querySelectorAll("p").length}`;
  log("probe start " + describe());
  new MutationObserver(() => log("rendered " + describe())).observe(doc, { childList: true });
})();
