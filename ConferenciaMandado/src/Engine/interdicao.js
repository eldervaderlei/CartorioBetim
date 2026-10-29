// ===== Conferência de mandado/sentença de INTERDIÇÃO (curatela) =====
// Usa as funções básicas de divorcio.js (prepare, findAll, snippet, segmentar, checkTransito, checkJustica…),
// que são carregadas antes deste arquivo.

// ---------- 1. Sentença ----------
function intSentenca(doc) {
  const titulo = findAll(doc, /(?:^|\s)sentenca(?:\s|$|\.|:)/g).filter(h => rankOf(doc, h) === 0);
  const julgo = findAll(doc, /\bjulgo\s+(?:parcialmente\s+)?procedente\w*|\bisto\s+posto\b|\bante\s+o\s+exposto\b|\bdiante\s+do\s+exposto\b|\bpelo\s+exposto\b/g);
  if (doc.temSentenca) return { status: 'ok', summary: 'O documento contém a sentença.', hits: uniqHits([...titulo, ...julgo.filter(h => rankOf(doc, h) === 0)]) };
  if (julgo.length) return { status: 'ok', summary: 'Encontrei a parte decisória da sentença (“julgo procedente…”).', hits: julgo };
  const mencao = findAll(doc, /\bsentenca\b/g);
  if (mencao.length) return { status: 'warn', summary: 'A sentença só é citada (ex.: no mandado), mas não achei o texto da sentença. A sentença é obrigatória: confira se ela veio junto.', hits: mencao };
  return { status: 'missing', summary: 'Não encontrei a sentença. Ela é obrigatória para o registro da interdição.', hits: [] };
}

// ---------- 2. Decreta a interdição ----------
function intDecreto(doc) {
  const re = /\b(?:decreto|decretar|decretando|decretada|decretado|decretou|declaro|declarar|declarando)\b[^.;]{0,60}?\b(?:interdicao|curatela)\b|\binterdicao\b[^.;]{0,40}?\bdecretad\w*|\b(?:julgo|julgar)\s+(?:parcialmente\s+)?procedente\w*[^.;]{0,200}?\b(?:interdicao|interditand\w*|interditad\w*|curatela|curateland\w*|curatelad\w*)|\b(?:submeto|sujeito|submetendo|submeter)\b[^.;]{0,60}?\bcuratela\b|\bnomeio\b[^.;]{0,140}?\bcurador\w*/g;
  const hits = findAll(doc, re);
  if (!hits.length) return { status: 'missing', summary: 'Não encontrei a decretação da interdição/curatela (ex.: “decreto a interdição”, “julgo procedente… curatela”, “nomeio curador”).', hits: [] };
  const r0 = minRank(doc, hits);
  if (doc.temSentenca && r0 >= 2) return { status: 'warn', summary: 'A interdição só aparece como pedido (ex.: na petição). Confira se a sentença decreta a interdição.', hits: porRank(doc, hits) };
  const limite = findAll(doc, /\b(?:atos?|natureza)\b[^.;]{0,30}?\b(?:patrimoni\w*|negoci\w*)\b|\bart\w*\.?\s*85\b/g).filter(h => rankOf(doc, h) <= 1);
  return { status: 'ok', summary: 'A sentença decreta a interdição/curatela.', note: limite.length ? 'Atenção aos limites da curatela (ex.: só atos de natureza patrimonial e negocial). Confira o alcance para o registro.' : '', hits: porRank(doc, hits) };
}

