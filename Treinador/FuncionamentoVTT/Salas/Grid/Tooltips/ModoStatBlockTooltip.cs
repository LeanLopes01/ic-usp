// As 4 formas de abrir o TokenStatBlockTooltipUI:
//   Hover  — passivo, 2s de mouse parado sobre o token. Mostra tudo, some ao mover o mouse.
//   Atacar — clicado no menu de atalho. Interativo: ações/ataques + todas as magias preparadas.
//   Curar  — clicado no menu de atalho. Interativo: magias de cura no lugar das magias e
//            itens de cura do inventário no lugar das ações/ataques.
//   Testes — clicado no menu de atalho. Interativo: TODAS as perícias e salvaguardas (treinadas ou
//            não), cada uma clicável — rola 1d20 + modificador e posta no chat (ver
//            TokenStatBlockTooltipUI.OnClicarTeste).
public enum ModoStatBlockTooltip
{
    Hover,
    Atacar,
    Curar,
    Testes,
}
