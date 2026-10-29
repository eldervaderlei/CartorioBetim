// ===== Conferência de mandado/sentença de INTERDIÇÃO (curatela) =====
// Usa as funções básicas de divorcio.js (prepare, findAll, snippet, segmentar, checkTransito, checkJustica…),
// que são carregadas antes deste arquivo.

// ---------- 1. Sentença ----------
// a peça "SENTENÇA" (título sozinho na linha) seguida da decisão; "diante do exposto" de uma petição não conta
function intSentenca(doc) {
  const t = doc.orig, hits = [];
  const reTit = /(?:^|\n)[ \t]*(?:S[ \t]*E[ \t]*N[ \t]*T[ \t]*E[ \t]*N[ \t]*[CÇ][ \t]*A|SENTEN[CÇ]A)[ \t]*(?:\n|$)/g;
  let m;
  while ((m = reTit.exec(t)) !== null) {
    const ini = m.index + m[0].search(/S/);
    const corpo = normTxt(t.slice(ini, ini + 20000));
    if (/\b(?:julgo|julgar|decreto|decretar|homologo|nomeio|nomeando|nomear|defiro|acolho)\b/.test(corpo.slice(0, 15000)))
      hits.push({ s: origToNorm(doc, ini), e: origToNorm(doc, ini + m[0].trim().length) });
  }
  const decisao = findAll(doc, /\bjulgo\s+(?:parcialmente\s+)?procedente\w*|\bacolho\s+o\s+parecer\b[^.;]{0,40}?\bjulgo\b/g);
  if (hits.length) return { status: 'ok', summary: 'O documento contém a sentença.', hits: uniqHits([...hits, ...decisao]) };
  if (decisao.length) return { status: 'warn', summary: 'Encontrei a parte decisória (“julgo procedente…”), mas não identifiquei a peça da sentença. Confira se a sentença completa veio junto.', hits: decisao };
  const mencao = findAll(doc, /\bsentenca\b[^.;]{0,80}?\b(?:interdicao|curatela|declaratoria|datada|proferida)\b/g);
  if (mencao.length) return { status: 'missing', summary: 'A sentença só é citada (ex.: no mandado), mas não veio junto. A sentença é obrigatória.', hits: mencao };
  return { status: 'missing', summary: 'Não encontrei a sentença. Ela é obrigatória para o registro da interdição.', hits: [] };
}

// ---------- 2. Decreta a interdição ----------
function intDecreto(doc) {
  const re = /\b(?:decreto|decretar|decretando|decretada|decretado|decretou|declaro|declarar|declarando|deferir|defiro)\b[^.;]{0,60}?\b(?:interdicao|curatela)\b|\binterdicao\b[^.;]{0,40}?\bdecretad\w*|\b(?:julgo|julgar)\s+(?:parcialmente\s+)?procedente\w*[^.;]{0,200}?\b(?:interdicao|interditand\w*|interditad\w*|curatela|curateland\w*|curatelad\w*)|\b(?:submeto|sujeito|submetendo|submeter)\b[^.;]{0,60}?\bcuratela\b|\bnomeio\b[^.;]{0,140}?\bcurador\w*/g;
  const hits = findAll(doc, re);
  if (!hits.length) {
    const cit = findAll(doc, /\b(?:sentenca|decisao)\b[^.;]{0,100}?\b(?:declaratoria\s+de\s+)?(?:interdicao|curatela)\b/g);
    if (cit.length) return { status: 'warn', summary: 'Outro documento (ex.: o mandado) cita a sentença de interdição, mas não encontrei o texto da sentença que a decreta.', hits: cit };
    return { status: 'missing', summary: 'Não encontrei a decretação da interdição/curatela (ex.: “decreto a interdição”, “julgo procedente… curatela”, “nomeio curador”).', hits: [] };
  }
  const r0 = minRank(doc, hits);
  if (doc.temSentenca && r0 >= 2) return { status: 'warn', summary: 'A interdição só aparece como pedido (ex.: na petição). Confira se a sentença decreta a interdição.', hits: porRank(doc, hits) };
  const limite = findAll(doc, /\b(?:atos?|natureza)\b[^.;]{0,30}?\b(?:patrimoni\w*|negoci\w*)\b|\bart\w*\.?\s*85\b/g).filter(h => rankOf(doc, h) <= 1);
  return { status: 'ok', summary: 'A sentença decreta a interdição/curatela.', note: limite.length ? 'Atenção aos limites da curatela (ex.: só atos de natureza patrimonial e negocial). Confira o alcance para o registro.' : '', hits: porRank(doc, hits) };
}

