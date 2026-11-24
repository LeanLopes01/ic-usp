"""
Painel de monitoramento de gases e qualidade do ar
==================================================
Servidor Flask local. Sobe com:

    python app.py                 → http://127.0.0.1:5000
    python app.py --porta 8080    → outra porta
    python app.py --rede          → acessível na rede local (0.0.0.0)

A página é única e as abas trocam no navegador; os gráficos chegam como
JSON do Plotly, sem iframes.
"""

import argparse
import json
import re
import traceback

import numpy as np
import plotly.io as pio
from flask import Flask, jsonify, render_template, request

import aparencia
import config as cfg
import dados
import graficos
import regressao

app = Flask(__name__)
app.json.ensure_ascii = False


# ─────────────────────────────────────────────
# PÁGINA
# ─────────────────────────────────────────────

@app.route('/plotly.js')
def plotly_js():
    """Serve o plotly.js que veio junto com a versão instalada do plotly.py.
    Evita depender de CDN (o painel roda offline) e garante que a biblioteca
    do navegador entenda o JSON gerado aqui."""
    try:
        from plotly.offline import get_plotlyjs
        codigo = get_plotlyjs()
    except Exception:
        return jsonify({'erro': 'plotly.js não encontrado no pacote instalado'}), 500
    return app.response_class(codigo, mimetype='application/javascript',
                              headers={'Cache-Control': 'public, max-age=86400'})


@app.route('/')
def index():
    return render_template(
        'index.html',
        abas=cfg.ABAS,
        periodos=cfg.PERIODOS,
        sensores=cfg.SERIES_REGRESSIVEIS,
        modelos=regressao.MODELOS,
        metodos=regressao.METODOS,
        tracejados=regressao.TRACEJADOS,
        bandas=regressao.BANDAS,
        faixas_iqa=cfg.IQA_FAIXAS,
        padrao_regressao=regressao.PADRAO,
        cards=cfg.catalogo_cards(),
        modos_x=aparencia.MODOS_X,
        padrao_aparencia=aparencia.PADRAO,
    )


# ─────────────────────────────────────────────
# FIGURAS
# ─────────────────────────────────────────────

def _params_da_query() -> tuple[int | None, str, dict]:
    horas = request.args.get('horas', default=24, type=int)
    horas = None if not horas else horas
    tema = request.args.get('tema', 'claro')
    tema = tema if tema in cfg.TEMAS else 'claro'

    ignorar = {'horas', 'tema', 'figura', 'card', '_'}
    params = {k: v for k, v in request.args.items() if k not in ignorar}
    return horas, tema, params


@app.route('/api/figura/<nome>')
def api_figura(nome):
    """Devolve a figura pronta em JSON + o resumo das regressões aplicadas."""
    try:
        horas, tema, params = _params_da_query()
        inicio, fim = params.get('inicio'), params.get('fim')
        df = (dados.carregar_intervalo(inicio, fim) if inicio and fim
              else dados.carregar_com_fallback(horas))
        if df.empty:
            return jsonify({'vazio': True,
                            'mensagem': 'Nenhuma leitura no banco ainda.'}), 200

        fig, resumo = graficos.montar(nome, df, params, tema)

        # Título, nomes de eixo, tamanhos de fonte e escala do eixo X.
        # A origem dos "dias decorridos" é a primeira leitura carregada.
        conf_aparencia = aparencia.obter(request.args.get('card'))
        aparencia.aplicar(fig, conf_aparencia, origem=df.index[0])
        fig.update_layout(autosize=True)

        filtros = [cfg.ROTULOS_PERIODO.get(horas or 0, f'{horas}h'),
                   params.get('chave'), params.get('vista')]
        arquivo = _nome_arquivo(fig, *[f for f in filtros if f])

        return app.response_class(
            json.dumps({
                'figura': json.loads(pio.to_json(fig)),
                'regressoes': resumo,
                'arquivo': arquivo,
                'aparencia': conf_aparencia,
                'periodo': dados.periodo_dos_dados(df),
            }, ensure_ascii=False),
            mimetype='application/json')
    except Exception as e:
        app.logger.error(traceback.format_exc())
        return jsonify({'erro': str(e)}), 500


