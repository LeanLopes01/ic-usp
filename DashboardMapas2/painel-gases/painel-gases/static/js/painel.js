/* ─────────────────────────────────────────────
   Painel de gases — comportamento da interface
   ───────────────────────────────────────────── */

const estado = {
  horas: 24,
  tema: 'escuro',
  aba: document.querySelector('.aba.ativa')?.dataset.aba,
  serieAtiva: null,
  seriesDaGaveta: [],
  editando: null,
  limites: { inicio: null, fim: null },
  timer: null,
};

const $ = (sel, raiz = document) => raiz.querySelector(sel);
const $$ = (sel, raiz = document) => [...raiz.querySelectorAll(sel)];

const CONFIG_PLOTLY = {
  responsive: true,
  displaylogo: false,
  modeBarButtonsToRemove: ['lasso2d', 'select2d'],
};

/* ── Avisos ────────────────────────────────── */
let timerAviso;
function avisar(texto, erro = false) {
  const el = $('#aviso');
  el.textContent = texto;
  el.classList.toggle('erro', erro);
  el.hidden = false;
  clearTimeout(timerAviso);
  timerAviso = setTimeout(() => { el.hidden = true; }, 3200);
}

async function pedir(url, opcoes = {}) {
  const resp = await fetch(url, opcoes);
  const corpo = await resp.json().catch(() => ({}));
  if (!resp.ok) throw new Error(corpo.erro || `Falha ao acessar ${url}`);
  return corpo;
}

/* ── Cartões de leitura + faixa do IQAr ────── */
async function atualizarStatus() {
  try {
    const dados = await pedir(`/api/status?horas=${estado.horas}`);
    const alvo = $('#leituras');

    if (dados.vazio) {
      alvo.innerHTML = '<p class="vazio">Nenhuma leitura no banco. Coloque o dump ' +
        '<code>banco_teste_gases.sql</code> ao lado do app.py e atualize.</p>';
      return;
    }

    alvo.innerHTML = Object.entries(dados.sensores).map(([chave, s]) => `
      <div class="leitura ${s.em_alerta ? 'alerta' : ''}" style="--cor:${s.cor}">
        <div class="leitura-nome">${s.nome}</div>
        <div class="leitura-valor">${s.ultimo}<small> ${s.unidade}</small></div>
        <div class="leitura-var">${s.variacao >= 0 ? '▲' : '▼'} ${Math.abs(s.variacao)} desde as últimas leituras</div>
      </div>`).join('');

    const faixa = $('#faixa-iqa');
    if (dados.iqa) {
      faixa.hidden = false;
      $('#iqa-valor').textContent = dados.iqa.valor;
      $('#iqa-nivel').textContent = dados.iqa.nivel;
      $('#iqa-dominante').textContent = `dominante: ${dados.iqa.dominante}`;
      const pino = $('#iqa-pino');
      pino.hidden = false;
      pino.style.left = `${Math.min(100, (dados.iqa.valor / dados.iqa.escala_max) * 100)}%`;
    } else {
      faixa.hidden = true;
    }
  } catch (e) {
    console.error(e);
  }
}

/* ── Gráficos ──────────────────────────────── */
function paramsDoCard(card) {
  const base = JSON.parse(card.dataset.params || '{}');
  const params = new URLSearchParams({ horas: estado.horas, tema: estado.tema });
  if (card.dataset.card) params.set('card', card.dataset.card);

  Object.entries(base).forEach(([k, v]) => {
    params.set(k, Array.isArray(v) ? v.join(',') : v);
  });

  const vista = $('.sub-aba.ativa', card);
  if (vista) params.set('vista', vista.dataset.vista);

  $$('.mini-select[data-eixo]', card).forEach(sel => params.set(sel.dataset.eixo, sel.value));
  return params;
}

