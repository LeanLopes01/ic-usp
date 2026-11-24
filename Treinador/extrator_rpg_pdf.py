#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Extrator de conteúdo de RPG (PDF -> JSON) usando um LLM local via Ollama.

Lê todos os PDFs da pasta 'Arquivos/', detecta capítulos e sub-entidades usando o
tamanho de fonte extraído do próprio PDF (PyMuPDF), envia o texto em pedaços
("chunks") para um modelo Llama 3 rodando localmente no Ollama, e salva o
resultado estruturado em JSON na pasta 'Resultados/', organizada por tipo de
conteúdo.

Arquitetura modular (independente de sistema de RPG):
  - Os esquemas/templates de cada tipo de conteúdo (raça, classe, vantagem de
    GURPS, implante de Cyberpunk, etc.) NÃO ficam fixos no código — eles vêm de
    um banco de templates externo em Templates/master_db.json. Para suportar um
    sistema de RPG novo, basta editar esse arquivo; nenhum código Python precisa
    mudar (veja o comentário na seção "BANCO DE TEMPLATES" abaixo).
  - Antes de processar cada PDF, uma "Fase 0" de reconhecimento envia o
    sumário/introdução do livro para o LLM, que decide quais tipos do banco de
    templates esse livro específico provavelmente usa (ex.: um livro de D&D não
    precisa nem tentar reconhecer "vantagens" de GURPS). Isso evita ruído e
    acelera a extração real.

O formato do JSON de saída segue a convenção usada pelo `JsonConvert.DeserializeXmlNode`
do Json.NET (já utilizada em ImportadorConteudoUniversal.cs no projeto do VTT):
  - Uma única chave de nível raiz = elemento raiz do XML (ex.: "race", "class").
  - Chaves iniciadas com "@" viram atributos XML (ex.: "@id", "@source").
  - A chave "#text" vira o texto de um elemento que também tem atributos.
  - Uma chave cujo valor é uma lista vira múltiplos elementos-irmãos com esse
    mesmo nome (ex.: "feature": [...] dentro de "features" vira várias
    tags <feature> dentro de <features>).
Isso deixa a futura conversão para XML o mais direta possível.

Uso:
    python extrator_rpg_pdf.py
        # roda a extração completa (com Fase 0 de reconhecimento por PDF)

    python extrator_rpg_pdf.py --dry-run
        # só mostra os capítulos/entidades/chunks detectados, sem chamar o
        # Ollama nem gravar nada (Fase 0 também é pulada nesse modo)

    python extrator_rpg_pdf.py --pagina-inicial 12
        # sobrescreve PAGINA_INICIAL para todos os PDFs desta execução

    python extrator_rpg_pdf.py --tipos-manuais dnd_class,dnd_race
        # pula a Fase 0 e usa exatamente essas chaves do master_db.json
