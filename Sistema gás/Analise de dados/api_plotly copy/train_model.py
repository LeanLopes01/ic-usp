"""
train_model.py — Treinamento da Rede Neural (MLP)
==================================================
Fase 3 do roadmap: Pseudo-calibração usando curvas dos datasheets.

Como funciona:
  1. Gera dados sintéticos de treinamento baseados nas curvas log-log
     de cada sensor para diferentes PPMs.
  2. Aplica as correções de temperatura e umidade de cada datasheet
     para simular o que o sensor "mediria" em diferentes condições.
  3. Treina um MLP para aprender o inverso: dado o sinal "sujo" do
     sensor + condições ambientais → PPM real compensado.
  4. Salva o modelo e o scaler para uso pelo app.py em tempo real.

Execute uma vez para gerar os arquivos de modelo:
  python train_model.py

Quando tiver dados reais rotulados (CSV com colunas definidas abaixo),
passe o argumento --dados-reais para treinar com maior precisão.

Outputs:
  models/scaler_nn.pkl   — StandardScaler para normalização
  models/model_nn.pkl    — MLPRegressor treinado
  models/model_meta.json — Metadados (features, targets, performance)
"""

import numpy as np
import pandas as pd
import json
import os
import argparse
from pathlib import Path
from sklearn.neural_network import MLPRegressor
from sklearn.preprocessing import StandardScaler
from sklearn.model_selection import train_test_split
from sklearn.metrics import mean_absolute_error, r2_score
import pickle
import warnings
warnings.filterwarnings('ignore')

# ─────────────────────────────────────────────────────────
# PARÂMETROS DOS SENSORES
# ─────────────────────────────────────────────────────────

SENSORES_NN = [
    {
        'key':         'mq135',
        'nome':        'MQ-135 (CO₂/NH₃/VOC)',
        'ratio_limpo': 3.60,
        'm': -0.417, 'b': 0.858,
        'ppm_min': 50.0,   'ppm_max': 5000.0,
        # Fatores de correção ambiental (datasheet Hanwei):
        # ratio_medido = ratio_real * fator_correcao(T, RH)
        # fator > 1 → sensor subestima resistência (aparenta mais poluição)
        'alpha_T':  -0.0060,   # efeito da temperatura
        'beta_RH':   0.0040,   # efeito da umidade
    },
    {
        'key':         'mq131',
        'nome':        'MQ-131 (O₃)',
        'ratio_limpo': 10.00,
        'm': -0.450, 'b': 0.750,
        'ppm_min': 0.005, 'ppm_max': 20.0,
        'alpha_T': -0.0080,
        'beta_RH':  0.0060,
    },
    {
        'key':         'mq2',
        'nome':        'MQ-2 (GLP/Fumaça)',
        'ratio_limpo': 9.83,
        'm': -0.380, 'b': 1.130,
        'ppm_min': 100.0, 'ppm_max': 10000.0,
        'alpha_T': -0.0050,
        'beta_RH':  0.0030,
    },
    {
        'key':         'mq7',
        'nome':        'MQ-7 (CO)',
        'ratio_limpo': 27.50,
        'm': -0.706, 'b': 1.148,
        'ppm_min': 5.0,   'ppm_max': 500.0,
        'alpha_T': -0.0055,
        'beta_RH':  0.0045,
    },
    {
        'key':         'mq136',
        'nome':        'MQ-136 (H₂S)',
        'ratio_limpo': 3.60,
        'm': -0.550, 'b': 0.800,
        'ppm_min': 0.1,   'ppm_max': 50.0,
        'alpha_T': -0.0070,
        'beta_RH':  0.0050,
    },
    {
        'key':         'mq137',
        'nome':        'MQ-137 (NH₃)',
        'ratio_limpo': 1.50,
        'm': -0.650, 'b': 0.850,
        'ppm_min': 5.0,   'ppm_max': 200.0,
        'alpha_T': -0.0065,
        'beta_RH':  0.0035,
    },
]

