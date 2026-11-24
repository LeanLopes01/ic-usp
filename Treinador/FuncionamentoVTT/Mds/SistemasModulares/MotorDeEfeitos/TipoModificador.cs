/// <summary>Como um Modificador combina com o valor base de uma propriedade (ver
/// dicevtt-motor-de-efeitos.md §3) — ordem de aplicação fixa em
/// EstadoPersonagem.CalcularComModificadores: Override vence primeiro (o maior entre os ativos),
/// depois soma dos Additive, depois produto dos Multiplier.</summary>
public enum TipoModificador
{
    Additive,   // Soma ao valor base (ex: +2)
    Multiplier, // Multiplica o valor (ex: x1.5)
    Override,   // Substitui o valor (ex: Força se torna 19) — o MAIOR override ativo vence
    Flat        // Reservado (spec original) — define o valor final ignorando o base por completo
}