// ---------- 3. Força de mandado ou mandado do juiz ----------
function intMandado(doc) {
  const forca = findAll(doc, /forca de (?:oficio\s*\/\s*)?mandado|\b(?:servira|serve|valera|vale|servindo|sirva|valendo|funcionara|servir|valer)\b(?:\s+[^\s.;]+){0,8}?\s+(?:como|de)\s+(?:oficio\s*\/\s*)?mandado/g);
  const expedir = /expe\w*(?:-se)?\s+(?:\S+\s+){0,4}$/;
  const titulo = findAll(doc, /\bmandado\s+(?:judicial\s+)?(?:de\s+)?(?:registro|inscricao|averbacao)\b|\bmandado\s+de\s+(?:interdicao|curatela)\b|\bm\s?a\s?n\s?d\s?a\s+ao\s+(?:oficial|cartorio)\b/g)
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
const Q_LOGR = /\b(?:rua|r\.|avenida|av\.?|travessa|alameda|praca|rodovia|estrada|beco|via|quadra|bairro|logradouro)\b|\brua(?=[a-z])/;
const Q_BETIM = /b[eé]t[il1|]m/;
const Q_NAO_PROF = /brasileir|estrangeir|naturalizad|solteir|casad|divorciad|viuv|separad|uniao|convivente|companheir|portador|inscrit|cpf|\brg\b|cedula|identidade|carteira|nascid|natural|filh[oa]|residente|domiciliad|reside|endereco|rua|avenida|maior|menor|capaz|incapaz|idos[oa]|alfabetizad|\d|neste ato|representad|assistid|nos autos|qualificad|abaixo|acima|e-?mail|telefone|\/mg|\bmg\b|betim|bairro|centro|\bn[o.º]|nacionalidade|estado civil|curador|interdit|requerent|requerid|autor|\bre\b|expedid|policia|ssp|cadastrad|regime|comunhao|bens|registro|geral|c\.?p\.?f|sra?\b|senhor/;
const Q_PROF_CONHECIDA = /\b(?:aposentad[oa]|pensionista|do\s+lar|desempregad[oa]|estudante|autonom[oa]|comerciante|servidor[a]?\s+public[oa]|professor[a]?|pedreiro|motorista|domestica|diarista|vendedor[a]?|agricultor[a]?|lavrador[a]?|empresari[oa]|advogad[oa]|enfermeir[oa]|cuidador[a]?|costureir[oa]|cozinheir[oa]|faxineir[oa]|mecanic[oa]|eletricista|pintor[a]?|tecnic[oa]\b[^,;]{0,25}|operador[a]?\b[^,;]{0,25}|auxiliar\b[^,;]{0,25}|assistente\b[^,;]{0,25}|analista\b[^,;]{0,25}|sem\s+profissao(?:\s+definida)?|sem\s+ocupacao)\b/;

// nome de pessoa: palavras com inicial maiúscula (em maiúsculas ou não), com "da/de/do/dos/das" no meio
const N_PAL = "[A-ZÀ-ÖØ-Ý][A-Za-zÀ-ÖØ-öø-ÿ']+";
const N_FONTE = N_PAL + "(?:[ \\t]*\\n?[ \\t]*(?:(?:d[aeo]s?|D[AEO]S?)[ \\t]*\\n?[ \\t]+)?" + N_PAL + "){1,8}";
const N_RE_INI = new RegExp("^(?:" + N_FONTE + ")");
const N_RE_G = new RegExp(N_FONTE, "g");
const NAO_NOME = /\b(?:COM|PEDIDO|CURATELA|INTERDICAO|INTERDITACAO|ACAO|TUTELA|URGENCIA|PROVISORIA|DEFINITIVA|DEFINITIVO|CIVEL|JUIZO|JUIZ|JUIZA|VARA|FAMILIA|COMARCA|BETIM|MINAS|GERAIS|ESTADO|PODER|JUDICIARIO|TRIBUNAL|JUSTICA|DIREITO|CODIGO|CIVIL|LEI|PROCESSO|SENTENCA|MANDADO|CERTIDAO|CERTIFICO|TERMO|MINISTERIO|PUBLICO|DEFENSORIA|CARTORIO|REGISTRO|OFICIO|SECRETARIA|DOUTOR|DOUTORA|EXCELENTISSIMO|EXCELENTISSIMA|SENHOR|SENHORA|REQUERENTE|REQUERIDO|REQUERIDA|AUTOR|AUTORA|REU|INTERDITANDO|INTERDITANDA|INTERDITADO|INTERDITADA|CURADOR|CURADORA|PRIORIDADE|TRAMITACAO|DIGITAL|SUCESSOES|AUSENCIA|CPF|MG|ID|PJE|EPROC|CONSTITUICAO|REPUBLICA|BRASIL|RUA|AVENIDA|BAIRRO|CENTRO|CEP|NUM|PAG|ASSINADO|DOCUMENTO|DECISAO|DESPACHO|EDITAL|INSCRICAO|DECLARATORIA|ART|ARTIGO|INCISO|CASA|REPOUSO|INSTITUTO|LAR|PARTES|ADVOGADO|ADVOGADOS|PERITO|FISCAL|CLASSE|ASSUNTO|ORGAO|JULGADOR|VALOR|CAUSA|ESCRIVAO|GERENTE|MATRICULA|COMPROMISSO|COMPROMISSADA|COMPROMISSADO|CAPACIDADE|SINDROME|DOENCA|ALZHEIMER|CID|SRA|SR|DRA|DR|NAO|SIM|MANDA|CUMPRA|DADO|PASSADO|PRIMEIRA|INSTANCIA|FORO|ELEITORAL|OAB|SUBSTITUICAO|NADA|MAIS|AOS|DIAS|MES|SEGUINTE|TEOR|LIVRO|TUTELA|FLS|ENFIM|PODENDO|CUMPRE|ACEITO|EIS|DOS|FATOS|PRELIMINARMENTE|FUNDAMENTOS|PEDIDOS|ATO|ORDINATORIO|CONCLUSAO|JUNTADA|INTIMACAO|CITACAO|VISTA|LAUDO|PARECER|RELATORIO|ESTUDO|SOCIAL|DECLARACAO|PROCURACAO|CONTESTACAO|AUDIENCIA|MANIFESTACAO|PETICAO|INICIAL|EMENDA|ANEXO|LOCAL|DATA|PRAZO|EVENTO|PAGINA|SEGREDO|OUTROS|PARTICIPANTES|FILHA|FILHO|NASCIDA|NASCIDO|BRASILEIRA|BRASILEIRO)\b/;

// "MARIA EUNICE ALVES DE SOUSA\nLocal" -> "MARIA EUNICE ALVES DE SOUSA"; devolve também quantos caracteres do original foram usados
function cortarNome(bruto) {
  const re = /\S+/g; let m, palavras = [], ini = -1, fim = 0, caixaAlta = null;
  const con = w => /^(?:d[aeo]s?|D[AEO]S?)$/.test(w);
  while ((m = re.exec(bruto)) !== null) {
    const w = m[0], u = semAcento(w);
    if (!con(w)) {
      const bloqueada = NAO_NOME.test(u);
      if (bloqueada || (caixaAlta === true && w !== w.toUpperCase())) {
        if (palavras.filter(x => !con(x)).length >= 2) break;
        // palavra antes do nome ("Interdição", "DIGITAL", "Definitiva de"): recomeça depois dela
        palavras = []; ini = -1; caixaAlta = null;
        continue;
      }
      if (palavras.filter(x => !con(x)).length === 1 && caixaAlta === null) caixaAlta = palavras[0] === palavras[0].toUpperCase() && w === w.toUpperCase();
    } else if (!palavras.length) continue;
    if (ini < 0) ini = m.index;
    palavras.push(w); fim = m.index + w.length;
  }
  while (palavras.length && con(palavras[palavras.length - 1])) palavras.pop();
  if (!palavras.length) return { v: '', ini: 0, n: 0 };
  const ultima = palavras[palavras.length - 1];
  fim = bruto.lastIndexOf(ultima, fim) + ultima.length;
  return { v: palavras.join(' '), ini, n: fim };
}

function nomeValido(n) {
  const u = semAcento(n), ws = u.split(/\s+/).filter(w => w.length > 2 && !/^D[AEO]S?$/.test(w));
  return ws.length >= 2 && !NAO_NOME.test(u);
}
function tokensNome(n) { return semAcento(n).split(/\s+/).filter(w => w.length > 2 && !/^D[AEO]S?$/.test(w)); }
function mesmoNome(a, b) {
  const A = tokensNome(a), B = tokensNome(b); if (!A.length || !B.length) return false;
  const inter = A.filter(w => B.includes(w)).length;
  return inter >= 2 && (A[0] === B[0] || inter >= 3 || inter === Math.min(A.length, B.length));
}
// nome logo depois da posição p (pula palavras de ligação como "sua genitora," "a Sra.")
const PULA = /^(?:[\s,.:;(\-–'"“]|\(a\)|(?:o|a|os|as|ao|à|sr|sra|sr\.|sra\.|senhor|senhora|dona|sua|seu|genitora?|genitor|esposa|esposo|marido|mulher|filha|filho|irmã|irmão|irma|irmao|mãe|mae|pai|tia|tio|sobrinha|sobrinho|neta|neto|avó|avô|companheira|companheiro|requerente|requerida|requerido|autora|autor|ré|réu|interditanda|interditando|interditada|interditado|curatelada|curatelado|ora|como|lhe|nomeando-lhe|definitiva|definitivo|provisória|provisório|sra\.|dra\.)(?=[\s,.:;(]))+/i;
function nomeEm(t, p) {
  const resto = t.slice(p, p + 260);
  const pulo = (resto.match(PULA) || [''])[0].length;
  const m = resto.slice(pulo).match(N_RE_INI);
  if (!m) return null;
  const c = cortarNome(m[0]);
  if (!nomeValido(c.v)) return null;
  return { v: c.v, s: p + pulo + c.ini, e: p + pulo + c.n };
}

function papeis(doc) {
  const t = doc.orig, cur = [], itd = [];
  const marca = (lista, n, peso) => { if (n) lista.push({ v: n.v, peso, s: n.s }); };
  const apos = (re, lista, peso, cond) => {
    for (const h of findAll(doc, re)) {
      const [, oe] = origRange(doc, h);
      const n = nomeEm(t, oe);
      if (n && (!cond || cond(n))) marca(lista, n, peso + (rankOf(doc, h) === 0 ? 1 : 0));
    }
  };
  // interditado
  apos(/\b(?:interdicao|interditacao|curatela)\s+(?:definitiva\s+|provisoria\s+)?(?:d[aeo]s?\s+)?(?=\S)/g, itd, 3);
  apos(/\b(?:interditand[oa]|interditad[oa]|curatelad[oa]|curateland[oa]|interdito)(?:\(a\))?\s+(?:sr\.?\s+|sra\.?\s+)?(?=\S)/g, itd, 3);
  apos(/\bem\s+face\s+d[eoa]s?\s+/g, itd, 2);
  apos(/\b(?:reu|re|requerid[oa](?:\(a\))?|interditand[oa](?:\(a\))?)\s*:\s*/g, itd, 2);
  // curador
  apos(/\bcurador[a]?(?:\(a\))?(?:\s+(?:definitiv[oa]|provisori[oa])(?:\(a\))?)?\s+(?!d[aeo]s?\b|de\b|para\b|provisori|definitiv|em\b|ao\b|a\b(?!\s+sra))/g, cur, 3);
  apos(/\b(?:nomeacao|nomear|nomeio|nomeando|nomeada|nomeado)(?:-a|-o|-lhe)?\s+(?:definitivamente\s+)?(?:d[aeo]\s+)?/g, cur, 3, n => /^\W{0,4}(?:\S+\s+){0,3}?como\s+(?:sua\s+|seu\s+)?curador/i.test(normTxt(t.slice(n.e, n.e + 60))) || /^[\s,]*(?:como\s+)?curador/.test(normTxt(t.slice(n.e, n.e + 30))));
  apos(/\b(?:compareceu|comparece|compromissad[oa](?:\(a\))?\s*:?)\s+/g, cur, 3);
  apos(/\bsenhor\(a\)\s+/g, cur, 2, n => /curador/.test(normTxt(t.slice(n.e, n.e + 500))));
  apos(/\b(?:requerente|autor[a]?)\s*:\s*/g, cur, 1);
  apos(/\b(?:formulad[oa]|ajuizad[oa]|propost[oa]|movid[oa])\s+por\s+/g, cur, 1);
  // cabeçalho do PJe: "NOME (REQUERENTE)" / "NOME (REQUERIDO(A))"
  N_RE_G.lastIndex = 0; let m;
  while ((m = N_RE_G.exec(t)) !== null) {
    const c = cortarNome(m[0]), v = c.v; if (!nomeValido(v)) continue;
    const dep = t.slice(m.index + c.n, m.index + c.n + 30);
    if (/^\s*\((?:REQUERID|INTERDITAND|R[ÉE]U|CURATELAND)/i.test(dep)) itd.push({ v, peso: 2, s: m.index + c.ini });
    else if (/^\s*\((?:REQUERENTE|AUTOR|CURADOR)/i.test(dep)) cur.push({ v, peso: 1, s: m.index + c.ini });
  }
  const agrupar = lista => {
    const g = [];
    for (const c of lista) {
      let x = g.find(y => mesmoNome(y.v, c.v));
      if (!x) g.push(x = { v: c.v, peso: 0, nomes: [] });
      x.peso += c.peso; x.nomes.push(c.v);
    }
    // nome exibido: a forma mais repetida (empate: a mais completa)
    for (const x of g) {
      const cont = {}; for (const n of x.nomes) { const k = semAcento(n); cont[k] = (cont[k] || 0) + 1; }
      x.v = x.nomes.slice().sort((a, b) => cont[semAcento(b)] - cont[semAcento(a)] || tokensNome(b).length - tokensNome(a).length)[0];
    }
    return g.sort((a, b) => b.peso - a.peso);
  };
  const gi = agrupar(itd), gc = agrupar(cur);
  let interditado = gi[0] || null;
  let curador = gc.find(c => !interditado || !mesmoNome(c.v, interditado.v)) || null;
  // se o mais citado como curador é o mesmo do interditado e pesa mais lá, troca
  if (interditado && gc[0] && mesmoNome(gc[0].v, interditado.v) && gc[0].peso > interditado.peso * 1.5) {
    curador = gc[0]; interditado = gi.find(i => !mesmoNome(i.v, curador.v)) || null;
  }
  return { curador, interditado };
}

// qualificação logo depois de cada ocorrência do nome da pessoa
const LIXO_LINHA = /^\s*(?:Num\.?\s*\d|Assinado eletronicamente|https?:|N[úu]mero do documento|Processo\s+\d|Digitalizado com|_{5,}|.*OAB\/|Telefone|\f)/i;
function qualificacoes(doc, pessoa) {
  const t = doc.orig, out = [];
  N_RE_G.lastIndex = 0; let m;
  while ((m = N_RE_G.exec(t)) !== null) {
    const c0 = cortarNome(m[0]), v = c0.v;
    if (!nomeValido(v) || !mesmoNome(v, pessoa.v)) continue;
    const fimNome = m.index + c0.n;
    let bruto = t.slice(fimNome, fimNome + 900);
    // tira rodapés e cabeçalhos de página que caem no meio do texto
    const limpo = bruto.split('\n').filter(l => !LIXO_LINHA.test(l)).join('\n').replace(/\f/g, ' ');
    const ln = normTxt(limpo.slice(0, 700));
    if (!/^[\s\-–]*(?:cpf[^,]{0,25})?[,(–\-]/.test(ln) && !/^\s*,/.test(ln)) continue;
    if (!(Q_NAC.test(ln.slice(0, 220)) || Q_EC.test(ln.slice(0, 220)))) continue;
    // fim do trecho: fim de frase (ponto + palavra com inicial maiúscula) ou começo de outra parte da frase
    let fimL = Math.min(limpo.length, 620);
    const cortes = [limpo.slice(5).search(/\.\s+(?=[A-ZÀ-Ý][a-zà-ÿ]{2,})/), limpo.slice(5).search(/;|\bneste ato\b|\bv[eê]m\b|\bpor (?:seu|sua|meio|intermédio)\b|\bnos autos\b|\bpropo\w*|\bajuiz\w*|\bem face d|\bcontra\b|\brequer\w*|\bnomead\w*|\ba quem\b|\bem trâmite\b|\bpelos fatos\b|\bdevendo\b|\bque foi\b|\bpara desempenhar|\bdo\(a\) interdit|\bpara exercer\b|\bdecretando\b/i)].filter(x => x >= 0);
    if (cortes.length) fimL = Math.min(fimL, Math.min(...cortes) + 5);
    const bn = normTxt(limpo.slice(0, fimL));
    const fim = bn.length;
    const c = { nacionalidade: Q_NAC.test(bn), estado: Q_EC.test(bn), profissao: false, endereco: false, betim: false };
    const ie = bn.search(Q_END);
    const antesEnd = ie >= 0 ? bn.slice(0, ie) : bn;
    if (/\b(?:profissao|ocupacao)\s*:?\s*[a-z]/.test(antesEnd) || Q_PROF_CONHECIDA.test(antesEnd)) c.profissao = true;
    else if (antesEnd.split(/[,;]/).slice(1, 9).map(p => p.trim()).some(p => p.length >= 3 && p.length <= 40 && p.split(' ').length <= 5 && !Q_NAO_PROF.test(p))) c.profissao = true;
    if (ie >= 0) {
      const end = bn.slice(ie, ie + 320);
      c.endereco = Q_LOGR.test(end) || /\bn\s*[o.º°]?\s*\d+/.test(end) || /\bcep\b/.test(end);
      c.betim = Q_BETIM.test(end);
    }
    // tamanho do trecho no texto original (aproximado: mesma proporção)
    const fimOrig = fimNome + Math.min(bruto.length, Math.round(fimL * bruto.length / Math.max(1, limpo.length)) + 5);
    out.push({ nome: v, c, h: { s: origToNorm(doc, m.index), e: origToNorm(doc, fimOrig) } });
  }
  return out;
}

function intQualificacao(doc, papel, pessoa) {
  const rot = papel === 'curador' ? 'do curador' : 'do interditado';
  if (!pessoa) return { status: 'missing', summary: 'Não identifiquei quem é o ' + (papel === 'curador' ? 'curador' : 'interditado') + ' nem sua qualificação.', hits: [], nome: '' };
  const nome = pessoa.v.toUpperCase();
  const qs = qualificacoes(doc, pessoa);
  if (!qs.length) return { status: 'missing', summary: nome + ': não encontrei a qualificação ' + rot + ' (nacionalidade, estado civil, profissão e endereço) em nenhum dos documentos.', hits: [], nome };
  const pts = q => ['nacionalidade', 'estado', 'profissao', 'endereco', 'betim'].filter(k => q.c[k]).length;
  qs.sort((a, b) => pts(b) - pts(a) || rankOf(doc, a.h) - rankOf(doc, b.h));
  // junta o que aparece em todas as qualificações da pessoa
  const u = {}; for (const k of ['nacionalidade', 'estado', 'profissao', 'endereco', 'betim']) u[k] = qs.some(q => q.c[k]);
  const falta = [];
  if (!u.nacionalidade) falta.push('nacionalidade');
  if (!u.estado) falta.push('estado civil');
  if (!u.profissao) falta.push('profissão');
  if (!u.endereco) falta.push('endereço');
  else if (!u.betim) falta.push('endereço em Betim');
  const usados = [qs[0]];
  const melhorFalta = ['nacionalidade', 'estado', 'profissao', 'endereco', 'betim'].filter(k => !qs[0].c[k] && u[k]);
  for (const k of melhorFalta) { const q = qs.find(x => x.c[k]); if (q && !usados.includes(q)) usados.push(q); }
  const varias = usados.length > 1 ? ' (juntando ' + usados.length + ' trechos dos documentos)' : '';
  if (!falta.length) return { status: 'ok', summary: 'Qualificação completa: ' + nome + ' (nacionalidade, estado civil, profissão e endereço em Betim)' + varias + '.', hits: usados.map(q => q.h), nome };
  return { status: 'warn', summary: nome + ': falta ' + falta.join(', ') + '.' + (u.endereco && !u.betim ? ' O endereço encontrado não é de Betim (ou a cidade não foi informada).' : ''), hits: usados.map(q => q.h), nome };
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
  const pp = papeis(doc);
  const jg = checkJustica(doc);
  const just = { status: jg.status === 'sim' ? 'ok' : jg.status === 'nao' ? 'missing' : 'warn', summary: jg.summary, hits: jg.hits };
  const itens = [
    { key: 'sentenca', label: 'Sentença', ...intSentenca(doc) },
    { key: 'decreto', label: 'Decreta a interdição', ...intDecreto(doc) },
    { key: 'mandado', label: 'Força de mandado ou mandado do juiz', ...mand },
    { key: 'termo', label: 'Termo de compromisso', ...intTermo(doc, mand) },
    { key: 'curador', label: 'Qualificação do curador', ...intQualificacao(doc, 'curador', pp.curador) },
    { key: 'interditado', label: 'Qualificação do interditado', ...intQualificacao(doc, 'interditado', pp.interditado) },
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
