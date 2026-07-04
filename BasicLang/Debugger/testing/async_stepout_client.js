#!/usr/bin/env node
/**
 * async_stepout_client.js — DAP scenarios for stepping OUT of / over the final
 * Return of an async method (caller-resumption stepping).
 *
 * Programs (build with programs/build_programs.ps1):
 *   programs/AsyncChain.bas — Main → Outer() [async, Await Inner()] → Inner() [async]
 *     5 inner before | 6 Await | 7 inner after | 8 Return 42
 *     12 outer before | 13 Await Inner() | 14 outer after | 15 Return v+1
 *     19 start | 20 t = Outer() | 21 t.Wait() | 22 done
 *   programs/SyncMain.bas — Main [sync, blocks on t.Wait()] → Inner() [async]
 *     5 inner before | 6 Await | 7 inner after | 8 Return 42
 *     12 y = x + 1 | 13 Return y                  (sync AddOne)
 *     17 start | 18 t = Inner() | 19 t.Wait() | 20 z = AddOne(1) | 21 done
 *
 * Usage:
 *   node async_stepout_client.js <adapterExe> [scenario]
 *
 * Scenarios (default = all):
 *   finalRetNextAsync   bp Inner:8 (final Return), next  → caller = Outer (13/14)
 *   finalRetNextSync    bp Inner:8 (final Return), next  → caller = Main (20)
 *   stepOutMidAsync     bp Inner:7, stepOut              → caller = Outer (13/14)
 *   stepOutMidSync      bp Inner:7, stepOut              → caller = Main (20)
 *   stepOutSyncFn       bp AddOne:12, stepOut            → Main (20/21)  [regression]
 *
 * Expected landing lines can be overridden via env:
 *   ASYNC_CALLER_LINES (default "13,14"), SYNC_CALLER_LINE (default 20),
 *   SYNCFN_CALLER_LINES (default "20,21")
 */

'use strict';
const path = require('path');
const { DapClient } = require('./dap_client.js');

const PROG_DIR = path.join(__dirname, 'programs');
const ASYNC_EXE = path.join(PROG_DIR, 'bin', 'AsyncChain', 'AsyncChain.exe');
const ASYNC_SRC = path.join(PROG_DIR, 'AsyncChain.bas');
const SYNC_EXE = path.join(PROG_DIR, 'bin', 'SyncMain', 'SyncMain.exe');
const SYNC_SRC = path.join(PROG_DIR, 'SyncMain.bas');

const ASYNC_CALLER_LINES = (process.env.ASYNC_CALLER_LINES || '13,14').split(',').map(Number);
const SYNC_CALLER_LINE = parseInt(process.env.SYNC_CALLER_LINE || '20', 10);
const SYNCFN_CALLER_LINES = (process.env.SYNCFN_CALLER_LINES || '20,21').split(',').map(Number);

const results = [];
function record(name, pass, detail) {
  results.push({ name, pass, detail });
  console.log(`  ${pass ? 'PASS' : 'FAIL'}  ${name}${detail ? ' — ' + detail : ''}`);
}

async function startSession(adapterExe, programExe, sourceFile, bpLines, verbose) {
  const c = new DapClient(adapterExe, { verbose });
  const init = await c.request('initialize', { adapterID: 'basiclang', clientID: 'async_stepout_client.js' });
  if (!init.success) throw new Error('initialize failed');
  const launch = await c.request('launch', { program: programExe, cwd: path.dirname(programExe) }, 30000);
  if (!launch.success) throw new Error('launch failed: ' + JSON.stringify(launch.body || {}));
  await c.waitForEvent('initialized', 5000);
  if (bpLines && bpLines.length) {
    await c.request('setBreakpoints', {
      source: { path: sourceFile },
      breakpoints: bpLines.map((l) => ({ line: l })),
    });
  }
  await c.request('configurationDone', {});
  return c;
}

async function topFrame(c, threadId) {
  const st = await c.request('stackTrace', { threadId });
  const frames = (st.body && st.body.stackFrames) || [];
  const userFrame = frames.find((f) => f.source && /\.bas$/i.test(f.source.path || ''));
  return { frames, userFrame };
}

/**
 * Common driver: run to a breakpoint, issue a step command, and verify the
 * next stop is a "step" stop whose top user frame is at one of the expected
 * lines. Then continue → terminated.
 */
