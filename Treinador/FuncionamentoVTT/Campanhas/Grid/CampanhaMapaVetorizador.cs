using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Parâmetros de calibração do CampanhaMapaVetorizador — agrupados numa classe em vez de uma lista
/// crescente de argumentos soltos, pra facilitar expor sliders no Inspector (ver CampanhaMapManager)
/// e adicionar novos knobs de calibração no futuro sem quebrar a assinatura de Vetorizar.
/// </summary>
[Serializable]
public class CampanhaMapaVetorizadorConfig
{
    [Range(0f, 1f)]
    [Tooltip("Luminância média (0=preto, 1=branco) abaixo da qual um pixel é considerado parede.")]
    public float limiarLuminancia = CampanhaMapaVetorizador.LimiarLuminanciaPadrao;

    [Range(0, 5)]
    [Tooltip("Raio (em pixels da máscara já reamostrada) da dilatação morfológica aplicada antes do " +
        "rastreamento de contorno. Funde linhas finas próximas (ex: rejuntes de muralha) numa massa " +
        "sólida só, evitando que o contorno fique 'serrilhado' seguindo cada rejunte individualmente.")]
    public int raioDilatacao = 1;

    [Range(0, 200)]
    [Tooltip("Contornos brutos (antes da simplificação RDP) com menos pontos que isso são descartados " +
        "inteiramente — mata as micro-ilhas de 'parede' gerada por ruído/sombra em terrenos rochosos.")]
    public int comprimentoMinimoContorno = 20;

    [Range(0.25f, 10f)]
    [Tooltip("Tolerância do RDP, em pixels da máscara já reamostrada — quanto maior, mais o contorno " +
        "é simplificado (menos vértices, retas mais grosseiras).")]
    public float epsilonRDP = 1.5f;
}

/// <summary>
/// Detecção automática de paredes a partir da imagem do mapa — 100% livre de grid (ver WallLine).
/// Pipeline de visão computacional em C# puro (sem lib externa):
///
/// 1) Máscara binária por luminância (pixels escuros = parede), lida em bloco via GetPixels32 (nunca
///    GetPixel por pixel) e reamostrada pra uma resolução máxima de análise.
/// 2) Dilatação morfológica: funde estruturas finas/próximas antes de traçar contorno — sem isso, o
///    contorno de uma muralha de pedra segue cada rejunte escuro em zig-zag em vez de tratar a
///    muralha como um bloco sólido.
/// 3) Contorno: Moore-Neighbor Tracing (boundary following 8-conectado) sobre a máscara já dilatada.
/// 4) Filtro de ruído: descarta contornos brutos curtos demais (micro-blobs de sombra/textura).
/// 5) Simplificação Ramer-Douglas-Peucker: reduz o contorno sobrevivente a um punhado de vértices.
///
/// Cada contorno simplificado vira uma sequência de WallLine (segmentos consecutivos do polígono
/// fechado). CampanhaMapManager depois reencadeia esses segmentos em EdgeCollider2D contíguos.
/// </summary>
public static class CampanhaMapaVetorizador
{
    public const float LimiarLuminanciaPadrao = 0.35f;

    // Teto de resolução pra análise (não afeta a imagem exibida, só a detecção) — evita que um mapa
    // 4K gere uma máscara de milhões de pixels e trave a coroutine.
    private const int ResolucaoMaximaAnalise = 800;

    // Descarta segmentos já simplificados menores que isso (ruído residual de 1-2 pixels).
    private const float ComprimentoMinimoSegmentoPx = 1.0f;

    private static readonly Vector2Int[] Direcoes8 =
    {
        new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(0, 1), new Vector2Int(-1, 1),
        new Vector2Int(-1, 0), new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
    };