async function desenharCard(card) {
  const alvo = $('.grafico', card);
  const painelMetricas = $('.metricas', card);
  alvo.classList.add('carregando');

  try {
    const url = `/api/figura/${card.dataset.figura}?${paramsDoCard(card)}`;
    const dados = await pedir(url);
    alvo.classList.remove('carregando');

    if (dados.vazio) {
      Plotly.purge(alvo);
      alvo.innerHTML = `<p class="vazio">${dados.mensagem}</p>`;
      alvo.dataset.texto = '1';
      painelMetricas.innerHTML = '';
      return;
    }

    if (alvo.dataset.texto) { alvo.innerHTML = ''; delete alvo.dataset.texto; }
    const config = { ...CONFIG_PLOTLY, toImageButtonOptions: { filename: dados.arquivo } };
    Plotly.react(alvo, dados.figura.data, dados.figura.layout, config);

    painelMetricas.innerHTML = (dados.regressoes || []).map(r => {
      if (r.erro) return `<span class="metrica falha">${r.nome || 'Tendência'}: ${r.erro}</span>`;
      const m = r.metricas || {};
      const pedacos = [`<b>${r.nome}</b>`, r.equacao];
      if (m.r2 !== null && m.r2 !== undefined) pedacos.push(`R² ${m.r2}`);
      if (m.rmse !== undefined) pedacos.push(`RMSE ${m.rmse}`);
      if (m.inclinacao) pedacos.push(m.inclinacao);
      if (m.n) pedacos.push(`n=${m.n}`);
      return `<span class="metrica">${pedacos.join(' · ')}</span>`;
    }).join('');
  } catch (e) {
    alvo.classList.remove('carregando');
    Plotly.purge(alvo);
    alvo.innerHTML = `<p class="vazio">Não foi possível montar o gráfico: ${e.message}</p>`;
    alvo.dataset.texto = '1';
  }
}

function desenharAbaAtiva() {
  const painel = $(`.painel[data-painel="${estado.aba}"]`);
  if (!painel) return;
  $$('.card[data-figura]', painel).forEach(desenharCard);
}

function redesenharTudoQueJaFoiVisto() {
  desenharAbaAtiva();
}

/* ── Navegação por abas ────────────────────── */
$$('.aba').forEach(botao => {
  botao.addEventListener('click', () => {
    $$('.aba').forEach(b => b.classList.toggle('ativa', b === botao));
    $$('.painel').forEach(p => p.classList.toggle(
      'ativo', p.dataset.painel === botao.dataset.aba));
    estado.aba = botao.dataset.aba;

    if (estado.aba === 'regressoes') carregarListaGlobal();
    else if (estado.aba === 'aparencia') abrirAparencia();
    else desenharAbaAtiva();
  });
});

/* Sub-abas dentro de um card (série / distribuição / estatísticas) */
document.addEventListener('click', ev => {
  const sub = ev.target.closest('.sub-aba');
  if (!sub) return;
  const card = sub.closest('.card');
  $$('.sub-aba', card).forEach(b => b.classList.toggle('ativa', b === sub));
  desenharCard(card);
});

/* Seletores X/Y do gráfico de dispersão */
document.addEventListener('change', ev => {
  const sel = ev.target.closest('.mini-select[data-eixo]');
  if (!sel) return;
  desenharCard(sel.closest('.card'));
});

/* ── Controles do topo ─────────────────────── */
$('#periodo').addEventListener('change', ev => {
  estado.horas = Number(ev.target.value);
  atualizarStatus();
  desenharAbaAtiva();
});

$('#btn-atualizar').addEventListener('click', () => {
  atualizarStatus();
  desenharAbaAtiva();
  avisar('Dados recarregados.');
});

$('#btn-tema').addEventListener('click', ev => {
  estado.tema = estado.tema === 'escuro' ? 'claro' : 'escuro';
  document.body.dataset.tema = estado.tema;
  ev.target.textContent = estado.tema === 'escuro' ? 'Tema claro' : 'Tema escuro';
  desenharAbaAtiva();
});

$('#auto-atualizar').addEventListener('change', ev => {
  clearInterval(estado.timer);
  if (ev.target.checked) {
    estado.timer = setInterval(() => { atualizarStatus(); desenharAbaAtiva(); }, 60000);
    avisar('Atualização automática a cada 60 segundos.');
  }
});

/* ─────────────────────────────────────────────
   EDITOR DE REGRESSÕES
   ───────────────────────────────────────────── */

const cortina = $('#cortina');
const formulario = $('#form-regressao');

function abrirGaveta(chaves, tituloCard) {
  estado.seriesDaGaveta = chaves;
  estado.serieAtiva = chaves[0];
  estado.editando = null;

  $('#gaveta-sub').textContent = tituloCard;
  montarChips();
  limparFormulario();
  carregarLista();
  cortina.hidden = false;
}

function fecharGaveta() {
  cortina.hidden = true;
  estado.editando = null;
}

$('#btn-fechar').addEventListener('click', fecharGaveta);
cortina.addEventListener('click', ev => { if (ev.target === cortina) fecharGaveta(); });
document.addEventListener('keydown', ev => {
  if (ev.key === 'Escape' && !cortina.hidden) fecharGaveta();
});

