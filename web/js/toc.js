// Table of contents (h1–h3) with scroll-spy.
export class Toc {
  constructor({ list, panel, scroller, article }) {
    Object.assign(this, { list, panel, scroller, article });
    this.headings = [];
    this.active = -1;
    let raf = 0;
    scroller.addEventListener("scroll", () => {
      cancelAnimationFrame(raf);
      raf = requestAnimationFrame(() => this.spy());
    }, { passive: true });
    list.addEventListener("click", (e) => {
      const a = e.target.closest("a[data-i]");
      if (!a) return;
      e.preventDefault();
      this.headings[Number(a.dataset.i)]?.scrollIntoView({ block: "start", behavior: "smooth" });
    });
  }

  build() {
    this.headings = [...this.article.querySelectorAll("h1[id], h2[id], h3[id]")];
    const minLevel = Math.min(...this.headings.map((h) => Number(h.tagName[1])), 3);
    this.list.innerHTML = this.headings.map((h, i) => {
      const level = Number(h.tagName[1]) - minLevel;
      const text = h.textContent.trim();
      return `<a href="#${encodeURIComponent(h.id)}" data-i="${i}" class="toc-l${level}" title="${text.replace(/"/g, "&quot;")}">${escapeHtml(text)}</a>`;
    }).join("");
    this.panel.classList.toggle("toc-empty", this.headings.length < 2);
    this.active = -1;
    this.spy();
  }

  spy() {
    if (!this.headings.length) return;
    const top = this.scroller.getBoundingClientRect().top + 90;
    let idx = 0;
    for (let i = 0; i < this.headings.length; i++) {
      if (this.headings[i].getBoundingClientRect().top <= top) idx = i;
      else break;
    }
    if (idx === this.active) return;
    this.active = idx;
    this.list.querySelector(".active")?.classList.remove("active");
    const a = this.list.querySelector(`a[data-i="${idx}"]`);
    a?.classList.add("active");
    a?.scrollIntoView({ block: "nearest" });
  }
}

function escapeHtml(s) {
  return s.replace(/[&<>]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" })[c]);
}
