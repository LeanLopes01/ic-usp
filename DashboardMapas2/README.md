# Painel de gases e qualidade do ar

Dashboard Flask para os sensores do ESP32 (MQ-2/3/9/135, AHT10, SCD40, PMS5003),
com abas, tema claro/escuro e um sistema de regressão configurável que funciona
em qualquer série do painel — inclusive no IQAr.

## Como rodar

```bash
pip install -r requirements.txt
python app.py                 # http://127.0.0.1:5000
python app.py --porta 8080    # outra porta
python app.py --rede          # acessível na rede local (0.0.0.0)
python app.py --debug         # recarrega ao salvar arquivo
```

O `plotly.js` é servido pelo próprio Flask (rota `/plotly.js`), a partir do
pacote que você instalou — o painel funciona sem internet e a versão do
navegador nunca fica fora de sincronia com a do Python.

Coloque `banco_teste_gases.sql` (o dump do ArgusDB) na mesma pasta do `app.py`.
O SQLite `banco_teste_gases.sqlite` é reconstruído sozinho toda vez que o dump
muda — o dump é a fonte da verdade, o banco é só cache.

Se você já usava o app antigo, rode uma vez:

```bash
python migrar_tendencias.py   # tendencias.json → regressoes.json
```

## Estrutura

```
app.py                  rotas Flask e API
config.py               sensores, cores, limites, faixas do IQAr e as abas
dados.py                dump → SQLite, limpeza, estatísticas, IQAr, anomalias
regressao.py            modelos, ajuste, métricas, bandas e persistência
aparencia.py            títulos, nomes de eixo, tamanhos de fonte e escala do X
graficos.py             construtores de figuras Plotly
templates/index.html    página única com as abas
static/css/estilo.css   estilo
static/js/painel.js     abas, gráficos e editor de regressões
regressoes.json         criado no primeiro salvamento
aparencia.json          criado no primeiro ajuste de aparência
```

Cada arquivo tem uma responsabilidade só. Para incluir um sensor novo, basta
acrescentar uma entrada em `config.py` (`SENSORES_*`) e citar a chave em `ABAS` —
gráficos, cartões de leitura, correlação e regressões passam a enxergá-lo sem
nenhuma outra alteração.

## Abas

| Aba | O que traz |
|---|---|
| Visão geral | os quatro grupos empilhados |
| Gases (MOS) | comparativo + um card por sensor, com série, distribuição e estatísticas |
| Clima | temperatura e umidade em eixos independentes + cards individuais |
| CO₂ | SCD40 com escala que começa na menor leitura |
| Particulado | PM1.0/2.5/10 juntos e separados |
| IQAr | índice CONAMA 506/2024 com faixas de fundo e sub-índices por poluente |
| Análise | correlação de Pearson, Isolation Forest e dispersão entre duas séries |
| Aparência | título, nomes de eixo, tamanhos de fonte e escala do eixo X, gráfico por gráfico |
| Regressões | tudo que está salvo, agrupado por série |

O seletor de período e o botão de tema valem para o painel inteiro. Os gráficos
chegam como JSON do Plotly (sem iframes), então trocar de aba não recarrega a
página.

## Aparência

A aba **Aparência** ajusta cada gráfico separadamente. Escolha o gráfico na
lista, edite os campos e veja o resultado na prévia logo abaixo — as mudanças
são gravadas em `aparencia.json` conforme você digita.

**Título:** texto próprio (vazio mantém o título automático) e tamanho da fonte.

**Eixos:** nome e dois tamanhos independentes para cada eixo — o dos números e o
do nome. O eixo Y da direita só tem efeito nos gráficos de dois eixos, como
temperatura × umidade.

**Escala do eixo X:** data e hora (padrão), **dias decorridos** ou horas
decorridas. Em dias, o eixo vira 1, 2, 3… com marcas de 1 em 1 e sem casas
decimais, contando a partir da primeira leitura do período carregado —
`Primeiro dia` escolhe se a contagem começa em 1 ou em 0. Histogramas, box plots
e o gráfico de dispersão não são afetados, porque o X deles não é tempo.

