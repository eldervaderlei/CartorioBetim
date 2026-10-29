// ===== Motor de análise do mandado (roda 100% no computador) =====
const MESES = 'janeiro|fevereiro|marco|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro';
const RE_DATA = new RegExp('\\b\\d{1,2}\\s*[\\/.-]\\s*\\d{1,2}\\s*[\\/.-]\\s*\\d{2,4}\\b|\\b\\d{1,2}\\s*(?:o|º|°)?\\s+de\\s+(?:' + MESES + ')\\s+de\\s+\\d{4}\\b');

const JUNTA_HIFEN = /([A-Za-zÀ-ÿ])-[ \t]*\n\s*([A-Za-zÀ-ÿ])/g;
function prepare(raw) {
  // o texto original fica intacto; a versão normalizada (sem acento, minúscula) junta
  // palavras quebradas por hífen no fim da linha, comum em OCR
  const t = String(raw || '');
  let norm = '', map = [], prevSpace = true;
  const LETRA = /[A-Za-zÀ-ÿ]/;
  for (let i = 0; i < t.length; i++) {
    const c = t[i];
    if (c === '-' && i > 0 && LETRA.test(t[i - 1])) {
      let j = i + 1; while (j < t.length && (t[j] === ' ' || t[j] === '\t')) j++;
      if (t[j] === '\n' || t[j] === '\r') { while (j < t.length && /\s/.test(t[j])) j++; if (j < t.length && LETRA.test(t[j])) { i = j - 1; continue; } }
    }
    if (/\s/.test(c)) { if (!prevSpace) { norm += ' '; map.push(i); prevSpace = true; } continue; }
    const n = c.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
    for (let k = 0; k < n.length; k++) { norm += n[k]; map.push(i); }
    prevSpace = false;
  }
  map.push(t.length);
  // quebras de página (\f) para saber em que página está cada trecho
  const quebras = []; for (let i = 0; i < t.length; i++) if (t[i] === '\f') quebras.push(i);
  return { orig: t, norm, map, quebras };
}
function paginaDe(doc, o) { let lo = 0, hi = doc.quebras.length; while (lo < hi) { const m = (lo + hi) >> 1; if (doc.quebras[m] < o) lo = m + 1; else hi = m; } return lo; }
const normTxt = s => String(s).normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(JUNTA_HIFEN, '$1$2').replace(/\s+/g, ' ').trim();
function findText(doc, str) {
  const q = normTxt(str), out = []; if (q.length < 3) return out;
  let i = -1; while ((i = doc.norm.indexOf(q, i + 1)) >= 0) out.push({ s: i, e: i + q.length });
  return out;
}

function findAll(doc, re) {
  const out = []; re.lastIndex = 0; let m;
  while ((m = re.exec(doc.norm)) !== null) {
    if (m[0].length === 0) { re.lastIndex++; continue; }
    out.push({ s: m.index, e: m.index + m[0].length });
  }
  return out;
}

function origRange(doc, h) { return [doc.map[h.s], doc.map[h.e - 1] + 1]; }

function snippet(doc, h, pad = 110) {
  const [os, oe] = origRange(doc, h);
  let a = Math.max(0, os - pad), b = Math.min(doc.orig.length, oe + pad);
  while (a > 0 && /\S/.test(doc.orig[a - 1]) && os - a < pad + 25) a--;
  while (b < doc.orig.length && /\S/.test(doc.orig[b]) && b - oe < pad + 25) b++;
  const clean = s => s.replace(JUNTA_HIFEN, '$1$2').replace(/\s+/g, ' ');
  return {
    before: (a > 0 ? '…' : '') + clean(doc.orig.slice(a, os)),
    match: clean(doc.orig.slice(os, oe)),
    after: clean(doc.orig.slice(oe, b)) + (b < doc.orig.length ? '…' : ''),
    range: [os, oe], page: paginaDe(doc, os)
  };
}

function uniqHits(hits) {
  hits.sort((x, y) => x.s - y.s);
  const out = [];
  for (const h of hits) { const last = out[out.length - 1]; if (last && h.s < last.e) { last.e = Math.max(last.e, h.e); } else out.push({ ...h }); }
  return out;
}