def _nome_arquivo(fig, *filtros) -> str:
    titulo = (fig.layout.title.text or 'grafico')
    titulo = re.sub(r'<[^>]+>', ' ', titulo)
    titulo = re.sub(r'[^\w\s.-]', '', titulo, flags=re.UNICODE)
    titulo = re.sub(r'\s+', '_', titulo.strip())
    return '_'.join([p for p in [titulo, *filtros] if p]) or 'grafico'


# ─────────────────────────────────────────────
# STATUS (cabeçalho e cartões de leitura)
# ─────────────────────────────────────────────

@app.route('/api/status')
def api_status():
    try:
        horas = request.args.get('horas', default=24, type=int) or None
        df = dados.carregar_com_fallback(horas)
        if df.empty:
            return jsonify({'vazio': True, 'sensores': {}, 'iqa': None})

        sensores = {}
        for chave, conf in cfg.TODOS_SENSORES.items():
            if conf['coluna'] not in df.columns:
                continue
            s = dados.serie(df, chave).dropna()
            if s.empty:
                continue
            ultimo = float(s.iloc[-1])
            anterior = float(s.iloc[-min(len(s), 12)])
            sensores[chave] = {
                'nome': conf['nome'],
                'gas': conf.get('gas'),
                'unidade': conf['unidade'],
                'cor': conf['cor'],
                'ultimo': round(ultimo, 2),
                'variacao': round(ultimo - anterior, 2),
                'alerta': conf.get('alerta'),
                'em_alerta': bool(conf.get('alerta') and ultimo >= conf['alerta']),
                'estatisticas': dados.estatisticas(s),
            }

        tabela = dados.calcular_iqa(df)
        info_iqa = None
        if not tabela.empty and not tabela['iqa'].dropna().empty:
            valor = float(tabela['iqa'].dropna().iloc[-1])
            dominante = (tabela['dominante'].dropna().iloc[-1]
                         if not tabela['dominante'].dropna().empty else None)
            nivel, cor = dados.faixa_iqa(valor)
            info_iqa = {'valor': round(valor, 1), 'nivel': nivel, 'cor': cor,
                        'dominante': cfg.POLUENTES_IQA.get(dominante, '—'),
                        'escala_max': cfg.IQA_FAIXAS[-1][1]}

        inicio, fim = dados.periodo_dos_dados(df)
        return jsonify({'vazio': False, 'sensores': sensores, 'iqa': info_iqa,
                        'leituras': int(len(df)), 'inicio': inicio, 'fim': fim})
    except Exception as e:
        app.logger.error(traceback.format_exc())
        return jsonify({'erro': str(e)}), 500


# ─────────────────────────────────────────────
# REGRESSÕES
# ─────────────────────────────────────────────

def _valida(chave):
    return chave in cfg.SERIES_REGRESSIVEIS


@app.route('/api/regressoes')
def regressoes_todas():
    """Tudo que está salvo, por série — alimenta a aba de gerenciamento."""
    guardadas = regressao.carregar_todas()
    saida = {}
    for chave, linhas in guardadas.items():
        if not _valida(chave):
            continue
        conf = cfg.SERIES_REGRESSIVEIS[chave]
        saida[chave] = {'nome': conf['nome'], 'unidade': conf['unidade'],
                        'cor': conf['cor'], 'linhas': linhas}
    return jsonify(saida)


@app.route('/api/regressoes/<chave>', methods=['GET'])
def regressoes_listar(chave):
    if not _valida(chave):
        return jsonify({'erro': f"Série '{chave}' não existe."}), 404
    return jsonify(regressao.listar(chave))


@app.route('/api/regressoes/<chave>', methods=['POST'])
def regressoes_criar(chave):
    if not _valida(chave):
        return jsonify({'erro': f"Série '{chave}' não existe."}), 404
    corpo = request.get_json(force=True, silent=True) or {}
    return jsonify(regressao.criar(chave, corpo)), 201


@app.route('/api/regressoes/<chave>/<id_reg>', methods=['PATCH'])
def regressoes_atualizar(chave, id_reg):
    if not _valida(chave):
        return jsonify({'erro': f"Série '{chave}' não existe."}), 404
    corpo = request.get_json(force=True, silent=True) or {}
    atualizada = regressao.atualizar(chave, id_reg, corpo)
    if atualizada is None:
        return jsonify({'erro': 'Regressão não encontrada.'}), 404
    return jsonify(atualizada)