    public static IEnumerator Vetorizar(Texture2D textura, CampanhaMapaVetorizadorConfig config,
        Action<float> onProgresso, Action<List<WallLine>> onConcluido)
    {
        var resultado = new List<WallLine>();
        if (textura == null || config == null)
        {
            onConcluido?.Invoke(resultado);
            yield break;
        }

        bool[,] mascara = ConstruirMascara(textura, config.limiarLuminancia, out int largura, out int altura);
        onProgresso?.Invoke(0.15f);
        yield return null;

        if (config.raioDilatacao > 0)
            mascara = Dilatar(mascara, largura, altura, config.raioDilatacao);
        onProgresso?.Invoke(0.3f);
        yield return null;

        var jaTracado = new bool[largura, altura];

        for (int y = 0; y < altura; y++)
        {
            for (int x = 0; x < largura; x++)
            {
                if (!mascara[x, y] || jaTracado[x, y]) continue;
                if (!EhPixelDeBorda(mascara, x, y, largura, altura)) continue;

                List<Vector2Int> contorno = TracarContornoMoore(mascara, jaTracado, x, y, largura, altura);

                // Filtro de ruído: um contorno bruto curto (poucos pixels de perímetro) é quase
                // sempre uma micro-ilha de sombra/textura, não uma parede real desenhada de propósito.
                if (contorno.Count < Mathf.Max(2, config.comprimentoMinimoContorno)) continue;

                AdicionarSegmentosDoContorno(contorno, largura, altura, config.epsilonRDP, resultado);
            }

            // Cede o frame a cada N linhas em vez de toda linha — ceder toda linha seria overhead
            // desnecessário em imagens altas; a cada 8 já dá responsividade suficiente pro popup.
            if (y % 8 == 0 || y == altura - 1)
            {
                onProgresso?.Invoke(0.3f + 0.7f * (y + 1) / altura);
                yield return null;
            }
        }

        onProgresso?.Invoke(1f);
        onConcluido?.Invoke(resultado);
    }

    // ── Passo 1: máscara binária ─────────────────────────────────────────────

    private static bool[,] ConstruirMascara(Texture2D textura, float limiarLuminancia, out int largura, out int altura)
    {
        int origLargura = textura.width;
        int origAltura = textura.height;
        float escala = Mathf.Min(1f, (float)ResolucaoMaximaAnalise / Mathf.Max(origLargura, origAltura));
        largura = Mathf.Max(1, Mathf.RoundToInt(origLargura * escala));
        altura = Mathf.Max(1, Mathf.RoundToInt(origAltura * escala));

        Color32[] pixels = textura.GetPixels32(); // única leitura em bloco — nunca GetPixel por pixel
        var mascara = new bool[largura, altura];

        for (int y = 0; y < altura; y++)
        {
            int origY = Mathf.Clamp(Mathf.FloorToInt(y / escala), 0, origAltura - 1);
            for (int x = 0; x < largura; x++)
            {
                int origX = Mathf.Clamp(Mathf.FloorToInt(x / escala), 0, origLargura - 1);
                mascara[x, y] = EhPixelDeParede(pixels[origY * origLargura + origX], limiarLuminancia);
            }
        }

        return mascara;
    }

    private static bool EhPixelDeParede(Color32 p, float limiarLuminancia)
    {
        if (p.a < 8) return false; // quase transparente = fora do desenho do mapa, nunca é parede
        float luminancia = (0.299f * p.r + 0.587f * p.g + 0.114f * p.b) / 255f;
        return luminancia < limiarLuminancia;
    }

    // ── Passo 2: dilatação morfológica ────────────────────────────────────────
    //
    // Estruturante quadrado de lado (2*raio+1) — a implementação NÃO faz a varredura ingênua O(raio²)
    // por pixel (custaria O(largura*altura*raio²) no total, caro pra raios maiores). Em vez disso,
    // usa o fato de que dilatação com estruturante quadrado é SEPARÁVEL: dilatar horizontalmente e
    // depois verticalmente sobre o resultado produz exatamente o mesmo resultado que dilatar nos dois
    // eixos de uma vez. Cada passe usa uma janela deslizante por CONTAGEM (não recalcula a janela
    // inteira a cada pixel — só soma/subtrai o pixel que entra/sai da janela), então cada passe é
    // O(largura*altura) e o total fica O(largura*altura) INDEPENDENTE do raio, em vez de crescer com
    // raio².