// ---------- Partes do processo: separa as peças e dá prioridade à sentença ----------
// Rank: 0 = sentença/decisão/mandado · 1 = certidão ou peça citada pela sentença (ex.: acordo) · 2 = outras · 3 = petição
function origToNorm(doc, o) { let lo = 0, hi = doc.map.length - 1; while (lo < hi) { const m = (lo + hi) >> 1; if (doc.map[m] < o) lo = m + 1; else hi = m; } return lo; }
function segmentar(doc) {
  const t = doc.orig, pages = []; let st = 0;
  for (let i = 0; i <= t.length; i++) if (i === t.length || t[i] === '\f') { if (t.slice(st, i).trim()) pages.push({ s: st, e: i }); st = i + 1; }
  const docs = [];
  for (const p of pages) {
    const txt = t.slice(p.s, p.e);
    const m = [...txt.matchAll(/Num\.\s*(\d{8,})\s*-\s*P[áa]g\.\s*\S+/g)].pop();
    const id = m ? m[1] : null;
    const last = docs[docs.length - 1];
    if (last && last.id === id && (id || !/\bnum\.?\s*\d{8,}/i.test(t.slice(last.s, last.e)))) last.e = p.e; else docs.push({ s: p.s, e: p.e, id });
  }
  if (!docs.length) docs.push({ s: 0, e: t.length, id: null });
  for (const d of docs) {
    d.ns = origToNorm(doc, d.s); d.ne = origToNorm(doc, d.e);
    const n = doc.norm.slice(d.ns, d.ne);
    // ignora a capa/índice do PJe e o cabeçalho para olhar o começo da peça
    const head = n.slice(0, 900);
    if (/excelentissim/.test(head.slice(0, 500))) d.kind = 'peticao';
    else if (/\bcertidao\b/.test(head) && /\bcertifico\b/.test(n)) d.kind = 'certidao';
    else if (/(?:^|\s)(?:sentenca|decisao|mandado de averbacao)(?:\s|$|\.)/.test(head) && /homolog|julgo|decret|averbac/.test(n) && !/\bdocumentos\s+id\.?/.test(head)) d.kind = 'sentenca';
    else d.kind = 'outro';
  }
  const citados = new Set();
  for (const d of docs) if (d.kind === 'sentenca') for (const m of doc.norm.slice(d.ns, d.ne).matchAll(/\bid\s*(?:n[o.º]*\s*)?:?\s*(\d{8,})/g)) citados.add(m[1]);
  const RANK = { sentenca: 0, certidao: 1, outro: 2, peticao: 3 };
  for (const d of docs) d.rank = d.kind !== 'sentenca' && d.id && citados.has(d.id) ? 1 : RANK[d.kind];
  if (citados.size) for (const d of docs) if (d.id && citados.has(d.id) && d.kind !== 'sentenca') d.kind = 'citado';
  doc.docs = docs; doc.citados = citados;
  doc.temSentenca = docs.some(d => d.kind === 'sentenca');
  return docs;
}
function rankOf(doc, h) { if (!doc.docs) return 2; for (const d of doc.docs) if (h.s >= d.ns && h.s < d.ne) return d.rank; return 2; }
function porRank(doc, hits) { return hits.slice().sort((a, b) => rankOf(doc, a) - rankOf(doc, b) || a.s - b.s); }
function melhores(doc, hits) { if (!hits.length) return hits; const r = Math.min(...hits.map(h => rankOf(doc, h))); return hits.filter(h => rankOf(doc, h) === r); }
function minRank(doc, hits) { return hits.length ? Math.min(...hits.map(h => rankOf(doc, h))) : 99; }

// ---------- 1. Força de mandado ----------
function checkMandado(doc) {
  const forca = findAll(doc, /forca de (?:oficio\s*\/\s*)?mandado|\b(?:servira|serve|valera|vale|servindo|sirva|valendo|funcionara|servir|valer)\b(?:\s+[^\s.;]+){0,8}?\s+(?:como|de)\s+(?:oficio\s*\/\s*)?mandado/g);
  const titulo = findAll(doc, /\bmandado\s+(?:judicial\s+)?(?:de\s+)?averbacao/g)
    .filter(h => !/expe\w*(?:-se)?\s+(?:\S+\s+){0,3}$/.test(doc.norm.slice(Math.max(0, h.s - 40), h.s)));
  const ok = uniqHits([...forca, ...titulo]);
  if (ok.length) return { status: 'ok', summary: 'O documento tem força de mandado.', hits: ok };
  const expeca = findAll(doc, /\bexpe\w*(?:-se)?\s+(?:\S+\s+){0,4}?mandado/g);
  if (expeca.length) return { status: 'warn', summary: 'A decisão só determina que o mandado seja expedido. Confira se o mandado em si (ou a cláusula “servirá como mandado”) está no documento.', hits: expeca };
  return { status: 'missing', summary: 'Não encontrei cláusula de força de mandado (ex.: “servirá como mandado”) nem título de mandado de averbação.', hits: [] };
}