N_SENSORES = len(SENSORES_NN)

# Features: [ratio_1..6, temp, umidade, pressao] = 9 entradas
FEATURE_NAMES = (
    [f"ratio_{s['key']}" for s in SENSORES_NN]
    + ['temp', 'umidade', 'pressao']
)

# Targets: [ppm_1..6] = 6 saídas (regressão múltipla, Opção A do spec)
TARGET_NAMES = [f"ppm_{s['key']}" for s in SENSORES_NN]


# ─────────────────────────────────────────────────────────
# FUNÇÕES FÍSICAS DO SENSOR
# ─────────────────────────────────────────────────────────

def ppm_para_ratio(ppm: float, s: dict) -> float:
    """
    Curva característica inversa: PPM → ratio Rs/R0 (em condições de referência).
    log10(ratio) = m * log10(ppm) + b
    """
    if ppm <= 0:
        return s['ratio_limpo']
    ratio = 10.0 ** (s['m'] * np.log10(ppm) + s['b'])
    return float(np.clip(ratio, 0.01, s['ratio_limpo'] * 3))


def fator_correcao(temp: float, rh: float, s: dict) -> float:
    """
    Fator de correção ambiental para o ratio medido.
    O sensor responde diferente de 20°C, 65%RH.
    
    ratio_medido ≈ ratio_real / fator_correcao(T, RH)
    Ou seja: fator > 1 → sensor parece ver mais poluição do que há.
    
    Aproximação linear dos gráficos de correção dos datasheets.
    """
    delta_T  = temp - 20.0
    delta_RH = rh   - 65.0
    # Correção multiplicativa sobre o ratio
    fator = 1.0 + s['alpha_T'] * delta_T + s['beta_RH'] * delta_RH
    return max(fator, 0.3)  # nunca inverte o sinal


def ratio_medido(ppm_real: float, temp: float, rh: float, s: dict,
                 ruido_std: float = 0.05) -> float:
    """
    Simula o que o sensor vai medir:
      1. Calcula ratio "real" a partir do PPM
      2. Aplica distorção ambiental (T, RH)
      3. Adiciona ruído gaussiano (simulando ADC)
    """
    r_real = ppm_para_ratio(ppm_real, s)
    fc     = fator_correcao(temp, rh, s)
    r_med  = r_real / fc
    # Ruído multiplicativo (ADC noise + sensor drift)
    r_med *= (1.0 + np.random.normal(0, ruido_std))
    return float(np.clip(r_med, 0.01, 200.0))


# ─────────────────────────────────────────────────────────
# GERAÇÃO DE DADOS SINTÉTICOS (PSEUDO-CALIBRAÇÃO)
# ─────────────────────────────────────────────────────────