document.addEventListener('click', ev => {
  const botao = ev.target.closest('.btn-regressao');
  if (!botao) return;
  const card = botao.closest('.card');
  const chaves = JSON.parse(card.dataset.regressao || '[]');
  if (!chaves.length) return;
  abrirGaveta(chaves, $('h2', card).textContent);
});

function montarChips() {
  const alvo = $('#seletor-serie');
  if (estado.seriesDaGaveta.length < 2) { alvo.innerHTML = ''; return; }

  alvo.innerHTML = estado.seriesDaGaveta.map(chave => {
    const s = window.PAINEL.sensores[chave];
    return `<button type="button" class="chip-serie ${chave === estado.serieAtiva ? 'ativo' : ''}"
              style="--cor:${s.cor}" data-serie="${chave}">
              <span class="ponto"></span>${s.nome}</button>`;
  }).join('');

  $$('.chip-serie', alvo).forEach(chip => {
    chip.addEventListener('click', () => {
      estado.serieAtiva = chip.dataset.serie;
      estado.editando = null;
      montarChips();
      limparFormulario();
      carregarLista();
    });
  });
}

function camposVisiveis() {
  const modelo = formulario.modelo.value;
  $$('[data-quando]', formulario).forEach(el => {
    el.style.display = el.dataset.quando.split(' ').includes(modelo) ? '' : 'none';
  });
}
formulario.modelo.addEventListener('change', camposVisiveis);

function limparFormulario() {
  const s = window.PAINEL.sensores[estado.serieAtiva];
  formulario.reset();
  estado.editando = null;
  formulario.cor.value = s ? s.cor : window.PAINEL.padrao.cor;
  $('#btn-salvar').textContent = 'Salvar regressão';
  $('#btn-cancelar-edicao').hidden = true;
  $('#previa').textContent = 'Ajuste os campos para ver o resultado.';
  $('#previa').classList.remove('falha');
  camposVisiveis();
}

function lerFormulario() {
  const d = new FormData(formulario);
  const bool = nome => formulario.elements[nome].checked;
  return {
    nome: d.get('nome'),
    cor: d.get('cor'),
    modelo: d.get('modelo'),
    metodo: d.get('metodo'),
    grau: Number(d.get('grau')),
    janela: Number(d.get('janela')),
    inicio: d.get('inicio') || null,
    fim: d.get('fim') || null,
    extrapolar_horas: Number(d.get('extrapolar_horas') || 0),
    extrapolar_antes_horas: Number(d.get('extrapolar_antes_horas') || 0),
    banda: d.get('banda'),
    nivel: Number(d.get('nivel')),
    tracejado: d.get('tracejado'),
    espessura: Number(d.get('espessura')),
    opacidade: Number(d.get('opacidade')),
    legenda: bool('legenda'),
    mostrar_equacao: bool('mostrar_equacao'),
    mostrar_r2: bool('mostrar_r2'),
    ativo: bool('ativo'),
  };
}

function preencherFormulario(reg) {
  Object.entries(reg).forEach(([campo, valor]) => {
    const el = formulario.elements[campo];
    if (!el) return;
    if (el.type === 'checkbox') el.checked = Boolean(valor);
    else el.value = valor === null || valor === undefined ? '' : valor;
  });
  camposVisiveis();
  $('#btn-salvar').textContent = 'Salvar alterações';
  $('#btn-cancelar-edicao').hidden = false;
  estado.editando = reg.id;
  formulario.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

$('#btn-cancelar-edicao').addEventListener('click', limparFormulario);

$('#btn-todo-periodo').addEventListener('click', () => {
  if (!estado.limites.inicio) { avisar('Ainda carregando os limites dos dados.'); return; }
  formulario.inicio.value = estado.limites.inicio;
  formulario.fim.value = estado.limites.fim;
});

$('#btn-simular').addEventListener('click', async () => {
  const previa = $('#previa');
  try {
    const corpo = { ...lerFormulario(), horas: estado.horas };
    const r = await pedir(`/api/regressoes/${estado.serieAtiva}/simular`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(corpo),
    });
    const m = r.metricas;
    previa.classList.remove('falha');
    previa.innerHTML = [
      `<b>${r.equacao}</b>`,
      `R² ${m.r2 ?? '—'}   R² ajustado ${m.r2_ajustado ?? '—'}`,
      `RMSE ${m.rmse}   MAE ${m.mae}   n ${m.n}`,
      m.inclinacao ? `Inclinação ${m.inclinacao} (${m.variacao_dia})` : '',
      m.inicio ? `Janela ${m.inicio} → ${m.fim} (${m.duracao_h} h)` : '',
    ].filter(Boolean).join('\n');
  } catch (e) {
    previa.classList.add('falha');
    previa.textContent = e.message;
  }
});