// ---------- 2. Nome que voltará a usar ----------
function checkNome(doc) {
  const grupos = {
    retorno: [
      /\b(?:retorno|volta|regresso)\s+(?:ao|a\s+usar\s+o|a\s+assinar\s+o)\s+(?:seu\s+)?nome\b/g,
      /\bnome\s+de\s+solteir[ao]\b/g,
      /\b(?:excluid|suprimid|retirad|exclusao|supressao)\w*\b(?:\s+\S+){0,4}?\s+(?:o\s+)?(?:patronimico|sobrenome|apelido\s+de\s+familia)/g
    ],
    mantem: [
      /\b(?:mantera|mantem|mantendo|manter|permanecera|permanece|continuara|conservara|conserva|conservar)\b(?:\s+\S+){0,5}?\s+(?:o\s+)?(?:seu\s+)?nome\b/g,
      /\bmanutencao\s+do\s+nome\b/g,
      /\bnome\s+de\s+casad[ao]\b/g
    ],
    // nenhum dos dois muda de nome
    nenhum: [
      /\b(?:as\s+partes|os\s+(?:ex-?)?conjuges|os\s+divorciandos|os\s+requerentes|os\s+contraentes|os\s+interessados|os\s+autores|ambos(?:\s+os\s+conjuges)?|ambas\s+as\s+partes|cada\s+(?:um|qual)|nenhum\w*\s+d[oa]s?\s+\w+)\b[^.;]{0,90}?\b(?:continuar\w*|permanece\w*|manter\w*|mantem|mantendo|conservar\w*|conserva\w*|seguir\w*|prosseguir\w*|utilizar\w*|usar\w*|assinar\w*)\b[^.;]{0,70}?\bnomes?\b/g,
      /\bnomes?\b[^.;]{0,70}?\b(?:permanece\w*|continua\w*|mantid\w*|mantem|ficam|ficarao|seguem|seguirao|restam|restarao)\b\s+(?:\S+\s+){0,3}?(?:inalterad\w*|(?:os|o)\s+mesmos?|iguais|sem\s+(?:qualquer\s+)?(?:alteracao|modificacao|mudanca))/g,
      /\b(?:nomes?\s+(?:\S+\s+){0,4}?)?(?:inalterad\w*)\s+(?:\S+\s+){0,2}?(?:os|o)\s+nomes?\b|\bnomes?\s+(?:\S+\s+){0,5}?inalterad\w*/g,
      /\b(?:nao|nenhum\w*|sem)\b[^.;]{0,40}?\b(?:houve|ha|havera|haver|havendo|existe|existiu|tera|tem|qualquer)\b\s+(?:\S+\s+){0,3}?(?:alteraca\w*|modificaca\w*|mudanca\w*|retificaca\w*)\s+(?:\S+\s+){0,4}?nomes?\b/g,
      /\b(?:sem|nao\s+ha|nao\s+houve)\s+(?:qualquer\s+)?(?:alteracao|modificacao|mudanca)\s+(?:de|do|dos|no|nos|em)\s+(?:seus\s+|seu\s+)?nomes?\b/g,
      /\b(?:nao|nenhum\w*\s+d[oa]s?\s+\w+)\b[^.;]{0,50}?\b(?:alter\w+|adot\w+|acresc\w+|incorpor\w+|mud\w+|acrescent\w+)\b[^.;]{0,50}?\b(?:nomes?|sobrenomes?|patronimico\w*|apelidos?\s+de\s+familia)\b/g,
      /\b(?:continuar\w*|permanecer\w*|permanece\w*|manter\w*|mantem|conservar\w*|seguir\w*)\b[^.;]{0,50}?\bnomes?\s+de\s+solteir[oa]s?\b/g
    ],
    geral: [
      /\b(?:voltara|volta|retornara|retorna|passara|passa|devera|podera|ira|voltando|passando|retornando)\s+(?:a\s+)?(?:usar|utilizar|assinar|adotar|se\s+chamar|ser\s+chamad[ao])\b/g
    ]
  };
  const found = {}; let all = [];
  for (const g in grupos) { found[g] = grupos[g].flatMap(re => findAll(doc, re)); all = all.concat(found[g]); }
  all = uniqHits(all);
  if (!all.length) return { status: 'missing', summary: 'Não encontrei informação sobre o nome que a parte passará a usar.', hits: [] };
  const r0 = minRank(doc, all);
  for (const g in found) found[g] = found[g].filter(h => rankOf(doc, h) === r0);
  // o que já é frase de "nenhum muda o nome" não conta como retorno/manutenção
  const dentro = h => found.nenhum.some(n => h.s < n.e + 2 && h.e > n.s - 2);
  for (const g of ['retorno', 'mantem', 'geral']) found[g] = found[g].filter(h => !dentro(h));
  const todosHits = porRank(doc, all);
  all = all.filter(h => rankOf(doc, h) === r0);
  if (doc.temSentenca && r0 >= 2) return { status: 'warn', summary: 'Só encontrei o assunto do nome fora da sentença (ex.: no pedido da petição). Confira o que a sentença decidiu sobre o nome.', hits: todosHits, nomes: [], quem: '' };
  const partes = [];
  if (found.retorno.length) partes.push('retorno ao nome de solteiro(a)');
  if (found.nenhum.length && !found.retorno.length && !found.geral.length) partes.push('nenhum dos contraentes altera o nome');
  if (found.mantem.length && !(found.nenhum.length && !found.retorno.length && !found.geral.length)) partes.push('manutenção do nome de casado(a)');
  if (!partes.length) partes.push('alteração de nome mencionada');
  // tenta achar o nome em maiúsculas logo após a menção
  const nomes = [];
  for (const h of all.filter(x => !dentro(x) || found.retorno.length)) {
    if (found.nenhum.some(n => h.s >= n.s && h.e <= n.e)) continue;
    const [, oe] = origRange(doc, h);
    const trecho = doc.orig.slice(oe, oe + 160).replace(JUNTA_HIFEN, '$1$2');
    const m = trecho.match(/[A-ZÀ-Ý]{2,}(?:\s+(?:D[AEO]S?|E|DI|DU)?\s*[A-ZÀ-Ý]{2,}){1,8}/);
    if (m && !/MANDADO|SENTEN|JUIZ|VARA|CART[OÓ]RIO|PODER|TRIBUNAL|COMARCA/.test(m[0])) nomes.push(m[0].replace(/\s+/g, ' ').trim());
  }
  const extra = nomes.length ? 'Possível nome indicado: ' + [...new Set(nomes)].join(' · ') : '';
  // gênero / quem: olha as palavras perto das menções
  const perto = all.map(h => doc.norm.slice(Math.max(0, h.s - 120), h.e + 60)).join(' ');
  const fem = /solteira|casada|divorcianda|varoa|virago|\ba (?:requerente|contraente|autora|re|conjuge|esposa|mulher)\b/.test(perto);
  const mas = /solteiro\b|casado\b|divorciando|varao|\bo (?:requerente|contraente|autor|reu|conjuge|esposo|marido)\b/.test(perto);
  const unicos = [...new Set(nomes)];
  let quem = '';
  if (found.nenhum.length && !found.retorno.length && !found.geral.length) quem = 'nenhum';
  else if (found.retorno.length || found.geral.length) quem = unicos.length >= 2 ? 'ambos' : (fem && !mas ? 'ela' : mas && !fem ? 'ele' : fem ? 'ela' : '');
  else if (found.mantem.length) quem = fem && !mas ? 'mantem_ela' : mas && !fem ? 'mantem_ele' : 'mantem_ela';
  return { status: 'ok', summary: 'Encontrado: ' + partes.join(' e ') + '.' + (r0 === 1 ? ' (No acordo/peça citada pela sentença.)' : ''), note: extra, hits: todosHits, nomes: unicos, quem };
}

