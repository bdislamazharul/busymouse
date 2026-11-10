// BusyMouse website: theme, copy buttons, release links, and the live prototype.
(() => {
  'use strict';

  const $ = (s, el = document) => el.querySelector(s);
  const $$ = (s, el = document) => [...el.querySelectorAll(s)];
  const store = {
    get (k) { try { return localStorage.getItem(k); } catch { return null; } },
    set (k, v) { try { localStorage.setItem(k, v); } catch { /* private mode */ } }
  };
  const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const timeFmt = new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit' });

  // ---------- Site theme ----------
  const siteIsDark = () => {
    const t = document.documentElement.dataset.theme;
    return t ? t === 'dark' : matchMedia('(prefers-color-scheme: dark)').matches;
  };
  $('#site-theme')?.addEventListener('click', () => {
    const next = siteIsDark() ? 'light' : 'dark';
    document.documentElement.dataset.theme = next;
    store.set('bm-theme', next);
    if (!store.get('bm-app-theme')) setAppTheme(next, false);
  });

  // ---------- Clocks in illustrations ----------
  $$('[data-clock]').forEach(el => {
    el.textContent = timeFmt.format(new Date(Date.now() + Number(el.dataset.clock) * 60000));
  });
  const heroCount = $('[data-hero-count]');
  if (heroCount && !reduceMotion) {
    let n = 9;
    setInterval(() => { n = n <= 1 ? 9 : n - 1; heroCount.textContent = `${n} s`; }, 1000);
  }

  // ---------- Copy buttons ----------
  $$('.cmd').forEach(box => {
    const btn = $('.cmd-copy', box);
    btn?.addEventListener('click', async () => {
      const text = box.dataset.copy;
      try {
        await navigator.clipboard.writeText(text);
      } catch {
        const r = document.createRange(); r.selectNodeContents($('code', box));
        const sel = getSelection(); sel.removeAllRanges(); sel.addRange(r);
        document.execCommand('copy');
      }
      btn.classList.add('copied');
      $('span', btn).textContent = 'Copied';
      setTimeout(() => { btn.classList.remove('copied'); $('span', btn).textContent = 'Copy'; }, 1800);
    });
  });

  // ---------- Latest release: direct download links ----------
  // Progressive: links already point at the Releases page; this swaps in the exact files.
  fetch('https://api.github.com/repos/bdislamazharul/busymouse/releases/latest', { headers: { Accept: 'application/vnd.github+json' } })
    .then(r => r.ok ? r.json() : Promise.reject(r.status))
    .then(rel => {
      const assets = rel.assets || [];
      $$('[data-asset]').forEach(a => {
        const hit = assets.find(x => x.name.endsWith(`-${a.dataset.asset}`) && !(a.dataset.asset.endsWith('setup.exe') && x.name.includes('no-runtime')));
        if (hit) a.href = hit.browser_download_url;
      });
      const note = $('#release-note');
      if (note && rel.tag_name) note.textContent = `Latest: ${rel.tag_name.replace(/^v/, 'version ')}.`;
    })
    .catch(() => { /* offline or rate-limited: the Releases page links still work */ });

  // ---------- Reveal on scroll ----------
  if ('IntersectionObserver' in window && !reduceMotion) {
    const io = new IntersectionObserver(entries => entries.forEach(e => {
      if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); }
    }), { rootMargin: '0px 0px -8% 0px' });
    $$('.section-head, .tile, .dl-card, .terminal, .shot-rail figure, .faq details, .cta-inner').forEach((el, i) => {
      el.classList.add('reveal');
      el.style.transitionDelay = `${(i % 3) * 70}ms`;
      io.observe(el);
    });
  }

  // =====================================================================
  // Live prototype
  // =====================================================================
  const stage = $('#stage');
  if (!stage) return;

  const ACCENTS = [
    ['#FFB900', 'Yellow gold'], ['#F7630C', 'Orange bright'], ['#CA5010', 'Orange dark'], ['#E81123', 'Red'],
    ['#EA005E', 'Rose bright'], ['#C30052', 'Rose'], ['#E3008C', 'Plum light'], ['#881798', 'Violet red'],
    ['#744DA9', 'Iris spring'], ['#6B69D6', 'Purple shadow dark'], ['#0078D7', 'Default blue'], ['#0099BC', 'Cool blue bright'],
    ['#038387', 'Seafoam teal'], ['#00B294', 'Mint light'], ['#107C10', 'Green'], ['#68768A', 'Steel blue']
  ];
  // The app's "Nudge every" steps run from 1 second to 3 hours.
  const INTERVALS = [1, 2, 3, 5, 10, 15, 20, 30, 45, 60, 90, 120, 180, 300, 600, 900, 1200, 1800, 3600, 5400, 7200, 10800];
  const IDLE_LOCK = 15; // seconds until the pretend PC locks, scaled down from minutes
  const DIM_AT = 10;

  const state = {
    on: false,
    mode: 'default',
    limit: 1800,
    distance: 10,
    interval: 2,
    random: false,
    endsAt: 0,
    nextAt: 0,
    step: 0,
    lastActivity: performance.now(),
    lastUserMove: 0,
    locked: false,
    offset: { x: 0, y: 0 }
  };

  const el = {
    power: $('#power'), flyPower: $('#fly-power'),
    title: $('#state-title'), sub: $('#state-sub'), tLabel: $('#timer-label'), tValue: $('#timer-value'),
    flyTitle: $('#fly-title'), flySub: $('#fly-sub'), flyLabel: $('#fly-label'), flyValue: $('#fly-value'),
    distance: $('#distance'), distanceOut: $('#distance-out'),
    interval: $('#interval'), intervalOut: $('#interval-out'),
    random: $('#random'),
    hud: $('#hud-text'),
    pointer: $('#fake-pointer'), zone: $('#pointer-zone'), trail: $('#pz-trail'), ripple: $('#pz-ripple'), toast: $('#pz-toast'),
    lock: $('#lock'), lockTime: $('#lock-time'), lockDate: $('#lock-date'), unlock: $('#unlock'),
    presence: $('#presence'), presenceText: $('#presence-text'),
    tray: $('#tray-icon'), flyout: $('#flyout'), clock: $('#tb-clock'),
    more: $('#more'), moreArea: $('#more-area'),
    previewArrow: $('#preview-arrow'), previewGuide: $('#preview-guide'), previewRipple: $('#preview-ripple')
  };

  // ----- formatting -----
  const fmtSeconds = s => s < 60 ? `${s} s` : s < 3600 ? `${s / 60} min` : `${+(s / 3600).toFixed(1)} h`;
  const fmtLimit = s => s === 0 ? 'No limit' : fmtSeconds(s);
  const fmtLeft = ms => {
    const t = Math.max(0, Math.ceil(ms / 1000));
    const h = Math.floor(t / 3600), m = Math.floor(t % 3600 / 60), s = t % 60;
    return h ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${m}:${String(s).padStart(2, '0')}`;
  };
  const intervalSec = () => INTERVALS[state.interval];

  // ----- theme & accent -----
  function setAppTheme (theme, remember = true) {
    stage.dataset.themeApp = theme;
    $$('[data-app-theme]').forEach(b => b.setAttribute('aria-checked', String(b.dataset.appTheme === theme)));
    if (remember) store.set('bm-app-theme', theme);
  }
  setAppTheme(store.get('bm-app-theme') || (siteIsDark() ? 'dark' : 'light'), false);
  $$('[data-app-theme]').forEach(b => b.addEventListener('click', () => setAppTheme(b.dataset.appTheme)));

  const swatches = $('#swatches');
  const setAccent = hex => {
    stage.style.setProperty('--acc', hex);
    $$('button', swatches).forEach(b => b.setAttribute('aria-checked', String(b.dataset.hex === hex)));
    store.set('bm-accent', hex);
  };
  ACCENTS.forEach(([hex, name]) => {
    const b = document.createElement('button');
    b.type = 'button'; b.setAttribute('role', 'radio'); b.dataset.hex = hex; b.title = name; b.setAttribute('aria-label', name);
    b.style.background = hex;
    b.addEventListener('click', () => setAccent(hex));
    swatches.append(b);
  });
  setAccent(store.get('bm-accent') || '#C30052');

  // ----- sliders -----
  const paintRange = r => r.style.setProperty('--p', `${(r.value - r.min) / (r.max - r.min) * 100}%`);
  el.distance.addEventListener('input', () => {
    state.distance = +el.distance.value; el.distanceOut.textContent = `${state.distance}×`; paintRange(el.distance);
  });
  el.interval.addEventListener('input', () => {
    state.interval = +el.interval.value; el.intervalOut.textContent = fmtSeconds(intervalSec()); paintRange(el.interval);
    if (state.on) scheduleNext();
    render();
  });
  el.random.addEventListener('change', () => { state.random = el.random.checked; if (state.on) scheduleNext(); });
  [el.distance, el.interval].forEach(paintRange);
  el.intervalOut.textContent = fmtSeconds(intervalSec());

  // ----- radio groups -----
  const radio = (selector, attr, apply) => $$(selector).forEach(b => b.addEventListener('click', () => {
    $$(selector).forEach(x => x.setAttribute('aria-checked', String(x === b)));
    apply(b.dataset[attr]);
  }));
  radio('.mode', 'mode', m => { state.mode = m; state.step = 0; returnPointer(); buildPreview(); render(); });
  radio('.segmented button', 'limit', v => {
    state.limit = +v;
    if (state.on) state.endsAt = state.limit ? Date.now() + state.limit * 1000 : 0;
    render();
  });
  // Arrow keys within radio groups.
  $$('[role="radiogroup"]').forEach(g => g.addEventListener('keydown', e => {
    const items = $$('[role="radio"]', g);
    const i = items.indexOf(document.activeElement);
    if (i < 0) return;
    const d = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key];
    if (!d) return;
    e.preventDefault();
    const next = items[(i + d + items.length) % items.length];
    next.focus(); next.click();
  }));

  // ----- more / less -----
  el.more.addEventListener('click', () => {
    const open = el.more.getAttribute('aria-expanded') !== 'true';
    el.more.setAttribute('aria-expanded', String(open));
    el.more.firstChild.textContent = open ? 'Less ' : 'More ';
    el.moreArea.classList.toggle('collapsed', !open);
  });

  // ----- on / off -----
  let wakeLock = null;
  async function holdWakeLock (want) {
    try {
      if (want && !wakeLock && 'wakeLock' in navigator && document.visibilityState === 'visible') {
        wakeLock = await navigator.wakeLock.request('screen');
        wakeLock.addEventListener('release', () => { wakeLock = null; });
      } else if (!want && wakeLock) {
        await wakeLock.release(); wakeLock = null;
      }
    } catch { /* not allowed here; the demo still works */ }
  }
  document.addEventListener('visibilitychange', () => { if (state.on) holdWakeLock(true); });

  function setOn (on) {
    state.on = on;
    stage.classList.toggle('is-on', on);
    [el.power, el.flyPower].forEach(b => b.setAttribute('aria-pressed', String(on)));
    if (on) {
      state.endsAt = state.limit ? Date.now() + state.limit * 1000 : 0;
      state.step = 0;
      activity();
      scheduleNext(true);
    } else {
      returnPointer();
    }
    holdWakeLock(on);
    render();
  }
  el.power.addEventListener('click', () => setOn(!state.on));
  el.flyPower.addEventListener('click', () => setOn(!state.on));

  // ----- tray flyout -----
  el.tray.addEventListener('click', e => {
    e.stopPropagation();
    const show = el.flyout.hidden;
    el.flyout.hidden = !show;
    el.tray.setAttribute('aria-expanded', String(show));
  });
  stage.addEventListener('click', e => {
    if (!el.flyout.hidden && !el.flyout.contains(e.target) && e.target !== el.tray) {
      el.flyout.hidden = true; el.tray.setAttribute('aria-expanded', 'false');
    }
  });

  // ----- activity, idle & lock -----
  function activity () {
    state.lastActivity = performance.now();
    if (state.locked) {
      state.locked = false;
      el.lock.hidden = true;
    }
  }
  // Real mouse use over the pretend desktop counts as activity, and makes BusyMouse skip its next nudge.
  let lastMove = 0;
  stage.addEventListener('pointermove', e => {
    if (state.locked || e.pointerType === 'touch') return;
    const now = performance.now();
    if (now - lastMove < 120) return;
    lastMove = now;
    state.lastActivity = now;
    // Only the desktop counts as "using the mouse"; working the window itself shouldn't cancel the next nudge.
    if (!e.target.closest('.appwin, .flyout, .taskbar')) state.lastUserMove = now;
  });
  stage.addEventListener('pointerdown', () => { if (!state.locked) activity(); });
  stage.addEventListener('keydown', () => { if (!state.locked) activity(); });
  el.unlock.addEventListener('click', e => { e.stopPropagation(); activity(); render(); });

  // ----- nudging -----
  function scheduleNext (soon = false) {
    const full = intervalSec() * 1000;
    const wait = state.random ? 1000 + Math.random() * Math.max(0, full - 1000) : full;
    state.nextAt = performance.now() + (soon ? Math.min(wait, 1200) : wait);
  }

  function patternStep () {
    const s = Math.min(3 * state.distance, 90); // pixels, capped so it stays inside the demo
    const i = state.step++;
    switch (state.mode) {
      case 'default': return i % 2 ? { x: 0, y: 0 } : { x: s, y: 0 };
      case 'diagonal': return i % 2 ? { x: 0, y: 0 } : { x: s, y: s };
      case 'quiet': return { x: 0, y: 0 };
      case 'orbit': {
        const a = 2 * Math.PI * ((i + 1) % 12) / 12;
        return { x: s * Math.cos(a) - s, y: s * Math.sin(a) };
      }
    }
    return { x: 0, y: 0 };
  }

  function movePointer (to) {
    const from = state.offset;
    state.offset = to;
    el.pointer.style.transform = `translate(${to.x}px, ${to.y}px)`;
    // A dot where it came from, so you can see the move.
    if (from.x !== to.x || from.y !== to.y) {
      const dot = document.createElement('i');
      dot.style.left = `calc(50% + ${from.x}px)`; dot.style.top = `calc(50% + ${from.y}px)`;
      el.trail.append(dot);
      setTimeout(() => dot.remove(), 1300);
    }
  }
  function returnPointer () { movePointer({ x: 0, y: 0 }); }

  function ripple () {
    const r = document.createElement('i');
    el.ripple.style.left = `calc(50% + ${state.offset.x}px)`;
    el.ripple.style.top = `calc(50% + ${state.offset.y}px)`;
    el.ripple.append(r);
    setTimeout(() => r.remove(), 950);
  }

  let toastTimer = 0;
  function toast (text) {
    el.toast.textContent = text;
    el.toast.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => el.toast.classList.remove('show'), 1600);
  }

  function nudge () {
    const now = performance.now();
    // Like the app: if you used the mouse since the last nudge, skip this one.
    if (now - state.lastUserMove < intervalSec() * 1000 && now - state.lastUserMove < 4000) {
      toast('Skipped: you\'re using the mouse');
      return;
    }
    movePointer(patternStep());
    ripple();
    toast(state.mode === 'quiet' ? 'Nudged by 0 px' : `Nudged by ${Math.min(3 * state.distance, 90)} px`);
    state.lastActivity = now;
  }

  // ----- render -----
  function render () {
    const now = performance.now();
    const left = state.endsAt ? state.endsAt - Date.now() : 0;
    const stopsAt = state.endsAt ? timeFmt.format(new Date(state.endsAt)) : '';
    const next = Math.max(0, Math.ceil((state.nextAt - now) / 1000));

    const title = state.on ? 'Awake' : 'Off';
    const sub = state.on
      ? (state.endsAt ? `Stops at ${stopsAt}` : `Nudge every ${fmtSeconds(intervalSec())}`)
      : `Nudge every ${fmtSeconds(intervalSec())}`;
    let label, value;
    if (!state.on) { label = state.limit ? 'Time limit' : 'Time limit'; value = fmtLimit(state.limit); }
    else if (state.endsAt) { label = 'Time left'; value = fmtLeft(left); }
    else { label = 'Next nudge'; value = `${next} s`; }

    el.title.textContent = el.flyTitle.textContent = title;
    el.sub.textContent = el.flySub.textContent = sub;
    el.tLabel.textContent = label; el.tValue.textContent = value;
    el.flyLabel.textContent = state.on ? 'Next nudge' : label;
    el.flyValue.textContent = state.on ? `${next} s` : value;

    // HUD and presence
    const idle = (now - state.lastActivity) / 1000;
    const untilLock = Math.max(0, Math.ceil(IDLE_LOCK - idle));
    stage.classList.toggle('is-dimming', !state.locked && !state.on && idle >= DIM_AT);
    stage.classList.toggle('is-warn', !state.on && idle >= DIM_AT);
    if (state.locked) el.hud.textContent = 'Locked. You look away.';
    else if (state.on) el.hud.textContent = `Kept awake · next nudge in ${next} s`;
    else el.hud.textContent = `Idle ${Math.floor(idle)} s · screen locks in ${untilLock} s`;
    el.presence.classList.toggle('away', state.locked);
    el.presenceText.textContent = state.locked ? 'Away' : 'Available';

    const d = new Date();
    el.clock.textContent = timeFmt.format(d);
    if (state.locked) {
      el.lockTime.textContent = timeFmt.format(d).replace(/\s?[AP]M$/i, '');
      el.lockDate.textContent = d.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' });
    }
  }

  // ----- the mode preview tile -----
  const SVGNS = 'http://www.w3.org/2000/svg';
  function buildPreview () {
    el.previewGuide.replaceChildren();
    const add = (tag, attrs) => {
      const n = document.createElementNS(SVGNS, tag);
      Object.entries(attrs).forEach(([k, v]) => n.setAttribute(k, v));
      el.previewGuide.append(n);
    };
    if (state.mode === 'default') add('path', { d: 'M37 57H77' });
    if (state.mode === 'diagonal') add('path', { d: 'M43 43L71 71' });
    if (state.mode === 'orbit') add('circle', { cx: 57, cy: 57, r: 16 });
    el.previewRipple.style.opacity = 0;
  }
  buildPreview();

  const ease = t => t < .5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
  function previewFrame (t) {
    const sec = t / 1000 * .8; // the app's preview runs at four fifths speed
    let x = 57, y = 57;
    if (state.mode === 'default' || state.mode === 'diagonal') {
      const p = sec % 2; // out for one second, back for one
      const k = ease(p < 1 ? p : 2 - p);
      x = 45 + 24 * k;
      y = state.mode === 'diagonal' ? 45 + 24 * k : 57;
    } else if (state.mode === 'orbit') {
      const a = sec * Math.PI * .9;
      x = 57 + 16 * Math.cos(a); y = 57 + 16 * Math.sin(a);
    } else {
      const p = sec % 1.6 / 1.6;
      el.previewRipple.setAttribute('r', String(6 + 26 * p));
      el.previewRipple.style.opacity = String(state.on ? (1 - p) * .9 : (1 - p) * .35);
    }
    el.previewArrow.setAttribute('transform', `translate(${x} ${y}) scale(2.6)`);
  }

  // ----- main loop -----
  let lastRender = 0;
  function loop (t) {
    if (stageVisible) {
      if (!reduceMotion) previewFrame(t);
      if (state.on) {
        if (state.endsAt && Date.now() >= state.endsAt) {
          setOn(false);
          toast('Time limit reached: stopped');
        } else if (t >= state.nextAt) {
          nudge();
          scheduleNext();
        }
      }
      const idle = (t - state.lastActivity) / 1000;
      if (!state.locked && idle >= IDLE_LOCK) {
        state.locked = true;
        el.lock.hidden = false;
        el.flyout.hidden = true;
        render();
      }
      if (t - lastRender > 200) { render(); lastRender = t; }
    } else {
      // Off screen: don't let the pretend PC lock behind the visitor's back.
      state.lastActivity = t;
      if (state.on && t >= state.nextAt) scheduleNext();
    }
    requestAnimationFrame(loop);
  }

  let stageVisible = true;
  if ('IntersectionObserver' in window) {
    new IntersectionObserver(([e]) => { stageVisible = e.isIntersecting; }).observe(stage);
  }
  render();
  requestAnimationFrame(loop);
})();
