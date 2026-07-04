#!/usr/bin/env node
/**
 * dap_client_vars.js — Sequenced DAP client harness for validating breakpoint
 * mapping and variable inspection (lambdas, async state machines).
 *
 * Speaks DAP over stdio (Content-Length framing) to `BasicLang.exe --debug-adapter`.
 * Strictly sequenced: every request waits for its response (and for expected
 * events) before the next request is sent. Do NOT pipe everything up front —
 * that false-negatives with this adapter.
 *
 * Usage:
 *   node dap_client_vars.js <adapterExe> <programExe> <sourceFile> <line1,line2,...> [maxStops]
 *
 * At every stop it captures: stop reason, full stack trace, and for the top
 * frame: scopes + variables for each scope (one level of expansion for
 * expandable variables). Emits a JSON report on stdout between
 * ===DAP-REPORT-BEGIN=== / ===DAP-REPORT-END=== markers.
 *
 * Security note: uses child_process.spawn with a fixed argument array and no
 * shell — arguments are never interpreted by a shell.
 */

const { spawn } = require('child_process');

const [, , adapterExe, programExe, sourceFile, linesArg, maxStopsArg] = process.argv;
if (!adapterExe || !programExe || !sourceFile || !linesArg) {
  console.error('usage: node dap_client_vars.js <adapterExe> <programExe> <sourceFile> <lines> [maxStops]');
  process.exit(2);
}
const bpLines = linesArg.split(',').map(s => parseInt(s, 10));
const maxStops = parseInt(maxStopsArg || '8', 10);

const adapter = spawn(adapterExe, ['--debug-adapter'], { stdio: ['pipe', 'pipe', 'pipe'], shell: false });
adapter.stderr.on('data', d => process.stderr.write('[adapter-stderr] ' + d));

let seq = 1;
let buffer = Buffer.alloc(0);
const pendingResponses = new Map(); // request_seq -> resolve
const eventWaiters = [];            // { match(evt), resolve }
const report = { breakpoints: [], stops: [], events: [], outputs: [], error: null };

adapter.stdout.on('data', chunk => {
  buffer = Buffer.concat([buffer, chunk]);
  while (true) {
    const headerEnd = buffer.indexOf('\r\n\r\n');
    if (headerEnd < 0) break;
    const header = buffer.slice(0, headerEnd).toString('utf8');
    const m = /Content-Length:\s*(\d+)/i.exec(header);
    if (!m) { buffer = buffer.slice(headerEnd + 4); continue; }
    const len = parseInt(m[1], 10);
    if (buffer.length < headerEnd + 4 + len) break;
    const body = buffer.slice(headerEnd + 4, headerEnd + 4 + len).toString('utf8');
    buffer = buffer.slice(headerEnd + 4 + len);
    let msg;
    try { msg = JSON.parse(body); } catch { continue; }
    handleMessage(msg);
  }
});

function handleMessage(msg) {
  if (msg.type === 'response') {
    const resolve = pendingResponses.get(msg.request_seq);
    if (resolve) { pendingResponses.delete(msg.request_seq); resolve(msg); }
  } else if (msg.type === 'event') {
    if (msg.event === 'output' && msg.body) {
      report.outputs.push(msg.body.output);
    } else {
      report.events.push({ event: msg.event, body: msg.body });
    }
    for (let i = 0; i < eventWaiters.length; i++) {
      if (eventWaiters[i].match(msg)) {
        const w = eventWaiters.splice(i, 1)[0];
        w.resolve(msg);
        return;
      }
    }
  }
}

function sendRequest(command, args) {
  const request_seq = seq++;
  const msg = { seq: request_seq, type: 'request', command, arguments: args || {} };
  const json = JSON.stringify(msg);
  adapter.stdin.write(`Content-Length: ${Buffer.byteLength(json, 'utf8')}\r\n\r\n${json}`);
  return new Promise((resolve, reject) => {
    pendingResponses.set(request_seq, resolve);
    setTimeout(() => {
      if (pendingResponses.has(request_seq)) {
        pendingResponses.delete(request_seq);
        reject(new Error(`timeout waiting for response to '${command}'`));
      }
    }, 20000);
  });
}

