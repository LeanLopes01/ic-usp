using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registro de resolvedores de rolagem + os dois resolvedores desta fase (linear_check pro d20
/// padrão de D&D, dice_pool pro pool de d10 de Vampire) — ver
/// Mds/SistemasModulares/dicevtt-motor-de-efeitos.md e a proposta de "motor de sistemas modulares".
///
/// Cada resolvedor: (1) resolve os parâmetros da ação via AvaliadorDeExpressao contra o
/// EstadoPersonagem REAL do personagem — qualquer Modificador ativo numa propriedade citada na
/// fórmula (item equipado, magia) entra na rolagem automaticamente, sem nenhum código extra de
/// sincronização; (2) rola os dados com UnityEngine.Random (mesma convenção de
/// Salas/Chat/RolagemDados.cs — não System.Random); (3) injeta pseudo-propriedades transitórias
/// (total/d1/sucessos/etc.) só pra avaliar os graus de resultado configurados; (4) restaura o
/// estado, sem deixar rastro da rolagem no EstadoPersonagem real.
///
/// Extensibilidade: RegistrarResolvedor permite adicionar roll_under/savage_step/etc. numa fase
/// futura sem tocar em EstadoPersonagem/AvaliadorDeExpressao/Propriedades.
/// </summary>
public static class MotorDeDados
{
    public delegate ResultadoRolagemMotor ResolvedorDeRolagem(DefinicaoAcaoRolagem acao, DefinicaoTipoRolagem tipo, EstadoPersonagem estado);

    private static readonly Dictionary<string, ResolvedorDeRolagem> resolvedores = new Dictionary<string, ResolvedorDeRolagem>
    {
        { "linear_check", ResolverLinearCheck },
        { "dice_pool",    ResolverDicePool },
    };

    public static void RegistrarResolvedor(string idTipoRolagem, ResolvedorDeRolagem resolvedor)
    {
        if (string.IsNullOrWhiteSpace(idTipoRolagem) || resolvedor == null) return;
        resolvedores[idTipoRolagem] = resolvedor;
    }

    public static ResultadoRolagemMotor Rolar(DefinicaoAcaoRolagem acao, DefinicaoTipoRolagem tipo, EstadoPersonagem estado)
    {
        if (acao == null || tipo == null || estado == null) return null;

        if (!resolvedores.TryGetValue(tipo.id, out ResolvedorDeRolagem resolvedor))
        {
            Debug.LogWarning($"[MotorDeDados] Nenhum resolvedor registrado pro tipo de rolagem '{tipo.id}'.");
            return null;
        }

        return resolvedor(acao, tipo, estado);
    }

    // ── linear_check (D&D 5e: {count}d{size} + modifier vs cd) ──────────────────

    private static ResultadoRolagemMotor ResolverLinearCheck(DefinicaoAcaoRolagem acao, DefinicaoTipoRolagem tipo, EstadoPersonagem estado)
    {
        int count = Mathf.Max(1, Mathf.RoundToInt(ObterParametro(acao, "count", estado, 1f)));
        int size  = Mathf.Max(2, Mathf.RoundToInt(ObterParametro(acao, "size", estado, 20f)));
        float modificador = ObterParametro(acao, "modifier", estado, 0f);
        float cd = ObterParametro(acao, "cd", estado, 0f);

        var dados = new List<int>(count);
        for (int i = 0; i < count; i++)
            dados.Add(Random.Range(1, size + 1));

        int maior = dados[0];
        int menor = dados[0];
        int soma = 0;
        foreach (int d in dados)
        {
            soma += d;
            if (d > maior) maior = d;
            if (d < menor) menor = d;
        }
        float total = soma + modificador;

        var idsPseudo = new List<string> { "total", "d1", "maior", "menor", "cd", "modifier" };
        Dictionary<string, PropriedadePersonagem> salvos = SalvarPropriedades(estado, idsPseudo);
        DefinirPseudoPropriedade(estado, "total", total);
        DefinirPseudoPropriedade(estado, "d1", dados[0]);
        DefinirPseudoPropriedade(estado, "maior", maior);
        DefinirPseudoPropriedade(estado, "menor", menor);
        DefinirPseudoPropriedade(estado, "cd", cd);
        DefinirPseudoPropriedade(estado, "modifier", modificador);

        string grau = ClassificarGrau(tipo, estado);

        RestaurarPropriedades(estado, salvos, idsPseudo);

        return new ResultadoRolagemMotor
        {
            idAcao = acao.id,
            idTipoRolagem = tipo.id,
            dados = dados,
            total = total,
            grau = grau,
        };
    }

    // ── dice_pool (Vampire: {pool_size}d{dado}, sucessos por limiar, pares de máximo) ───────────

    private static ResultadoRolagemMotor ResolverDicePool(DefinicaoAcaoRolagem acao, DefinicaoTipoRolagem tipo, EstadoPersonagem estado)
    {
        int poolSize = Mathf.Max(0, Mathf.RoundToInt(ObterParametro(acao, "pool_size", estado, 0f)));
        int faces = tipo.dado > 0 ? tipo.dado : 10;

        // "threshold" por ação sobrescreve o limiarSucesso padrão do tipo de rolagem, se informado.
        int limiar = acao.parametros != null && acao.parametros.ContainsKey("threshold")
            ? Mathf.RoundToInt(ObterParametro(acao, "threshold", estado, tipo.limiarSucesso))
            : tipo.limiarSucesso;

        var dados = new List<int>(poolSize);
        for (int i = 0; i < poolSize; i++)
            dados.Add(Random.Range(1, faces + 1));

        int sucessos = ContarSucessos(dados, limiar);
        int paresMax = ContarParesDoValorMaximo(dados, tipo.parMaximo);

        var idsPseudo = new List<string> { "sucessos", "pares_max", "pool_size" };
        Dictionary<string, PropriedadePersonagem> salvos = SalvarPropriedades(estado, idsPseudo);
        DefinirPseudoPropriedade(estado, "sucessos", sucessos);
        DefinirPseudoPropriedade(estado, "pares_max", paresMax);
        DefinirPseudoPropriedade(estado, "pool_size", poolSize);

        string grau = ClassificarGrau(tipo, estado);

        RestaurarPropriedades(estado, salvos, idsPseudo);

        return new ResultadoRolagemMotor
        {
            idAcao = acao.id,
            idTipoRolagem = tipo.id,
            dados = dados,
            total = sucessos,
            grau = grau,
        };
    }

    // ── Helpers públicos SEM RNG — dá pra conferir manualmente sem depender de sorte ─────────────

    public static int ContarSucessos(List<int> dados, int limiar)
    {
        if (dados == null) return 0;
        int total = 0;
        foreach (int d in dados)
            if (d >= limiar) total++;
        return total;
    }

    public static int ContarParesDoValorMaximo(List<int> dados, int valorMaximo)
    {
        if (dados == null) return 0;
        int quantidade = 0;
        foreach (int d in dados)
            if (d == valorMaximo) quantidade++;
        return quantidade / 2;
    }

    // Primeiro grau (em ordem de autoria) cuja condição avaliar como verdadeira vence — "estado" já
    // deve ter as pseudo-propriedades da rolagem injetadas (ver os dois resolvedores acima).
    public static string ClassificarGrau(DefinicaoTipoRolagem tipo, EstadoPersonagem estado, string grauPadrao = "falha")
    {
        if (tipo?.graus == null) return grauPadrao;

        foreach (DefinicaoGrauResultado grau in tipo.graus)
        {
            if (grau == null || string.IsNullOrWhiteSpace(grau.condicao)) continue;
            if (AvaliadorDeExpressao.AvaliarCondicao(grau.condicao, estado))
                return grau.id;
        }

        return grauPadrao;
    }

    // ── Parâmetros e pseudo-propriedades ─────────────────────────────────────────

    private static float ObterParametro(DefinicaoAcaoRolagem acao, string chave, EstadoPersonagem estado, float valorPadrao)
    {
        if (acao?.parametros == null || !acao.parametros.TryGetValue(chave, out string formula) || string.IsNullOrWhiteSpace(formula))
            return valorPadrao;
        return AvaliadorDeExpressao.Avaliar(formula, estado);
    }

    // Escreve DIRETO em PropriedadesBase (não via AdicionarModificador/DefinirValorBase) de
    // propósito — pseudo-propriedades de rolagem são estado de rascunho transitório, não devem
    // disparar OnValorPropriedadeAlterado/recálculo em cascata (ver EstadoPersonagem).
    private static void DefinirPseudoPropriedade(EstadoPersonagem estado, string id, float valor)
    {
        estado.PropriedadesBase[id] = new PropriedadePersonagem { id = id, valorBase = valor };
    }

    // Salva qualquer propriedade REAL que porventura já exista sob um dos ids reservados de
    // pseudo-propriedade — defensivo: a Forja já rejeita esses ids ao criar uma Propriedade nova
    // (ver CustomSystemManager), mas um motor-de-dados.xml editado à mão poderia burlar isso.
    private static Dictionary<string, PropriedadePersonagem> SalvarPropriedades(EstadoPersonagem estado, List<string> ids)
    {
        var salvos = new Dictionary<string, PropriedadePersonagem>();
        foreach (string id in ids)
            if (estado.PropriedadesBase.TryGetValue(id, out PropriedadePersonagem existente))
                salvos[id] = existente;
        return salvos;
    }

    private static void RestaurarPropriedades(EstadoPersonagem estado, Dictionary<string, PropriedadePersonagem> salvos, List<string> ids)
    {
        foreach (string id in ids)
        {
            if (salvos.TryGetValue(id, out PropriedadePersonagem original))
                estado.PropriedadesBase[id] = original;
            else
                estado.PropriedadesBase.Remove(id);
        }
    }
}
