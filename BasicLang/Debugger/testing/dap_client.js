#!/usr/bin/env node
/**
 * dap_client.js — Sequenced DAP (Debug Adapter Protocol) test client for
 * `BasicLang.exe --debug-adapter`.
 *
 * IMPORTANT LESSON (from LSP testing in this repo): do NOT pipe all requests
 * up-front. The client below is fully SEQUENCED — every request waits for its
 * response (and any expected events) before the next request is sent.
 * DAP uses the same Content-Length framing as LSP.
 *
 * Usage:
 *   node dap_client.js <adapterExe> <programExe> <sourceFile.bas> [scenario]
 *
 * Scenarios (default = all, run in separate debug sessions):
 *   launch        initialize/launch/configurationDone → run to exit
 *   bpBefore      breakpoint on the line BEFORE an Await → hit?
 *   stepOver      breakpoint on the Await line, step-over → exactly one
 *                 stopped(step) on the line after the Await?
 *   stepIn        breakpoint on the async call in Main, step-into → lands
 *                 in the Async Function?
 *   bpAfter       breakpoint on a line AFTER the Await (continuation runs
 *                 on a threadpool thread) → hit?
 *
 * Line numbers below match dbgtest/AsyncDemo.bas:
 *   5  Console.WriteLine("before await")    (inside Async Function)
 *   6  Await Task.Delay(500)
 *   7  Console.WriteLine("after await")     (after the await → continuation)
 *   8  Dim answer As Integer = 42
 *   13 Console.WriteLine("start")           (in Main)
 *   14 Dim t As Task(Of Integer) = DoWorkAsync()
 * Override with env vars LINE_BEFORE_AWAIT, LINE_AWAIT, LINE_AFTER_AWAIT,
 * LINE_MAIN_CALL, LINE_ASYNC_FIRST if using a different program.
 */

'use strict';
const { spawn } = require('child_process');
const path = require('path');

// ---------------------------------------------------------------------------
// DapClient — Content-Length framed, sequenced request/response + event queue
// ---------------------------------------------------------------------------
class DapClient {
  constructor(adapterExe, opts = {}) {
    this.verbose = !!opts.verbose;
    this.seq = 1;
    this.pending = new Map();   // request_seq -> {resolve, reject, command}
    this.events = [];           // buffered events not yet consumed
    this.eventWaiters = [];     // {name, predicate, resolve, timer}
    this.buffer = Buffer.alloc(0);
    this.closed = false;
    this.stderr = '';

    this.proc = spawn(adapterExe, ['--debug-adapter'], {
      stdio: ['pipe', 'pipe', 'pipe'],
    });
    this.proc.stdout.on('data', (d) => this._onData(d));
    this.proc.stderr.on('data', (d) => {
      this.stderr += d.toString();
      if (this.verbose) process.stderr.write('[adapter-stderr] ' + d.toString());
    });
    this.proc.on('exit', (code) => {
      this.closed = true;
      if (this.verbose) console.log(`[client] adapter exited code=${code}`);
      for (const [, p] of this.pending) p.reject(new Error('adapter exited'));
      this.pending.clear();
    });
  }

  _onData(chunk) {
    this.buffer = Buffer.concat([this.buffer, chunk]);
    for (;;) {
      const headerEnd = this.buffer.indexOf('\r\n\r\n');
      if (headerEnd < 0) return;
      const header = this.buffer.slice(0, headerEnd).toString('utf8');
      const m = header.match(/Content-Length:\s*(\d+)/i);
      if (!m) { this.buffer = this.buffer.slice(headerEnd + 4); continue; }
      const len = parseInt(m[1], 10);
      if (this.buffer.length < headerEnd + 4 + len) return;
      const body = this.buffer.slice(headerEnd + 4, headerEnd + 4 + len).toString('utf8');
      this.buffer = this.buffer.slice(headerEnd + 4 + len);
      let msg;
      try { msg = JSON.parse(body); } catch (e) { continue; }
      this._dispatch(msg);
    }
  }