function waitForEvent(name, timeoutMs, extraMatch) {
  return new Promise((resolve, reject) => {
    const waiter = {
      match: m => m.event === name && (!extraMatch || extraMatch(m)),
      resolve
    };
    eventWaiters.push(waiter);
    setTimeout(() => {
      const idx = eventWaiters.indexOf(waiter);
      if (idx >= 0) { eventWaiters.splice(idx, 1); reject(new Error(`timeout waiting for event '${name}'`)); }
    }, timeoutMs);
  });
}

function waitForStopOrExit(timeoutMs) {
  return new Promise((resolve, reject) => {
    const waiter = {
      match: m => m.event === 'stopped' || m.event === 'terminated' || m.event === 'exited',
      resolve
    };
    eventWaiters.push(waiter);
    setTimeout(() => {
      const idx = eventWaiters.indexOf(waiter);
      if (idx >= 0) { eventWaiters.splice(idx, 1); reject(new Error('timeout waiting for stopped/terminated')); }
    }, timeoutMs);
  });
}

async function captureStop(stoppedEvent) {
  const stop = {
    reason: stoppedEvent.body && stoppedEvent.body.reason,
    threadId: (stoppedEvent.body && stoppedEvent.body.threadId) || 1,
    frames: [],
    topFrame: null,
    scopes: {}
  };
  try {
    const st = await sendRequest('stackTrace', { threadId: stop.threadId, startFrame: 0, levels: 20 });
    const frames = (st.body && st.body.stackFrames) || [];
    stop.frames = frames.map(f => ({
      name: f.name,
      line: f.line,
      source: f.source && f.source.name
    }));
    if (frames.length > 0) {
      stop.topFrame = frames[0];
      const sc = await sendRequest('scopes', { frameId: frames[0].id });
      for (const scope of (sc.body && sc.body.scopes) || []) {
        const vr = await sendRequest('variables', { variablesReference: scope.variablesReference });
        const vars = [];
        for (const v of (vr.body && vr.body.variables) || []) {
          const entry = { name: v.name, value: v.value, type: v.type };
          if (v.variablesReference > 0) {
            try {
              const child = await sendRequest('variables', { variablesReference: v.variablesReference });
              entry.children = ((child.body && child.body.variables) || []).map(c => ({
                name: c.name, value: c.value, type: c.type
              }));
            } catch (e) { entry.children = ['<child fetch failed: ' + e.message + '>']; }
          }
          vars.push(entry);
        }
        stop.scopes[scope.name] = vars;
      }
    }
  } catch (e) {
    stop.error = e.message;
  }
  return stop;
}

(async () => {
  try {
    await sendRequest('initialize', {
      clientID: 'dap_client_vars', adapterID: 'basiclang',
      pathFormat: 'path', linesStartAt1: true, columnsStartAt1: true
    });

    const launchPromise = sendRequest('launch', { program: programExe, stopOnEntry: false });
    await waitForEvent('initialized', 20000);

    const bpResp = await sendRequest('setBreakpoints', {
      source: { path: sourceFile, name: require('path').basename(sourceFile) },
      breakpoints: bpLines.map(l => ({ line: l })),
      lines: bpLines
    });
    report.breakpoints = ((bpResp.body && bpResp.body.breakpoints) || []).map((b, i) => ({
      requestedLine: bpLines[i], verified: b.verified, line: b.line, id: b.id
    }));

    await sendRequest('configurationDone', {});
    await launchPromise.catch(() => {});

    for (let i = 0; i < maxStops; i++) {
      let evt;
      try { evt = await waitForStopOrExit(15000); }
      catch (e) { report.error = e.message; break; }

      if (evt.event === 'terminated' || evt.event === 'exited') {
        report.processEnd = evt.event;
        break;
      }
      const stop = await captureStop(evt);
      report.stops.push(stop);
      await sendRequest('continue', { threadId: stop.threadId }).catch(e => {
        report.error = 'continue failed: ' + e.message;
      });
    }

    try { await sendRequest('disconnect', { terminateDebuggee: true }); } catch { }
  } catch (e) {
    report.error = e.message;
  }

  console.log('===DAP-REPORT-BEGIN===');
  console.log(JSON.stringify(report, null, 2));
  console.log('===DAP-REPORT-END===');
  setTimeout(() => { try { adapter.kill(); } catch { } process.exit(0); }, 500);
})();
