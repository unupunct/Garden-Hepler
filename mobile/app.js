"use strict";
// Garden Helper phone page. Talks to the desktop app's local API with the secret token (kept only in this browser).
const T = {
  en: { today: "Today", tasks: "Tasks", animals: "Animals", greenhouse: "Greenhouse", cameras: "Cameras", token: "Secret token", connect: "Connect", tokenHelp: "Paste the token from Garden Helper (Connect page), or open the phone link it shows.",
    badToken: "The app did not accept that token.", offline: "Cannot reach Garden Helper. Is the PC on and the app open?", overdue: "overdue", dueToday: "due today", thisWeek: "this week", water: "to water", animalsN: "animals", eggs: "eggs today",
    month: "This month", income: "Income", expenses: "Expenses", withdrawal: "Treatment withdrawal active: do not use eggs, milk or meat.", comingUp: "Coming up", nothing: "Nothing due.", done: "Done", noTasks: "No open tasks.",
    add: "Add", quantity: "Number", until: "withdrawal until", none: "No data yet.", ago: "min ago", on: "On", off: "Off", auto: "Auto", manual: "by hand", vent: "Roof vent", fan: "Fan", water_: "Watering valve", pump: "Water pump", light: "Grow light",
    air: "Air", humidity: "Humidity", soil: "Soil", alerts: "alerts", noCams: "No cameras set up.", refresh: "Refresh", sign: "Sign out", added: "Added", failed: "That did not work", noHouse: "No greenhouse yet.", needsYou: "Needs you today", allClear: "Nothing needs you today.", photo: "Add a photo", caption: "Caption (optional)", saved: "Photo saved" },
  ro: { today: "Azi", tasks: "Sarcini", animals: "Animale", greenhouse: "Seră", cameras: "Camere", token: "Token secret", connect: "Conectare", tokenHelp: "Lipește tokenul din Garden Helper (pagina Conectare) sau deschide linkul pentru telefon afișat acolo.",
    badToken: "Aplicația nu a acceptat tokenul.", offline: "Nu pot ajunge la Garden Helper. PC-ul e pornit și aplicația deschisă?", overdue: "întârziate", dueToday: "scadente azi", thisWeek: "săptămâna aceasta", water: "de udat", animalsN: "animale", eggs: "ouă azi",
    month: "Luna aceasta", income: "Venituri", expenses: "Cheltuieli", withdrawal: "Pauză de tratament activă: nu folosi ouă, lapte sau carne.", comingUp: "Urmează", nothing: "Nimic scadent.", done: "Gata", noTasks: "Nicio sarcină deschisă.",
    add: "Adaugă", quantity: "Număr", until: "pauză până la", none: "Încă nu sunt date.", ago: "min în urmă", on: "Pornit", off: "Oprit", auto: "Auto", manual: "manual", vent: "Fereastra din acoperiș", fan: "Ventilator", water_: "Robinet irigare", pump: "Pompă de apă", light: "Lumină de creștere",
    air: "Aer", humidity: "Umiditate", soil: "Sol", alerts: "alerte", noCams: "Nicio cameră configurată.", refresh: "Reîmprospătează", sign: "Deconectare", added: "Adăugat", failed: "Nu a mers", noHouse: "Nicio seră încă.", needsYou: "Te așteaptă azi", allClear: "Nimic nu te așteaptă azi.", photo: "Adaugă o poză", caption: "Descriere (opțional)", saved: "Poză salvată" },
};
const L = T[(navigator.language || "en").toLowerCase().startsWith("ro") ? "ro" : "en"];
document.documentElement.lang = (navigator.language || "en").slice(0, 2);

const store = { get(k) { try { return localStorage.getItem(k); } catch { return null; } }, set(k, v) { try { localStorage.setItem(k, v); } catch { /* private mode */ } }, del(k) { try { localStorage.removeItem(k); } catch { /* ignore */ } } };
const main = document.getElementById("main"), nav = document.getElementById("nav"), sub = document.getElementById("sub");
let token = store.get("gh.token"), tab = "today", timer = null;

// Opening the phone link (…/#t=TOKEN) saves the token and removes it from the address bar.
const m = /[#&]t=([0-9a-f]+)/i.exec(location.hash);
if (m) { token = m[1]; store.set("gh.token", token); history.replaceState(null, "", location.pathname); }

function h(tag, props, ...kids) {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(props || {})) { if (k === "class") e.className = v; else if (k.startsWith("on")) e.addEventListener(k.slice(2), v); else if (v !== false && v != null) e.setAttribute(k, v === true ? "" : v); }
  for (const kid of kids.flat()) if (kid != null && kid !== false) e.append(kid.nodeType ? kid : document.createTextNode(String(kid)));
  return e;
}
const show = (...nodes) => main.replaceChildren(...nodes);