async function stepScenario(name, adapterExe, opts, verbose) {
  const { exe, src, bpLine, stepCommand, expectLines, expectFrameRe } = opts;
  console.log(`\n=== scenario: ${name} (bp line ${bpLine}, ${stepCommand} → line(s) ${expectLines.join('/')}) ===`);
  const c = await startSession(adapterExe, exe, src, [bpLine], verbose);
  try {
    const stopped = await c.waitForEvent('stopped', 20000);
    const tid = stopped.body.threadId;
    const { userFrame } = await topFrame(c, tid);
    record(`${name}: initial bp hit`,
      stopped.body.reason === 'breakpoint' && userFrame && userFrame.line === bpLine,
      `line ${userFrame && userFrame.line}`);

    await c.request(stepCommand, { threadId: tid });
    let stepStopped = null;
    try {
      stepStopped = await c.waitForEvent('stopped', 25000);
    } catch (e) {
      record(`${name}: stopped event after ${stepCommand}`, false, 'no stopped event (lost step): ' + e.message);
      await c.kill();
      return;
    }
    const tid2 = stepStopped.body.threadId;
    const { frames, userFrame: f2 } = await topFrame(c, tid2);
    const line2 = f2 ? f2.line : -1;
    const fname = f2 ? f2.name : (frames[0] ? frames[0].name : '<no frames>');
    const topIsUser = frames.length > 0 && frames[0].source && /\.bas$/i.test(frames[0].source.path || '');
    record(`${name}: landed in caller at expected line`,
      stepStopped.body.reason === 'step' && topIsUser && expectLines.includes(line2) &&
        (!expectFrameRe || expectFrameRe.test(fname)),
      `reason=${stepStopped.body.reason} line=${line2} frame='${fname}' topIsUser=${topIsUser} thread=${tid2}`);

    // no duplicate/spurious stopped events
    await c.sleep(1500);
    const dups = c.drainEvents('stopped');
    record(`${name}: no duplicate stopped events`, dups.length === 0,
      dups.length ? `${dups.length} extra: ${dups.map(d => d.body.reason).join(',')}` : '');

    await c.request('continue', { threadId: tid2 });
    const term = await c.waitForEvent('terminated', 20000);
    record(`${name}: continue → terminated`, !!term);
  } catch (e) {
    record(name, false, e.message);
  }
  await c.kill();
}

const scenarios = {
  finalRetNextAsync: (a, v) => stepScenario('finalRetNextAsync', a, {
    exe: ASYNC_EXE, src: ASYNC_SRC, bpLine: 8, stepCommand: 'next',
    expectLines: ASYNC_CALLER_LINES, expectFrameRe: /Outer/i,
  }, v),
  finalRetNextSync: (a, v) => stepScenario('finalRetNextSync', a, {
    exe: SYNC_EXE, src: SYNC_SRC, bpLine: 8, stepCommand: 'next',
    expectLines: [SYNC_CALLER_LINE], expectFrameRe: /Main/i,
  }, v),
  stepOutMidAsync: (a, v) => stepScenario('stepOutMidAsync', a, {
    exe: ASYNC_EXE, src: ASYNC_SRC, bpLine: 7, stepCommand: 'stepOut',
    expectLines: ASYNC_CALLER_LINES, expectFrameRe: /Outer/i,
  }, v),
  stepOutMidSync: (a, v) => stepScenario('stepOutMidSync', a, {
    exe: SYNC_EXE, src: SYNC_SRC, bpLine: 7, stepCommand: 'stepOut',
    expectLines: [SYNC_CALLER_LINE], expectFrameRe: /Main/i,
  }, v),
  stepOutSyncFn: (a, v) => stepScenario('stepOutSyncFn', a, {
    exe: SYNC_EXE, src: SYNC_SRC, bpLine: 12, stepCommand: 'stepOut',
    expectLines: SYNCFN_CALLER_LINES, expectFrameRe: /Main/i,
  }, v),
};

async function main() {
  const [adapterExe, scenario] = process.argv.slice(2);
  const verbose = process.env.DAP_VERBOSE === '1';
  if (!adapterExe) {
    console.error('usage: node async_stepout_client.js <adapterExe> [scenario]');
    process.exit(2);
  }
  const toRun = scenario ? { [scenario]: scenarios[scenario] } : scenarios;
  for (const [name, fn] of Object.entries(toRun)) {
    if (!fn) { console.error(`unknown scenario '${name}'`); process.exit(2); }
    try {
      await fn(adapterExe, verbose);
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

main().catch((e) => { console.error(e); process.exit(1); });