  _dispatch(msg) {
    if (this.verbose) console.log('[recv]', JSON.stringify(msg));
    if (msg.type === 'response') {
      const p = this.pending.get(msg.request_seq);
      if (p) { this.pending.delete(msg.request_seq); p.resolve(msg); }
    } else if (msg.type === 'event') {
      // Try waiters first (in registration order)
      for (let i = 0; i < this.eventWaiters.length; i++) {
        const w = this.eventWaiters[i];
        if (msg.event === w.name && (!w.predicate || w.predicate(msg))) {
          this.eventWaiters.splice(i, 1);
          clearTimeout(w.timer);
          w.resolve(msg);
          return;
        }
      }
      this.events.push(msg);
    }
  }

  /** Send a request and await its response. */
  request(command, args, timeoutMs = 15000) {
    const seq = this.seq++;
    const msg = { seq, type: 'request', command };
    if (args !== undefined) msg.arguments = args;
    const json = JSON.stringify(msg);
    const payload = `Content-Length: ${Buffer.byteLength(json, 'utf8')}\r\n\r\n${json}`;
    if (this.verbose) console.log('[send]', json);
    return new Promise((resolve, reject) => {
      if (this.closed) return reject(new Error('adapter closed'));
      const timer = setTimeout(() => {
        this.pending.delete(seq);
        reject(new Error(`timeout waiting for response to '${command}'`));
      }, timeoutMs);
      this.pending.set(seq, {
        resolve: (r) => { clearTimeout(timer); resolve(r); },
        reject: (e) => { clearTimeout(timer); reject(e); },
        command,
      });
      this.proc.stdin.write(payload);
    });
  }

  /**
   * Wait for an event by name (optionally matching predicate).
   * Consumes a buffered event if one already arrived.
   */
  waitForEvent(name, timeoutMs = 15000, predicate = null) {
    // Check buffer first
    for (let i = 0; i < this.events.length; i++) {
      const e = this.events[i];
      if (e.event === name && (!predicate || predicate(e))) {
        this.events.splice(i, 1);
        return Promise.resolve(e);
      }
    }
    return new Promise((resolve, reject) => {
      const w = { name, predicate, resolve };
      w.timer = setTimeout(() => {
        const idx = this.eventWaiters.indexOf(w);
        if (idx >= 0) this.eventWaiters.splice(idx, 1);
        reject(new Error(`timeout waiting for event '${name}'`));
      }, timeoutMs);
      this.eventWaiters.push(w);
    });
  }

  /** Drain any buffered events matching name (non-blocking). */
  drainEvents(name) {
    const out = this.events.filter((e) => e.event === name);
    this.events = this.events.filter((e) => e.event !== name);
    return out;
  }

  /** Sleep helper to detect duplicate/spurious events. */
  sleep(ms) { return new Promise((r) => setTimeout(r, ms)); }

  async kill() {
    try { await this.request('disconnect', { terminateDebuggee: true }, 3000); } catch (e) { /* ignore */ }
    try { this.proc.stdin.end(); } catch (e) { /* ignore */ }
    await this.sleep(300);
    try { this.proc.kill(); } catch (e) { /* ignore */ }
  }
}

// ---------------------------------------------------------------------------
// Test scenarios
// ---------------------------------------------------------------------------
const L = {
  beforeAwait: parseInt(process.env.LINE_BEFORE_AWAIT || '5', 10),
  awaitLine:   parseInt(process.env.LINE_AWAIT || '6', 10),
  afterAwait:  parseInt(process.env.LINE_AFTER_AWAIT || '7', 10),
  mainCall:    parseInt(process.env.LINE_MAIN_CALL || '14', 10),
  asyncFirst:  parseInt(process.env.LINE_ASYNC_FIRST || '5', 10),
};

async function startSession(adapterExe, programExe, sourceFile, bpLines, verbose) {
  const c = new DapClient(adapterExe, { verbose });
  const init = await c.request('initialize', { adapterID: 'basiclang', clientID: 'dap_client.js' });
  if (!init.success) throw new Error('initialize failed');
  const launch = await c.request('launch', { program: programExe, cwd: path.dirname(programExe) }, 30000);
  if (!launch.success) throw new Error('launch failed: ' + JSON.stringify(launch.body || {}));
  // initialized event should have arrived (buffered) by now
  await c.waitForEvent('initialized', 5000);
  let bpResp = null;
  if (bpLines && bpLines.length) {
    bpResp = await c.request('setBreakpoints', {
      source: { path: sourceFile },
      breakpoints: bpLines.map((l) => ({ line: l })),
    });
  }
  await c.request('configurationDone', {});
  return { c, bpResp };
}

