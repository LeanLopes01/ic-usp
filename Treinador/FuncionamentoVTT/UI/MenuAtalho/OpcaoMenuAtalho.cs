using System;

// Uma entrada do menu de atalho: o texto exibido e o que acontece ao clicar.
// Quem chama MenuAtalhoManager.Mostrar monta a lista dessas opções — o manager só interpreta.
public class OpcaoMenuAtalho
{
    public string texto;
    public Action onClicar;
    public bool interativel;
    // Marca a opção como "excluir" — MenuAtalhoBotaoUI usa isso pra trocar o visual de hover do
    // botão (sprite/cor próprios) via HoverGradientFix, em vez do degradê padrão.
    public bool ehExclusao;

    public OpcaoMenuAtalho(string texto, Action onClicar, bool interativel = true, bool ehExclusao = false)
    {
        this.texto = texto;
        this.onClicar = onClicar;
        this.interativel = interativel;
        this.ehExclusao = ehExclusao;
    }
}