// ---------- 3. Força de mandado ou mandado do juiz ----------
function intMandado(doc) {
  const forca = findAll(doc, /forca de (?:oficio\s*\/\s*)?mandado|\b(?:servira|serve|valera|vale|servindo|sirva|valendo|funcionara|servir|valer)\b(?:\s+[^\s.;]+){0,8}?\s+(?:como|de)\s+(?:oficio\s*\/\s*)?mandado/g);
  const expedir = /expe\w*(?:-se)?\s+(?:\S+\s+){0,4}$/;
  const titulo = findAll(doc, /\bmandado\s+(?:judicial\s+)?(?:de\s+)?(?:registro|inscricao|averbacao)\b|\bmandado\s+de\s+(?:interdicao|curatela)\b/g)
    .filter(h => !expedir.test(doc.norm.slice(Math.max(0, h.s - 40), h.s)));
  const forcaSent = forca.filter(h => rankOf(doc, h) <= 1);
  if (forcaSent.length || (forca.length && !titulo.length))
    return { status: 'ok', summary: 'A sentença tem força de mandado. Por isso, o termo de compromisso do curador também é necessário.', hits: forca, tipo: 'forca' };
  if (titulo.length) return { status: 'ok', summary: 'Há mandado do juiz para o registro da interdição.', hits: titulo, tipo: 'mandado' };
  const expeca = findAll(doc, /\bexpe\w*(?:-se)?\s+(?:\S+\s+){0,4}?mandado/g);
  if (expeca.length) return { status: 'warn', summary: 'A sentença não tem força de mandado e só determina que o mandado seja expedido. É necessário o mandado assinado pelo juiz.', hits: expeca, tipo: '' };
  return { status: 'missing', summary: 'A sentença não tem força de mandado e não encontrei o mandado do juiz. Um dos dois é necessário.', hits: [], tipo: '' };
}

// ---------- 4. Termo de compromisso ----------
function intTermo(doc, mand) {
  const hits = findAll(doc, /\btermo\s+de\s+(?:compromisso|curatela)\b|\bcompromisso\s+de\s+curador\w*|\b(?:prestou|prestado|presta|assinou|firmou)\s+(?:o\s+)?compromisso\b|\bcompromisso\s+(?:legal\s+)?de\s+bem\s+e\s+fielmente\b|\bbem\s+e\s+fielmente\s+(?:desempenhar|exercer|cumprir)/g);
  const futuro = /\b(?:lavre-se|lavrar|expeca-se|expedir|intime-se|intimem-se|prestar|para\s+(?:assinar|firmar)|devera|apos|mediante|aguarde-se|providencie)\b[^.;]{0,40}$/;
  const reais = hits.filter(h => !futuro.test(doc.norm.slice(Math.max(0, h.s - 60), h.s)));
  const exigido = mand.tipo === 'forca' || !mand.tipo;
  if (reais.length) return { status: 'ok', summary: 'Termo de compromisso do curador encontrado.', hits: reais };
  if (!exigido) return { status: 'na', summary: 'Não exigido: a sentença não tem força de mandado e há mandado do juiz.' + (hits.length ? ' (O termo é citado no processo.)' : ''), hits };
  if (hits.length) return { status: 'warn', summary: 'O termo de compromisso só aparece como providência futura (ex.: “lavre-se o termo de compromisso”). Como a sentença tem força de mandado, é necessário o termo assinado.', hits };
  return { status: 'missing', summary: 'Não encontrei o termo de compromisso do curador. Ele é necessário porque a sentença tem força de mandado.', hits: [] };
}

// ---------- 5 e 6. Qualificação do curador e do interditado ----------
const Q_NAC = /\b(?:brasileir[oa]s?|estrangeir[oa]|portugues[a]?|naturalizad[oa]|argentin[oa]|paraguai[oa]|venezuelan[oa]|haitian[oa]|bolivian[oa]|colombian[oa])\b/;
const Q_EC = /\b(?:solteir[oa]|casad[oa]|divorciad[oa]|viuv[oa]|separad[oa](?:\s+judicialmente)?|em\s+uniao\s+estavel|convivente|companheir[oa])\b/;
const Q_END = /\b(?:residente|residentes|domiciliad[oa]s?|reside|residindo|endereco|morador[a]?|com\s+endereco)\b/;
const Q_LOGR = /\b(?:rua|r\.|avenida|av\.?|travessa|alameda|praca|rodovia|estrada|beco|via|quadra|bairro|logradouro)\b/;
const Q_NAO_PROF = /brasileir|estrangeir|naturalizad|solteir|casad|divorciad|viuv|separad|uniao|convivente|companheir|portador|inscrit|cpf|\brg\b|cedula|identidade|carteira|nascid|natural|filh[oa]|residente|domiciliad|reside|endereco|rua|avenida|maior|menor|capaz|incapaz|idos[oa]|alfabetizad|\d|neste ato|representad|assistid|nos autos|qualificad|abaixo|acima|e-?mail|telefone|\/mg|\bmg\b|betim|bairro|centro|\bn[o.º]|nacionalidade|estado civil|curador|interdit|requerent|requerid|autor|\bre\b/;
const Q_PROF_CONHECIDA = /\b(?:aposentad[oa]|pensionista|do\s+lar|desempregad[oa]|estudante|autonom[oa]|comerciante|servidor[a]?\s+public[oa]|professor[a]?|pedreiro|motorista|domestica|diarista|vendedor[a]?|agricultor[a]?|lavrador[a]?|empresari[oa]|advogad[oa]|enfermeir[oa]|tecnic[oa]\b[^,;]{0,25}|operador[a]?\b[^,;]{0,25}|auxiliar\b[^,;]{0,25}|sem\s+profissao(?:\s+definida)?|incapaz\s+para\s+o\s+trabalho)\b/;
const Q_PAPEL_CURADOR = /\bcurador[a]?\b/;
const Q_PAPEL_INTERD = /\b(?:interditand[oa]|interditad[oa]|curatelad[oa]|curateland[oa])\b/;
const SEM_PAPEL = /^(?:(?:O|A|AO|À|DO|DA|SR|SRA)\s+)?(?:CURADOR[A]?|INTERDITAND[OA]|INTERDITAD[OA]|CURATELAD[OA]|CURATELAND[OA]|REQUERENTE|REQUERID[OA]|AUTOR[A]?)\s*:?\s+/;

