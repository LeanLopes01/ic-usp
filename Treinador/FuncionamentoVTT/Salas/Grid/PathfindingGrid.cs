// Dijkstra, não BFS puro: o pedido inclui custo variável (terreno difícil penaliza/impede) — BFS
// puro assume custo uniforme (1 por passo) e não modela isso corretamente. Dijkstra se reduz
// EXATAMENTE a BFS quando todos os custos são 1, então não há regressão nenhuma no caso simples (sem
// terreno difícil configurado — ver MapManager.CustoDeMovimentoDaCelula). Implementação O(V²) por
// varredura linear da fronteira (sem System.Collections.Generic.PriorityQueue<T,TPriority> — a API
// compatibility level do projeto pode não garantir essa classe do BCL do .NET 6 no runtime do Unity)
// — suficiente pro tamanho típico de um grid de combate de VTT (dezenas a poucas centenas de células).
using System;
using System.Collections.Generic;

public static class PathfindingGrid
{
    // Sem diagonais — só as 4 direções cardeais, conforme pedido.
    private static readonly (int dc, int dl)[] Direcoes4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>Menor caminho de origem até destino, 4-direcional (sem diagonais), evitando paredes
    /// (temParedeEntre retorna true = bloqueado) e ponderando terreno (custoDaCelula —
    /// float.PositiveInfinity = intransponível, tratado igual parede). Retorna null se origem/destino
    /// caem fora dos limites [0,colunas)x[0,linhas) ou se não existir caminho algum. Caso contrário,
    /// retorna a lista de células origem→destino em ordem (incluindo os dois extremos); se
    /// origem == destino, retorna uma lista de 1 elemento.</summary>
    public static List<(int col, int lin)> CalcularCaminho(
        (int col, int lin) origem, (int col, int lin) destino,
        int colunas, int linhas,
        Func<(int, int), (int, int), bool> temParedeEntre,
        Func<(int, int), float> custoDaCelula)
    {
        if (!DentroDosLimites(origem, colunas, linhas) || !DentroDosLimites(destino, colunas, linhas))
            return null;

        if (origem == destino)
            return new List<(int, int)> { origem };

        var distancia = new Dictionary<(int, int), float> { [origem] = 0f };
        var predecessor = new Dictionary<(int, int), (int, int)>();
        var visitados = new HashSet<(int, int)>();

        while (true)
        {
            (int, int) atual = default;
            float menorDistancia = float.PositiveInfinity;
            bool encontrouCandidato = false;

            // Varredura linear da fronteira (nós com distância conhecida ainda não visitados) — O(V)
            // por iteração, O(V²) no total. Sem fila de prioridade — ver justificativa no cabeçalho.
            foreach (KeyValuePair<(int, int), float> par in distancia)
            {
                if (visitados.Contains(par.Key)) continue;
                if (par.Value < menorDistancia)
                {
                    menorDistancia = par.Value;
                    atual = par.Key;
                    encontrouCandidato = true;
                }
            }

            if (!encontrouCandidato) return null; // fronteira esgotada — destino inalcançável
            if (atual == destino) return ReconstruirCaminho(predecessor, origem, destino);

            visitados.Add(atual);

            foreach ((int dc, int dl) in Direcoes4)
            {
                (int, int) vizinho = (atual.Item1 + dc, atual.Item2 + dl);
                if (!DentroDosLimites(vizinho, colunas, linhas)) continue;
                if (visitados.Contains(vizinho)) continue;
                if (temParedeEntre(atual, vizinho)) continue;

                float custoVizinho = custoDaCelula(vizinho);
                if (float.IsPositiveInfinity(custoVizinho)) continue; // intransponível (ex: lava)

                float novaDistancia = menorDistancia + custoVizinho;
                if (!distancia.TryGetValue(vizinho, out float distanciaAtualVizinho) || novaDistancia < distanciaAtualVizinho)
                {
                    distancia[vizinho] = novaDistancia;
                    predecessor[vizinho] = atual;
                }
            }
        }
    }

    private static bool DentroDosLimites((int col, int lin) c, int colunas, int linhas) =>
        c.col >= 0 && c.col < colunas && c.lin >= 0 && c.lin < linhas;

    private static List<(int col, int lin)> ReconstruirCaminho(
        Dictionary<(int, int), (int, int)> predecessor, (int col, int lin) origem, (int col, int lin) destino)
    {
        var caminho = new List<(int col, int lin)> { destino };
        (int, int) atual = destino;
        while (atual != origem)
        {
            atual = predecessor[atual];
            caminho.Add(atual);
        }
        caminho.Reverse();
        return caminho;
    }
}