formulario.addEventListener('submit', async ev => {
  ev.preventDefault();
  const corpo = lerFormulario();
  try {
    if (estado.editando) {
      await pedir(`/api/regressoes/${estado.serieAtiva}/${estado.editando}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(corpo),
      });
      avisar('Regressão atualizada.');
    } else {
      await pedir(`/api/regressoes/${estado.serieAtiva}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(corpo),
      });
      avisar('Regressão salva.');
    }
    limparFormulario();
    carregarLista();
    redesenharTudoQueJaFoiVisto();
  } catch (e) {
    avisar(e.message, true);
  }
});

function descrever(reg) {
  const modelo = window.PAINEL.modelos[reg.modelo] || reg.modelo;
  const trecho = reg.inicio || reg.fim
    ? `${(reg.inicio || 'início').replace('T', ' ')} → ${(reg.fim || 'fim').replace('T', ' ')}`
    : 'todo o período';
  const extras = [];
  if (reg.modelo === 'linear' && reg.metodo !== 'ols') extras.push(window.PAINEL.metodos[reg.metodo]);
  if (reg.extrapolar_horas) extras.push(`projeta +${reg.extrapolar_horas}h`);
  if (reg.banda && reg.banda !== 'nenhuma') extras.push(`banda ${Math.round(reg.nivel * 100)}%`);
  return [modelo, trecho, ...extras].join(' · ');
}

function itemHTML(chave, reg) {
  return `
    <li class="item-regressao ${reg.ativo ? '' : 'inativa'}" data-serie="${chave}" data-id="${reg.id}">
      <span class="item-swatch" style="background:${reg.cor}"></span>
      <div class="item-texto">
        <div class="item-nome">${reg.nome || 'Tendência sem nome'}</div>
        <div class="item-detalhe">${descrever(reg)}</div>
      </div>
      <div class="item-acoes">
        <button class="btn-mini" data-acao="alternar">${reg.ativo ? 'Desativar' : 'Ativar'}</button>
        <button class="btn-mini" data-acao="editar">Editar</button>
        <button class="btn-mini" data-acao="remover">Remover</button>
      </div>
    </li>`;
}

async function carregarLista() {
  const alvo = $('#lista-regressoes');
  const s = window.PAINEL.sensores[estado.serieAtiva];
  $('#gaveta-titulo').textContent = `Regressões — ${s ? s.nome : estado.serieAtiva}`;
  try {
    const linhas = await pedir(`/api/regressoes/${estado.serieAtiva}`);
    alvo.innerHTML = linhas.length
      ? linhas.map(r => itemHTML(estado.serieAtiva, r)).join('')
      : '<li class="vazio">Nenhuma regressão nesta série ainda.</li>';
  } catch (e) {
    alvo.innerHTML = `<li class="vazio">${e.message}</li>`;
  }
}

