// Smoke test for docket-mcp over stdio: lists tools and runs the main calls.
// Usage: node tools\mcp-smoke.mjs [path\to\docket-mcp.exe] [--list] [--probe]
// It uses %LOCALAPPDATA%\Docket\settings.json, or the file in DOCKET_SETTINGS (use a test account there:
//   $env:DOCKET_SETTINGS='tools\_tmp\settings.json'). The full run adds and changes tasks.
import { spawn } from 'node:child_process';
const exe = process.argv.slice(2).find(a => !a.startsWith('--')) ?? new URL('../bin/mcp/docket-mcp.exe', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');
const p = spawn(exe, [], { stdio: ['pipe', 'pipe', 'inherit'], env: process.env });
let buf = ''; const waiting = new Map(); let next = 1; let failures = 0;
p.stdout.on('data', d => {
  buf += d; let i;
  while ((i = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, i).trim(); buf = buf.slice(i + 1);
    if (!line) continue;
    const msg = JSON.parse(line);
    if (msg.id && waiting.has(msg.id)) { waiting.get(msg.id)(msg); waiting.delete(msg.id); }
  }
});
const req = (method, params) => new Promise(res => { const id = next++; waiting.set(id, res); p.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n'); });
const call = async (name, args, expectError = false) => {
  const r = await req('tools/call', { name, arguments: args });
  const text = r.result?.content?.map(c => c.text).join('\n') ?? JSON.stringify(r.error);
  const isError = !!r.result?.isError || !!r.error;
  if (isError !== expectError) failures++;
  console.log(`\n> ${name} ${JSON.stringify(args)}${isError ? '  [error]' : ''}${isError !== expectError ? '  <-- UNEXPECTED' : ''}\n${text}`);
  return text;
};
const idOf = text => Number((text.match(/#(\d+)/) ?? [])[1]);

const init = await req('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'smoke', version: '1' } });
console.log('server:', JSON.stringify(init.result.serverInfo));
p.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
const tools = (await req('tools/list', {})).result.tools;
for (const t of tools) console.log(`- ${t.name}${t.annotations?.readOnlyHint ? ' (read-only)' : ''}: ${t.description.slice(0, 90)}`);
if (tools.some(t => /delete/.test(t.name))) { console.log('FAIL: a delete tool is exposed'); failures++; }
if (process.argv.includes('--list')) { if (process.argv.includes('--probe')) await call('list_tasks', {}); p.kill(); process.exit(0); }

await call('create_list', { title: 'Smoke list' });
const a = idOf(await call('add_task', { title: 'Draft LinkedIn post tomorrow !2', project: 'Smoke list', labels: ['content'] }));
const b = idOf(await call('add_task', { title: 'Standup notes', project: 'Smoke list' }));
await call('set_my_day', { ids: [a, b] });
await call('set_important', { ids: [a] });
await call('steps', { id: a, action: 'add', text: 'Pick the clip' });
await call('steps', { id: a, action: 'add', text: 'Write the hook' });
await call('steps', { id: a, action: 'complete', index: 1 });
await call('set_notes', { id: a, notes: 'Keep it under 150 words.' });
await call('set_repeat', { id: b, repeat: 'weekdays' });
await call('set_reminder', { id: a, times: ['tomorrow 9:00', 'in 2 hours'] });
await call('set_categories', { id: a, categories: ['Content', 'Quartifex'] });
await call('my_day', { suggestions: true });
await call('list_tasks', { view: 'important' });
await call('list_tasks', { view: 'planned' });
await call('list_tasks', { view: 'my_day', project: 'Smoke list' });
await call('get_task', { id: a });
await call('complete_task', { ids: [b] });
await call('list_tasks', { view: 'completed', limit: 5 });
await call('move_task', { id: a, column: 'doing' });
await call('get_board', { project: 'Smoke list' });
await call('list_projects', {});
// expected failures
await call('steps', { id: a, action: 'complete', index: 9 }, true);
await call('set_repeat', { id: a, repeat: 'sometimes' }, true);
await call('set_reminder', { id: a, times: ['whenever'] }, true);
await call('move_task', { id: a, column: 'someday' }, true);
await call('set_reminder', { id: a, times: [] });
p.kill();
console.log(failures === 0 ? '\nALL AS EXPECTED' : `\n${failures} UNEXPECTED RESULT(S)`);
process.exit(failures === 0 ? 0 : 1);
