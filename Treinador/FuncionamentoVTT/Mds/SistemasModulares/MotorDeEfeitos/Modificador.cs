/// <summary>Um bônus/penalidade rastreável aplicado a uma propriedade do personagem — nunca altera
/// PropriedadePersonagem.valorBase diretamente, só entra numa lista ativa que é resolvida a cada
/// leitura (ver EstadoPersonagem.ObterValor). Resolve os 3 problemas do dicionário estático
/// descritos em dicevtt-motor-de-efeitos.md §1: rastreabilidade da origem, remoção segura ao
/// desequipar/expirar, e regras de empilhamento.</summary>
public class Modificador
{
    public string idOrigem;          // ID do item/magia/talento que gerou este modificador
    public string idPropriedade;     // Ex: "for", "ca", "deslocamento"
    public TipoModificador tipo;
    public float valor;
    public string grupoEmpilhamento; // Modificadores do MESMO grupo não acumulam — só o maior vale
    public string condicao;          // Nome de condição avaliada externamente (ex: "esta_equipado")
    public int prioridade;           // Menor aplica primeiro
}
