// Read-only smoke test for the remote connector (docket-mcp --http): health, auth, MCP over HTTP,
// REST + OpenAPI, and the full OAuth flow (register, sign in with the key, PKCE token, refresh).
// Usage: node tools/http-smoke.mjs http://localhost:8080 <connector key>
import { createHash, randomBytes } from 'node:crypto';

const [base = 'http://localhost:8080', key = process.env.DOCKET_MCP_KEY] = process.argv.slice(2);
if (!key) { console.error('Pass the connector key as the 2nd argument or DOCKET_MCP_KEY.'); process.exit(2); }
let failed = 0;
const ok = (name, cond, extra = '') => { console.log(`${cond ? 'PASS' : 'FAIL'}  ${name}${extra ? '  ' + extra : ''}`); if (!cond) failed++; };
const b64url = b => b.toString('base64').replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');

async function mcp(token, method, params = {}, id = 1) {
  const r = await fetch(`${base}/mcp`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', accept: 'application/json, text/event-stream', ...(token ? { authorization: `Bearer ${token}` } : {}) },
    body: JSON.stringify({ jsonrpc: '2.0', id, method, params }),
  });
  const text = await r.text();
  const line = text.split('\n').find(l => l.startsWith('data:'));
  return { status: r.status, headers: r.headers, body: line ? JSON.parse(line.slice(5)) : (text ? JSON.parse(text) : null) };
}
const init = { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'smoke', version: '1' } };

// Direct: /health. Behind the Docker stack's Caddy, /health is Vikunja's and the connector's is /connector/health.
const asJson = async r => { try { return JSON.parse(await r.text()); } catch { return null; } };
const health = (await asJson(await fetch(`${base}/connector/health`))) ?? (await asJson(await fetch(`${base}/health`))) ?? {};
ok('health', health.status === 'ok', JSON.stringify(health));

const anon = await mcp(null, 'initialize', init);
ok('mcp without key is 401', anon.status === 401);
ok('401 points at resource metadata', (anon.headers.get('www-authenticate') ?? '').includes('resource_metadata='));
ok('wrong key is 401', (await mcp('not-the-key', 'initialize', init)).status === 401);

const i = await mcp(key, 'initialize', init);
ok('mcp initialize with key', i.status === 200 && i.body?.result?.serverInfo?.name === 'docket', i.body?.result?.serverInfo?.version);
const tl = await mcp(key, 'tools/list', {}, 2);
const names = tl.body?.result?.tools?.map(t => t.name) ?? [];
ok('tools/list', names.length >= 20 && !names.some(n => n.includes('delete')), `${names.length} tools`);
const call = await mcp(key, 'tools/call', { name: 'list_projects', arguments: {} }, 3);
ok('tools/call list_projects', call.body?.result && !call.body.result.isError);

const spec = await fetch(`${base}/openapi.json`).then(r => r.json());
const ops = Object.keys(spec.paths ?? {}).length;
ok('openapi.json', spec.openapi?.startsWith('3.1') && ops === names.length && ops <= 30, `${ops} operations`);
const rest = await fetch(`${base}/api/tools/list_projects`, { method: 'POST', headers: { authorization: `Bearer ${key}`, 'content-type': 'application/json' }, body: '{}' });
ok('REST list_projects', rest.status === 200 && typeof (await rest.json()).result === 'string');
ok('REST without key is 401', (await fetch(`${base}/api/tools/list_projects`, { method: 'POST' })).status === 401);
const bad = await fetch(`${base}/api/tools/get_task`, { method: 'POST', headers: { authorization: `Bearer ${key}`, 'content-type': 'application/json' }, body: '{}' });
ok('REST missing argument is 400', bad.status === 400);

