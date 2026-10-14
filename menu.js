#!/usr/bin/env node
'use strict';

// Luraph Deobfuscator — локальное меню (веб-интерфейс).
// Запуск: node menu.js [--port 8000]
// Открывает http://localhost:8000 — выбор файлов, все настройки движка
// и запуск deob.js прямо из браузера.

const http = require('http');
const fs = require('fs');
const path = require('path');
const os = require('os');
const { spawn, execFile } = require('child_process');

const ROOT = __dirname;
const DEOB = path.join(ROOT, 'deob.js');
const HTML = path.join(ROOT, 'menu.html');
const PORT = parseInt(process.argv[process.argv.indexOf('--port') + 1], 10) || 8000;

const SUPPORTED = ['.lua', '.luau', '.txt'];

// -------------------------------------------------- нативные диалоги Windows

let dialogBusy = false;

const PICK_FILES_PS = [
  "Add-Type -AssemblyName System.Windows.Forms",
  "$out = %OUT%",
  "$d = New-Object System.Windows.Forms.OpenFileDialog",
  "$d.Title = 'Выберите файлы для деобфускации'",
  "$d.Multiselect = $true",
  "$d.Filter = 'Lua / Luau / Text (*.lua;*.luau;*.txt)|*.lua;*.luau;*.txt|All files (*.*)|*.*'",
  "$d.CheckFileExists = $true",
  "if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {",
  "  $d.FileNames | Out-File -FilePath $out -Encoding UTF8",
  "}",
].join('\n');

const PICK_DIR_PS = [
  "Add-Type -AssemblyName System.Windows.Forms",
  "$out = %OUT%",
  "$def = %DEF%",
  "$f = New-Object System.Windows.Forms.FolderBrowserDialog",
  "$f.Description = 'Выберите папку с .lua / .luau / .txt'",
  "if ($def) { $f.SelectedPath = $def }",
  "if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {",
  "  $f.SelectedPath | Out-File -FilePath $out -Encoding UTF8",
  "}",
].join('\n');

