"""
Migração do tendencias.json antigo → regressoes.json
====================================================
O app anterior guardava só linhas lineares de CO₂, temperatura e umidade.
Este script converte cada uma delas numa regressão do novo formato,
preservando nome, cor, recorte e a opção de aparecer na legenda.

    python migrar_tendencias.py
"""

import json
import os

import config as cfg
import regressao

ANTIGO = os.path.join(cfg.BASE_DIR, 'tendencias.json')


def main():
    if not os.path.exists(ANTIGO):
        print('Nada a migrar: tendencias.json não existe nesta pasta.')
        return

    with open(ANTIGO, 'r', encoding='utf-8') as f:
        antigas = json.load(f)

    novas = regressao.carregar_todas()
    total = 0

    for chave, linhas in antigas.items():
        if chave not in cfg.SERIES_REGRESSIVEIS:
            print(f'  ignorada: série "{chave}" não existe mais.')
            continue
        for linha in linhas:
            reg = regressao.normalizar({
                'modelo': 'linear',
                'metodo': 'ols',
                'inicio': linha.get('inicio'),
                'fim': linha.get('fim'),
                'nome': linha.get('nome'),
                'cor': linha.get('cor') or cfg.SERIES_REGRESSIVEIS[chave]['cor'],
                'legenda': linha.get('legenda', True),
            })
            reg['id'] = linha.get('id') or reg['id']
            novas.setdefault(chave, []).append(reg)
            total += 1

    regressao.salvar_todas(novas)
    print(f'{total} linha(s) migrada(s) para regressoes.json.')
    print('O arquivo antigo foi mantido — apague quando conferir o resultado.')


if __name__ == '__main__':
    main()
