// Takes every docs/screenshots image from the harness: 800x771 at 2x, dark theme, generic data.
// Usage: node shots.mjs [Name,Name...] to take all or only the named ones. Each PNG goes to docs/screenshots/.
import { fileURLToPath } from 'node:url';
import { open } from './shoot.mjs';
const OUT = fileURLToPath(new URL('../../docs/screenshots', import.meta.url));
const only = process.argv[2];
const HEADER = 95;

async function pick(p, name) {
  await p.evaluate((name) => {
    const s = document.querySelector('#selectScript');
    s.value = Array.from(s.options).find(o => o.textContent.replace(' (disabled)', '') === name).value;
    s.dispatchEvent(new Event('change', { bubbles: true }));
  }, name);
  await p.waitForTimeout(500);
}
// Scrolls so the element's top sits `gap` px under the header.
async function scrollTo(p, selector, gap = 20) {
  await p.evaluate(([sel, gap, h]) => {
    const e = document.querySelector(sel);
    window.scrollTo(0, e.getBoundingClientRect().top + window.scrollY - h - gap);
  }, [selector, gap, HEADER]);
  await p.waitForTimeout(300);
}
async function sectionTop(p, title, gap = 20) {
  await p.evaluate(([title, gap, h]) => {
    const t = Array.from(document.querySelectorAll('.base-section-title')).find(x => x.textContent.trim() === title);
    window.scrollTo(0, t.getBoundingClientRect().top + window.scrollY - h - gap);
  }, [title, gap, HEADER]);
  await p.waitForTimeout(300);
}
async function openStep(p, index) {
  await p.evaluate((i) => {
    document.querySelectorAll('#operationList .base-card-item-head')[i].click();
  }, index);
  await p.waitForTimeout(500);
}
async function blur(p) { await p.evaluate(() => { document.activeElement && document.activeElement.blur(); document.querySelectorAll('[role=dialog] *, dialog *, .base-dialog *').forEach(e => { if (e.scrollTop) e.scrollTop = 0; }); }); }

const shots = {
  Scripts: { page: 'scripts', act: async (p) => { await pick(p, 'Limit transcodes'); } },
  NewScript: { page: 'scripts', act: async (p) => {
      await pick(p, 'Limit transcodes');
      await p.click('#btnNewScript'); await p.waitForTimeout(500);
      const d = p.locator('.base-dialog, [role=dialog]').last();
      const inputs = d.locator('input');
      await inputs.nth(0).fill('Scan after downloads');
      await inputs.nth(1).fill('Scans every library when the download manager calls the webhook.');
      await d.locator('select').selectOption('webhook-scan');
      await blur(p);
    } },
  Triggers: { page: 'scripts', act: async (p) => { await pick(p, 'Limit transcodes'); await sectionTop(p, 'Triggers'); } },
  Schedule: { page: 'scripts', act: async (p) => { await pick(p, 'Nightly scan'); await scrollTo(p, '#scheduleOptions', 190); } },
  Webhook: { page: 'scripts', act: async (p) => { await pick(p, 'Scan after downloads'); await scrollTo(p, '#webhookOptions', 230); } },
  Conditions: { page: 'scripts', act: async (p) => { await pick(p, 'Limit transcodes'); await sectionTop(p, 'Conditions'); } },
  Steps: { page: 'scripts', act: async (p) => { await pick(p, 'Health check'); await openStep(p, 1); await sectionTop(p, 'Steps'); } },
  SavedOutput: { page: 'scripts', act: async (p) => { await pick(p, 'Health check'); await openStep(p, 0); await scrollTo(p, '#opFieldOutputName', 470); } },
  Secrets: { page: 'scripts', act: async (p) => { await pick(p, 'Post new movies'); await sectionTop(p, 'Secrets'); } },
  Behavior: { page: 'scripts', act: async (p) => { await pick(p, 'Tag unwatched movies'); await sectionTop(p, 'Behavior'); } },
  TestRun: { page: 'scripts', act: async (p) => {
      await pick(p, 'Limit transcodes');
      await scrollTo(p, '#saveBarMount', 560);
      await p.click('#btnTestScript'); await p.waitForTimeout(900); await blur(p);
    } },
  History: { page: 'history', act: async (p) => {} },
  RunDetails: { page: 'history', act: async (p) => {
      await p.evaluate(() => { const c = Array.from(document.querySelectorAll('#historyTable *')).find(e => e.children.length === 0 && e.tagName !== 'OPTION' && /^Post new movies/.test(e.textContent)); c.click(); }); await p.waitForTimeout(700); await blur(p);
    } },
  Settings: { page: 'settings', act: async (p) => { await scrollTo(p, '#saveBarMount', 590); } },
  ScheduledTasks: { page: 'tasks', vp: { width: 800, height: 140 }, act: async (p) => {} }
};

for (const [file, s] of Object.entries(shots)) {
  if (only && !only.split(',').includes(file)) continue;
  const { b, p } = await open(s.page, s.vp);
  await s.act(p);
  await p.waitForTimeout(400);
  await p.screenshot({ path: `${OUT}/${file}.png` });
  const errs = await p.evaluate(() => window.__errs);
  console.log(file, errs.length ? JSON.stringify(errs) : 'ok');
  await b.close();
}