// Вызывает системный диалог через powershell и возвращает выбранные пути
// (записываются во временный файл, чтобы не потерять кодировку).
// Скрипт кладём в .ps1 и запускаем через -File: так не ломаются ни
// кодировки, ни многострочность (в отличие от -Command).
function nativeDialog(psLines, defDir, cb) {
  if (dialogBusy) return cb('Диалог уже открыт', []);
  dialogBusy = true;
  const outFile = path.join(os.tmpdir(), 'dex_pick_' + Date.now() + '_' + Math.floor(Math.random() * 1e6) + '.txt');
  const psFile = outFile + '.ps1';
  let script = psLines
    .replace(/%OUT%/g, "'" + outFile.replace(/'/g, "''") + "'")
    .replace(/%DEF%/g, defDir ? "'" + String(defDir).replace(/'/g, "''") + "'" : "''");
  try {
    fs.writeFileSync(psFile, '\uFEFF' + script, 'utf8'); // BOM: PS 5.1 читает UTF-8 корректно
  } catch (e) {
    dialogBusy = false;
    return cb('Не удалось подготовить диалог: ' + (e.message || e), []);
  }
  execFile('powershell.exe', ['-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', psFile], {
    windowsHide: true,
    timeout: 180000,
  }, (err) => {
    dialogBusy = false;
    let paths = [];
    if (!err) {
      try {
        if (fs.existsSync(outFile)) {
          paths = fs.readFileSync(outFile, 'utf8')
            .split(/\r?\n/).map((s) => s.replace(/^\uFEFF/, '').trim()).filter(Boolean);
        }
      } catch {}
    }
    try { fs.unlinkSync(outFile); } catch {}
    try { fs.unlinkSync(psFile); } catch {}
    cb(err ? (err.message || 'Диалог не удалось открыть') : null, paths);
  });
}

// ---------------------------------------------------------------- helpers

function readJSON(req) {
  return new Promise((resolve) => {
    let data = '';
    req.on('data', (c) => { data += c; if (data.length > 8e6) req.destroy(); });
    req.on('end', () => {
      try { resolve(JSON.parse(data || '{}')); } catch { resolve({}); }
    });
    req.on('error', () => resolve({}));
  });
}

function json(res, o, code = 200) {
  const s = JSON.stringify(o);
  res.writeHead(code, {
    'content-type': 'application/json; charset=utf-8',
    'content-length': Buffer.byteLength(s),
    'cache-control': 'no-store',
  });
  res.end(s);
}

function norm(p) {
  if (!p || typeof p !== 'string') return '';
  try { return path.resolve(p); } catch { return ''; }
}

function listDir(p) {
  const out = { root: p, entries: [] };
  for (const e of fs.readdirSync(p, { withFileTypes: true })) {
    if (e.name.startsWith('.')) continue;
    const fp = path.join(p, e.name);
    const isDir = e.isDirectory();
    let size = 0, mtime = 0;
    if (!isDir) {
      try { const st = fs.statSync(fp); size = st.size; mtime = st.mtimeMs; } catch {}
    }
    if (isDir) {
      try { mtime = fs.statSync(fp).mtimeMs; } catch {}
    }
    out.entries.push({
      name: e.name, path: fp, dir: isDir,
      ext: isDir ? null : path.extname(e.name).toLowerCase(),
      lua: !isDir && SUPPORTED.includes(path.extname(e.name).toLowerCase()),
      size, mtime,
    });
  }
  out.entries.sort((a, b) =>
    ((b.dir ? 1 : 0) - (a.dir ? 1 : 0)) ||
    a.name.localeCompare(b.name, 'ru'));
  return out;
}

function roots() {
  const out = [];
  if (process.platform === 'win32') {
    for (const d of 'CDEFGHIJ'.split('')) {
      try { if (fs.existsSync(d + ':\\')) out.push({ path: d + ':\\', name: d + ':\\' }); } catch {}
    }
  } else {
    out.push({ path: '/', name: '/' });
  }
  try {
    const home = os.homedir();
    if (fs.existsSync(home)) out.push({ path: home, name: 'Home (' + path.basename(home) + ')' });
  } catch {}
  const extras = [
    ['D:\\Downloads', 'Downloads (D)'],
    ['D:\\Temp\\opencode', 'Temp\\opencode'],
    ['C:\\Users\\' + (os.userInfo ? os.userInfo().username : '') + '\\Downloads', 'Downloads'],
    [path.join(ROOT, 'sample'), 'sample (тесты движка)'],
  ];
  for (const [p, name] of extras) {
    if (p && fs.existsSync(p)) out.push({ path: p, name });
  }
  return out;
}

// ---------------------------------------------------------------- jobs

const ENV_TOGGLES = [
  'DEOB_V14_DEEP', 'DEOB_V14_DECODE_BUDGET', 'DEOB_CHUNK_MIN', 'DEOB_SPIN',
  'DEOB_PIPELINE_TIMEOUT', 'DEOB_SPIN_LATE', 'DEOB_DUMP_TRACE', 'DEOB_DEBUG_PARTIAL',
];

let jobSeq = 1;
const jobs = new Map();
const history = [];

function newJob(state, cmdline, envNotes) {
  const id = String(jobSeq++);
  const j = {
    id, state, cmdline, envNotes, lines: [], error: null, result: null,
    startedAt: Date.now(), finishedAt: null, proc: null, clients: [],
  };
  jobs.set(id, j);
  history.unshift(id);
  if (history.length > 100) history.pop();
  return j;
}

function logLine(j, text) {
  const line = { i: j.lines.length, t: Date.now(), text };
  j.lines.push(line);
  if (j.lines.length > 20000) j.lines.splice(0, j.lines.length - 20000);
  for (const c of j.clients) {
    try { c.write('data: ' + JSON.stringify({ type: 'line', line }) + '\n\n'); } catch {}
  }
}

function setState(j, state, result) {
  j.state = state;
  if (result) j.result = result;
  if (state === 'done' || state === 'error' || state === 'cancelled') j.finishedAt = Date.now();
  for (const c of j.clients) {
    try { c.write('data: ' + JSON.stringify({ type: 'state', state, result: j.result }) + '\n\n'); } catch {}
  }
  if (j.proc && (state === 'done' || state === 'error' || state === 'cancelled')) {
    try { j.proc.removeAllListeners('exit'); } catch {}
  }
}

function buildArgs(cfg) {
  const args = [DEOB];
  for (const f of (cfg.inputs || []).map(norm).filter(Boolean)) args.push(f);
  const toggles = {
    detect: '--detect', noDevirt: '--no-devirt', noHooks: '--no-hooks',
    noFold: '--no-fold', debug: '--debug', strings: '--strings',
    keepHarness: '--keep-harness', keepPreamble: '--keep-preamble',
  };
  for (const [k, flag] of Object.entries(toggles)) if (cfg[k]) args.push(flag);
  if (cfg.output && cfg.output.trim()) args.push('-o', norm(cfg.output));
  const numOpts = {
    timeout: '--timeout', budget: '--budget', maxRuns: '--max-runs',
    jobs: '--jobs', devirtRounds: '--devirt-rounds',
  };
  for (const [k, flag] of Object.entries(numOpts)) {
    const v = Number(cfg[k]);
    if (Number.isFinite(v) && v > 0) args.push(flag, String(Math.floor(v)));
  }
  if (cfg.executor && cfg.executor !== 'Wave') args.push('--executor', String(cfg.executor));
  if (cfg.inputText && cfg.inputText.trim()) args.push('--input-text', cfg.inputText);
  return args;
}

function buildEnv(cfg) {
  const env = Object.assign({}, process.env);
  const notes = [];
  if (cfg.v14Deep) { env.DEOB_V14_DEEP = '1'; notes.push('DEOB_V14_DEEP=1'); }
  const enums = {
    v14Budget: 'DEOB_V14_DECODE_BUDGET', chunkMin: 'DEOB_CHUNK_MIN',
    deobSpin: 'DEOB_SPIN', pipelineTimeout: 'DEOB_PIPELINE_TIMEOUT',
  };
  for (const [k, name] of Object.entries(enums)) {
    const v = Number(cfg[k]);
    if (Number.isFinite(v) && v > 0) { env[name] = String(v); notes.push(name + '=' + v); }
  }
  if (cfg.spinLate) { env.DEOB_SPIN_LATE = '1'; notes.push('DEOB_SPIN_LATE=1'); }
  if (cfg.dumpTrace) { env.DEOB_DUMP_TRACE = '1'; notes.push('DEOB_DUMP_TRACE=1'); }
  if (cfg.debugPartial) { env.DEOB_DEBUG_PARTIAL = '1'; notes.push('DEOB_DEBUG_PARTIAL=1'); }
  for (const name of ENV_TOGGLES) delete env['MENU_' + name];
  return { env, notes };
}

function runJob(cfg) {
  const args = buildArgs(cfg);
  const { env, notes } = buildEnv(cfg);
  const j = newJob('running', 'node ' + args.map((a, i) => (i === 1 ? '"' + a + '"' : /^--/.test(a) || /^-[a-z]$/.test(a) ? a : '"' + a + '"')).join(' '), notes);

  logLine(j, '> node ' + args.map((a) => /^--/.test(a) ? a : '"' + a + '"').join(' '));
  if (notes.length) logLine(j, '> env: ' + notes.join(', '));
  logLine(j, 'Запуск deob.js …');

  let child;
  try {
    child = spawn(process.execPath, args, { cwd: ROOT, env, windowsHide: true });
  } catch (e) {
    logLine(j, '[!] не удалось запустить: ' + (e.message || e));
    setState(j, 'error');
    return j;
  }
  j.proc = child;

  const pump = (buf) => {
    const text = String(buf);
    for (const part of text.split('\n')) {
      if (!part) continue;
      logLine(j, part.replace(/\r$/, ''));
      if (/^\s*\[\+\] result: (.*)$/.test(part)) {
        j.result = (part.match(/^\s*\[\+\] result: (.*)$/) || [])[1].trim();
        setState(j, 'running', j.result);
      }
    }
  };
  child.stdout.on('data', pump);
  child.stderr.on('data', pump);
  child.on('error', (e) => {
    logLine(j, '[!] процесс: ' + (e.message || e));
    setState(j, 'error');
  });
  child.on('exit', (code, sig) => {
    if (sig === 'SIGKILL') {
      logLine(j, '[i] задание остановлено');
      setState(j, 'cancelled', j.result);
    } else if (code === 0) {
      setState(j, 'done', j.result);
    } else if (code === 2) {
      logLine(j, '[i] нет корректных исходных файлов');
      setState(j, 'no-file', j.result);
    } else {
      setState(j, 'error', j.result);
    }
  });
  return j;
}

// ---------------------------------------------------------------- server

function serveStatic(res, file, ctype) {
  fs.readFile(file, (err, data) => {
    if (err) return json(res, { error: 'не найден ' + file }, 404);
    res.writeHead(200, { 'content-type': ctype, 'cache-control': 'no-store' });
    res.end(data);
  });
}

const server = http.createServer(async (req, res) => {
  const u = new URL(req.url, 'http://localhost');
  const p = u.pathname;
  try {
    if (p === '/' || p === '/index.html') return serveStatic(res, HTML, 'text/html; charset=utf-8');

    if (p === '/api/roots') return json(res, { roots: roots() });

    if (p === '/api/browse') {
      const dir = norm(u.searchParams.get('p') || '');
      if (!dir || !fs.existsSync(dir)) return json(res, { error: 'Каталог не найден', path: u.searchParams.get('p') || '' }, 404);
      if (!fs.statSync(dir).isDirectory()) return json(res, { error: 'Это не каталог' }, 400);
      try {
        return json(res, { dir: listDir(dir) });
      } catch (e) {
        return json(res, { error: e.message || 'Не удалось прочитать каталог' }, 500);
      }
    }

    if (p === '/api/pick') {
      return nativeDialog(PICK_FILES_PS, '', (err, paths) =>
        json(res, err ? { error: err } : { paths }));
    }

    if (p === '/api/pick-dir') {
      return nativeDialog(PICK_DIR_PS, norm(u.searchParams.get('p') || ''), (err, paths) =>
        json(res, err ? { error: err } : { path: (paths[0] || '') }));
    }

    if (p === '/api/upload') {
      const body = await readJSON(req);
      const name = String(body.name || 'upload');
      let buf;
      if (typeof body.b64 === 'string' && body.b64) {
        buf = Buffer.from(body.b64, 'base64');
      } else {
        buf = Buffer.from(String(body.text || ''), 'utf8');
      }
      const sane = path.basename(name).replace(/[^\w.\- ]/g, '_').replace(/\s+/g, '_');
      const ext = path.extname(sane).toLowerCase();
      const safeName = SUPPORTED.includes(ext) ? sane : sane.replace(/[^.]*$/, '') + 'lua';
      const up = path.join(os.tmpdir(), 'dex_up_' + Date.now() + '_' + safeName);
      fs.writeFileSync(up, buf);
      return json(res, { path: up, size: buf.length });
    }

    if (p === '/api/run') {
      const cfg = await readJSON(req);
      const inputs = (cfg.inputs || []).filter((x) => norm(x));
      const hasInputText = cfg.inputText && cfg.inputText.trim();
      if (inputs.length === 0 && !hasInputText) return json(res, { error: 'Выберите хотя бы один файл' }, 400);
      const j = runJob(cfg);
      return json(res, { id: j.id, cmdline: j.cmdline });
    }

    if (p === '/api/cancel') {
      const j = jobs.get(String(u.searchParams.get('id')));
      if (!j || !j.proc) return json(res, { error: 'задание не найдено' }, 404);
      try { j.proc.kill('SIGKILL'); } catch {}
      return json(res, { ok: true });
    }

    if (p === '/api/events') {
      const j = jobs.get(String(u.searchParams.get('id')));
      if (!j) return json(res, { error: 'задание не найдено' }, 404);
      res.writeHead(200, {
        'content-type': 'text/event-stream; charset=utf-8',
        'cache-control': 'no-cache',
        connection: 'keep-alive',
      });
      res.write('retry: 1000\n\n');
      res.write('data: ' + JSON.stringify({ type: 'snapshot', lines: j.lines.slice(-3000), state: j.state, result: j.result }) + '\n\n');
      j.clients.push(res);
      res.on('close', () => { j.clients = j.clients.filter((c) => c !== res); });
      return;
    }

    if (p === '/api/jobs') {
      const out = [];
      for (const id of history) {
        const j = jobs.get(id);
        if (!j) continue;
        out.push({ id: j.id, state: j.state, cmdline: j.cmdline, result: j.result,
          startedAt: j.startedAt, finishedAt: j.finishedAt, lines: j.lines.length, last: j.lines[j.lines.length - 1] && j.lines[j.lines.length - 1].text });
      }
      return json(res, { jobs: out });
    }

    if (p === '/api/open') {
      const target = norm(u.searchParams.get('p') || '');
      let cmd;
      if (process.platform === 'win32') {
        cmd = fs.existsSync(target) ? 'explorer' + (fs.statSync(target).isDirectory() ? ' "' + target + '"' : ' /select,"' + target + '"')
          : 'explorer "' + path.dirname(target) + '"';
        spawn('cmd', ['/c', cmd], { windowsHide: true, detached: true }).unref();
      } else if (fs.existsSync(target)) {
        spawn('xdg-open', [target]).unref();
      }
      return json(res, { ok: true });
    }

    if (p === '/api/copy-command') {
      const cfg = await readJSON(req);
      const args = buildArgs(cfg).slice(1).map((x) => (/^--/.test(x) ? x : '"' + x + '"')).join(' ');
      const env = buildEnv(cfg);
      const line = 'node deob.js ' + args + (env.notes.length ? '   # ' + env.notes.join(', ') : '');
      return json(res, { line });
    }

    if (p === '/favicon.ico') return res.writeHead(204).end();
    return json(res, { error: 'нет такого пути: ' + p }, 404);
  } catch (e) {
    try { return json(res, { error: (e && e.stack) || String(e) }, 500); } catch {}
  }
});

server.on('clientError', () => {});
server.listen(PORT, () => {
  process.stderr.write(`[i] Меню запущено: http://localhost:${PORT}\n`);
  process.stderr.write('[i] Остановить: Ctrl+C\n');
});