    private static bool[,] Dilatar(bool[,] mascara, int largura, int altura, int raio)
    {
        var horizontal = new bool[largura, altura];
        for (int y = 0; y < altura; y++)
        {
            int contagem = 0;
            for (int i = -raio; i <= raio; i++)
                if (i >= 0 && i < largura && mascara[i, y]) contagem++;

            for (int x = 0; x < largura; x++)
            {
                horizontal[x, y] = contagem > 0;

                int remover = x - raio;
                int adicionar = x + raio + 1;
                if (remover >= 0 && mascara[remover, y]) contagem--;
                if (adicionar < largura && mascara[adicionar, y]) contagem++;
            }
        }

        var resultado = new bool[largura, altura];
        for (int x = 0; x < largura; x++)
        {
            int contagem = 0;
            for (int j = -raio; j <= raio; j++)
                if (j >= 0 && j < altura && horizontal[x, j]) contagem++;

            for (int y = 0; y < altura; y++)
            {
                resultado[x, y] = contagem > 0;

                int remover = y - raio;
                int adicionar = y + raio + 1;
                if (remover >= 0 && horizontal[x, remover]) contagem--;
                if (adicionar < altura && horizontal[x, adicionar]) contagem++;
            }
        }

        return resultado;
    }

    private static bool EhPixelDeBorda(bool[,] mascara, int x, int y, int largura, int altura)
    {
        if (x == 0 || y == 0 || x == largura - 1 || y == altura - 1) return true;
        return !mascara[x - 1, y] || !mascara[x + 1, y] || !mascara[x, y - 1] || !mascara[x, y + 1];
    }

    // ── Passo 3: contorno (Moore-Neighbor Tracing) ───────────────────────────
    //
    // Percorre o perímetro de uma região de parede em ordem, 8-conectado. Convenção clássica: ao
    // "chegar" num pixel vindo de uma direção D, a busca pelo próximo pixel de borda começa em D+1
    // (sentido horário) e para no primeiro vizinho que também é parede. Isso naturalmente também
    // encontra o contorno de FUROS internos (uma sala vazia cercada de parede), já que o scan externo
    // trata qualquer pixel de borda não visitado como um novo ponto de partida, sem distinguir
    // "externo" de "interno".
    //
    // Ressalva conhecida: a condição de parada aqui é "voltou pro pixel inicial" (não o critério
    // completo de Jacob, que também compara a direção de chegada) — mais simples e correto pra quase
    // todo formato realista de parede; o caso degenerado é um contorno que se autointersecta num
    // único pixel (formato em "8"), que pode fechar cedo demais. Aceitável pra uma primeira versão;
    // validar contra mapas reais.

    private static List<Vector2Int> TracarContornoMoore(bool[,] mascara, bool[,] jaTracado, int startX, int startY, int largura, int altura)
    {
        var contorno = new List<Vector2Int>();
        var start = new Vector2Int(startX, startY);
        Vector2Int atual = start;

        // Convenção de inicialização padrão do Moore tracing: o raster scan varre da esquerda pra
        // direita, então o pixel "de onde viemos" é sempre o Oeste (índice 4 em Direcoes8).
        int direcaoEntrada = 4;
        bool primeiraVolta = true;
        int maxPassos = largura * altura * 4 + 8; // proteção contra loop infinito em máscara degenerada

        for (int passo = 0; passo < maxPassos; passo++)
        {
            contorno.Add(atual);
            jaTracado[atual.x, atual.y] = true;

            int buscaInicio = (direcaoEntrada + 1) % 8;
            Vector2Int proximo = atual;
            int direcaoEncontrada = -1;

            for (int i = 0; i < 8; i++)
            {
                int d = (buscaInicio + i) % 8;
                Vector2Int vizinho = atual + Direcoes8[d];
                if (vizinho.x < 0 || vizinho.y < 0 || vizinho.x >= largura || vizinho.y >= altura) continue;
                if (mascara[vizinho.x, vizinho.y])
                {
                    proximo = vizinho;
                    direcaoEncontrada = d;
                    break;
                }
            }

            if (direcaoEncontrada < 0) break; // pixel isolado (parede de 1px sem vizinhos) — contorno degenerado
            if (proximo == start && !primeiraVolta) break; // voltou ao início — contorno fechado

            primeiraVolta = false;
            direcaoEntrada = (direcaoEncontrada + 4) % 8; // direção oposta à que usamos pra chegar em 'proximo'
            atual = proximo;
        }

        return contorno;
    }