def gerar_dados_sinteticos(n_amostras: int = 50_000) -> pd.DataFrame:
    """
    Gera dataset sintético variando:
      - PPM de cada sensor (log-uniforme no range do sensor)
      - Temperatura: 15°C a 40°C
      - Umidade: 20% a 90%
      - Pressão: 95000 Pa a 105000 Pa (altitude 0–500m)
    """
    np.random.seed(42)
    print(f"🔧 Gerando {n_amostras:,} amostras sintéticas...")

    # Condições ambientais (distribuição realista)
    temp     = np.random.uniform(15.0, 40.0, n_amostras)
    umidade  = np.random.uniform(20.0, 90.0, n_amostras)
    pressao  = np.random.uniform(95000.0, 105000.0, n_amostras)

    # PPMs verdadeiros (log-uniforme para cobrir ordens de magnitude)
    ppms_reais = {}
    for s in SENSORES_NN:
        ppms_reais[s['key']] = np.exp(
            np.random.uniform(
                np.log(s['ppm_min']),
                np.log(s['ppm_max']),
                n_amostras
            )
        )

    # Ratios medidos (com distorção ambiental + ruído)
    rows = []
    for i in range(n_amostras):
        T, RH, P = temp[i], umidade[i], pressao[i]

        # Cenário: ar limpo 20% das amostras
        limpo = np.random.random() < 0.20

        feats  = []
        tgts   = []

        for s in SENSORES_NN:
            if limpo:
                ppm_real = s['ppm_min'] * 0.5  # quase ar limpo
            else:
                ppm_real = ppms_reais[s['key']][i]

            # Ruído mais alto em temperaturas extremas (comportamento realista)
            noise = 0.05 + 0.02 * abs(T - 20) / 20.0

            r_med = ratio_medido(ppm_real, T, RH, s, ruido_std=noise)
            feats.append(r_med)
            tgts.append(ppm_real)

        feats += [T, RH, P]
        rows.append(feats + tgts)

    cols  = FEATURE_NAMES + TARGET_NAMES
    df    = pd.DataFrame(rows, columns=cols)

    # Remove linhas com NaN ou infinitos (falhas na simulação)
    df.replace([np.inf, -np.inf], np.nan, inplace=True)
    df.dropna(inplace=True)

    print(f"   Dataset gerado: {len(df):,} amostras válidas.")
    return df


# ─────────────────────────────────────────────────────────
# TREINAMENTO
# ─────────────────────────────────────────────────────────

def treinar(df: pd.DataFrame) -> tuple:
    """
    Treina o MLP e retorna (scaler, modelo, métricas).
    
    Arquitetura do MLP conforme spec do usuário:
      Input:   9 features (6 ratios + T, H, P)
      Hidden1: 128 neurônios, ReLU
      Hidden2: 64  neurônios, ReLU
      Hidden3: 32  neurônios, ReLU
      Output:  6  neurônios (PPM de cada sensor — regressão)
    
    Os PPMs são treinados no espaço log para estabilidade
    (distribuição log-normal é mais adequada para concentrações).
    """
    X = df[FEATURE_NAMES].values

    # Transformação log dos targets (PPM em escala log é mais estável)
    Y_raw = df[TARGET_NAMES].values
    Y_raw = np.clip(Y_raw, 1e-6, None)
    Y     = np.log10(Y_raw)

    # Split treino/validação
    X_train, X_val, Y_train, Y_val = train_test_split(
        X, Y, test_size=0.15, random_state=42
    )

    # Normaliza as features (obrigatório para MLP — escalas muito diferentes)
    scaler  = StandardScaler()
    X_train = scaler.fit_transform(X_train)
    X_val   = scaler.transform(X_val)

    print("🧠 Treinando rede neural MLP...")
    print(f"   Arquitetura: {len(FEATURE_NAMES)} → 128 → 64 → 32 → {len(TARGET_NAMES)}")
    print(f"   Amostras de treino: {len(X_train):,}")

    modelo = MLPRegressor(
        hidden_layer_sizes=(128, 64, 32),
        activation='relu',
        solver='adam',
        alpha=0.001,          # L2 regularization
        learning_rate='adaptive',
        learning_rate_init=0.001,
        max_iter=300,
        early_stopping=True,
        validation_fraction=0.1,
        n_iter_no_change=20,
        random_state=42,
        verbose=False
    )

    modelo.fit(X_train, Y_train)

    # Avalia no conjunto de validação
    Y_pred = modelo.predict(X_val)

    # Métricas no espaço log (log-PPM)
    mae_log = mean_absolute_error(Y_val, Y_pred)
    r2_log  = r2_score(Y_val, Y_pred)

    # Métricas no espaço linear (PPM real)
    mae_ppm = mean_absolute_error(10**Y_val, 10**Y_pred)

    print(f"\n   📊 Resultados (validação):")
    print(f"      MAE (log-PPM): {mae_log:.4f}")
    print(f"      R² (log-PPM):  {r2_log:.4f}")
    print(f"      MAE (PPM):     {mae_ppm:.2f}")

    metricas = {
        'mae_log_ppm':  round(float(mae_log), 6),
        'r2_log_ppm':   round(float(r2_log),  6),
        'mae_ppm':      round(float(mae_ppm),  4),
        'n_treino':     int(len(X_train)),
        'n_validacao':  int(len(X_val)),
        'tipo_dados':   'pseudo_calibracao',
        'iteracoes':    int(modelo.n_iter_),
    }

    # Resumo por sensor
    for i, s in enumerate(SENSORES_NN):
        mae_s = mean_absolute_error(10**Y_val[:, i], 10**Y_pred[:, i])
        r2_s  = r2_score(Y_val[:, i], Y_pred[:, i])
        print(f"      {s['nome']:<22}: MAE={mae_s:8.3f} PPM, R²={r2_s:.4f}")
        metricas[f"r2_{s['key']}"] = round(float(r2_s), 4)

    return scaler, modelo, metricas


