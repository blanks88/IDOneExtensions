// Local test double derived from the repository client/models, never a live provider.
const http = require('http');
const key = 'syscom.sync.fixture';
const initial = (mode = 'normal') => ({ mode, counts: {}, tokens: 0 });
const getState = async () => (await pm.state.get(key)) || initial();
const save = state => pm.state.set(key, state);
const product = page => ({ producto_id: String(page), modelo: `SIM-${page}`,
  titulo: `Simulation product ${page}`, marca: 'Fixture', sat_key: '46171604',
  sat_description: 'Sistemas de seguridad', unidad_de_medida: {codigo_unidad: '1', nombre: 'Pieza', clave_unidad_sat: 'H87'},
  precios: {precio_descuento: String(page * 10)} });
http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost');
  const state = await getState();
  const reply = (status, body, headers = {}) => {
    res.writeHead(status, { 'Content-Type': 'application/json', ...headers });
    res.end(JSON.stringify(body));
  };
  if (url.pathname === '/__control/reset' && req.method === 'POST') {
    const mode = url.searchParams.get('mode') || 'normal';
    if (!['normal', 'transient', 'fail-page', 'repeat-page', 'missing', 'incomplete'].includes(mode))
      return reply(400, {error: 'Unknown fixture mode'});
    await save(initial(mode)); return reply(200, {mode});
  }
  if (url.pathname === '/__control/state') return reply(200, state);
  const id = `${req.method} ${url.pathname}?${url.searchParams}`;
  state.counts[id] = (state.counts[id] || 0) + 1;
  if (req.method === 'POST' && ['/oauth/token', '/api/v1/oauth/token'].includes(url.pathname)) {
    let body = req.body;
    if (typeof body === 'string') {
      try { body = JSON.parse(body); } catch { body = Object.fromEntries(new URLSearchParams(body)); }
    }
    if (!body || body.grant_type !== 'client_credentials' || !body.client_id || !body.client_secret)
      return reply(400, {error: 'Invalid token request'});
    state.tokens++; await save(state);
    return reply(200, {access_token: `fixture-token-${state.tokens}`, token_type: 'Bearer', expires_in: 3600});
  }
  if (!String(req.headers.authorization || '').startsWith('Bearer fixture-token-'))
    return reply(401, {error: 'Missing fixture bearer'});
  if (req.method === 'GET' && url.pathname === '/api/v1/categorias') {
    await save(state);
    if (state.mode === 'transient' && state.counts[id] === 1) return reply(429, {error: 'Rate limited fixture'}, {'Retry-After': '0'});
    return reply(200, [{id: '1', nombre: 'Fixture', nivel: 1}]);
  }
  if (req.method === 'GET' && url.pathname === '/api/v1/productos') {
    const page = Number(url.searchParams.get('pagina'));
    if (url.searchParams.get('categoria') !== '1' || ![1,2].includes(page)) return reply(400, {error: 'Invalid pagination'});
    await save(state);
    if (state.mode === 'transient' && page === 1 && state.counts[id] === 1) return reply(401, {error: 'Expired fixture token'});
    if (state.mode === 'transient' && page === 1 && state.counts[id] === 2) return reply(503, {error: 'Temporary fixture outage'});
    if (state.mode === 'fail-page' && page === 2) return reply(503, {error: 'Permanent fixture outage'});
    const pages = state.mode === 'missing' ? 1 : 2;
    return reply(200, {pagina: state.mode === 'repeat-page' ? 1 : page, paginas: pages, cantidad: state.mode === 'incomplete' ? 3 : pages, productos: [product(page)]});
  }
  await save(state); reply(501, {error: 'Outside synchronization contract'});
}).listen(process.env.PORT);
