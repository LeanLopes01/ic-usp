using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pipeline de cálculo de atributos orientado a eventos (ver dicevtt-motor-de-efeitos.md) —
/// substitui um dicionário estático simples: cada propriedade guarda só o VALOR BASE, e o valor
/// final é sempre recalculado passando os Modificadores ativos por um filtro de empilhamento e
/// prioridade. Equipar/desequipar item, ativar/expirar magia etc. só adicionam/removem
/// Modificador — nunca escrevem direto num número final — resolvendo os 3 problemas do modelo
/// antigo descritos em §1 do documento: rastreabilidade da origem, desequipamento seguro e
/// empilhamento correto.
/// </summary>
public class EstadoPersonagem
{
    public Dictionary<string, PropriedadePersonagem> PropriedadesBase = new Dictionary<string, PropriedadePersonagem>();
    public List<Modificador> ModificadoresAtivos = new List<Modificador>();
    public HashSet<string> TracosAtivos = new HashSet<string>();

    /// <summary>Disparado quando o valor CALCULADO de uma propriedade muda — a UI reativa
    /// (ver LabelReativo/CampoNumericoReativo/LinhaAtributoReativa) se inscreve nisso em vez de
    /// fazer polling a cada frame.</summary>
    public event Action<string, float> OnValorPropriedadeAlterado;

    public void AdicionarModificador(Modificador modificador)
    {
        ModificadoresAtivos.Add(modificador);
        DispararRecalculo(modificador.idPropriedade);
    }

    /// <summary>Remove TODOS os modificadores de uma origem (ex: item desequipado, magia
    /// expirada) — o chamador não precisa saber quais/quantos modificadores aquela origem tinha
    /// ativo, só o idOrigem que ele mesmo usou ao adicionar.</summary>
    public void RemoverModificadoresDaOrigem(string idOrigem)
    {
        List<Modificador> paraRemover = ModificadoresAtivos.Where(m => m.idOrigem == idOrigem).ToList();
        foreach (Modificador modificador in paraRemover)
        {
            ModificadoresAtivos.Remove(modificador);
            DispararRecalculo(modificador.idPropriedade);
        }
    }

    /// <summary>Define o valor BASE de uma propriedade diretamente (ex: jogador digitando o HP
    /// atual num campo) — dispara o mesmo recálculo/propagação de qualquer outra mudança. Só
    /// funciona pra propriedades SEM fórmula; uma derivada nunca deveria ser escrita direto (ela
    /// só muda quando a propriedade que ela referencia muda).</summary>
    public void DefinirValorBase(string idPropriedade, float novoValor)
    {
        if (!PropriedadesBase.TryGetValue(idPropriedade, out PropriedadePersonagem propriedade)) return;
        if (!string.IsNullOrEmpty(propriedade.formula)) return;

        propriedade.valorBase = novoValor;
        DispararRecalculo(idPropriedade);
    }

    public float ObterValor(string idPropriedade)
    {
        if (!PropriedadesBase.TryGetValue(idPropriedade, out PropriedadePersonagem propriedade)) return 0f;

        // Derivada por fórmula (ex: modificador de atributo) — nunca sofre Modificador
        // diretamente, ela herda o efeito ao recalcular a propriedade que ela referencia.
        if (!string.IsNullOrEmpty(propriedade.formula))
            return AvaliadorDeExpressao.Avaliar(propriedade.formula, this);

        return CalcularComModificadores(propriedade);
    }

    private float CalcularComModificadores(PropriedadePersonagem propriedade)
    {
        float valorAtual = propriedade.valorBase;

        List<Modificador> validos = ModificadoresAtivos
            .Where(m => m.idPropriedade == propriedade.id)
            .OrderBy(m => m.prioridade)
            .ToList();

        List<Modificador> empilhados = ResolverEmpilhamento(validos);

        // Ordem fixa (ver dicevtt-motor-de-efeitos.md §3): Override (o maior vence) → soma dos
        // Additive → produto dos Multiplier.
        List<Modificador> overrides = empilhados.Where(m => m.tipo == TipoModificador.Override).ToList();
        if (overrides.Count > 0)
            valorAtual = overrides.Max(m => m.valor);

        float somaAditivos = empilhados.Where(m => m.tipo == TipoModificador.Additive).Sum(m => m.valor);
        valorAtual += somaAditivos;

        float produtoMultiplicadores = empilhados
            .Where(m => m.tipo == TipoModificador.Multiplier)
            .Aggregate(1f, (acumulado, m) => acumulado * m.valor);
        valorAtual *= produtoMultiplicadores;

        return valorAtual;
    }

    // Modificadores do mesmo grupo de empilhamento não acumulam (regra clássica de RPG: dois
    // itens do mesmo bônus de armadura não somam) — só o de maior valor no grupo se aplica. Sem
    // grupo definido, todos acumulam livremente.
    private static List<Modificador> ResolverEmpilhamento(List<Modificador> modificadoresBrutos)
    {
        var resolvidos = new List<Modificador>();
        foreach (IGrouping<string, Modificador> grupo in modificadoresBrutos.GroupBy(m => m.grupoEmpilhamento))
        {
            if (string.IsNullOrEmpty(grupo.Key))
                resolvidos.AddRange(grupo);
            else
                resolvidos.Add(grupo.OrderByDescending(m => m.valor).First());
        }
        return resolvidos;
    }

    private void DispararRecalculo(string idPropriedade)
    {
        float novoValor = ObterValor(idPropriedade);
        OnValorPropriedadeAlterado?.Invoke(idPropriedade, novoValor);
        PropagarPropriedadesDependentes(idPropriedade);
    }

    // Cascata: se "for" mudou, qualquer propriedade cuja fórmula CITE "{for}" (ex: "mod_for")
    // também precisa recalcular e notificar a UI — sem isso o modificador de atributo ficaria
    // desatualizado na tela até a próxima leitura manual de ObterValor.
    private void PropagarPropriedadesDependentes(string idPropriedadeAlterada)
    {
        string referencia = "{" + idPropriedadeAlterada + "}";
        foreach (PropriedadePersonagem propriedade in PropriedadesBase.Values)
        {
            if (!string.IsNullOrEmpty(propriedade.formula) && propriedade.formula.Contains(referencia))
                DispararRecalculo(propriedade.id);
        }
    }
}
