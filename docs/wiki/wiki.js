/* Visual Game Studio Engine — wiki runtime.
   Renders the pages declared in wiki-content.js: a small Markdown subset,
   hash routing, full-text search, and an on-this-page rail. No dependencies. */
(function () {
  'use strict';

  var PAGES = window.WIKI.pages;
  var GROUPS = window.WIKI.groups;
  var BY_ID = {};
  var ORDER = [];
  GROUPS.forEach(function (g) {
    g.pages.forEach(function (id) { ORDER.push(id); });
  });
  PAGES.forEach(function (p) { BY_ID[p.id] = p; });

  /* ---------- markdown ------------------------------------------------ */

  function esc(s) {
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  // BasicLang / C-family keyword highlighting for fenced blocks.
  var KEYWORDS = ('Dim|As|Auto|Const|If|Then|ElseIf|Else|End|For|To|Step|Next|Each|In|While|Wend|Do|Loop|Until|' +
    'Select|Case|When|Is|Sub|Function|Return|Class|Structure|Interface|Module|Implements|Inherits|New|' +
    'Public|Private|Protected|Friend|Shared|Overrides|Overridable|MustInherit|MustOverride|NotInheritable|' +
    'Property|Get|Set|Try|Catch|Finally|Throw|Async|Await|Of|Optional|ByRef|ByVal|Nothing|True|False|Me|MyBase|' +
    'Import|Using|Option|Enum|Yield|Continue|Exit|Not|And|Or|Xor|Mod|Shl|Shr|From|Where|Rem|Declare|Event|' +
    'RaiseEvent|AddHandler|Handles|With|Static|Operator|Partial|Global|Call|' +
    'auto|bool|char|class|const|double|else|enum|extern|float|for|if|int|namespace|return|struct|template|' +
    'typedef|typename|unsigned|using|void|while|public|private|virtual|override|static|inline|co_yield').split('|');
  var KW_RE = new RegExp('\\b(' + KEYWORDS.join('|') + ')\\b', 'g');
  var SENT = '';

  function highlight(code, lang) {
    var out = esc(code);
    if (lang === 'text' || lang === 'output' || lang === '') return out;
    var parts = [];
    // In shell dialects '#' opens a comment; in BasicLang it opens a directive.
    var hashIsComment = /^(powershell|pwsh|ps1|bash|sh|shell|yaml|toml|ini)$/.test(lang);
    var TOKENS = hashIsComment
      ? /#[^\n]*|&quot;[^&\n]*&quot;|"[^"\n]*"|'[^'\n]*'/g
      : /'[^\n]*|\/\/[^\n]*|&quot;[^&\n]*&quot;|"[^"\n]*"|#[A-Za-z]+/g;
    // Pull comments and strings out first so keywords inside them stay plain.
    out = out.replace(TOKENS, function (m) {
      var cls = 'tok-com';
      if (m.charAt(0) === '"' || m.indexOf('&quot;') === 0) cls = 'tok-str';
      else if (m.charAt(0) === "'") cls = hashIsComment ? 'tok-str' : 'tok-com';
      else if (m.charAt(0) === '#') cls = hashIsComment ? 'tok-com' : 'tok-kw';
      parts.push('<span class="' + cls + '">' + m + '</span>');
      // The placeholder carries an 's' prefix so the numeric-literal pass below cannot
      // match its index digits. Without it, the number rule wrapped them in a <span>,
      // the restore regex stopped matching, and every string and comment on the site
      // was replaced by a bare number.
      return SENT + 's' + (parts.length - 1) + SENT;
    });
    out = out.replace(KW_RE, '<span class="tok-kw">$1</span>');
    out = out.replace(/\b(\d+(?:\.\d+)?)\b/g, '<span class="tok-num">$1</span>');
    out = out.replace(new RegExp(SENT + 's(\\d+)' + SENT, 'g'), function (_, i) { return parts[+i]; });
    return out;
  }

  // Inline HTML the page content is allowed to use verbatim (status pills, breaks).
  // Everything else is escaped, so stray angle brackets in prose stay literal.
  var INLINE_HTML = /<\/?(?:span|br|kbd|sup|sub)(?:\s[^<>]*)?\/?>/g;

  function inline(s) {
    var stash = [];
    s = s.replace(/`([^`]+)`/g, function (_, c) {
      stash.push('<code>' + esc(c) + '</code>');
      return SENT + (stash.length - 1) + SENT;
    });
    s = s.replace(INLINE_HTML, function (tag) {
      stash.push(tag);
      return SENT + (stash.length - 1) + SENT;
    });
    s = esc(s);
    s = s.replace(/\[([^\]]+)\]\(([^)]+)\)/g, function (_, t, href) {
      var ext = /^https?:/.test(href) ? ' target="_blank" rel="noopener"' : '';
      return '<a href="' + href + '"' + ext + '>' + t + '</a>';
    });
    // Bold first, allowing a nested italic inside it (`**a *b* c**`), then any
    // remaining standalone italic. The old [^*]+ body stopped at the inner star
    // and printed four literal asterisks.
    s = s.replace(/\*\*([\s\S]+?)\*\*/g, function (_, body) {
      return '<strong>' + body.replace(/\*([^*\n]+)\*/g, '<em>$1</em>') + '</strong>';
    });
    s = s.replace(/(^|[\s(])\*([^*\n]+)\*/g, '$1<em>$2</em>');
    s = s.replace(new RegExp(SENT + '(\\d+)' + SENT, 'g'), function (_, i) { return stash[+i]; });
    return s;
  }

  function slug(s) {
    return s.toLowerCase()
      .replace(/[`*_]/g, '')
      .replace(/\+\+/g, 'pp')          // C++ -> cpp, so it cannot collide with C#
      .replace(/#/g, 's')              // C# -> cs
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-|-$/g, '') || 'section';
  }

  // Headings that still collide after slugging get -2, -3, … so every id is unique
  // and each on-this-page link reaches its own section.
  function uniqueSlug(base, used) {
    var id = base, n = 2;
    while (used[id]) id = base + '-' + n++;
    used[id] = true;
    return id;
  }

  function render(md, headings) {
    var lines = md.split('\n');
    var html = [];
    var i = 0;
    var listStack = [];
    var usedIds = {};

    // A line that opens a new block, so a paragraph or list item must not swallow it.
    function startsBlock(l) {
      return /^(#{2,4}\s|```|\||>|---+$|<)/.test(l) || /^\s*([-*]|\d+\.)\s/.test(l);
    }

    function closeList() {
      while (listStack.length) html.push('</' + listStack.pop() + '>');
    }

    while (i < lines.length) {
      var line = lines[i];

      // raw HTML block passthrough (stat rows, card grids, pipelines)
      if (/^<(div|section|nav)/.test(line)) {
        closeList();
        var buf = [];
        var depth = 0;
        do {
          buf.push(lines[i]);
          depth += (lines[i].match(/<(?!\/)[a-z]/g) || []).length;
          depth -= (lines[i].match(/<\//g) || []).length;
          depth -= (lines[i].match(/\/>/g) || []).length;
          i++;
        } while (i < lines.length && depth > 0);
        html.push(buf.join('\n'));
        continue;
      }

      // fenced code
      if (line.indexOf('```') === 0) {
        closeList();
        var lang = line.slice(3).trim();
        var code = [];
        i++;
        while (i < lines.length && lines[i].indexOf('```') !== 0) { code.push(lines[i]); i++; }
        i++;
        html.push('<pre tabindex="0"' + (lang ? ' data-lang="' + esc(lang) + '"' : '') + '><code>' +
          highlight(code.join('\n'), lang) + '</code></pre>');
        continue;
      }

      // table
      if (line.indexOf('|') === 0 && i + 1 < lines.length && /^\|[\s:|-]+\|$/.test(lines[i + 1])) {
        closeList();
        var cells = function (l) {
          return l.replace(/^\||\|$/g, '').split('|').map(function (c) { return c.trim(); });
        };
        var head = cells(line);
        i += 2;
        var body = [];
        while (i < lines.length && lines[i].indexOf('|') === 0) { body.push(cells(lines[i])); i++; }
        var caption = (head[0] || 'Table').replace(/[`*]/g, '');
        var t = ['<div class="table-scroll" tabindex="0" role="region" aria-label="' +
                 esc(caption) + ' table"><table><thead><tr>'];
        head.forEach(function (h) { t.push('<th>' + inline(h) + '</th>'); });
        t.push('</tr></thead><tbody>');
        body.forEach(function (r) {
          t.push('<tr>');
          r.forEach(function (c) { t.push('<td>' + inline(c) + '</td>'); });
          t.push('</tr>');
        });
        t.push('</tbody></table></div>');
        html.push(t.join(''));
        continue;
      }

      // headings
      var h = /^(#{2,4})\s+(.*)$/.exec(line);
      if (h) {
        closeList();
        var lvl = h[1].length;
        var text = h[2];
        var id = uniqueSlug(slug(text), usedIds);
        if (lvl <= 3 && headings) headings.push({ id: id, text: text.replace(/[`*]/g, ''), lvl: lvl });
        html.push('<h' + lvl + ' id="' + id + '">' + inline(text) + '</h' + lvl + '>');
        i++;
        continue;
      }

      // blockquote, optionally tagged: "> [trap] ..."
      if (line.indexOf('>') === 0) {
        closeList();
        var q = [];
        var cls = '';
        while (i < lines.length && lines[i].indexOf('>') === 0) {
          var qline = lines[i].replace(/^>\s?/, '');
          var m = /^\[(trap|note|good)\]\s*/.exec(qline);
          if (m) { cls = m[1]; qline = qline.slice(m[0].length); }
          q.push(qline);
          i++;
        }
        // A quote may carry its own fenced block; render the inner markdown rather
        // than printing the fence and its contents as literal text.
        var inner = q.join('\n');
        var quoteBody = inner.indexOf('```') !== -1
          ? render(inner, null)
          : '<p>' + inner.split('\n\n').map(inline).join('</p><p>') + '</p>';
        html.push('<blockquote' + (cls ? ' class="' + cls + '"' : '') + '>' + quoteBody + '</blockquote>');
        continue;
      }

      // horizontal rule
      if (/^---+$/.test(line)) { closeList(); html.push('<hr>'); i++; continue; }

      // lists
      var li = /^(\s*)([-*]|\d+\.)\s+(.*)$/.exec(line);
      if (li) {
        var tag = (li[2] === '-' || li[2] === '*') ? 'ul' : 'ol';
        var want = Math.floor(li[1].length / 2) + 1;
        while (listStack.length > want) html.push('</' + listStack.pop() + '>');
        while (listStack.length < want) { html.push('<' + tag + '>'); listStack.push(tag); }
        // Lazy continuation: a bullet whose text wraps onto following lines stays one
        // item. Without this the continuation was ejected from the list and rendered
        // as a full-width unbulleted paragraph, tearing the sentence in half.
        var itemText = [li[3]];
        i++;
        while (i < lines.length && lines[i].trim() !== '' && !startsBlock(lines[i])) {
          itemText.push(lines[i].trim());
          i++;
        }
        html.push('<li>' + inline(itemText.join(' ')) + '</li>');
        continue;
      }

      if (line.trim() === '') { closeList(); i++; continue; }

      // paragraph
      closeList();
      var para = [];
      while (i < lines.length && lines[i].trim() !== '' && !startsBlock(lines[i])) {
        para.push(lines[i]); i++;
      }
      if (para.length) {
        html.push('<p>' + inline(para.join(' ')) + '</p>');
      } else {
        // Forward progress is mandatory. A line that opens no known block (an HTML tag
        // other than div/section/nav, say) used to satisfy neither branch, so `i` never
        // advanced and the whole site hung on a blank page.
        html.push(lines[i]);
        i++;
      }
    }
    closeList();
    return html.join('\n');
  }

  /* ---------- nav ------------------------------------------------------ */

  var navEl = document.getElementById('nav');
  GROUPS.forEach(function (g) {
    var sec = document.createElement('div');
    sec.className = 'nav-group';
    sec.setAttribute('data-pillar', g.pillar);
    var h = document.createElement('div');
    h.className = 'nav-head';
    h.textContent = g.title;
    sec.appendChild(h);
    g.pages.forEach(function (id) {
      var p = BY_ID[id];
      if (!p) return;
      var a = document.createElement('a');
      a.href = '#/' + id;
      a.textContent = p.title;
      a.setAttribute('data-id', id);
      sec.appendChild(a);
    });
    navEl.appendChild(sec);
  });

  /* ---------- routing -------------------------------------------------- */

  var articleEl = document.getElementById('article');
  var tocEl = document.getElementById('toc');
  var crumbEl = document.getElementById('crumb');

  function groupOf(id) {
    for (var i = 0; i < GROUPS.length; i++) {
      if (GROUPS[i].pages.indexOf(id) !== -1) return GROUPS[i];
    }
    return GROUPS[0];
  }

  function show(id, anchor) {
    var page = BY_ID[id] || BY_ID[ORDER[0]];
    var g = groupOf(page.id);
    var headings = [];
    var bodyHtml = render(page.body, headings);

    crumbEl.innerHTML = 'wiki / ' + esc(g.title.toLowerCase()) + ' / <b>' + esc(page.title) + '</b>';
    document.title = page.title + ' — VGSE Wiki';

    var idx = ORDER.indexOf(page.id);
    var prev = idx > 0 ? BY_ID[ORDER[idx - 1]] : null;
    var next = idx < ORDER.length - 1 ? BY_ID[ORDER[idx + 1]] : null;
    var pagenav = '<nav class="pagenav">';
    if (prev) pagenav += '<a href="#/' + prev.id + '"><span class="dir">Previous</span><span class="t">' + esc(prev.title) + '</span></a>';
    if (next) pagenav += '<a class="next" href="#/' + next.id + '"><span class="dir">Next</span><span class="t">' + esc(next.title) + '</span></a>';
    pagenav += '</nav>';

    articleEl.innerHTML =
      '<header class="page-head" data-pillar="' + g.pillar + '">' +
        '<div class="eyebrow">' + esc(g.title) + '</div>' +
        '<h1>' + esc(page.title) + '</h1>' +
        (page.lede ? '<p class="page-lede">' + inline(page.lede) + '</p>' : '') +
      '</header>' + bodyHtml + pagenav;

    tocEl.innerHTML = headings.length > 1
      ? '<div class="toc-h">On this page</div>' + headings.map(function (hd) {
          return '<a class="' + (hd.lvl === 3 ? 'sub' : '') + '" href="#/' + page.id + '#' + hd.id + '">' + esc(hd.text) + '</a>';
        }).join('')
      : '';

    Array.prototype.forEach.call(navEl.querySelectorAll('a'), function (a) {
      var on = a.getAttribute('data-id') === page.id;
      a.classList.toggle('active', on);
      if (on) a.setAttribute('aria-current', 'page');
      else a.removeAttribute('aria-current');
    });

    var first = tocEl.querySelector('a');
    if (first) first.classList.add('active');

    if (anchor) {
      var target = document.getElementById(anchor);
      if (target) { target.scrollIntoView(); return; }
    }
    window.scrollTo(0, 0);
    // Announce the new page to assistive tech and put the caret at its start, so a
    // keyboard user is not left in the nav list after every navigation.
    var h1 = articleEl.querySelector('h1');
    if (h1) { h1.setAttribute('tabindex', '-1'); h1.focus({ preventScroll: true }); }
  }

  function route() {
    var hash = location.hash.replace(/^#\/?/, '');
    var parts = hash.split('#');
    var id = parts[0] || ORDER[0];
    if (!BY_ID[id]) {
      // Don't render Overview while the address bar still shows a page that does not
      // exist — correct the URL so what is shown and what is linked agree.
      location.replace('#/' + ORDER[0]);
      return;
    }
    show(id, parts[1]);
    closeDrawer();
  }
  window.addEventListener('hashchange', route);

  // Same-hash activation fires no hashchange, so re-clicking the current page (or an
  // anchor you already jumped to) did nothing at all. Handle those clicks directly.
  document.addEventListener('click', function (e) {
    var a = e.target.closest && e.target.closest('a[href^="#/"]');
    if (!a) return;
    if (a.getAttribute('href') === location.hash) {
      e.preventDefault();
      var anchor = location.hash.split('#')[2];
      var el = anchor && document.getElementById(anchor);
      if (el) el.scrollIntoView();
      else window.scrollTo(0, 0);
      closeDrawer();
    }
  });

  /* ---------- on-this-page highlight ----------------------------------- */

  var ticking = false;
  window.addEventListener('scroll', function () {
    if (ticking) return;
    ticking = true;
    requestAnimationFrame(function () {
      ticking = false;
      var links = tocEl.querySelectorAll('a');
      if (!links.length) return;
      var best = null;
      Array.prototype.forEach.call(links, function (a) {
        var parts = a.getAttribute('href').split('#');
        var el = parts[2] && document.getElementById(parts[2]);
        if (el && el.getBoundingClientRect().top <= 90) best = a;
      });
      Array.prototype.forEach.call(links, function (a) { a.classList.remove('active'); });
      (best || links[0]).classList.add('active');
    });
  }, { passive: true });

  /* ---------- search ---------------------------------------------------- */

  var INDEX = PAGES.map(function (p) {
    return {
      id: p.id,
      title: p.title,
      lede: p.lede || '',
      hay: (p.title + ' ' + (p.lede || '') + ' ' + p.body).toLowerCase()
    };
  });

  var searchEl = document.getElementById('search');
  var resultsEl = document.getElementById('results');
  var sel = -1;

  function runSearch() {
    var q = searchEl.value.trim().toLowerCase();
    if (q.length < 2) { resultsEl.classList.remove('open'); resultsEl.innerHTML = ''; sel = -1; return; }
    var terms = q.split(/\s+/);
    var hits = INDEX.map(function (e) {
      var score = 0;
      for (var i = 0; i < terms.length; i++) {
        var t = terms[i];
        if (e.hay.indexOf(t) === -1) return null;
        if (e.title.toLowerCase().indexOf(t) !== -1) score += 40;
        if (e.lede.toLowerCase().indexOf(t) !== -1) score += 12;
        score += Math.min(8, e.hay.split(t).length - 1);
      }
      return { e: e, score: score };
    }).filter(Boolean).sort(function (a, b) { return b.score - a.score; }).slice(0, 9);

    resultsEl.innerHTML = hits.length
      ? hits.map(function (h, n) {
          return '<a class="result" role="option" id="result-' + n + '" aria-selected="false" href="#/' +
            h.e.id + '"><span class="result-t">' + esc(h.e.title) +
            '</span><span class="result-s">' + esc(summarize(h.e.lede)) + '</span></a>';
        }).join('')
      : '<div class="result-empty" role="status">No page matches that. Try a symbol name, a file name, or a subsystem.</div>';
    resultsEl.classList.add('open');
    searchEl.setAttribute('aria-expanded', 'true');
    searchEl.removeAttribute('aria-activedescendant');
    sel = -1;
  }

  // Strip markdown from a lede and cut on a word boundary rather than mid-word.
  function summarize(lede) {
    var s = lede.replace(/`/g, '').replace(/\*\*?/g, '');
    if (s.length <= 92) return s;
    var cut = s.slice(0, 92);
    var sp = cut.lastIndexOf(' ');
    return (sp > 40 ? cut.slice(0, sp) : cut) + '…';
  }

  function closeResults() {
    resultsEl.classList.remove('open');
    searchEl.setAttribute('aria-expanded', 'false');
    searchEl.removeAttribute('aria-activedescendant');
    sel = -1;
  }

  function markSelected(items) {
    Array.prototype.forEach.call(items, function (it, n) {
      var on = n === sel;
      it.classList.toggle('sel', on);
      it.setAttribute('aria-selected', on ? 'true' : 'false');
    });
    if (sel >= 0) searchEl.setAttribute('aria-activedescendant', items[sel].id);
    else searchEl.removeAttribute('aria-activedescendant');
  }

  searchEl.addEventListener('input', runSearch);
  searchEl.addEventListener('keydown', function (e) {
    var items = resultsEl.querySelectorAll('.result');
    if (e.key === 'Escape') { searchEl.value = ''; closeResults(); return; }
    if (!items.length) return;
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      if (sel === -1) sel = e.key === 'ArrowDown' ? 0 : items.length - 1;
      else sel = (sel + (e.key === 'ArrowDown' ? 1 : items.length - 1)) % items.length;
      markSelected(items);
      items[sel].scrollIntoView({ block: 'nearest' });
    } else if (e.key === 'Enter') {
      e.preventDefault();
      items[sel >= 0 ? sel : 0].click();
      closeResults();
    }
  });
  resultsEl.addEventListener('click', function (e) {
    if (e.target.closest('.result')) { searchEl.value = ''; closeResults(); }
  });
  document.addEventListener('click', function (e) {
    if (!e.target.closest('.search-wrap')) closeResults();
  });
  document.addEventListener('keydown', function (e) {
    var tag = document.activeElement ? document.activeElement.tagName : '';
    if (e.key === '/' && !/INPUT|TEXTAREA/.test(tag)) {
      e.preventDefault();
      searchEl.focus();
      searchEl.select();
    }
  });

  /* ---------- theme + drawer -------------------------------------------- */

  var themeBtn = document.getElementById('theme');
  var root = document.documentElement;
  var stored = null;
  try { stored = localStorage.getItem('vgse-wiki-theme'); } catch (err) { /* storage blocked */ }
  if (stored === 'dark' || stored === 'light') root.setAttribute('data-theme', stored);

  function isDark() {
    var explicit = root.getAttribute('data-theme');
    return explicit ? explicit === 'dark' : matchMedia('(prefers-color-scheme: dark)').matches;
  }
  function themeLabel() {
    var dark = isDark();
    themeBtn.textContent = dark ? 'Light' : 'Dark';
    themeBtn.setAttribute('aria-label', 'Switch to ' + (dark ? 'light' : 'dark') + ' theme');
  }
  themeBtn.addEventListener('click', function () {
    var nextTheme = isDark() ? 'light' : 'dark';
    root.setAttribute('data-theme', nextTheme);
    try { localStorage.setItem('vgse-wiki-theme', nextTheme); } catch (err) { /* storage blocked */ }
    themeLabel();
  });
  themeLabel();

  var sidebar = document.getElementById('sidebar');
  var menuBtn = document.getElementById('menu');
  var scrim = null;

  function closeDrawer() {
    var wasOpen = sidebar.classList.contains('open');
    sidebar.classList.remove('open');
    document.body.classList.remove('drawer-open');
    menuBtn.setAttribute('aria-expanded', 'false');
    if (scrim) { scrim.remove(); scrim = null; }
    if (wasOpen) menuBtn.focus();
  }

  function openDrawer() {
    sidebar.classList.add('open');
    // The drawer covers the page, so stop the article scrolling underneath it.
    document.body.classList.add('drawer-open');
    menuBtn.setAttribute('aria-expanded', 'true');
    scrim = document.createElement('div');
    scrim.className = 'scrim';
    scrim.addEventListener('click', closeDrawer);
    document.body.appendChild(scrim);
    var firstLink = sidebar.querySelector('#search, .nav a');
    if (firstLink) firstLink.focus();
  }

  menuBtn.addEventListener('click', function () {
    if (sidebar.classList.contains('open')) closeDrawer();
    else openDrawer();
  });

  // A modal that traps you is worse than no modal: Escape always gets you out, and
  // Tab cycles within the drawer instead of walking the page behind it.
  document.addEventListener('keydown', function (e) {
    if (!sidebar.classList.contains('open')) return;
    if (e.key === 'Escape') { e.preventDefault(); closeDrawer(); return; }
    if (e.key !== 'Tab') return;
    var f = sidebar.querySelectorAll('a[href], button, input:not([disabled])');
    if (!f.length) return;
    var first = f[0], last = f[f.length - 1];
    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  });

  route();
})();