"""

import argparse
import json
import logging
import re
import sys
import time
import unicodedata
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

import pymupdf as fitz  # PyMuPDF (o nome de módulo "fitz" está sendo descontinuado)
import requests

# =============================================================================
# CONFIGURAÇÃO — ajuste livremente antes de rodar
# =============================================================================

DIRETORIO_SCRIPT = Path(__file__).resolve().parent

# Pasta com os PDFs a processar e pasta onde os JSONs resultantes são salvos,
# ambas relativas à localização deste script (não ao diretório de onde ele é
# chamado, para funcionar bem em qualquer terminal).
PASTA_ENTRADA = DIRETORIO_SCRIPT / "Arquivos"
PASTA_SAIDA = DIRETORIO_SCRIPT / "Resultados"

# --- Ollama --------------------------------------------------------------
OLLAMA_HOST = "http://localhost:11434"
OLLAMA_MODEL = "llama3:8b"  # ajuste para o nome exato que aparece em `ollama list`
# Llama 3 8B suporta nativamente até 8192 tokens de contexto, mas o Ollama usa
# 2048 por padrão se isso não for definido explicitamente na chamada — o que
# truncaria o prompt/esquema silenciosamente. Por isso definimos sempre.
OLLAMA_NUM_CTX = 8192
TIMEOUT_OLLAMA = 600  # segundos; inferência local pode ser lenta — sem problema

# --- Paginação -------------------------------------------------------------
PAGINA_INICIAL = 1  # primeira página (1-indexada) a ser lida; pule capa/sumário aqui
# Overrides opcionais por nome de arquivo, se um PDF específico precisar de uma
# página inicial diferente do padrão acima:
PAGINAS_INICIAIS_POR_ARQUIVO = {
    # "meu_livro.pdf": 14,
}

# --- Banco de templates / Fase 0 de reconhecimento --------------------------
PASTA_TEMPLATES = DIRETORIO_SCRIPT / "Templates"
CAMINHO_MASTER_DB = PASTA_TEMPLATES / "master_db.json"
PAGINAS_RECONHECIMENTO = 8   # quantas primeiras páginas ler na Fase 0 (sumário/introdução)
MAX_TENTATIVAS_FASE0 = 3     # tentativas da Fase 0 antes de cair no fallback

# --- Chunking ----------------------------------------------------------------
MAX_PALAVRAS_PADRAO = 900     # limite de palavras por chamada ao modelo
MIN_PALAVRAS_ENTIDADE = 20    # títulos sem conteúdo relevante abaixo são descartados
MODO_DUAS_COLUNAS = False     # ative (True) se o PDF usa layout de 2 colunas por página

# Limiares de detecção de título por tamanho de fonte, relativos ao tamanho do
# "corpo de texto" (calculado automaticamente a partir do próprio PDF).
NIVEL1_RATIO = 2.0               # título de capítulo (ex.: "Classes", "Raças") — deve ser
                                  # bem maior que o corpo de texto
NIVEL2_RATIO = 1.15              # título de entidade dentro do capítulo (ex.: "Guerreiro")
# Se o --dry-run mostrar capítulos e entidades misturados no mesmo nível, ou
# títulos reais caindo como corpo de texto, ajuste esses dois valores olhando
# os tamanhos de fonte reais do seu PDF (aumente o espaço entre eles se
# capítulo/entidade estiverem com tamanhos parecidos).
NIVEL_MAX_PALAVRAS_TITULO = 10  # blocos de texto maiores que isso não são títulos

# --- Retry / robustez --------------------------------------------------------
MAX_TENTATIVAS = 3            # tentativas por chunk antes de desistir e logar falha
PULAR_JA_PROCESSADOS = True   # não reprocessa entidades cujo .json de saída já existe
                               # (importante: a extração pode levar horas; isso permite retomar)


# =============================================================================
# ESTRUTURAS DE DADOS
# =============================================================================

@dataclass
class Bloco:
    """Um bloco de texto do PDF (tipicamente um parágrafo ou uma linha de título)."""
    pagina: int
    texto: str
    tamanho: float
    negrito: bool
    y0: float
    coluna: int
    n_palavras: int
    nivel: int = 0  # 0 = corpo de texto, 1 = título de capítulo, 2 = título de entidade


@dataclass
class Entidade:
    """Uma unidade de conteúdo que vai virar um arquivo JSON (uma classe, raça, etc.)."""
    tipo: str
    titulo: str
    paragrafos: list = field(default_factory=list)


# =============================================================================
# BANCO DE TEMPLATES (externo, por sistema de RPG)
#
# Os esquemas/templates de cada tipo de conteúdo (raça, classe, vantagem de
# GURPS, implante de Cyberpunk, etc.) NÃO ficam fixos neste script — eles vêm
# de um arquivo JSON externo (Templates/master_db.json), carregado uma única
# vez no início da execução (veja main()). Para suportar um sistema de RPG
# novo, basta adicionar entradas nesse arquivo; nenhum código Python precisa
# mudar.
#
# Cada entrada do master_db.json precisa ter estes campos:
#   "pasta"              -> subpasta dentro de Resultados/ onde os JSONs vão parar
#   "palavras_chave"     -> palavras usadas para reconhecer o título do capítulo
#   "max_palavras_chunk" -> limite de palavras por chamada ao modelo para esse tipo
#   "instrucoes"         -> instrução específica dada ao LLM para esse tipo
#   "template"           -> esqueleto de exemplo do JSON de saída esperado
#
# Exemplo de chave: "dnd_class", "dnd_race", "gurps_advantage", "cyberpunk_implante".
# =============================================================================

CHAVES_OBRIGATORIAS_TEMPLATE = {"pasta", "palavras_chave", "max_palavras_chunk", "instrucoes", "template"}

# Tipo de fallback universal — ao contrário dos demais, este NÃO vem do
# master_db.json: é o mesmo para qualquer sistema de RPG, usado para capítulos
# de regras em prosa sem entidades nomeadas, ou quando a Fase 0 não consegue
# identificar nenhum tipo específico para o livro.
TEMPLATE_REGRA_FALLBACK = {
    "pasta": "regras",
    "palavras_chave": [],
    "max_palavras_chunk": 900,
    "instrucoes": (
        "Este texto é um trecho de conteúdo genérico que não se encaixa em nenhum dos "
        "tipos específicos deste sistema de RPG. Organize o conteúdo em seções com "
        "título e descrição."
    ),
    "template": {
        "document": {
            "@title": "Título do capítulo/seção",
            "section": [{"title": "Subtítulo", "description": "Texto da seção (pode ser longo)"}],
        }
    },
}


def carregar_banco_templates(caminho):
    """
    Carrega o banco de templates de um arquivo JSON externo. Cada chave do
    arquivo é um "tipo de conteúdo" (ex.: "dnd_class", "gurps_advantage"), com
    os campos listados no comentário da seção acima. Isso é o que torna o
    script independente de sistema de RPG.
    """
    caminho = Path(caminho)
    if not caminho.exists():
        raise FileNotFoundError(
            f"Banco de templates não encontrado em '{caminho}'. Crie o arquivo com a "
            f"definição dos tipos de conteúdo do seu sistema de RPG (veja o formato "
            f"esperado no comentário da seção 'BANCO DE TEMPLATES' deste script)."
        )
    with open(caminho, "r", encoding="utf-8") as f:
        try:
            banco = json.load(f)
        except json.JSONDecodeError as e:
            raise ValueError(f"'{caminho}' não é um JSON válido: {e}") from e

    if not isinstance(banco, dict) or not banco:
        raise ValueError(f"'{caminho}' deve conter um objeto JSON com pelo menos um tipo de conteúdo.")

    for chave, info in banco.items():
        if not isinstance(info, dict):
            raise ValueError(f"Tipo '{chave}' em '{caminho}' deveria ser um objeto JSON.")
        faltando = CHAVES_OBRIGATORIAS_TEMPLATE - set(info.keys())
        if faltando:
            raise ValueError(f"Tipo '{chave}' em '{caminho}' está incompleto — faltam os campos: {sorted(faltando)}")

    return banco


def resumir_chaves_para_fase0(banco_templates):
    """
    Reduz o banco de templates a um resumo leve (descrição + palavras-chave) para
    enviar à Fase 0 de reconhecimento — não há necessidade de gastar tokens com
    os templates JSON completos (que podem ser grandes) só para decidir QUAIS
    tipos de conteúdo este livro específico provavelmente usa.
    """
    return {
        chave: {
            "descricao": info.get("instrucoes", ""),
            "palavras_chave_exemplo": info.get("palavras_chave", []),
        }
        for chave, info in banco_templates.items()
    }


def montar_tipos_ativos(chaves_escolhidas, banco_templates):
    """
    Monta o dicionário de tipos ativos para um PDF específico: só as chaves
    escolhidas na Fase 0 (ou informadas manualmente / usadas como fallback),
    mais o tipo de regra genérico, que está sempre disponível.
    """
    tipos_ativos = {chave: banco_templates[chave] for chave in chaves_escolhidas if chave in banco_templates}
    tipos_ativos["regra"] = TEMPLATE_REGRA_FALLBACK
    return tipos_ativos


# =============================================================================
# UTILIDADES GERAIS
# =============================================================================

def slugify(texto, tamanho_maximo=60):
    """Converte um título em um identificador seguro para nome de arquivo/XML id."""
    texto = unicodedata.normalize("NFKD", texto).encode("ascii", "ignore").decode("ascii")
    texto = texto.lower()
    texto = re.sub(r"[^a-z0-9]+", "_", texto).strip("_")
    texto = texto[:tamanho_maximo].rstrip("_")
    return texto or "sem_titulo"


def identificar_tipo(titulo_capitulo, tipos_ativos):
    """
    Decide o tipo de conteúdo a partir do título do capítulo, dentre os tipos
    ativos para este PDF (definidos pela Fase 0 de reconhecimento, por
    --tipos-manuais, ou pelo fallback com todas as chaves do banco).
    """
    texto = titulo_capitulo.lower()
    for tipo, info in tipos_ativos.items():
        if tipo == "regra":
            continue
        if any(palavra in texto for palavra in info["palavras_chave"]):
            return tipo
    return "regra"


# =============================================================================
# LEITURA E ANÁLISE DE ESTRUTURA DO PDF (PyMuPDF)
# =============================================================================

def extrair_linhas_pdf(caminho_pdf, pagina_inicial):
    """
    Lê o PDF a partir de `pagina_inicial` (1-indexada) e retorna uma lista de
    Blocos, um por LINHA de texto (não por bloco de layout do PyMuPDF).

    Importante: processamos por linha, e não pelo "block" que o PyMuPDF
    devolve em get_text("dict"), porque esse agrupamento de layout pode juntar
    um título e o parágrafo logo abaixo dele no mesmo bloco quando não há um
    espaçamento vertical suficiente entre os dois — o que diluiria o tamanho
    de fonte do título na média do bloco inteiro e quebraria a detecção de
    título. Linhas de corpo de texto consecutivas são remontadas em parágrafos
    depois, em `agrupar_paragrafos`.

    Blocos de imagem (type != 0) são ignorados automaticamente (requisito de
    ignorar imagens/mapas/gráficos).
    """
    linhas_documento = []
    with fitz.open(caminho_pdf) as doc:
        indice_inicial = max(0, pagina_inicial - 1)
        for num_pagina in range(indice_inicial, doc.page_count):
            pagina = doc[num_pagina]
            largura_pagina = pagina.rect.width
            dados = pagina.get_text("dict")
            linhas_pagina = []

            for bloco in dados.get("blocks", []):
                if bloco.get("type") != 0:
                    continue  # ignora blocos de imagem/gráfico

                for linha in bloco.get("lines", []):
                    spans = linha.get("spans", [])
                    texto_linha = "".join(s.get("text", "") for s in spans).strip()
                    if not texto_linha:
                        continue

                    tamanhos = [s.get("size", 0) for s in spans]
                    # bit 4 (valor 16) dos "flags" de um span = negrito, conforme docs do PyMuPDF
                    negrito_votos = [bool(s.get("flags", 0) & 2**4) for s in spans]
                    tamanho = sum(tamanhos) / len(tamanhos) if tamanhos else 0.0
                    eh_negrito = (sum(negrito_votos) / len(negrito_votos)) > 0.5 if negrito_votos else False

                    x0, y0, x1, _ = linha.get("bbox", (0, 0, 0, 0))
                    coluna = 0 if (x0 + x1) / 2 < largura_pagina / 2 else 1

                    linhas_pagina.append(
                        Bloco(
                            pagina=num_pagina,
                            texto=texto_linha,
                            tamanho=tamanho,
                            negrito=eh_negrito,
                            y0=y0,
                            coluna=coluna,
                            n_palavras=len(texto_linha.split()),
                        )
                    )

            if MODO_DUAS_COLUNAS:
                # Layout de 2 colunas: lê toda a coluna esquerda antes da direita.
                # Observação: sem isso, PDFs de 2 colunas podem ter linhas fora de
                # ordem de leitura, pois o PyMuPDF geralmente segue a ordem do
                # conteúdo interno do PDF, nem sempre a ordem visual.
                linhas_pagina.sort(key=lambda b: (b.coluna, b.y0))

            linhas_documento.extend(linhas_pagina)

    return linhas_documento


def agrupar_paragrafos(linhas):
    """
    Remonta parágrafos: mescla linhas consecutivas de corpo de texto (nível 0)
    em um único Bloco de parágrafo, mantendo títulos (nível 1/2) como blocos
    isolados. Deve ser chamada DEPOIS de `marcar_niveis`.
    """
    agrupados = []
    buffer_textos = []
    buffer_pagina = [None]

    def flush():
        if buffer_textos:
            texto = " ".join(buffer_textos)
            agrupados.append(
                Bloco(
                    pagina=buffer_pagina[0],
                    texto=texto,
                    tamanho=0.0,
                    negrito=False,
                    y0=0.0,
                    coluna=0,
                    n_palavras=len(texto.split()),
                    nivel=0,
                )
            )
            buffer_textos.clear()

    for b in linhas:
        if b.nivel == 0:
            if not buffer_textos:
                buffer_pagina[0] = b.pagina
            buffer_textos.append(b.texto)
        else:
            flush()
            agrupados.append(b)
    flush()
    return agrupados


def calcular_tamanho_corpo(blocos):
    """
    Estima o tamanho de fonte do "corpo de texto" do documento: pondera cada
    tamanho pela quantidade de palavras (não pela quantidade de blocos), para
    que títulos curtos e numerosos não distorçam o resultado.
    """
    contagem = Counter()
    for b in blocos:
        chave = round(b.tamanho * 2) / 2  # arredonda para múltiplos de 0.5pt
        contagem[chave] += b.n_palavras
    if not contagem:
        return 10.0
    return contagem.most_common(1)[0][0]


def marcar_niveis(blocos, tamanho_corpo):
    """Classifica cada bloco em nível 0 (corpo), 1 (título de capítulo) ou 2 (título de entidade)."""
    limite1 = tamanho_corpo * NIVEL1_RATIO
    limite2 = tamanho_corpo * NIVEL2_RATIO
    for b in blocos:
        curto = b.n_palavras <= NIVEL_MAX_PALAVRAS_TITULO
        if curto and b.tamanho >= limite1:
            b.nivel = 1
        elif curto and (
            b.tamanho >= limite2
            or (b.negrito and b.n_palavras <= 6)
            or (b.texto.isupper() and b.n_palavras <= 8)
        ):
            b.nivel = 2
        else:
            b.nivel = 0


# =============================================================================
# SEGMENTAÇÃO EM ENTIDADES E CHUNKING POR LIMITE DE PALAVRAS
# =============================================================================

def segmentar_entidades(blocos, tipos_ativos):
    """
    Percorre os blocos em ordem e agrupa o texto em Entidades:
      - um título de nível 1 (capítulo) define o tipo de conteúdo daquele trecho,
        escolhido dentre `tipos_ativos` (o resultado da Fase 0 para este PDF);
      - um título de nível 2 (dentro do capítulo) inicia uma nova entidade/arquivo;
      - se um capítulo nunca tiver um título de nível 2, ele inteiro vira UMA
        entidade só (típico de capítulos de regras em prosa).
    """
    entidades = []
    tipo_atual = None
    entidade_atual = None

    def fechar_entidade():
        nonlocal entidade_atual
        if entidade_atual is not None:
            total_palavras = sum(len(p.split()) for p in entidade_atual.paragrafos)
            if total_palavras >= MIN_PALAVRAS_ENTIDADE:
                entidades.append(entidade_atual)
        entidade_atual = None

    for b in blocos:
        if b.nivel == 1:
            fechar_entidade()
            tipo_atual = identificar_tipo(b.texto, tipos_ativos)
            entidade_atual = Entidade(tipo=tipo_atual, titulo=b.texto)
        elif b.nivel == 2:
            if tipo_atual is None:
                tipo_atual = "regra"
            fechar_entidade()
            entidade_atual = Entidade(tipo=tipo_atual, titulo=b.texto)
        else:
            if tipo_atual is None:
                continue  # texto antes do primeiro título detectado é descartado
            if entidade_atual is None:
                entidade_atual = Entidade(tipo=tipo_atual, titulo="Conteudo")
            entidade_atual.paragrafos.append(b.texto)

    fechar_entidade()
    return entidades


def _dividir_paragrafo_longo(paragrafo, max_palavras):
    """Último recurso: divide um único parágrafo enorme em frases, nunca no meio de uma."""
    frases = re.split(r"(?<=[.!?])\s+", paragrafo)
    partes, atual, palavras_atual = [], [], 0
    for frase in frases:
        n = len(frase.split())
        if palavras_atual + n > max_palavras and atual:
            partes.append(" ".join(atual))
            atual, palavras_atual = [], 0
        atual.append(frase)
        palavras_atual += n
    if atual:
        partes.append(" ".join(atual))
    return partes


def dividir_paragrafos_em_chunks(paragrafos, max_palavras):
    """Agrupa parágrafos em chunks de até `max_palavras`, sempre em fronteira de parágrafo/frase."""
    chunks, atual, palavras_atual = [], [], 0
    for paragrafo in paragrafos:
        n = len(paragrafo.split())
        if n > max_palavras:
            if atual:
                chunks.append(" ".join(atual))
                atual, palavras_atual = [], 0
            chunks.extend(_dividir_paragrafo_longo(paragrafo, max_palavras))
            continue
        if palavras_atual + n > max_palavras and atual:
            chunks.append(" ".join(atual))
            atual, palavras_atual = [], 0
        atual.append(paragrafo)
        palavras_atual += n
    if atual:
        chunks.append(" ".join(atual))
    return chunks or [""]


# =============================================================================
# PROMPTS
# =============================================================================

PERSONA_BASE = """Você é um especialista em sistemas de RPG de mesa (D&D 5e e sistemas \
similares), trabalhando como um extrator de dados estruturados. Sua única tarefa é ler um \
trecho de texto extraído de um livro de RPG em PDF e devolver os dados em formato JSON, \
seguindo ESTRITAMENTE o formato de exemplo fornecido.

