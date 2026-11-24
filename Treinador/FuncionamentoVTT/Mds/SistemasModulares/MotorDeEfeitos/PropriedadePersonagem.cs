/// <summary>Valor base + fórmula opcional de uma propriedade do personagem. Propriedades com
/// formula preenchida são DERIVADAS (ex: "mod_for" = "floor(({for}-10)/2)") e nunca sofrem
/// Modificador diretamente — elas recalculam sozinhas quando a propriedade citada na fórmula muda
/// (ver EstadoPersonagem.PropagarPropriedadesDependentes).</summary>
public class PropriedadePersonagem
{
    public string id;
    public float valorBase;
    public string formula;
}