/* Ações dos itens (lista da gaveta e lista global) */
document.addEventListener('click', async ev => {
  const botao = ev.target.closest('.item-acoes button');
  if (!botao) return;

  const item = botao.closest('.item-regressao');
  const chave = item.dataset.serie;
  const id = item.dataset.id;

  try {
    if (botao.dataset.acao === 'remover') {
      await pedir(`/api/regressoes/${chave}/${id}`, { method: 'DELETE' });
      avisar('Regressão removida.');
    } else if (botao.dataset.acao === 'alternar') {
      const atual = botao.textContent.trim() === 'Ativar';
      await pedir(`/api/regressoes/${chave}/${id}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ativo: atual }),
      });
    } else if (botao.dataset.acao === 'editar') {
      const linhas = await pedir(`/api/regressoes/${chave}`);
      const reg = linhas.find(r => r.id === id);
      if (!reg) return;
      if (cortina.hidden) abrirGaveta([chave], window.PAINEL.sensores[chave].nome);
      estado.serieAtiva = chave;
      setTimeout(() => preencherFormulario(reg), 0);
      return;
    }

    if (!cortina.hidden) carregarLista();
    if (estado.aba === 'regressoes') carregarListaGlobal();
    else redesenharTudoQueJaFoiVisto();
  } catch (e) {
    avisar(e.message, true);
  }
});

/* ── Aba de gerenciamento global ───────────── */
async function carregarListaGlobal() {
  const alvo = $('#lista-global');
  try {
    const dados = await pedir('/api/regressoes');
    const grupos = Object.entries(dados).filter(([, g]) => g.linhas.length);

    alvo.innerHTML = grupos.length ? grupos.map(([chave, grupo]) => `
      <div class="grupo-global">
        <h3><span class="item-swatch" style="background:${grupo.cor}"></span>
          ${grupo.nome} <span class="item-detalhe">(${grupo.unidade})</span></h3>
        <ul class="lista-regressoes">
          ${grupo.linhas.map(r => itemHTML(chave, r)).join('')}
        </ul>
      </div>`).join('')
      : '<p class="vazio">Nenhuma regressão salva. Use o botão “Regressões” em qualquer ' +
        'gráfico, ou escolha uma série acima para começar.</p>';
  } catch (e) {
    alvo.innerHTML = `<p class="vazio">${e.message}</p>`;
  }
}

$('#btn-nova-global').addEventListener('click', () => {
  const chave = $('#nova-serie').value;
  abrirGaveta([chave], window.PAINEL.sensores[chave].nome);
});

/* ─────────────────────────────────────────────
   APARÊNCIA DOS GRÁFICOS
   ───────────────────────────────────────────── */

const formAparencia = $('#form-aparencia');
const previaCard = $('#previa-card');
const seletorGrafico = $('#grafico-alvo');

function cardDoCatalogo(id) {
  return (window.PAINEL.cards || []).find(c => c.id === id);
}

function preencherAparencia(conf) {
  Object.entries(conf).forEach(([campo, valor]) => {
    const el = formAparencia.elements[campo];
    if (!el) return;
    el.value = valor === null || valor === undefined ? '' : valor;
  });
  alternarCampoDia();
}

function alternarCampoDia() {
  const dias = formAparencia.modo_x.value === 'dias';
  $$('[data-quando-x]', formAparencia).forEach(el => {
    el.style.display = el.dataset.quandoX === formAparencia.modo_x.value ? '' : 'none';
  });
  return dias;
}

function lerAparencia() {
  const d = new FormData(formAparencia);
  const conf = {};
  d.forEach((valor, campo) => { conf[campo] = valor; });
  return conf;
}

function montarPrevia(id) {
  const card = cardDoCatalogo(id);
  if (!card) return;
  previaCard.dataset.card = card.id;
  previaCard.dataset.figura = card.figura;
  previaCard.dataset.params = JSON.stringify(card.params || {});
  $('#previa-desc').textContent = `${card.aba} › ${card.titulo}`;
  desenharCard(previaCard);
}

async function abrirAparencia() {
  const id = seletorGrafico.value;
  try {
    const conf = await pedir(`/api/aparencia/${id}`);
    preencherAparencia(conf);
    montarPrevia(id);
  } catch (e) {
    avisar(e.message, true);
  }
}

let timerAparencia;
function salvarAparencia() {
  clearTimeout(timerAparencia);
  $('#estado-aparencia').textContent = 'salvando…';
  timerAparencia = setTimeout(async () => {
    try {
      await pedir(`/api/aparencia/${seletorGrafico.value}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(lerAparencia()),
      });
      $('#estado-aparencia').textContent = 'salvo';
      montarPrevia(seletorGrafico.value);
    } catch (e) {
      $('#estado-aparencia').textContent = e.message;
    }
  }, 400);
}

seletorGrafico.addEventListener('change', abrirAparencia);
formAparencia.addEventListener('input', ev => {
  if (ev.target.name === 'modo_x') alternarCampoDia();
  salvarAparencia();
});
formAparencia.addEventListener('change', () => { alternarCampoDia(); salvarAparencia(); });

$('#btn-aparencia-restaurar').addEventListener('click', async () => {
  try {
    const conf = await pedir(`/api/aparencia/${seletorGrafico.value}`, { method: 'DELETE' });
    preencherAparencia(conf);
    montarPrevia(seletorGrafico.value);
    avisar('Gráfico voltou ao padrão.');
  } catch (e) {
    avisar(e.message, true);
  }
});

$('#btn-aparencia-todos').addEventListener('click', async () => {
  try {
    await pedir('/api/aparencia/global', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(lerAparencia()),
    });
    avisar('Padrão aplicado a todos os gráficos sem ajuste próprio.');
  } catch (e) {
    avisar(e.message, true);
  }
});

/* ── Início ────────────────────────────────── */
(async function iniciar() {
  camposVisiveis();
  atualizarStatus();
  desenharAbaAtiva();
  try {
    const catalogo = await pedir('/api/series');
    if (catalogo.inicio) {
      estado.limites.inicio = catalogo.inicio.slice(0, 16).replace(' ', 'T');
      estado.limites.fim = catalogo.fim.slice(0, 16).replace(' ', 'T');
    }
  } catch (e) { /* limites são opcionais */ }
})();