async function api(path, opts = {}) {
  const r = await fetch(path, { ...opts, headers: { ...(opts.headers || {}), Authorization: "Bearer " + token, "Content-Type": "application/json" } });
  if (r.status === 401) { signOut(true); throw new Error("401"); }
  return r;
}
const json = async (path, opts) => { const r = await api(path, opts); if (!r.ok) throw new Error(r.status); return r.json(); };
const post = (path, body) => json(path, { method: "POST", body: JSON.stringify(body || {}) });

function signOut(bad) { store.del("gh.token"); token = null; clearInterval(timer); login(bad ? L.badToken : ""); }

function login(msg) {
  nav.hidden = true; sub.textContent = "";
  const input = h("input", { type: "password", autocomplete: "off", "aria-label": L.token, placeholder: L.token });
  const note = h("div", { class: "err", role: "alert" }, msg || "");
  show(h("div", { class: "card" }, h("h2", {}, L.connect), h("p", { class: "muted" }, L.tokenHelp), input, note,
    h("div", { class: "row" }, h("span"), h("button", { class: "primary", onclick: async () => {
      token = input.value.trim(); if (!token) return;
      try { const r = await fetch("/api/v1/status", { headers: { Authorization: "Bearer " + token } }); if (!r.ok) { note.textContent = L.badToken; return; } store.set("gh.token", token); start(); }
      catch { note.textContent = L.offline; }
    } }, L.connect))));
}

const tile = (n, label, alert) => h("div", { class: "tile" + (alert && n > 0 ? " alert" : "") }, h("b", {}, n), label);
const dateText = d => new Date(d + "T00:00:00").toLocaleDateString(undefined, { weekday: "short", day: "numeric", month: "short" });
const money = v => Number(v).toLocaleString(undefined, { maximumFractionDigits: 0 });

async function viewToday() {
  const s = await json("/api/v1/summary");
  sub.textContent = dateText(s.date);
  show(
    s.withdrawalActive ? h("div", { class: "warn" }, L.withdrawal) : null,
    h("div", { class: "tiles" }, tile(s.tasks.overdue, L.overdue, true), tile(s.tasks.today, L.dueToday), tile(s.tasks.week, L.thisWeek), tile(s.wateringDue, L.water), tile(s.animalsTotal, L.animalsN), tile(s.eggsToday, L.eggs)),
    todayCard(await json("/api/v1/today")),
    photoCard(),
    h("div", { class: "card" }, h("h2", {}, L.comingUp), s.upcoming.length ? s.upcoming.map(u => h("div", { class: "row" }, h("span", {}, u.title), h("span", { class: "muted" }, dateText(u.date)))) : h("div", { class: "muted" }, L.nothing)),
    h("div", { class: "card" }, h("h2", {}, L.month), h("div", { class: "row" }, L.income, h("b", {}, money(s.month.income))), h("div", { class: "row" }, L.expenses, h("b", {}, money(s.month.expenses)))));
}

function todayCard(items) {
  const labels = { Done: L.done, Watered: L.done, WaterBed: L.done, LogProduction: L.add };
  const acts = items.filter(i => labels[i.action] || i.action.startsWith("Open")).slice(0, 25);
  return h("div", { class: "card" }, h("h2", {}, L.needsYou), acts.length ? acts.map(i => {
    const row = h("div", { class: "row" });
    const q = i.needsNumber ? h("input", { type: "number", inputmode: "decimal", min: "0", placeholder: L.quantity, "aria-label": L.quantity, style: "width:84px" }) : null;
    const btn = labels[i.action] ? h("button", { class: i.severity === "urgent" ? "primary" : "", onclick: async e => {
      const body = { action: i.action, refId: i.refId, refIds: i.refIds };
      if (q) { const n = parseFloat(q.value.replace(",", ".")); if (!(n >= 0)) return; body.quantity = n; }
      e.target.disabled = true;
      try { await post("/api/v1/today/act", body); row.style.opacity = ".4"; } catch { e.target.disabled = false; alert(L.failed); }
    } }, labels[i.action]) : null;
    row.append(h("span", { class: i.severity === "urgent" ? "err" : "" }, i.glyph + " " + i.text), h("span", {}, q, btn));
    return row;
  }) : h("div", { class: "muted" }, L.allClear));
}

function photoCard() {
  const file = h("input", { type: "file", accept: "image/*", capture: "environment", "aria-label": L.photo });
  const cap = h("input", { type: "text", placeholder: L.caption, "aria-label": L.caption, maxlength: "200" });
  const note = h("div", { class: "muted", role: "status" });
  file.addEventListener("change", async () => {
    const f = file.files[0]; if (!f) return;
    try {
      const r = await api("/api/v1/photos?kind=general&caption=" + encodeURIComponent(cap.value), { method: "POST", body: f, headers: { "Content-Type": "application/octet-stream" } });
      note.textContent = r.ok ? L.saved : L.failed; if (r.ok) { cap.value = ""; file.value = ""; }
    } catch { note.textContent = L.failed; }
  });
  return h("div", { class: "card" }, h("h2", {}, L.photo), cap, h("div", { class: "row" }, file), note);
}