/** Get top user frame (line/name) for a stopped event. */
async function topFrame(c, threadId) {
  const st = await c.request('stackTrace', { threadId });
  const frames = (st.body && st.body.stackFrames) || [];
  const userFrame = frames.find((f) => f.source && /\.bas$/i.test(f.source.path || ''));
  return { frames, userFrame };
}

const results = [];
function record(name, pass, detail) {
  results.push({ name, pass, detail });
  console.log(`  ${pass ? 'PASS' : 'FAIL'}  ${name}${detail ? ' — ' + detail : ''}`);
}

async function scenarioLaunch(adapterExe, programExe, sourceFile, verbose) {
  console.log('\n=== scenario: launch (init/launch/configurationDone → run to exit) ===');
  const { c } = await startSession(adapterExe, programExe, sourceFile, [], verbose);
  try {
    const term = await c.waitForEvent('terminated', 20000);
    record('launch: terminated event on exit', !!term);
  } catch (e) {
    record('launch: terminated event on exit', false, e.message);
  }
  await c.kill();
}

async function scenarioBpBefore(adapterExe, programExe, sourceFile, verbose) {
  console.log(`\n=== scenario: bpBefore (breakpoint line ${L.beforeAwait}, before Await) ===`);
  const { c } = await startSession(adapterExe, programExe, sourceFile, [L.beforeAwait], verbose);
  try {
    const stopped = await c.waitForEvent('stopped', 20000);
    const tid = stopped.body.threadId;
    const { userFrame } = await topFrame(c, tid);
    const line = userFrame ? userFrame.line : -1;
    record(`bpBefore: hit breakpoint (reason=${stopped.body.reason})`,
      stopped.body.reason === 'breakpoint' && line === L.beforeAwait,
      `stopped at line ${line}, thread ${tid}`);
    await c.request('continue', { threadId: tid });
    const term = await c.waitForEvent('terminated', 20000);
    record('bpBefore: continue → terminated', !!term);
  } catch (e) {
    record('bpBefore', false, e.message);
  }
  await c.kill();
}

async function scenarioStepOver(adapterExe, programExe, sourceFile, verbose) {
  console.log(`\n=== scenario: stepOver (bp on Await line ${L.awaitLine}, next → line ${L.afterAwait}) ===`);
  const { c } = await startSession(adapterExe, programExe, sourceFile, [L.awaitLine], verbose);
  try {
    const stopped = await c.waitForEvent('stopped', 20000);
    const tid = stopped.body.threadId;
    const { userFrame } = await topFrame(c, tid);
    record('stepOver: initial bp on Await line hit',
      stopped.body.reason === 'breakpoint' && userFrame && userFrame.line === L.awaitLine,
      `line ${userFrame && userFrame.line}`);

    await c.request('next', { threadId: tid });
    let stepStopped = null;
    try {
      stepStopped = await c.waitForEvent('stopped', 20000);
    } catch (e) {
      record('stepOver: stopped event after step-over of Await', false, 'no stopped event (hang/lost step): ' + e.message);
      await c.kill();
      return;
    }
    const tid2 = stepStopped.body.threadId;
    const { userFrame: f2 } = await topFrame(c, tid2);
    const line2 = f2 ? f2.line : -1;
    record('stepOver: stopped on the line after Await',
      line2 === L.afterAwait,
      `reason=${stepStopped.body.reason} line=${line2} thread=${tid2}`);

    // check for duplicate/spurious stopped events
    await c.sleep(1500);
    const dups = c.drainEvents('stopped');
    record('stepOver: exactly one stopped event (no duplicates)',
      dups.length === 0, dups.length ? `${dups.length} extra stopped event(s): ${dups.map(d=>d.body.reason).join(',')}` : '');

    await c.request('continue', { threadId: tid2 });
    const term = await c.waitForEvent('terminated', 20000);
    record('stepOver: continue → terminated', !!term);
  } catch (e) {
    record('stepOver', false, e.message);
  }
  await c.kill();
}