@app.route('/api/regressoes/<chave>/<id_reg>', methods=['DELETE'])
def regressoes_remover(chave, id_reg):
    if not _valida(chave):
        return jsonify({'erro': f"Série '{chave}' não existe."}), 404
    if not regressao.remover(chave, id_reg):
        return jsonify({'erro': 'Regressão não encontrada.'}), 404
    return jsonify({'ok': True})


@app.route('/api/regressoes/<chave>/simular', methods=['POST'])
def regressoes_simular(chave):
    """Ajusta sem salvar: a interface mostra R², RMSE e a equação antes de o
    usuário confirmar."""
    if not _valida(chave):
        return jsonify({'erro': f"Série '{chave}' não existe."}), 404

    corpo = request.get_json(force=True, silent=True) or {}
    horas = corpo.get('horas', 24) or None
    df = dados.carregar_com_fallback(horas)
    if df.empty:
        return jsonify({'erro': 'Sem dados no período.'}), 400

    s = dados.serie(df, chave)
    conf = cfg.SERIES_REGRESSIVEIS[chave]
    resultado = regressao.ajustar(s, regressao.normalizar(corpo), unidade=conf['unidade'])
    if resultado is None:
        return jsonify({'erro': 'Pontos insuficientes na janela escolhida.'}), 400

    return jsonify({'equacao': resultado['equacao'],
                    'metricas': resultado['metricas'],
                    'parametros': {k: (round(v, 6) if isinstance(v, float) else v)
                                   for k, v in resultado['parametros'].items()}})


@app.route('/api/series')
def api_series():
    """Catálogo de séries que aceitam regressão + limites de tempo dos dados,
    usado para preencher os campos de data do formulário."""
    df = dados.carregar_com_fallback(None)
    inicio, fim = dados.periodo_dos_dados(df)
    catalogo = {k: {'nome': v['nome'], 'unidade': v['unidade'], 'cor': v['cor'],
                    'grupo': v.get('gas')}
                for k, v in cfg.SERIES_REGRESSIVEIS.items()}
    return jsonify({'series': catalogo, 'inicio': inicio, 'fim': fim})


# ─────────────────────────────────────────────
# APARÊNCIA DOS GRÁFICOS
# ─────────────────────────────────────────────

@app.route('/api/cards')
def api_cards():
    """Lista dos gráficos personalizáveis + quais já têm ajuste próprio."""
    return jsonify({'cards': cfg.catalogo_cards(),
                    'personalizados': aparencia.personalizados()})


@app.route('/api/aparencia', methods=['GET'])
def aparencia_listar():
    return jsonify(aparencia.carregar_arquivo())


@app.route('/api/aparencia/global', methods=['GET'])
def aparencia_global_ler():
    return jsonify(aparencia.obter(None))


@app.route('/api/aparencia/global', methods=['PUT'])
def aparencia_global_salvar():
    """Grava os valores como padrão de todo gráfico sem ajuste próprio."""
    corpo = request.get_json(force=True, silent=True) or {}
    return jsonify(aparencia.salvar_global(corpo))


@app.route('/api/aparencia/<id_card>', methods=['GET'])
def aparencia_ler(id_card):
    return jsonify(aparencia.obter(id_card))


@app.route('/api/aparencia/<id_card>', methods=['PUT'])
def aparencia_salvar(id_card):
    corpo = request.get_json(force=True, silent=True) or {}
    return jsonify(aparencia.salvar(id_card, corpo))


@app.route('/api/aparencia/<id_card>', methods=['DELETE'])
def aparencia_restaurar(id_card):
    """Apaga o ajuste do card; ele volta a seguir o padrão global."""
    aparencia.restaurar(id_card)
    return jsonify(aparencia.obter(id_card))


# ─────────────────────────────────────────────
# EXECUÇÃO LOCAL
# ─────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description='Painel de gases e qualidade do ar')
    parser.add_argument('--porta', type=int, default=5000)
    parser.add_argument('--rede', action='store_true',
                        help='expõe o painel na rede local (0.0.0.0)')
    parser.add_argument('--debug', action='store_true')
    args = parser.parse_args()

    host = '0.0.0.0' if args.rede else '127.0.0.1'
    print(f'\n  Painel no ar em http://{"127.0.0.1" if not args.rede else host}:{args.porta}\n')
    app.run(host=host, port=args.porta, debug=args.debug)


if __name__ == '__main__':
    main()