# ─────────────────────────────────────────────────────────
# CARGA DE DADOS REAIS (quando disponíveis)
# ─────────────────────────────────────────────────────────

def carregar_dados_reais(caminho_csv: str) -> pd.DataFrame:
    """
    Carrega CSV com dados reais rotulados para treino supervisionado.
    
    Formato esperado do CSV (colunas obrigatórias):
      ratio_mq135, ratio_mq131, ratio_mq2, ratio_mq7, ratio_mq136, ratio_mq137,
      temp, umidade, pressao,
      ppm_mq135, ppm_mq131, ppm_mq2, ppm_mq7, ppm_mq136, ppm_mq137
    
    As colunas de PPM real devem vir de uma estação de referência certificada
    (ex: CETESB, INMET) co-localizada com o seu hardware.
    """
    print(f"📂 Carregando dados reais de: {caminho_csv}")
    df = pd.read_csv(caminho_csv)

    # Verifica colunas
    faltando = [c for c in FEATURE_NAMES + TARGET_NAMES if c not in df.columns]
    if faltando:
        raise ValueError(f"Colunas faltando no CSV: {faltando}")

    print(f"   {len(df):,} amostras reais carregadas.")
    return df[FEATURE_NAMES + TARGET_NAMES].dropna()


# ─────────────────────────────────────────────────────────
# MAIN
# ─────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description='Treina a rede neural AirWatch')
    parser.add_argument('--dados-reais', type=str, default=None,
                        help='Caminho para CSV com dados reais rotulados')
    parser.add_argument('--amostras', type=int, default=50_000,
                        help='Número de amostras sintéticas (padrão: 50000)')
    args = parser.parse_args()

    # Cria diretório de modelos
    Path("models").mkdir(exist_ok=True)

    # Carrega ou gera os dados
    if args.dados_reais:
        df = carregar_dados_reais(args.dados_reais)
    else:
        df = gerar_dados_sinteticos(args.amostras)

    # Treina o modelo
    scaler, modelo, metricas = treinar(df)

    # Salva os artefatos
    with open("models/scaler_nn.pkl", "wb") as f:
        pickle.dump(scaler, f)
    with open("models/model_nn.pkl", "wb") as f:
        pickle.dump(modelo, f)

    metricas['features']    = FEATURE_NAMES
    metricas['targets']     = TARGET_NAMES
    metricas['sensores']    = [s['key'] for s in SENSORES_NN]
    with open("models/model_meta.json", "w", encoding="utf-8") as f:
        json.dump(metricas, f, indent=2, ensure_ascii=False)

    print("\n✅ Modelo salvo em models/")
    print("   models/scaler_nn.pkl")
    print("   models/model_nn.pkl")
    print("   models/model_meta.json")
    print("\nExecute o app.py — ele carregará o modelo automaticamente.")


if __name__ == "__main__":
    main()