async function scenarioStepIn(adapterExe, programExe, sourceFile, verbose) {
  console.log(`\n=== scenario: stepIn (bp line ${L.mainCall} in Main, stepIn → async fn line ${L.asyncFirst}) ===`);
  const { c } = await startSession(adapterExe, programExe, sourceFile, [L.mainCall], verbose);
  try {
    const stopped = await c.waitForEvent('stopped', 20000);
    const tid = stopped.body.threadId;
    await c.request('stepIn', { threadId: tid });
    let stepStopped;
    try {
      stepStopped = await c.waitForEvent('stopped', 20000);
    } catch (e) {
      record('stepIn: stopped event after step-into async call', false, 'no stopped event: ' + e.message);
      await c.kill();
      return;
    }
    const tid2 = stepStopped.body.threadId;
    const { userFrame: f2 } = await topFrame(c, tid2);
    const line2 = f2 ? f2.line : -1;
    record('stepIn: landed inside the Async Function',
      line2 === L.asyncFirst,
      `reason=${stepStopped.body.reason} line=${line2} thread=${tid2}`);
    await c.request('continue', { threadId: tid2 });
    const term = await c.waitForEvent('terminated', 20000);
    record('stepIn: continue → terminated', !!term);
  } catch (e) {
    record('stepIn', false, e.message);
  }
  await c.kill();
}

async function scenarioBpAfter(adapterExe, programExe, sourceFile, verbose) {
  console.log(`\n=== scenario: bpAfter (breakpoint line ${L.afterAwait}, AFTER the Await — continuation) ===`);
  const { c } = await startSession(adapterExe, programExe, sourceFile, [L.afterAwait], verbose);
  try {
    const stopped = await c.waitForEvent('stopped', 20000);
    const tid = stopped.body.threadId;
    const { userFrame } = await topFrame(c, tid);
    const line = userFrame ? userFrame.line : -1;
    record('bpAfter: hit breakpoint in continuation',
      stopped.body.reason === 'breakpoint' && line === L.afterAwait,
      `stopped at line ${line}, thread ${tid}`);
    await c.request('continue', { threadId: tid });
    const term = await c.waitForEvent('terminated', 20000);
    record('bpAfter: continue → terminated', !!term);
  } catch (e) {
    record('bpAfter', false, e.message);
  }
  await c.kill();
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------
async function main() {
  const [adapterExe, programExe, sourceFile, scenario] = process.argv.slice(2);
  const verbose = process.env.DAP_VERBOSE === '1';
  if (!adapterExe || !programExe || !sourceFile) {
    console.error('usage: node dap_client.js <adapterExe> <programExe> <sourceFile.bas> [scenario]');
    process.exit(2);
  }
  const scenarios = {
    launch: scenarioLaunch,
    bpBefore: scenarioBpBefore,
    stepOver: scenarioStepOver,
    stepIn: scenarioStepIn,
    bpAfter: scenarioBpAfter,
  };
  const toRun = scenario ? { [scenario]: scenarios[scenario] } : scenarios;
  for (const [name, fn] of Object.entries(toRun)) {
    if (!fn) { console.error(`unknown scenario '${name}'`); process.exit(2); }
    try {
      await fn(adapterExe, programExe, sourceFile, verbose);
    } catch (e) {
      record(name + ': scenario crashed', false, e.message);
    }
  }
  console.log('\n=== SUMMARY ===');
  let failed = 0;
  for (const r of results) {
    console.log(`${r.pass ? 'PASS' : 'FAIL'}  ${r.name}${r.detail ? ' — ' + r.detail : ''}`);
    if (!r.pass) failed++;
  }
  console.log(`${results.length - failed}/${results.length} checks passed`);
  process.exit(failed ? 1 : 0);
}

if (require.main === module) {
  main().catch((e) => { console.error(e); process.exit(1); });
}

module.exports = { DapClient };
