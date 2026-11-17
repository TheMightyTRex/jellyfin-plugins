// Takes the help centre's screenshots from the throwaway Jellyfin, marked up to the design system's SCREENSHOTS.md
// with its tools/annotate.js: a red outline round each control, a curved arrow, and a badge with the step's number
// in the article. Adapted from Starboard's tools/screenshots/help_shoot.mjs (17 November 2026).
//
// Each shot in tools/help_shots/shots-<phase>.json: { file, route, width?, height?, actions?: [...], marks: [{ t, step?, side?, tail? }],
// steps: [...], alt }. An action is { click: "text" | { role, name } | "$css" }, { fill: ["$css" or label, value] },
// { press: key }, { wait: ms }, { eval: "js" } or { goto: route }. A mark's t is the visible text of a control,
// "$css", "choice:<label>" (a tick box or radio with its words) or "near:<text>|<css>" (the closest css element
// round that text). A shot whose mark-up check fails is not taken.
import { signedIn, BASE } from "./common.mjs";
import fs from "fs";

const ONLY = (process.env.ONLY || "").split(",").filter(Boolean);
const SET = process.env.SET || "1-before-install";
const OUT = process.env.OUT || "/work/shots";
const annotate = fs.readFileSync("/annotate.js", "utf8");
const shots = JSON.parse(fs.readFileSync(`/tools/shots-${SET}.json`, "utf8")).shots
  .filter((s) => !ONLY.length || ONLY.some((o) => s.file.includes(o)));
fs.mkdirSync(OUT, { recursive: true });
const problems = [];
const sessions = {};

async function pageFor(shot) {
  const w = shot.width || 1280, h = shot.height || 900, user = shot.user || "Alex";
  const key = `${w}x${h}-${user}`;
  if (!sessions[key]) sessions[key] = await signedIn({ width: w, height: h, user });
  const page = await sessions[key].context.newPage();
  page.setDefaultTimeout(20000);
  return page;
}

function locate(page, t) {
  if (typeof t === "object") return page.getByRole(t.role, { name: t.name, exact: t.exact ?? true }).first();
  if (t.startsWith("$")) return page.locator(t.slice(1)).first();
  if (t.startsWith("label:")) return page.getByLabel(t.slice(6), { exact: true }).first();
  return page.getByText(t, { exact: true }).locator("visible=true").first();
}

// Waits until Jellyfin's spinners have gone (up to a minute), then the settling time.
async function settle(page, ms) {
  await page.waitForFunction(() => ![...document.querySelectorAll('.MuiCircularProgress-root, .docspinner, .mdlSpinnerActive')]
    .some((e) => e.getBoundingClientRect().width > 0), null, { timeout: 60000 }).catch(() => {});
  await page.waitForTimeout(ms);
}

async function act(page, a) {
  if (a.goto) { await page.goto(`${BASE}/web/#${a.goto}`); await settle(page, a.settle ?? 10000); return; }
  if (a.click) { await locate(page, a.click).click(); await settle(page, a.settle ?? 8000); return; }
  if (a.fill) { await locate(page, a.fill[0]).fill(a.fill[1]); await page.waitForTimeout(a.settle ?? 1500); return; }
  if (a.check) { await locate(page, a.check).check({ force: true }); await page.waitForTimeout(a.settle ?? 1500); return; }
  if (a.upload) { await page.locator(a.upload[0].slice(1)).setInputFiles(a.upload[1]); await page.waitForTimeout(a.settle ?? 2000); return; }
  if (a.hover) { await locate(page, a.hover).hover(); await page.waitForTimeout(a.settle ?? 3000); return; }
  if (a.select) { await locate(page, a.select[0]).selectOption({ label: a.select[1] }); await page.waitForTimeout(a.settle ?? 2000); return; }
  if (a.press) { await page.keyboard.press(a.press); await page.waitForTimeout(a.settle ?? 1500); return; }
  if (a.wait) { await page.waitForTimeout(a.wait); return; }
  if (a.eval) { await page.evaluate(`(async () => { ${a.eval} })()`); await page.waitForTimeout(a.settle ?? 1500); return; }
  if (a.mouse) { await page.mouse.click(a.mouse[0], a.mouse[1]); await page.waitForTimeout(a.settle ?? 8000); return; }
}

// "{task:Shrink pictures}" in a route is that scheduled task's id on this server, read from Jellyfin's API.
async function route(shot) {
  const m = shot.route.match(/\{task:([^}]+)\}/);
  if (!m) return shot.route;
  const s = Object.values(sessions)[0];
  const tasks = await (await fetch(`${BASE}/ScheduledTasks`, { headers: { Authorization: `MediaBrowser Token="${s.token}"` } })).json();
  return shot.route.replace(m[0], tasks.find((t) => t.Name === m[1]).Id);
}

async function take(shot) {
  const page = await pageFor(shot);
  try {
    await page.goto(`${BASE}/web/#${await route(shot)}`);
    await settle(page, shot.settle ?? 11000); // SCREENSHOTS.md section 3: at least 10 seconds after a load
    for (const a of shot.actions || []) await act(page, a);
    await page.evaluate(annotate);
    const result = await page.evaluate(({ marks, steps }) => {
      const find = (t) => {
        if (t.startsWith("$")) return document.querySelector(t.slice(1));
        if (t.startsWith("js:")) return (0, eval)(t.slice(3));
        if (t.startsWith("choice:")) return window.Annotate.choice(t.slice(7));
        if (t.startsWith("near:")) { const [text, css] = t.slice(5).split("|"); const e = window.Annotate.byText(text); return e && e.closest(css); }
        return window.Annotate.byText(t);
      };
      window.Annotate.clear();
      const missing = [];
      for (const m of marks) { const el = find(m.t); if (!el) missing.push(m.t); else if (m.scroll !== false) el.scrollIntoView({ block: m.block || "nearest" }); }
      for (const m of marks) { const el = find(m.t); if (el) window.Annotate.mark(el, { step: m.step ?? null, side: m.side || "right", tail: m.tail || 110 }); }
      const v = window.Annotate.verify({ steps });
      if (missing.length) { v.problems.unshift(`not on the page: ${missing.join(", ")}`); v.pass = false; }
      return v;
    }, { marks: shot.marks || [], steps: shot.steps || [] });
    await page.waitForTimeout(500);
    if (!result.pass) {
      problems.push(`${shot.file}: ${result.problems.join("; ")}`);
      await page.evaluate(() => window.Annotate.clear());
      await page.screenshot({ path: `${OUT}/FAILED-${shot.file}` });
      console.log(shot.file, "NOT TAKEN");
      return;
    }
    await page.screenshot({ path: `${OUT}/${shot.file}` });
    console.log(shot.file, "ok");
  } catch (e) {
    problems.push(`${shot.file}: ${String(e.message || e).split("\n")[0]}`);
    await page.screenshot({ path: `${OUT}/FAILED-${shot.file}` }).catch(() => {});
    console.log(shot.file, "NOT TAKEN");
  } finally {
    await page.close().catch(() => {});
  }
}

for (const shot of shots) await take(shot);
console.log(problems.length ? `Problems:\n${problems.join("\n")}` : "No problems");
for (const s of Object.values(sessions)) await s.browser.close();
process.exit(problems.length ? 1 : 0);