**Usar tamanhos e escala em todos** grava os tamanhos de fonte e a escala do eixo
X como padrão do painel inteiro. Títulos e nomes de eixo ficam de fora — não
faria sentido repetir o mesmo texto em todo gráfico. Gráficos que já têm ajuste
próprio continuam com o deles; **Restaurar padrão** devolve um gráfico ao
comportamento global.

## Regressões

O botão **Regressões** em qualquer card abre a gaveta de edição. Quando o
gráfico tem várias séries (temperatura × umidade, por exemplo), aparecem chips
para escolher em qual delas a linha entra.

**Modelos:** linear, polinomial (grau 1–6), exponencial, logarítmica, potência,
média móvel e Savitzky-Golay.

**Métodos de ajuste do modelo linear:** mínimos quadrados, Huber, RANSAC e
Theil-Sen. Os três últimos ignoram outliers — úteis quando um pico de sensor
puxaria a reta inteira.

**Ajustes por linha:** trecho ajustado (início/fim), projeção em horas antes e
depois do trecho, banda de confiança ou de predição com nível configurável, cor,
espessura, tipo de traço, opacidade, presença na legenda e um interruptor
ativa/inativa que preserva a configuração.

**Métricas:** R², R² ajustado, RMSE, MAE, número de pontos e — no modelo linear —
a inclinação em unidade/hora e unidade/dia. O botão **Testar ajuste** calcula
tudo isso sem salvar nada, então dá para comparar modelos antes de decidir.
Depois de salvar, as mesmas métricas aparecem embaixo do gráfico.

O eixo x do ajuste é sempre "horas desde o primeiro ponto do trecho", por isso a
inclinação sai numa unidade que se lê direto: `10,7 ppm/h`.

### Formato do `regressoes.json`

```json
{
  "co2": [
    {
      "id": "a1b2c3d4",
      "ativo": true,
      "modelo": "linear",
      "metodo": "theilsen",
      "grau": 2,
      "janela": 12,
      "inicio": "2026-08-27T08:00",
      "fim": null,
      "nome": "subida da manhã",
      "cor": "#7c8cff",
      "espessura": 2.5,
      "tracejado": "dash",
      "opacidade": 1.0,
      "legenda": true,
      "mostrar_equacao": true,
      "mostrar_r2": true,
      "extrapolar_horas": 4,
      "extrapolar_antes_horas": 0,
      "banda": "predicao",
      "nivel": 0.95
    }
  ]
}
```

Datas em branco usam a ponta dos dados carregados. O arquivo pode ser editado à
mão e versionado junto com o projeto.

## API

| Rota | Método | O que faz |
|---|---|---|
| `/api/figura/<nome>` | GET | figura em JSON + resumo das regressões (`horas`, `tema` e os parâmetros da figura vão na query) |
| `/api/status` | GET | última leitura, variação e alerta de cada sensor, mais o IQAr atual |
| `/api/series` | GET | catálogo de séries e limites de tempo dos dados |
| `/api/regressoes` | GET | tudo que está salvo, agrupado por série |
| `/api/regressoes/<serie>` | GET, POST | lista e cria |
| `/api/regressoes/<serie>/<id>` | PATCH, DELETE | edita e remove |
| `/api/regressoes/<serie>/simular` | POST | ajusta e devolve as métricas sem salvar |
| `/api/cards` | GET | lista dos gráficos personalizáveis |
| `/api/aparencia` | GET | arquivo inteiro (global + por gráfico) |
| `/api/aparencia/global` | GET, PUT | padrão de todos os gráficos |
| `/api/aparencia/<card>` | GET, PUT, DELETE | ajuste de um gráfico (DELETE volta ao global) |

A query aceita `card=<id>` para aplicar a aparência salva daquele gráfico.

Nomes de figura: `visao_geral`, `sensor` (`chave`, `vista`), `combo` (`chaves`
separadas por vírgula, `secundario`), `iqa`, `subindices`, `correlacao`,
`anomalias`, `dispersao` (`x`, `y`).

## Tabela do IQAr

A faixa N1 (índice 0–40) vem do Anexo II da Resolução CONAMA 506/2024. As faixas
N2–N5 foram derivadas alinhando cada categoria do índice aos padrões PI-4 a PI-1
do Anexo I. Se a sua referência trouxer valores diferentes, ajuste
`BREAKPOINTS_IQA` em `config.py` — é o único lugar que precisa mudar.