// ---------- 3. Divisão de bens ----------
function checkPartilha(doc) {
  const semBens = findAll(doc, /\b(?:nao\s+ha|nao\s+houve|nao\s+existem|nao\s+existe|nao\s+possuem|nao\s+possuir|nao\s+possui|nao\s+ter|nao\s+haver|nao\s+existirem|nao\s+tem|nao\s+tendo|nao\s+adquiriram|nao\s+amealharam|inexistencia\s+de|inexistem|sem)\s+(?:\S+\s+){0,3}?bens\b/g);
  const posterior = findAll(doc, /\bpartilha\b[^.;]{0,120}?\b(?:posterior\w*|oportun\w*|futur\w*|via\s+propria|acao\s+propria|autos\s+proprios|separad\w*)|\bbens\b[^.;]{0,80}?\bpartilhad\w*\s+(?:posteriormente|oportunamente|futuramente)|\bprossig\w*[^.;]{0,260}?\bpartilha\b|\b(?:seguira|prosseguira|segue|prossegue)\s+quanto\s+aos\s+demais\s+pedidos/g);
  const partilha = findAll(doc, /\bpartilh\w*|\bmeacao\b|\bdivisao\s+(?:dos|de)\s+bens\b|\bbens\b[^.;]{0,100}?\b(?:serao|sera|ficara|ficarao|restara|restarao)\s+(?:divididos|dividido|partilhad\w*|com\b)|\b(?:ficara|ficarao|restara|restarao)\s+com\s+(?:o|a)\s+(?:conjuge|requerente|requerid[oa]|varao|virago|varoa|autor\w*|re)\b|\bpatrimonio\s+do\s+casal\b/g)
    // "ação de divórcio com partilha" / "c/c partilha" só descreve o tipo da ação
    .filter(h => !/\b(?:acao|divorcio|litigioso|consensual)\b[^.;]{0,40}\b(?:com|c\s*\/\s*c|cumulad\w*\s+com)\s*$/.test(doc.norm.slice(Math.max(0, h.s - 60), h.s)));
  const all = uniqHits([...semBens, ...posterior, ...partilha]);
  const homologa = findAll(doc, /\bhomolog\w*\b[^.;]{0,160}?\bacordo\b|\bacordo\b[^.;]{0,60}?\bhomologad\w*/g).filter(h => rankOf(doc, h) === 0);
  if (!all.length) return { status: homologa.length ? 'warn' : 'missing', summary: homologa.length ? 'A sentença homologou o acordo das partes, mas não achei como ficaram os bens. Confira no acordo.' : 'Não encontrei menção à divisão de bens (partilha, “não há bens a partilhar” ou “partilha posterior”).', hits: homologa };
  const r0 = minRank(doc, all), f = hs => hs.filter(h => rankOf(doc, h) === r0);
  const sb = f(semBens), po = f(posterior);
  const ordenados = porRank(doc, all);
  if (doc.temSentenca && r0 >= 2) return { status: 'warn', summary: homologa.length ? 'A sentença homologou o acordo das partes, mas a partilha só aparece fora dele (ex.: no pedido da petição). Confira no acordo como ficaram os bens.' : 'Só encontrei a partilha fora da sentença (ex.: no pedido da petição). Confira o que foi decidido sobre os bens.', hits: porRank(doc, [...homologa, ...all]), tipo: '' };
  let s;
  if (sb.length) s = 'Declarado que não há bens a partilhar.';
  else if (po.length) s = 'A partilha de bens será feita posteriormente (o processo segue quanto aos bens).';
  else s = 'Bens partilhados' + (r0 === 1 ? ' no acordo homologado pela sentença.' : ' conforme a decisão.');
  const tipo = sb.length ? 'sem' : po.length ? 'posterior' : 'partilhados';
  return { status: 'ok', summary: s, hits: ordenados, tipo };
}

// ---------- 4. Trânsito em julgado ----------
function checkTransito(doc) {
  const hits = findAll(doc, /\btransit\w*\s+em\s+julgad\w*/g)
    .concat(findAll(doc, /\btransita\s+em\s+julgado\s+nesta\s+data/g));
  if (!hits.length) {
    const prazo = findAll(doc, /\b(?:apos|depois\s+d[oe]|decorrido|decurso\s+d[oe])\s+(?:o\s+)?prazo\s+recursa\w*/g);
    if (prazo.length) return { status: 'warn', summary: 'A decisão vale como mandado só depois do prazo recursal, e não achei certidão de trânsito em julgado (ou de decurso de prazo). Peça a certidão antes de averbar.', hits: prazo };
    return { status: 'missing', summary: 'Não encontrei menção ao trânsito em julgado.', hits: [] };
  }
  const confirmados = [], datas = [];
  for (const h of hits) {
    const palavra = doc.norm.slice(h.s, h.e);
    const antes = doc.norm.slice(Math.max(0, h.s - 160), h.s);
    const depois = doc.norm.slice(h.e, h.e + 60);
    const antesCurto = doc.norm.slice(Math.max(0, h.s - 70), h.s);
    let ok = false;
    if (/^transit(?:ou|aram)\b/.test(palavra) || /nesta\s+data/.test(palavra)) ok = true;
    if (/\bcertifico\b/.test(antes)) ok = true;
    const md = depois.slice(0, 45).match(RE_DATA);
    if (md) { ok = true; const ds = h.e + md.index; datas.push(doc.orig.slice(doc.map[ds], doc.map[ds + md[0].length - 1] + 1).replace(/\s+/g, ' ')); }
    if (/nesta\s+data/.test(palavra) || /^\s*(?:nesta|na\s+presente)\s+data/.test(depois)) { ok = true; datas.push('na data da própria decisão (“nesta data”)'); }
    if (/certidao\s+de\s+$/.test(antes) && md) ok = true;
    if (/\b(?:apos|com\s+o|depois\s+do|quando\s+do|ate\s+o|antes\s+do|aguarde-se|aguardar)\s*(?:o\s+)?$/.test(antes) && !md && !/certifico/.test(antes)) ok = false;
    if (ok) confirmados.push(h);
  }
  if (confirmados.length) {
    const d = [...new Set(datas)];
    return { status: 'ok', summary: 'Trânsito em julgado certificado.' + (d.length ? ' Data encontrada: ' + d.join(', ') + '.' : ' (Não identifiquei a data; confira.)'), hits: uniqHits(confirmados) };
  }
  const renuncia = findAll(doc, /\brenunci\w*\b[^.;]{0,60}?\bprazo\w*\s+recursa\w*/g);
  if (renuncia.length) return { status: 'warn', summary: 'As partes renunciaram ao prazo recursal, mas não achei a certidão ou a data do trânsito em julgado. Confira.', hits: uniqHits([...hits, ...renuncia]) };
  return { status: 'warn', summary: 'O trânsito em julgado só aparece como condição futura (ex.: “após o trânsito em julgado…”). Confira se há certidão de trânsito em julgado com data.', hits: uniqHits(hits) };
}

