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
    // Pull comments and strings out first so keywords inside them stay plain.
    out = out.replace(/'[^\n]*|\/\/[^\n]*|&quot;[^&\n]*&quot;|"[^"\n]*"|#[A-Za-z]+/g, function (m) {
      var cls = 'tok-com';
      if (m.charAt(0) === '"' || m.indexOf('&quot;') === 0) cls = 'tok-str';
      else if (m.charAt(0) === '#') cls = 'tok-kw';
      parts.push('<span class="' + cls + '">' + m + '</span>');
      return SENT + (parts.length - 1) + SENT;
    });
    out = out.replace(KW_RE, '<span class="tok-kw">$1</span>');
    out = out.replace(/\b(\d+(?:\.\d+)?)\b/g, '<span class="tok-num">$1</span>');
    out = out.replace(new RegExp(SENT + '(\\d+)' + SENT, 'g'), function (_, i) { return parts[+i]; });
    return out;
  }

  function inline(s) {
    var stash = [];
    s = s.replace(/`([^`]+)`/g, function (_, c) {
      stash.push('<code>' + esc(c) + '</code>');
      return SENT + (stash.length - 1) + SENT;
    });
    s = esc(s);
    s = s.replace(/\[([^\]]+)\]\(([^)]+)\)/g, function (_, t, href) {
      var ext = /^https?:/.test(href) ? ' target="_blank" rel="noopener"' : '';
      return '<a href="' + href + '"' + ext + '>' + t + '</a>';
    });
    s = s.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
    s = s.replace(/(^|[\s(])\*([^*\n]+)\*/g, '$1<em>$2</em>');
    s = s.replace(new RegExp(SENT + '(\\d+)' + SENT, 'g'), function (_, i) { return stash[+i]; });
    return s;
  }

  function slug(s) {
    return s.toLowerCase().replace(/[`*_]/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
  }

  function render(md, headings) {
    var lines = md.split('\n');
    var html = [];
    var i = 0;
    var listStack = [];

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
        html.push('<pre' + (lang ? ' data-lang="' + esc(lang) + '"' : '') + '><code>' +
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
        var t = ['<div class="table-scroll"><table><thead><tr>'];
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
        var id = slug(text);
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
        html.push('<blockquote' + (cls ? ' class="' + cls + '"' : '') + '><p>' +
          q.join('\n').split('\n\n').map(inline).join('</p><p>') + '</p></blockquote>');
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
        html.push('<li>' + inline(li[3]) + '</li>');
        i++;
        continue;
      }

      if (line.trim() === '') { closeList(); i++; continue; }

      // paragraph
      closeList();
      var para = [];
      while (i < lines.length && lines[i].trim() !== '' &&
             !/^(#{2,4}\s|```|\||>|---+$|\s*([-*]|\d+\.)\s|<)/.test(lines[i])) {
        para.push(lines[i]); i++;
      }
      if (para.length) html.push('<p>' + inline(para.join(' ')) + '</p>');
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

    if (anchor) {
      var target = document.getElementById(anchor);
      if (target) { target.scrollIntoView(); return; }
    }
    window.scrollTo(0, 0);
  }

  function route() {
    var hash = location.hash.replace(/^#\/?/, '');
    var parts = hash.split('#');
    show(parts[0] || ORDER[0], parts[1]);
    closeDrawer();
  }
  window.addEventListener('hashchange', route);

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
      ? hits.map(function (h) {
          return '<a class="result" href="#/' + h.e.id + '"><span class="result-t">' + esc(h.e.title) +
            '</span><span class="result-s">' + esc(h.e.lede.slice(0, 92)) + '</span></a>';
        }).join('')
      : '<div class="result-empty">No page matches that. Try a symbol name, a file name, or a subsystem.</div>';
    resultsEl.classList.add('open');
    sel = -1;
  }

  searchEl.addEventListener('input', runSearch);
  searchEl.addEventListener('keydown', function (e) {
    var items = resultsEl.querySelectorAll('.result');
    if (e.key === 'Escape') { searchEl.value = ''; resultsEl.classList.remove('open'); searchEl.blur(); return; }
    if (!items.length) return;
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      sel = (sel + (e.key === 'ArrowDown' ? 1 : items.length - 1)) % items.length;
      Array.prototype.forEach.call(items, function (it, n) { it.classList.toggle('sel', n === sel); });
      items[sel].scrollIntoView({ block: 'nearest' });
    } else if (e.key === 'Enter') {
      e.preventDefault();
      items[sel >= 0 ? sel : 0].click();
      searchEl.blur();
      resultsEl.classList.remove('open');
    }
  });
  document.addEventListener('click', function (e) {
    if (!e.target.closest('.search-wrap')) resultsEl.classList.remove('open');
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
    sidebar.classList.remove('open');
    if (scrim) { scrim.remove(); scrim = null; }
  }
  menuBtn.addEventListener('click', function () {
    if (sidebar.classList.contains('open')) { closeDrawer(); return; }
    sidebar.classList.add('open');
    scrim = document.createElement('div');
    scrim.className = 'scrim';
    scrim.addEventListener('click', closeDrawer);
    document.body.appendChild(scrim);
  });

  route();
})();