REGRAS OBRIGATÓRIAS:
1. Responda APENAS com um objeto JSON válido. Nunca inclua texto explicativo, markdown, \
   comentários, ou frases como "aqui está o JSON" antes ou depois.
2. Nunca se recuse a responder, peça desculpas, ou diga que não pode ajudar — se o texto for \
   confuso ou incompleto, extraia o que for possível e deixe os demais campos ausentes.
3. NÃO traduza o conteúdo: preserve o idioma original do texto-fonte nos valores (nomes, \
   descrições). Os NOMES DAS CHAVES do JSON devem continuar exatamente como no exemplo \
   (em inglês).
4. Convenção do JSON: chaves iniciadas com "@" viram atributos XML; a chave "#text" vira o \
   texto de um elemento que também tem atributos; uma chave cujo valor é uma lista vira \
   múltiplos elementos-irmãos com esse mesmo nome (ex.: várias "feature" dentro de "features").
5. Não invente informações que não estejam no texto fornecido. Campos não encontrados podem \
   ser omitidos ou deixados como string vazia — nunca com texto de preenchimento genérico.
6. Se o texto fornecido incluir "JSON já extraído das partes anteriores", sua resposta deve \
   ser o JSON COMPLETO e MESCLADO (o que já existia + o que você extraiu agora), pronto para \
   substituir o anterior — nunca devolva só a parte nova isolada.