// ---------- Justiça gratuita (não é obrigatória, só destacada) ----------
function checkJustica(doc) {
  const JG = '(?:justica\\s+gratuita|gratuidade(?:\\s+(?:de|da)\\s+justica|\\s+judiciaria|\\s+processual)?|assistencia\\s+judiciaria(?:\\s+gratuita)?|beneficios?\\s+da\\s+lei\\s+(?:n[o.º]*\\s*)?1\\.?060)';
  const re = s => new RegExp(s, 'g');
  const deferida = [
    ...findAll(doc, re('\\b(?:defiro|deferid[ao]s?|deferiu|defere|concedo|concedid[ao]s?|concede|concedeu|mantenho)\\b[^.;]{0,90}?' + JG)),
    ...findAll(doc, re(JG + '[^.;]{0,50}?\\b(?:deferid[ao]s?|concedid[ao]s?)\\b')),
    ...findAll(doc, re('\\bbeneficiari[oa]s?\\s+da\\s+' + JG)),
    ...findAll(doc, re('\\bsob\\s+o\\s+palio\\s+da\\s+' + JG)),
    ...findAll(doc, re('\\bjustica\\s+gratuita\\s*\\??\\s*:?\\s*sim\\b'))
  ];
  const indeferida = [
    ...findAll(doc, re('\\b(?:indefiro|indeferid[ao]s?|indeferiu|revogo|revogad[ao]s?|nego)\\b[^.;]{0,90}?' + JG)),
    ...findAll(doc, re('\\bjustica\\s+gratuita\\s*\\??\\s*:?\\s*nao\\b'))
  ];
  const mencao = findAll(doc, re(JG));
  if (deferida.length) {
    const hs = melhores(doc, uniqHits(deferida));
    return { key: 'justica', label: 'Justiça gratuita', status: 'sim', summary: 'O processo tem justiça gratuita deferida. Veja os trechos abaixo.', hits: porRank(doc, hs) };
  }
  if (indeferida.length) return { key: 'justica', label: 'Justiça gratuita', status: 'indeferida', summary: 'A justiça gratuita foi indeferida ou revogada.', hits: porRank(doc, uniqHits(indeferida)) };
  if (mencao.length) return { key: 'justica', label: 'Justiça gratuita', status: 'pedida', summary: 'A justiça gratuita foi pedida, mas não achei a decisão que a defere. Confira.', hits: porRank(doc, uniqHits(mencao)) };
  return { key: 'justica', label: 'Justiça gratuita', status: 'nao', summary: 'Não há menção à justiça gratuita no documento.', hits: [] };
}

// ---------- Informações complementares ----------
function checkExtras(doc) {
  const out = [];
  const proc = doc.orig.match(/\b\d{7}\s?-\s?\d{2}\s?\.\s?\d{4}\s?\.\s?\d\s?\.\s?\d{2}\s?\.\s?\d{4}\b/g);
  out.push({ label: 'Número do processo', found: !!proc, value: proc ? [...new Set(proc.map(p => p.replace(/\s/g, '')))].join(', ') : '' });
  const vara = findAll(doc, /\b\d+\s*[ªa]?\s*vara\b|\bvara\s+(?:de|da|do|civel|unica)\b[^.;]{0,50}/g);
  let varaVal = '';
  if (vara.length) {
    const [os] = origRange(doc, vara[0]);
    const ls = doc.orig.lastIndexOf('\n', os) + 1; let le = doc.orig.indexOf('\n', os); if (le < 0) le = doc.orig.length;
    varaVal = doc.orig.slice(Math.max(ls, os - 40), Math.min(le, os + 90)).replace(/\s+/g, ' ').trim();
  }
  out.push({ label: 'Vara / juízo', found: vara.length > 0, value: varaVal });
  const mat = doc.orig.match(/\b\d{6}\s?\d{2}\s?\d{2}\s?\d{4}\s?\d\s?\d{5}\s?\d{3}\s?\d{7}\s?\d{2}\b/g);
  const lft = [];
  const temLivro = findAll(doc, /\blivro\s*(?:n[o.º°]*\s*)?[a-z]{0,2}\s*-?\s*\d+/g).length > 0;
  if (temLivro) for (const re of [/\blivro\s*(?:n[o.º°]*\s*)?[a-z]{0,2}\s*-?\s*\d+/g, /\b(?:folhas?|fls?\.?)\s*(?:n[o.º°]*\s*)?\d+(?:\s*v)?/g, /\btermo\s*(?:n[o.º°]*\s*)?\d[\d.]*\d|\btermo\s*(?:n[o.º°]*\s*)?\d/g]) {
    const h = findAll(doc, re); if (h.length) lft.push(snippet(doc, h[0], 0).match);
  }
  let regVal = '';
  if (mat) regVal = 'Matrícula: ' + [...new Set(mat.map(m => m.replace(/\s/g, '')))].join(', ');
  if (lft.length) regVal += (regVal ? ' · ' : '') + lft.join(' · ');
  out.push({ label: 'Dados do registro de casamento', found: !!regVal, value: regVal });
  const ass = findAll(doc, /assinad\w*\s+(?:eletronicamente|digitalmente)|assinatura\s+(?:eletronica|digital)|codigo\s+(?:de\s+)?verifica\w*|chave\s+de\s+acesso|autenticidade|\bvalidar\b|conferencia\s+de\s+documento/g);
  out.push({ label: 'Assinatura eletrônica / código de autenticidade', found: ass.length > 0, value: ass.length ? snippet(doc, ass[0], 0).match : '', hit: ass[0] });
  return out;
}