// OAuth, as claude.ai and ChatGPT do it.
const prm = await fetch(`${base}/.well-known/oauth-protected-resource/mcp`).then(r => r.json());
ok('protected resource metadata', Array.isArray(prm.authorization_servers));
const meta = await fetch(`${base}/.well-known/oauth-authorization-server`).then(r => r.json());
ok('authorization server metadata', meta.code_challenge_methods_supported?.includes('S256'));
const redirect = 'https://claude.ai/api/mcp/auth_callback';
const reg = await fetch(meta.registration_endpoint.replace(/^https?:\/\/[^/]+/, base), { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ client_name: 'Smoke test', redirect_uris: [redirect] }) }).then(r => r.json());
ok('dynamic client registration', !!reg.client_id);
const evil = await fetch(`${base}/oauth/register`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ redirect_uris: ['http://evil.example/cb'] }) });
ok('plain-http redirect refused', evil.status === 400);
const phish = await fetch(`${base}/oauth/register`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ client_name: 'Claude', redirect_uris: ['https://evil.example/cb'] }) });
ok('unlisted https redirect refused', phish.status === 400);
const loop = await fetch(`${base}/oauth/register`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ redirect_uris: ['http://127.0.0.1:33418/callback'] }) });
ok('loopback redirect allowed', loop.status === 201);
const junk = await fetch(`${base}/oauth/register`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: '[1,2]' });
ok('non-object registration is 400', junk.status === 400);

const verifier = b64url(randomBytes(32));
const challenge = b64url(createHash('sha256').update(verifier).digest());
const q = new URLSearchParams({ response_type: 'code', client_id: reg.client_id, redirect_uri: redirect, state: 'xyz', code_challenge: challenge, code_challenge_method: 'S256', scope: 'tasks' });
const page = await fetch(`${base}/oauth/authorize?${q}`);
const pageText = await page.text();
ok('sign-in page shows the return host', page.status === 200 && pageText.includes('Connector key') && pageText.includes('claude.ai'));
ok('sign-in page refuses framing', page.headers.get('x-frame-options') === 'DENY' && (page.headers.get('content-security-policy') ?? '').includes("frame-ancestors 'none'"));
const wrong = await fetch(`${base}/oauth/authorize`, { method: 'POST', redirect: 'manual', body: new URLSearchParams({ ...Object.fromEntries(q), key: 'wrong' }) });
ok('wrong key stays on page', wrong.status === 200);
const yes = await fetch(`${base}/oauth/authorize`, { method: 'POST', redirect: 'manual', body: new URLSearchParams({ ...Object.fromEntries(q), key }) });
const loc = new URL(yes.headers.get('location') ?? 'http://x/');
ok('approve redirects with code + state', yes.status === 302 && loc.searchParams.get('state') === 'xyz' && !!loc.searchParams.get('code'));

const tokenUrl = `${base}/oauth/token`;
const badPkce = await fetch(tokenUrl, { method: 'POST', body: new URLSearchParams({ grant_type: 'authorization_code', code: loc.searchParams.get('code'), client_id: reg.client_id, redirect_uri: redirect, code_verifier: b64url(randomBytes(32)) }) });
ok('wrong PKCE verifier refused', badPkce.status === 400);
// The failed attempt used up the code, as it should; sign in again for a fresh one.
const yes2 = await fetch(`${base}/oauth/authorize`, { method: 'POST', redirect: 'manual', body: new URLSearchParams({ ...Object.fromEntries(q), key }) });
const code2 = new URL(yes2.headers.get('location')).searchParams.get('code');
const tok = await fetch(tokenUrl, { method: 'POST', body: new URLSearchParams({ grant_type: 'authorization_code', code: code2, client_id: reg.client_id, redirect_uri: redirect, code_verifier: verifier }) }).then(r => r.json());
ok('token exchange', !!tok.access_token && !!tok.refresh_token, `expires_in ${tok.expires_in}`);
const tokHead = await fetch(tokenUrl, { method: 'POST', body: new URLSearchParams({ grant_type: 'nope' }) });
ok('token responses are no-store', tokHead.headers.get('cache-control') === 'no-store');
ok('mcp with OAuth token', (await mcp(tok.access_token, 'tools/list', {}, 4)).status === 200);
ok('refresh token is not an access token', (await mcp(tok.refresh_token, 'tools/list', {}, 5)).status === 401);
const ref = await fetch(tokenUrl, { method: 'POST', body: new URLSearchParams({ grant_type: 'refresh_token', refresh_token: tok.refresh_token, client_id: reg.client_id }) }).then(r => r.json());
ok('refresh', !!ref.access_token && ref.refresh_token !== tok.refresh_token);
const reuse = await fetch(tokenUrl, { method: 'POST', body: new URLSearchParams({ grant_type: 'refresh_token', refresh_token: tok.refresh_token, client_id: reg.client_id }) });
ok('old refresh token rotated out', reuse.status === 400);

console.log(failed ? `\n${failed} failed` : '\nAll passed');
process.exit(failed ? 1 : 0);