    // ── Passo 4: simplificação (Ramer-Douglas-Peucker) ───────────────────────

    // Trata o contorno fechado como uma polilinha aberta que começa e termina no mesmo ponto (anexa
    // o primeiro ponto no fim antes de simplificar) — abordagem pragmática comum pra RDP em anéis
    // fechados: os dois pontos de maior desvio dentro de cada metade acabam virando vértices de
    // qualquer forma, então o resultado fica correto na prática mesmo sem tratamento topológico completo.
    private static void AdicionarSegmentosDoContorno(List<Vector2Int> contorno, int largura, int altura, float epsilonRDP, List<WallLine> destino)
    {
        var pontos = new List<Vector2>(contorno.Count + 1);
        foreach (Vector2Int p in contorno) pontos.Add(new Vector2(p.x, p.y));
        pontos.Add(pontos[0]);

        List<Vector2> simplificado = SimplificarRDP(pontos, epsilonRDP);
        if (simplificado.Count > 1) simplificado.RemoveAt(simplificado.Count - 1); // remove o fechamento duplicado

        if (simplificado.Count < 2) return;

        for (int i = 0; i < simplificado.Count; i++)
        {
            Vector2 a = simplificado[i];
            Vector2 b = simplificado[(i + 1) % simplificado.Count];
            if (Vector2.Distance(a, b) < ComprimentoMinimoSegmentoPx) continue;

            destino.Add(new WallLine
            {
                pontoA = new Vector2(a.x / largura, a.y / altura),
                pontoB = new Vector2(b.x / largura, b.y / altura),
            });
        }
    }

    // Versão ITERATIVA do RDP — usa uma pilha explícita (na heap) em vez de recursão em C#. Um
    // contorno ruidoso pode gerar splits bem desbalanceados (o ponto de maior desvio perto de uma das
    // pontas, repetidamente), o que levava a versão recursiva original a uma profundidade de chamada
    // O(n) no pior caso — pra contornos com milhares de pontos brutos, isso estourava a pilha de
    // thread. StackOverflowException não pode ser capturada em C#/.NET: derruba o processo inteiro na
    // hora, sem diálogo de erro e quase sem log — exatamente o "vetorizou e o app fechou sozinho,
    // sem nada no console" relatado. Um Stack<T> na heap cresce dinamicamente, sem esse limite fixo.
    private static List<Vector2> SimplificarRDP(List<Vector2> pontos, float epsilon)
    {
        int n = pontos.Count;
        if (n < 3) return new List<Vector2>(pontos);

        var manter = new bool[n];
        manter[0] = true;
        manter[n - 1] = true;

        var pilha = new Stack<(int inicio, int fim)>();
        pilha.Push((0, n - 1));

        while (pilha.Count > 0)
        {
            (int inicio, int fim) = pilha.Pop();
            if (fim - inicio < 2) continue; // nenhum ponto intermediário nesse trecho

            Vector2 a = pontos[inicio];
            Vector2 b = pontos[fim];

            float distMax = 0f;
            int indiceMax = -1;
            for (int i = inicio + 1; i < fim; i++)
            {
                float dist = DistanciaPontoSegmento(pontos[i], a, b);
                if (dist > distMax) { distMax = dist; indiceMax = i; }
            }

            if (indiceMax >= 0 && distMax > epsilon)
            {
                manter[indiceMax] = true;
                pilha.Push((inicio, indiceMax));
                pilha.Push((indiceMax, fim));
            }
        }

        var resultado = new List<Vector2>();
        for (int i = 0; i < n; i++)
            if (manter[i]) resultado.Add(pontos[i]);

        return resultado;
    }

    private static float DistanciaPontoSegmento(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 1e-8f) return Vector2.Distance(p, a);

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
        Vector2 proj = a + t * ab;
        return Vector2.Distance(p, proj);
    }
}