async function viewTasks() {
  const tasks = await json("/api/v1/tasks");
  sub.textContent = L.tasks;
  show(tasks.length ? h("div", { class: "card" }, tasks.map(t => h("div", { class: "row" },
    h("span", {}, t.title, h("br"), h("span", { class: t.overdue ? "err muted" : "muted" }, dateText(t.due))),
    h("button", { onclick: async e => { e.target.disabled = true; try { await post(`/api/v1/tasks/${t.id}/done`); } catch { /* shown on reload */ } render(); } }, L.done)))) : h("div", { class: "card muted" }, L.noTasks));
}

async function viewAnimals() {
  const s = await json("/api/v1/summary");
  sub.textContent = L.animals;
  show(...s.animals.map(a => {
    const q = h("input", { type: "number", inputmode: "decimal", min: "0", placeholder: L.quantity, "aria-label": L.quantity + " " + a.name });
    return h("div", { class: "card" }, h("h2", {}, a.name), h("div", { class: "muted" }, `${a.count} · ${L.eggs}: ${a.eggsToday}`),
      a.withdrawalUntil ? h("div", { class: "warn" }, `${L.until} ${dateText(a.withdrawalUntil)}`) : null,
      h("div", { class: "row" }, q, h("button", { class: "primary", onclick: async () => { const n = parseFloat(q.value.replace(",", ".")); if (!(n > 0)) return; try { await post(`/api/v1/animals/${a.id}/production`, { quantity: n }); } catch { alert(L.failed); } render(); } }, L.add)));
  }));
}

async function viewGreenhouse() {
  const s = await json("/api/v1/summary");
  sub.textContent = L.greenhouse;
  if (!s.greenhouses.length) return show(h("div", { class: "card muted" }, L.noHouse));
  show(...s.greenhouses.map(g => {
    const val = (v, u) => v == null ? "–" : v.toFixed(1) + u;
    return h("div", { class: "card" }, h("h2", {}, g.name),
      h("div", { class: "tiles" }, tile(val(g.airTemp, "°"), L.air), tile(val(g.humidity, "%"), L.humidity), tile(val(g.soilMoisture, "%"), L.soil), tile(g.alerts, L.alerts, true)),
      g.minutesSinceData == null ? h("div", { class: "muted" }, L.none) : h("div", { class: "muted" }, `${g.minutesSinceData} ${L.ago}`),
      ...Object.entries(g.switches).map(([k, on]) => h("div", { class: "row" }, h("span", {}, L[k === "water" ? "water_" : k] || k),
        h("span", {}, h("button", { class: on ? "on" : "", "aria-pressed": on, onclick: async () => { await post(`/api/v1/greenhouse/${g.id}/switch/${k}`, { on: !on }); render(); } }, on ? L.on : L.off), " ",
          h("button", { onclick: async () => { await post(`/api/v1/greenhouse/${g.id}/switch/${k}`, { auto: true }); render(); } }, L.auto)))));
  }));
}

async function viewCameras() {
  const cams = await json("/api/v1/cameras");
  sub.textContent = L.cameras;
  if (!cams.length) return show(h("div", { class: "card muted" }, L.noCams));
  const cards = cams.map(c => {
    const img = h("img", { class: "cam", alt: c.name });
    const load = async () => { try { const r = await api(`/api/v1/cameras/${c.id}/snapshot`); if (r.ok) { const u = URL.createObjectURL(await r.blob()); const old = img.src; img.src = u; if (old.startsWith("blob:")) URL.revokeObjectURL(old); } } catch { /* offline */ } };
    load();
    return h("div", { class: "card" }, h("h2", {}, c.name), img, h("div", { class: "row" }, h("span"), h("button", { onclick: load }, L.refresh)));
  });
  show(...cards);
}

const VIEWS = { today: viewToday, tasks: viewTasks, animals: viewAnimals, greenhouse: viewGreenhouse, cameras: viewCameras };
async function render() {
  try { await VIEWS[tab](); }
  catch (e) { if (String(e.message) !== "401") show(h("div", { class: "card err", role: "alert" }, L.offline)); }
}

function start() {
  nav.hidden = false;
  nav.replaceChildren(...Object.keys(VIEWS).map(k => h("button", { "aria-current": k === tab ? "page" : false, onclick: () => { tab = k; start(); } }, L[k])), h("button", { onclick: () => signOut(false), "aria-label": L.sign }, "⎋"));
  render();
  clearInterval(timer);
  if (tab !== "cameras") timer = setInterval(() => { if (!document.hidden && tab !== "animals") render(); }, 30000);   // not on Animals: it would wipe a typed number
}

if ("serviceWorker" in navigator && window.isSecureContext) navigator.serviceWorker.register("/sw.js").catch(() => {});
token ? start() : login("");