"""


def construir_system_prompt(tipo_info):
    exemplo = json.dumps(tipo_info["template"], ensure_ascii=False, indent=2)
    return (
        f"{PERSONA_BASE}\n\n"
        f"{tipo_info['instrucoes']}\n\n"
        f"Formato de saída esperado (isto é apenas a FORMA do JSON, não os valores reais — "
        f"preencha com os dados reais encontrados no texto):\n{exemplo}"
    )


def construir_user_prompt(chunk_texto, idx, total, acumulado, reforco):
    partes = []
    if total > 1:
        partes.append(f"Trecho {idx + 1} de {total} desta entrada (o conteúdo pode continuar em outras partes).")
    partes.append("Texto extraído do PDF:\n" + chunk_texto)
    if acumulado is not None:
        partes.append(
            "JSON já extraído das partes anteriores desta MESMA entrada — complemente-o com as "
            "novas informações deste trecho, devolvendo o objeto completo e mesclado:\n"
            + json.dumps(acumulado, ensure_ascii=False, indent=2)
        )
    if reforco:
        partes.append(reforco)
    return "\n\n".join(partes)


# =============================================================================
# CLIENTE OLLAMA (com retry e validação)
# =============================================================================

PADROES_RECUSA = [
    "estou pronto para", "não posso ajudar", "não posso continuar",
    "i cannot", "i can't", "as an ai", "i'm sorry", "i am sorry",
    "desculpe, não posso", "não tenho capacidade", "i am not able",
    "as a language model",
]


def verificar_ollama_disponivel():
    """Checagem rápida de saúde do serviço Ollama antes de começar horas de processamento."""
    try:
        resposta = requests.get(f"{OLLAMA_HOST}/api/tags", timeout=10)
        resposta.raise_for_status()
        modelos = [m.get("name", "") for m in resposta.json().get("models", [])]
        prefixo_modelo = OLLAMA_MODEL.split(":")[0]
        if OLLAMA_MODEL not in modelos and not any(m.startswith(prefixo_modelo) for m in modelos):
            logging.warning(
                f"Modelo '{OLLAMA_MODEL}' não encontrado em 'ollama list' "
                f"(modelos disponíveis: {modelos}). Ajuste OLLAMA_MODEL no topo do script "
                f"ou rode: ollama pull {OLLAMA_MODEL}"
            )
        return True
    except requests.exceptions.RequestException as e:
        logging.error(
            f"Não foi possível conectar ao Ollama em {OLLAMA_HOST}. "
            f"Verifique se o serviço está rodando ('ollama serve'). Detalhe: {e}"
        )
        return False


def chamar_ollama(system_prompt, user_prompt):
    payload = {
        "model": OLLAMA_MODEL,
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": user_prompt},
        ],
        "format": "json",  # força o Ollama a garantir saída JSON sintaticamente válida
        "stream": False,
        "options": {
            "temperature": 0.2,
            "num_ctx": OLLAMA_NUM_CTX,
        },
    }
    resposta = requests.post(f"{OLLAMA_HOST}/api/chat", json=payload, timeout=TIMEOUT_OLLAMA)
    resposta.raise_for_status()
    dados = resposta.json()
    return dados["message"]["content"]


def limpar_e_parsear_json(bruto):
    texto = (bruto or "").strip()
    if texto.startswith("```"):
        texto = re.sub(r"^```(?:json)?\s*", "", texto)
        texto = re.sub(r"\s*```$", "", texto)
    return json.loads(texto)


def contem_boilerplate(objeto):
    texto = json.dumps(objeto, ensure_ascii=False).lower()
    return any(padrao in texto for padrao in PADROES_RECUSA)


def salvar_falha(contexto, bruto):
    pasta_falhas = PASTA_SAIDA / "_falhas"
    pasta_falhas.mkdir(parents=True, exist_ok=True)
    nome_arquivo = f"{slugify(contexto)}_{int(time.time())}.txt"
    caminho = pasta_falhas / nome_arquivo
    caminho.write_text(bruto or "(sem resposta do modelo)", encoding="utf-8")
    logging.error(f"[{contexto}] Falhou após {MAX_TENTATIVAS} tentativas. Resposta bruta salva em: {caminho}")


def extrair_entidade_chunk(tipo, chunk_texto, idx, total, acumulado, contexto, tipos_ativos):
    """Chama o Ollama para um chunk, com retry em caso de JSON malformado/recusa."""
    tipo_info = tipos_ativos[tipo]
    system_prompt = construir_system_prompt(tipo_info)
    reforco = ""
    bruto = None

    for tentativa in range(1, MAX_TENTATIVAS + 1):
        user_prompt = construir_user_prompt(chunk_texto, idx, total, acumulado, reforco)
        try:
            bruto = chamar_ollama(system_prompt, user_prompt)
        except requests.exceptions.RequestException as e:
            logging.error(f"[{contexto}] Erro de conexão com o Ollama (tentativa {tentativa}/{MAX_TENTATIVAS}): {e}")
            reforco = ""
            time.sleep(2)
            continue

        try:
            objeto = limpar_e_parsear_json(bruto)
        except json.JSONDecodeError as e:
            logging.warning(f"[{contexto}] JSON malformado na tentativa {tentativa}/{MAX_TENTATIVAS}: {e}")
            reforco = (
                "IMPORTANTE: sua resposta anterior NÃO era um JSON válido. Responda SOMENTE "
                "com o objeto JSON, sem nenhum texto antes ou depois, sem markdown."
            )
            continue

        if not isinstance(objeto, dict):
            logging.warning(f"[{contexto}] Resposta não é um objeto JSON na tentativa {tentativa}/{MAX_TENTATIVAS}.")
            reforco = "IMPORTANTE: a resposta deve ser um objeto JSON (com chaves), não uma lista ou valor solto."
            continue

        if contem_boilerplate(objeto):
            logging.warning(f"[{contexto}] Resposta parece conter texto de recusa/placeholder na tentativa {tentativa}/{MAX_TENTATIVAS}.")
            reforco = (
                "IMPORTANTE: não inclua pedidos de desculpa, recusas, ou comentários sobre a "
                "tarefa — apenas os dados extraídos no formato pedido."
            )
            continue

        return objeto  # sucesso

    salvar_falha(contexto, bruto)
    return acumulado  # mantém o que já tinha extraído; esta parte específica falhou


# =============================================================================
# FASE 0 — RECONHECIMENTO (roteador de tipos de conteúdo por PDF)
# =============================================================================

def _extrair_texto_bruto_paginas(caminho_pdf, n_paginas):
    """
    Extrai texto simples (sem análise de fonte/estrutura) das primeiras páginas
    de um PDF — usado só pela Fase 0, que precisa apenas do sumário/introdução,
    não da estrutura completa do documento.
    """
    with fitz.open(caminho_pdf) as doc:
        limite = min(n_paginas, doc.page_count)
        textos = [doc[i].get_text() for i in range(limite)]
    return "\n".join(textos)


def fase0_reconhecimento(caminho_pdf, chaves_disponiveis):
    """
    "Fase 0" — reconhecimento rápido antes de processar o PDF inteiro: lê o
    texto das primeiras páginas (capa/sumário/introdução) e pergunta ao LLM
    quais dos tipos de conteúdo disponíveis no banco de templates
    (`chaves_disponiveis`, o resumo vindo de `resumir_chaves_para_fase0`)
    este livro específico provavelmente contém.

    É isso que permite o mesmo script suportar D&D, GURPS, Cyberpunk etc. sem
    tentar encaixar tipos de conteúdo que não têm nada a ver com o livro atual
    (ex.: não perder tempo tentando extrair "vantagens" de GURPS de um livro
    de D&D).

    Retorna a lista de chaves escolhidas pelo LLM (subconjunto de
    chaves_disponiveis), ou uma lista vazia se a Fase 0 não conseguir uma
    resposta válida após as tentativas configuradas — quem chama esta função
    decide o fallback (ver processar_pdf).
    """
    texto_paginas_iniciais = _extrair_texto_bruto_paginas(caminho_pdf, PAGINAS_RECONHECIMENTO)
    if not texto_paginas_iniciais.strip():
        logging.warning(f"Fase 0: não foi possível extrair texto das primeiras páginas de '{caminho_pdf.name}'.")
        return []

    lista_chaves = list(chaves_disponiveis.keys())
    descricao_chaves = "\n".join(
        f'- "{chave}": {info["descricao"]} '
        f'(palavras-chave típicas: {", ".join(info["palavras_chave_exemplo"]) or "nenhuma"})'
        for chave, info in chaves_disponiveis.items()
    )

    system_prompt = (
        "Você é um roteador especialista em sistemas de RPG de mesa. Sua tarefa é ler um "
        "trecho do início de um livro de RPG (geralmente capa, sumário e introdução) e "
        "decidir quais dos seguintes TIPOS DE CONTEÚDO provavelmente aparecem neste livro "
        "específico:\n\n"
        f"{descricao_chaves}\n\n"
        "REGRAS OBRIGATÓRIAS:\n"
        f"1. Responda APENAS com um array JSON de strings, usando somente chaves da lista "
        f"acima ({', '.join(lista_chaves)}). Nunca invente uma chave que não esteja na lista.\n"
        "2. Não inclua texto explicativo, markdown ou comentários — apenas o array JSON.\n"
        "3. Inclua só os tipos que o sumário/introdução sugere que o livro realmente tem "
        "(ex.: se só há classes e raças, não inclua magias ou monstros).\n"
        '4. Se não for possível identificar nada com confiança, devolva um array vazio: [].'
    )
    user_prompt_base = "Texto extraído das primeiras páginas do PDF:\n\n" + texto_paginas_iniciais

    reforco = ""
    for tentativa in range(1, MAX_TENTATIVAS_FASE0 + 1):
        user_prompt = user_prompt_base if not reforco else f"{user_prompt_base}\n\n{reforco}"
        try:
            bruto = chamar_ollama(system_prompt, user_prompt)
        except requests.exceptions.RequestException as e:
            logging.error(f"Fase 0: erro de conexão com o Ollama (tentativa {tentativa}/{MAX_TENTATIVAS_FASE0}): {e}")
            time.sleep(2)
            continue

        try:
            objeto = limpar_e_parsear_json(bruto)
        except json.JSONDecodeError as e:
            logging.warning(f"Fase 0: JSON malformado na tentativa {tentativa}/{MAX_TENTATIVAS_FASE0}: {e}")
            reforco = 'IMPORTANTE: responda SOMENTE com um array JSON de strings, ex.: ["chave1", "chave2"].'
            continue

        # Tolera um modelo que envolva o array em um objeto (ex.: {"tipos": [...]})
        if isinstance(objeto, dict):
            listas_encontradas = [v for v in objeto.values() if isinstance(v, list)]
            objeto = listas_encontradas[0] if len(listas_encontradas) == 1 else None

        if not isinstance(objeto, list):
            logging.warning(f"Fase 0: resposta não é um array JSON na tentativa {tentativa}/{MAX_TENTATIVAS_FASE0}.")
            reforco = "IMPORTANTE: a resposta deve ser um array JSON de strings, não um objeto ou texto solto."
            continue

        chaves_validas = [c for c in objeto if isinstance(c, str) and c in chaves_disponiveis]
        chaves_ignoradas = [c for c in objeto if c not in chaves_validas]
        if chaves_ignoradas:
            logging.warning(
                f"Fase 0: ignorando chaves que o modelo inventou (não existem no banco de templates): {chaves_ignoradas}"
            )

        return chaves_validas  # pode ser [] — quem chama decide o fallback

    logging.warning(f"Fase 0: falhou após {MAX_TENTATIVAS_FASE0} tentativa(s) para '{caminho_pdf.name}'.")
    return []


# =============================================================================
# PIPELINE PRINCIPAL
# =============================================================================

def processar_entidade(nome_pdf, entidade, dry_run, contadores_nomes, tipos_ativos):
    tipo_info = tipos_ativos[entidade.tipo]
    slug_titulo = slugify(entidade.titulo)
    base_nome = f"{nome_pdf}_{slug_titulo}"

    n = contadores_nomes[base_nome]
    contadores_nomes[base_nome] += 1
    nome_arquivo = base_nome if n == 0 else f"{base_nome}_{n + 1}"

    subpasta = PASTA_SAIDA / tipo_info["pasta"]
    caminho_saida = subpasta / f"{nome_arquivo}.json"

    if not dry_run and PULAR_JA_PROCESSADOS and caminho_saida.exists():
        logging.info(f"[pular] Já existe: {caminho_saida.relative_to(PASTA_SAIDA)}")
        return

    max_palavras = tipo_info.get("max_palavras_chunk", MAX_PALAVRAS_PADRAO)
    chunks = dividir_paragrafos_em_chunks(entidade.paragrafos, max_palavras)

    if dry_run:
        total_palavras = sum(len(p.split()) for p in entidade.paragrafos)
        print(f"\n[{entidade.tipo}] '{entidade.titulo}' -> {caminho_saida.relative_to(PASTA_SAIDA)}")
        print(f"    {total_palavras} palavras, dividido em {len(chunks)} chunk(s) de até {max_palavras} palavras")
        for i, c in enumerate(chunks):
            preview = c[:100].replace("\n", " ")
            print(f"    chunk {i + 1}: {len(c.split())} palavras — início: {preview!r}...")
        return

    logging.info(f"Extraindo '{entidade.titulo}' ({entidade.tipo}) em {len(chunks)} chunk(s)...")
    contexto = f"{nome_pdf}/{entidade.tipo}/{entidade.titulo}"
    acumulado = None
    for i, chunk_texto in enumerate(chunks):
        acumulado = extrair_entidade_chunk(entidade.tipo, chunk_texto, i, len(chunks), acumulado, contexto, tipos_ativos)

    if not acumulado:
        logging.error(f"Nada foi extraído para '{contexto}'; nenhum arquivo salvo.")
        return

    subpasta.mkdir(parents=True, exist_ok=True)
    with open(caminho_saida, "w", encoding="utf-8") as f:
        json.dump(acumulado, f, ensure_ascii=False, indent=2)
    logging.info(f"[ok] Salvo: {caminho_saida.relative_to(PASTA_SAIDA)}")


def processar_pdf(caminho_pdf, dry_run, banco_templates, tipos_manuais_cli):
    pagina_inicial = PAGINAS_INICIAIS_POR_ARQUIVO.get(caminho_pdf.name, PAGINA_INICIAL)

    # --- Fase 0: decide quais tipos de conteúdo (dentre o banco de templates)
    # este PDF provavelmente usa, antes de gastar tempo processando o livro
    # inteiro. `tipos_manuais_cli` (já validado em main()) tem prioridade
    # absoluta; em --dry-run a Fase 0 é pulada para não depender do Ollama.
    if tipos_manuais_cli is not None:
        chaves_escolhidas = tipos_manuais_cli
        logging.info(f"Fase 0 pulada (--tipos-manuais): usando {chaves_escolhidas}")
    elif dry_run:
        logging.info("Fase 0 pulada (--dry-run): usando todas as chaves do banco de templates.")
        chaves_escolhidas = list(banco_templates.keys())
    else:
        logging.info(f"Fase 0: identificando tipos de conteúdo de '{caminho_pdf.name}'...")
        chaves_disponiveis = resumir_chaves_para_fase0(banco_templates)
        chaves_escolhidas = fase0_reconhecimento(caminho_pdf, chaves_disponiveis)
        if chaves_escolhidas:
            logging.info(f"Fase 0 identificou: {chaves_escolhidas}")
        else:
            logging.warning(
                f"Fase 0 não identificou nenhum tipo com confiança para '{caminho_pdf.name}'. "
                f"Usando TODAS as {len(banco_templates)} chave(s) do banco de templates como "
                f"fallback (ou rode novamente com --tipos-manuais chave1,chave2,... para "
                f"escolher manualmente)."
            )
            chaves_escolhidas = list(banco_templates.keys())

    tipos_ativos = montar_tipos_ativos(chaves_escolhidas, banco_templates)

    logging.info(f"Lendo '{caminho_pdf.name}' a partir da página {pagina_inicial}...")
    linhas = extrair_linhas_pdf(caminho_pdf, pagina_inicial)
    if not linhas:
        logging.warning(
            f"Nenhum texto extraído de '{caminho_pdf.name}' (PDF vazio, escaneado sem OCR, "
            f"ou página inicial além do fim do documento)."
        )
        return

    tamanho_corpo = calcular_tamanho_corpo(linhas)
    logging.info(f"Tamanho de fonte do corpo de texto detectado: {tamanho_corpo}pt")
    marcar_niveis(linhas, tamanho_corpo)
    blocos = agrupar_paragrafos(linhas)

    entidades = segmentar_entidades(blocos, tipos_ativos)
    logging.info(f"{len(entidades)} entidade(s)/seção(ões) detectada(s) em '{caminho_pdf.name}'.")

    nome_pdf = slugify(caminho_pdf.stem)
    contadores_nomes = defaultdict(int)
    for entidade in entidades:
        processar_entidade(nome_pdf, entidade, dry_run, contadores_nomes, tipos_ativos)


def configurar_logging():
    pasta_logs = PASTA_SAIDA / "_logs"
    pasta_logs.mkdir(parents=True, exist_ok=True)
    caminho_log = pasta_logs / f"execucao_{datetime.now():%Y%m%d_%H%M%S}.log"
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s [%(levelname)s] %(message)s",
        handlers=[logging.FileHandler(caminho_log, encoding="utf-8"), logging.StreamHandler(sys.stdout)],
    )


def parse_args():
    parser = argparse.ArgumentParser(
        description="Extrai conteúdo de RPG de PDFs para JSON usando um LLM local via Ollama."
    )
    parser.add_argument(
        "--dry-run", action="store_true",
        help="Mostra os capítulos/entidades e chunks detectados sem chamar o Ollama nem gravar JSON.",
    )
    parser.add_argument(
        "--pagina-inicial", type=int, default=None,
        help="Sobrescreve PAGINA_INICIAL para todos os PDFs nesta execução.",
    )
    parser.add_argument(
        "--tipos-manuais", type=str, default=None,
        help=(
            "Lista de chaves do master_db.json separadas por vírgula, para pular a Fase 0 de "
            "reconhecimento automático (ex.: --tipos-manuais dnd_class,dnd_race,dnd_item). "
            "Útil também quando a Fase 0 não está confiável para um livro específico."
        ),
    )
    return parser.parse_args()


def main():
    args = parse_args()
    global PAGINA_INICIAL
    if args.pagina_inicial is not None:
        PAGINA_INICIAL = args.pagina_inicial

    PASTA_ENTRADA.mkdir(parents=True, exist_ok=True)
    PASTA_SAIDA.mkdir(parents=True, exist_ok=True)

    if not args.dry_run:
        configurar_logging()
    else:
        logging.basicConfig(level=logging.INFO, format="%(message)s")

    try:
        banco_templates = carregar_banco_templates(CAMINHO_MASTER_DB)
    except (FileNotFoundError, ValueError) as e:
        logging.error(str(e))
        sys.exit(1)
    logging.info(
        f"Banco de templates carregado: {len(banco_templates)} tipo(s) disponível(is) "
        f"({', '.join(banco_templates.keys())})"
    )

    tipos_manuais_cli = None
    if args.tipos_manuais:
        chaves_pedidas = [c.strip() for c in args.tipos_manuais.split(",") if c.strip()]
        invalidas = [c for c in chaves_pedidas if c not in banco_templates]
        if invalidas:
            logging.warning(f"--tipos-manuais contém chaves que não existem no banco de templates, ignoradas: {invalidas}")
        tipos_manuais_cli = [c for c in chaves_pedidas if c in banco_templates]
        if not tipos_manuais_cli:
            logging.error(f"Nenhuma chave válida em --tipos-manuais. Chaves disponíveis: {', '.join(banco_templates.keys())}")
            sys.exit(1)
        logging.info(f"--tipos-manuais definido: {tipos_manuais_cli} (Fase 0 será pulada para todos os PDFs)")

    pdfs = sorted(PASTA_ENTRADA.glob("*.pdf"))
    if not pdfs:
        logging.warning(f"Nenhum PDF encontrado em '{PASTA_ENTRADA}'. Coloque os arquivos ali e rode novamente.")
        return

    if not args.dry_run and not verificar_ollama_disponivel():
        sys.exit(1)

    for caminho_pdf in pdfs:
        logging.info(f"=== Processando: {caminho_pdf.name} ===")
        try:
            processar_pdf(caminho_pdf, dry_run=args.dry_run, banco_templates=banco_templates, tipos_manuais_cli=tipos_manuais_cli)
        except Exception:
            logging.exception(f"Erro inesperado ao processar '{caminho_pdf.name}'; pulando para o próximo arquivo.")

    logging.info("Processamento concluído.")


if __name__ == "__main__":
    main()