function trechos(doc, it, max) {
  it.snippets = []; const vistos = [];
  for (const h of it.hits) {
    if (it.snippets.length >= max) break;
    const sn = snippet(doc, h), chave = (sn.match + sn.after).slice(0, 80);
    if (vistos.some(v => Math.abs(v.p - sn.range[0]) < 220 || v.k === chave)) continue;
    vistos.push({ p: sn.range[0], k: chave }); it.snippets.push(sn);
  }
  it.ranges = it.hits.map(h => origRange(doc, h));
  // páginas dos trechos que decidiram o resultado (os de maior prioridade)
  const best = it.hits.length ? melhores(doc, it.hits) : [];
  it.best = best;
  it.pages = [...new Set(best.map(h => paginaDe(doc, origRange(doc, h)[0])))].sort((x, y) => x - y);
}
function analisar(raw) {
  const doc = prepare(raw);
  segmentar(doc);
  const itens = [
    { key: 'mandado', label: 'Força de mandado', ...checkMandado(doc) },
    { key: 'nome', label: 'Nome que voltará a assinar', ...checkNome(doc) },
    { key: 'partilha', label: 'Divisão de bens', ...checkPartilha(doc) },
    { key: 'transito', label: 'Trânsito em julgado', ...checkTransito(doc) }
  ];
  for (const it of itens) {
    // títulos ("DO PATRIMÔNIO DO CASAL") por último; trechos repetidos ou muito próximos só uma vez
    it.hits = it.hits.slice().sort((a, b) => rankOf(doc, a) - rankOf(doc, b) || (/^patrimonio/.test(doc.norm.slice(a.s, a.e)) - /^patrimonio/.test(doc.norm.slice(b.s, b.e))) || a.s - b.s);
    trechos(doc, it, 3);
  }
  const justica = checkJustica(doc); trechos(doc, justica, 2);
  const dados = extrairDados(doc, itens);

  // marcas para o PDF grifado: [categoria, início, fim] no texto original
  const marcas = [];
  const add = (cat, h) => { const [a, b] = origRange(doc, h); marcas.push({ cat, a, b, page: paginaDe(doc, a) }); };
  for (const it of itens) for (const h of it.best.slice(0, 8)) add(it.key, h);
  if (justica.status !== 'nao') for (const h of justica.best.slice(0, 4)) add('justica', h);
  const local = {};
  const achar = (cat, str, campo, perto) => {
    if (!str) return; const hs = findText(doc, str); if (!hs.length) return;
    let h = porRank(doc, hs)[0];
    // nome de solteiro: a ocorrência logo depois da frase que fala do nome
    if (perto && perto.length) { const d = x => Math.min(...perto.map(p => x.s >= p.s ? x.s - p.s : 1e9)); const c = hs.slice().sort((x, y) => d(x) - d(y))[0]; if (d(c) < 400) h = c; }
    add(cat, h); local[campo] = paginaDe(doc, origRange(doc, h)[0]);
  };
  (dados.partes || []).forEach((p, i) => achar('dados', p, 'parte' + i));
  const nomeBest = (itens.find(i => i.key === 'nome') || {}).best || [];
  (dados.nomes || []).forEach((n, i) => achar('nome', n, 'solteiro' + i, nomeBest));
  achar('dados', dados.trechoLFT, 'lft');
  achar('dados', dados.processo, 'processo');
  dados.paginas = local;
  return { doc, itens, justica, extras: checkExtras(doc), dados, marcas, temPaginas: doc.quebras.length > 0 };
}