function chaveNome(n) { return semAcento(n).replace(SEM_PAPEL, '').split(' ').filter(w => w.length > 2).slice(0, 2).join(' '); }

// trechos do tipo "NOME, brasileira, solteira, do lar, residente na Rua…, Betim/MG"
function blocosQualificacao(doc) {
  const t = doc.orig, out = [];
  for (const n of nomesNoTexto(t)) {
    const nome = n.v.replace(SEM_PAPEL, '').trim();
    if (nome.split(' ').filter(w => w.length > 2).length < 2) continue;
    const depois = t.slice(n.e, n.e + 700);
    if (!/^\s*(?:,|\(|–|-|\s(?:brasileir|portador|solteir|casad|viuv|divorciad))/i.test(depois)) continue;
    const dn = normTxt(depois);
    if (!(Q_NAC.test(dn.slice(0, 260)) || Q_EC.test(dn.slice(0, 260)) || /portador|inscrit|cpf/.test(dn.slice(0, 260)))) continue;
    // fim do bloco: fim do endereço ou da frase
    let fim = Math.min(depois.length, 520);
    const iEnd = depois.search(/residente|domiciliad|reside\b|endere[cç]o|morador/i);
    const corte = depois.slice(iEnd >= 0 ? iEnd : 0).search(/\.\s+[A-ZÀ-Ý]|;|\bneste ato\b|\bv[eê]m\b|\bpor (?:seu|sua|meio)\b|\bnos autos\b|\bpropo\w*|\bajuiz\w*|\bem face d|\bcontra\b|\brequer\w*\b|\bnomead\w*|\n\s*\n/i);
    if (corte >= 0) fim = Math.min(fim, (iEnd >= 0 ? iEnd : 0) + corte + 1);
    const bloco = depois.slice(0, fim);
    const bn = normTxt(bloco);
    const campos = { nacionalidade: Q_NAC.test(bn), estado: Q_EC.test(bn), profissao: false, endereco: false, betim: false };
    const mp = bn.match(/\b(?:profissao|ocupacao)\s*:?\s*([^,;]{3,40})/);
    if (mp || Q_PROF_CONHECIDA.test(bn)) campos.profissao = true;
    else {
      // só o que vem antes do endereço
      const ate = bn.search(Q_END);
      const partes = (ate >= 0 ? bn.slice(0, ate) : bn).split(/[,;]/).slice(1, 8).map(p => p.trim());
      if (partes.some(p => p.length >= 3 && p.length <= 40 && p.split(' ').length <= 5 && !Q_NAO_PROF.test(p))) campos.profissao = true;
    }
    const ie = bn.search(Q_END);
    if (ie >= 0) {
      const end = bn.slice(ie, ie + 260);
      campos.endereco = Q_LOGR.test(end) || /\bn[o.º°]?\s*\d+/.test(end);
      campos.betim = /\bbetim\b/.test(end);
      campos.trechoEnd = end;
    }
    const antes = normTxt(t.slice(Math.max(0, n.s - 140), n.s));
    const s = n.s, e = n.e + fim;
    out.push({ nome: nome.replace(/\s+/g, ' '), chave: chaveNome(nome), s, e, campos, antes, apos: normTxt(t.slice(e, e + 80)),
      h: { s: origToNorm(doc, s), e: origToNorm(doc, e) } });
  }
  return out;
}

// nomes ligados a um papel (curador / interditado) nas frases da sentença ou do mandado
function nomesDoPapel(doc, rePapel) {
  const t = doc.orig, todos = nomesNoTexto(t), cont = {};
  for (const h of findAll(doc, new RegExp(rePapel.source, 'g'))) {
    const [os, oe] = origRange(doc, h);
    // o nome mais perto do papel, na mesma frase
    let melhor = null, dist = 1e9;
    for (const n of todos) {
      const depois = n.s >= oe;
      let d = depois ? n.s - oe : os >= n.e ? os - n.e : 0;
      if (d > 140) continue;
      const entre = t.slice(Math.min(oe, n.e), Math.max(os, n.s));
      if (/[.;]\s/.test(entre)) continue;
      // "nomeio curador FULANO": o nome costuma vir depois do papel; antes, só em "FULANO como curador"
      if (!depois && !/^\s*,?\s*(?:como|na\s+qualidade\s+de|para\s+(?:o\s+)?cargo\s+de)?\s*(?:sua|seu)?\s*$/i.test(normTxt(entre).replace(/\b(?:o|a)\b/g, ''))) d += 60;
      if (d < dist) { dist = d; melhor = n; }
    }
    if (melhor) { const k = chaveNome(melhor.v); if (k) cont[k] = (cont[k] || 0) + (rankOf(doc, h) === 0 ? 3 : 1); }
  }
  return Object.entries(cont).sort((a, b) => b[1] - a[1]).map(x => x[0]);
}

function intQualificacao(doc, papel, blocos, nomesPapel, nomesOutro) {
  const rePapel = papel === 'curador' ? Q_PAPEL_CURADOR : Q_PAPEL_INTERD;
  // a mesma pessoa não pode ser curador e interditado: o nome mais citado no outro papel fica de fora
  const meus = nomesPapel.filter(n => n !== nomesOutro[0] || nomesPapel[0] === n && nomesOutro.length === 0);
  let cand = blocos.filter(b => meus.includes(b.chave));
  if (!cand.length) cand = blocos.filter(b => rePapel.test(b.antes.slice(-90)) || rePapel.test(b.apos.slice(0, 40)));
  const pts = b => ['nacionalidade', 'estado', 'profissao', 'endereco'].filter(k => b.campos[k]).length + (b.campos.betim ? 1 : 0);
  cand.sort((a, b) => pts(b) - pts(a) || rankOf(doc, a.h) - rankOf(doc, b.h));
  const rot = papel === 'curador' ? 'do curador' : 'do interditado';
  if (!cand.length) {
    if (nomesPapel.length) return { status: 'warn', summary: 'Encontrei o nome ' + rot + ', mas não a qualificação (nacionalidade, estado civil, profissão e endereço).', hits: findAll(doc, new RegExp(rePapel.source, 'g')).slice(0, 3), nome: '' };
    return { status: 'missing', summary: 'Não encontrei a qualificação ' + rot + '.', hits: [], nome: '' };
  }
  const b = cand[0], c = b.campos, falta = [];
  if (!c.nacionalidade) falta.push('nacionalidade');
  if (!c.estado) falta.push('estado civil');
  if (!c.profissao) falta.push('profissão');
  if (!c.endereco) falta.push('endereço');
  else if (!c.betim) falta.push('endereço em Betim');
  const nome = b.nome.toUpperCase();
  if (!falta.length) return { status: 'ok', summary: 'Qualificação completa: ' + nome + ' (nome, nacionalidade, estado civil, profissão e endereço em Betim).', hits: [b.h], nome };
  const endFora = c.endereco && !c.betim;
  return { status: 'warn', summary: nome + ': falta ' + falta.join(', ') + '.' + (endFora ? ' O endereço encontrado não é de Betim (ou a cidade não foi informada).' : ''), hits: [b.h], nome };
}

// ---------- 9. Assinatura eletrônica ----------
function intAssinatura(doc, ctx) {
  const ass = findAll(doc, /assinad\w*\s+(?:eletronicamente|digitalmente)|assinatura\s+(?:eletronica|digital)|documento\s+assinado\s+eletronicamente|codigo\s+(?:de\s+)?verifica\w*|chave\s+de\s+acesso|\bvalidar\b[^.;]{0,40}?(?:documento|autenticidade)|conferencia\s+de\s+documento/g);
  if (ass.length) return { status: 'ok', summary: 'Assinatura eletrônica / código de verificação encontrados.', hits: melhores(doc, ass).length ? porRank(doc, ass) : ass };
  if (ctx && ctx.digital === false) return { status: 'na', summary: 'Documento escaneado (papel): confira a assinatura do juiz no original.', hits: [] };
  return { status: 'missing', summary: 'O documento é digital, mas não encontrei a assinatura eletrônica nem o código de verificação.', hits: [] };
}

function conferir(raw, ctx) {
  const doc = prepare(raw);
  segmentar(doc);
  const mand = intMandado(doc);
  const blocos = blocosQualificacao(doc);
  const nCur = nomesDoPapel(doc, /\bcurador[a]?\b/), nInt = nomesDoPapel(doc, /\b(?:interditand[oa]|interditad[oa]|curatelad[oa]|curateland[oa]|interdicao\s+de)\b/);
  const jg = checkJustica(doc);
  const just = { status: jg.status === 'sim' ? 'ok' : jg.status === 'nao' ? 'missing' : 'warn', summary: jg.summary, hits: jg.hits };
  const itens = [
    { key: 'sentenca', label: 'Sentença', ...intSentenca(doc) },
    { key: 'decreto', label: 'Decreta a interdição', ...intDecreto(doc) },
    { key: 'mandado', label: 'Força de mandado ou mandado do juiz', ...mand },
    { key: 'termo', label: 'Termo de compromisso', ...intTermo(doc, mand) },
    { key: 'curador', label: 'Qualificação do curador', ...intQualificacao(doc, 'curador', blocos, nCur, nInt) },
    { key: 'interditado', label: 'Qualificação do interditado', ...intQualificacao(doc, 'interditado', blocos, nInt, nCur) },
    { key: 'transito', label: 'Trânsito em julgado', ...checkTransito(doc) },
    { key: 'justica', label: 'Justiça gratuita', ...just },
    { key: 'assinatura', label: 'Assinatura eletrônica', ...intAssinatura(doc, ctx) }
  ];
  for (const it of itens) { it.hits = porRank(doc, uniqHits(it.hits.slice())); trechos(doc, it, 2); }

  const marcas = [];
  const add = (cat, h) => { const [a, b] = origRange(doc, h); marcas.push({ cat, a, b, page: paginaDe(doc, a) }); };
  for (const it of itens) for (const h of it.best.slice(0, 6)) add(it.key, h);

  const extras = [];
  const proc = doc.orig.match(/\b\d{7}\s?-\s?\d{2}\s?\.\s?\d{4}\s?\.\s?\d\s?\.\s?\d{2}\s?\.\s?\d{4}\b/g);
  extras.push({ label: 'Número do processo', found: !!proc, value: proc ? [...new Set(proc.map(p => p.replace(/\s/g, '')))].join(', ') : '' });
  const vara = findAll(doc, /\b\d+\s*[ªa]?\s*vara\b|\bvara\s+(?:de|da|do|civel|unica)\b[^.;]{0,50}/g);
  let varaVal = '';
  if (vara.length) {
    const [os] = origRange(doc, vara[0]);
    const ls = doc.orig.lastIndexOf('\n', os) + 1; let le = doc.orig.indexOf('\n', os); if (le < 0) le = doc.orig.length;
    varaVal = doc.orig.slice(Math.max(ls, os - 40), Math.min(le, os + 90)).replace(/\s+/g, ' ').trim();
  }
  extras.push({ label: 'Vara / juízo', found: vara.length > 0, value: varaVal });
  const cur = itens[4].nome, itd = itens[5].nome;
  extras.push({ label: 'Curador(a)', found: !!cur, value: cur });
  extras.push({ label: 'Interditado(a)', found: !!itd, value: itd });
  return { doc, itens, justica: null, extras, dados: { processo: proc ? proc[0].replace(/\s/g, '') : '', partes: [cur, itd].filter(Boolean) }, marcas };
}

if (typeof module !== 'undefined') module.exports = Object.assign(module.exports || {}, { conferir });
