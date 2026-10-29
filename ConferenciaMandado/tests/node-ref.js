const m = require('../src/Engine/divorcio.js'); const fs = require('fs');
for (const f of process.argv.slice(2)) {
  const r = m.analisar(fs.readFileSync(f, 'utf8'));
  console.log('=== ' + require('path').basename(f));
  for (const it of [...r.itens, r.justica]) { console.log(`${it.key}|${it.status}|${it.summary}|pgs=${it.pages.join(',')}`); for (const t of it.snippets) console.log(`   [${t.page}] ${t.before}<<${t.match}>>${t.after}`); }
  for (const e of r.extras) console.log(`extra|${e.label}|${e.found ? 'True' : 'False'}|${e.value || ''}`);
  const d = r.dados; console.log(`dados|${d.partes.join(' / ')}|${d.nomes.join(' / ')}|${d.processo}|${d.livro}|${d.folha}|${d.termo}|${d.cartorio}|${d.quem}|${d.bens}`);
}