// ---------- dados para a etiqueta ----------
const STOP = /BENS|D[IÍ]VIDA|CASAL|GUARDA|ALIMENTOS|VISITA|PROLE|FATOS|PEDIDOS|M[EÉ]RITO|PRELIMINAR|JUSTI[CÇ]A|GRATUITA|SEGREDO|ADVOCACIA|OAB|PROCURA[CÇ]|EXCELENT|SENHOR|DOUTOR|PATRIM[OÔ]NIO|NOME DE|PROLEG|IMPOSSIBILIDADE|CERTIFICO|TERMO|TRIAGEM|CLASSE|ASSUNTO|PJE|EPROC|PODER|JUDICI|TRIBUNAL|ESTADO|MINAS|GERAIS|COMARCA|VARA|SENTEN|MANDADO|AVERBA|CART[OÓ]RIO|REGISTRO|CIVIL|JU[IÍ]Z|DIREITO|PROCESSO|FAM[IÍ]LIA|CERTID|CERTIFIC|DIV[OÓ]RCIO|CONSENSUAL|LITIGIOS|A[CÇ][AÃ]O|MINIST[EÉ]RIO|P[UÚ]BLICO|DEFENSORIA|OFICIAL|PESSOAS|NATURAIS|SECRETARIA|ASSINAD|DOCUMENTO|BRASIL|REP[UÚ]BLICA|SUCESS|REQUER|HOMOLOG|VISTOS|INTIME|PUBLIQUE|ARQUIVE|CUMPRA/;
const NOME_RE = /[A-ZÀ-ÖØ-Ý][A-ZÀ-ÖØ-Ý']+(?:[ \t]*\n?[ \t]+(?:(?:D[AEO]S?|DI|DU)[ \t]*\n?[ \t]+)?[A-ZÀ-ÖØ-Ý][A-ZÀ-ÖØ-Ý']+|[ \t]*\n[ \t]*[A-ZÀ-ÖØ-Ý][A-ZÀ-ÖØ-Ý']+){1,9}/g;
function nomesNoTexto(t) {
  const out = []; let m; NOME_RE.lastIndex = 0;
  while ((m = NOME_RE.exec(t)) !== null) {
    // um nome pode continuar na linha de baixo, mas para antes de uma linha que é título (SENTENÇA, PODER JUDICIÁRIO…)
    const linhas = m[0].split('\n'); let bruto = '';
    for (const l of linhas) { if (STOP.test(l)) break; bruto += (bruto ? '\n' : '') + l; }
    const v = bruto.replace(/\s+/g, ' ').trim();
    if (!v || STOP.test(v) || v.split(' ').filter(w => w.length > 2).length < 2) continue;
    out.push({ v, s: m.index, e: m.index + bruto.replace(/\s+$/, '').length });
  }
  return out;
}
const semAcento = s => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toUpperCase();
function titulo(s) { return s.toLowerCase().replace(/(^|\s)(\S)/g, (a, b, c) => b + c.toUpperCase()).replace(/\s(D[aeo]s?|E)\s/g, x => x.toLowerCase()); }

// palpite do sexo de uma parte: palavras ao redor do nome, quem volta ao nome de solteira e, por último, o prenome
function sexo(t, nome, nomeIt, nomesSolteiro, outro) {
  const T = semAcento(t), N = semAcento(nome), pren = N.split(' ')[0];
  let f = 0, m = 0, i = -1;
  while ((i = T.indexOf(N, i + 1)) >= 0) {
    const antes = T.slice(Math.max(0, i - 70), i), depois = T.slice(i + N.length, i + N.length + 160);
    if (/\b(?:A|DA|PELA)\s+(?:REQUERENTE|REQUERIDA|AUTORA|RE|CONJUGE|DIVORCIANDA|EXEQUENTE)\b[^A-Z]{0,8}$|VIRAGO|VAROA|DIVORCIANDA|ESPOSA/.test(antes)) f += 2;
    if (/\b(?:O|DO|PELO)\s+(?:REQUERENTE|REQUERIDO|AUTOR|REU|CONJUGE|DIVORCIANDO|EXEQUENTE)\b[^A-Z]{0,8}$|VARAO|DIVORCIANDO|ESPOSO|MARIDO/.test(antes)) m += 2;
    let d = depois.slice(0, 120);
    const corte = d.search(/\.\s|;|\(|\n\s*\n/); if (corte >= 0) d = d.slice(0, corte);
    const io = outro ? d.indexOf(semAcento(outro)) : -1; if (io >= 0) d = d.slice(0, io);
    f += (d.match(/\b(?:BRASILEIRA|CASADA|DIVORCIADA|SEPARADA|SOLTEIRA|PORTADORA|INSCRITA|DOMICILIADA|NASCIDA|FILHA|AUTONOMA|APOSENTADA|COMPANHEIRA|VIRAGO)\b/g) || []).length;
    m += (d.match(/\b(?:BRASILEIRO|CASADO|DIVORCIADO|SEPARADO|SOLTEIRO|PORTADOR|INSCRITO|DOMICILIADO|NASCIDO|FILHO|AUTONOMO|APOSENTADO|COMPANHEIRO|VARAO)\b/g) || []).length;
  }
  // quem volta ao nome de solteira/solteiro
  const q = nomeIt.quem;
  if ((nomesSolteiro || []).some(n => semAcento(n).split(' ')[0] === pren)) { if (q === 'ela' || q === 'mantem_ela') f += 3; if (q === 'ele' || q === 'mantem_ele') m += 3; }
  if (f > m) return 'F'; if (m > f) return 'M';
  if (/A$/.test(pren) && !/^(?:LUCA|JOSHUA|NICOLA|ANDREA|BATISTA)$/.test(pren)) return 'F';
  if (/[OSRLUIEMNZ]$/.test(pren)) return 'M';
  return '';
}

function extrairDados(doc, itens) {
  const t = doc.orig.replace(JUNTA_HIFEN, '$1$2'), it = k => itens.find(i => i.key === k) || {};
  const nomeIt = it('nome'), solteiro = (nomeIt.nomes || []).map(semAcento);
  const todos = nomesNoTexto(t).filter(n => !solteiro.includes(semAcento(n.v)));
  // 1º: partes rotuladas (cabeçalho do PJe/eproc ou da sentença)
  let partes = [];
  const rot = [];
  const reRot1 = /(?:^|\n)[ \t]*(?:-\s*)?([A-ZÀ-ÖØ-Ý][A-ZÀ-ÖØ-Ý' ]{5,80}?)\s*\((?:REQUERENTE|REQUERID[OA](?:\(A\))?|AUTOR(?:\(A\))?|AUTORA|R[ÉE]U|R[ÉE]|INTERESSAD[OA](?:\(A\))?|EXEQUENTE|EXECUTAD[OA])\)/g;
  const reRot2 = /\b(?:REQUERENTES?|REQUERID[OA]S?(?:\(A\))?|AUTORA?|R[ÉE]U|R[ÉE]|INTERESSAD[OA]S?)\s*:\s*([A-ZÀ-ÖØ-Ý][A-ZÀ-ÖØ-Ý' ]{5,80})/g;
  for (const re of [reRot2, reRot1]) { let m; while ((m = re.exec(t)) !== null) { const v = m[1].replace(/\s+/g, ' ').trim(); if (!STOP.test(v) && v.split(' ').length >= 2) rot.push(v); } }
  const rotU = [...new Set(rot.map(v => v.toUpperCase()))].filter(v => !solteiro.includes(semAcento(v)));
  if (rotU.length >= 2) partes = rotU.slice(0, 2);
  // 2º: par "NOME e NOME"
  for (let i = 0; i + 1 < todos.length; i++) {
    if (partes.length) break;
    if (/^\s*,?\s*[eE]\s*$/.test(t.slice(todos[i].e, todos[i + 1].s))) { partes = [todos[i].v, todos[i + 1].v]; break; }
  }
  if (!partes.length) {
    const anc = /requerent|requerid|autor|\bre\b|reu\b|divorci|casal|entre|conjuge|varao|varoa|partes|interessad/;
    const ancorados = todos.filter(n => anc.test(semAcento(t.slice(Math.max(0, n.s - 90), n.s)).toLowerCase()));
    const lista = (ancorados.length >= 2 ? ancorados : todos).map(n => n.v);
    partes = [...new Set(lista)].slice(0, 2);
  }
  // nome de solteiro: põe na ordem das partes (ex.: a que volta ao nome é a parte cujo 1º nome bate)
  let nomes = nomeIt.nomes || [];
  let quem = nomeIt.quem || '';
  if (quem === 'ela' || quem === 'ele') nomes = nomes.slice(0, 1);
  // livro / folha / termo
  let livro = '', folha = '', termo = '';
  // procura "livro" perto de "casamento" e fica com a menção mais completa (livro + folha + termo)
  let melhorLFT = null;
  for (const m of t.matchAll(/\blivro\s*(?:n[º°o.]*\s*)?[:.]?\s*((?:[A-Z]{1,2}\s*-?\s*)?\d+(?:\s*-?\s*[A-Z](?![\wÀ-ÿ]))?)/gi)) {
    const ctx = semAcento(t.slice(Math.max(0, m.index - 260), m.index + 60));
    const iCas = Math.max(ctx.lastIndexOf('CASAMENTO'), ctx.lastIndexOf('ASSENTO'), ctx.lastIndexOf('MATRIMON')), iNasc = ctx.lastIndexOf('NASCIMENTO');
    if (!(iCas >= 0 && iCas > iNasc)) continue;
    const depois = t.slice(m.index, m.index + 140);
    const mF = /\b(?:folhas?|fls?\.?|f\.)\s*(?:n[º°o.]*\s*)?[:.]?\s*(\d+\s*(?:v(?:erso)?\b)?)/i.exec(depois);
    const mT = /\btermo\s*(?:n[º°o.]*\s*)?[:.]?\s*(\d[\d.]*\d|\d)/i.exec(depois);
    const fim = Math.max(m[0].length, mF ? mF.index + mF[0].length : 0, mT ? mT.index + mT[0].length : 0);
    const c = { livro: m[1].replace(/\s+/g, '').toUpperCase(), folha: mF ? mF[1].replace(/\s+/g, '') : '', termo: mT ? mT[1] : '', trecho: t.slice(m.index, m.index + fim) };
    c.score = 1 + (c.folha ? 1 : 0) + (c.termo ? 1 : 0);
    if (!melhorLFT || c.score > melhorLFT.score) melhorLFT = c;
  }
  let trechoLFT = '';
  if (melhorLFT) ({ livro, folha, termo, trecho: trechoLFT } = melhorLFT);
  const mat = t.match(/\b(\d{6})\s?(\d{2})\s?(\d{2})\s?(\d{4})\s?(\d)\s?(\d{5})\s?(\d{3})\s?(\d{7})\s?(\d{2})\b/);
  if (mat) {
    const pref = { '2': 'B', '3': 'B-AUX' }[mat[5]] || '';
    if (!livro) livro = (pref ? pref + '-' : '') + String(+mat[6]);
    if (!folha) folha = String(+mat[7]);
    if (!termo) termo = String(+mat[8]);
  }
  // cidade do cartório onde está o casamento
  let cartorio = '';
  const reC = /\bde\s+([A-ZÀ-Ý][A-Za-zÀ-ÿ]+(?:\s+(?:d[aeo]s?\s+)?[A-ZÀ-Ý][A-Za-zÀ-ÿ]+){0,2})\s*(?:\/|-|–|,)\s*MG\b/g;
  let mc, melhor = null;
  while ((mc = reC.exec(t)) !== null) {
    const antes = semAcento(t.slice(Math.max(0, mc.index - 160), mc.index));
    const score = /CARTORIO|REGISTRO CIVIL|SERVENTIA|OFICIO|CASAMENTO/.test(antes) ? 2 : /COMARCA/.test(antes) ? 0 : 1;
    if (!melhor || score > melhor.score) melhor = { v: mc[1], score };
  }
  if (melhor && melhor.score > 0) cartorio = titulo(melhor.v);
  const proc = t.match(/\b\d{7}\s?-\s?\d{2}\s?\.\s?\d{4}\s?\.\s?\d\s?\.\s?\d{2}\s?\.\s?\d{4}\b/);
  // contraente 1 = homem, contraente 2 = mulher
  partes = partes.map(p => p.toUpperCase());
  if (partes.length === 2) {
    const sx = partes.map((p, i) => sexo(t, p, nomeIt, nomes, partes[1 - i]));
    if ((sx[0] === 'F' && sx[1] !== 'F') || (sx[1] === 'M' && sx[0] !== 'M')) partes.reverse();
  }
  // nomes de solteiro na mesma ordem (homem, mulher)
  if (nomes.length >= 2 && partes.length === 2) {
    const p1 = semAcento(partes[0]).split(' ')[0];
    if (semAcento(nomes[1]).split(' ')[0] === p1) nomes = [nomes[1], nomes[0]];
  }
  return {
    partes, nomes: nomes.map(n => n.toUpperCase()), quem,
    bens: it('partilha').status === 'ok' ? it('partilha').tipo : '',
    processo: proc ? proc[0].replace(/\s/g, '') : '', livro, folha, termo, cartorio, trechoLFT
  };
}

function pecasCitadas(raw) { const doc = prepare(raw); segmentar(doc); return [...doc.citados]; }

if (typeof module !== 'undefined') module.exports = { analisar, prepare, extrairDados, pecasCitadas };
