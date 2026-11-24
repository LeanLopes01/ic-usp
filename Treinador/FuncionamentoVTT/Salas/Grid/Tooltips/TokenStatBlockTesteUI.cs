using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Uma linha clicável de perícia/salvaguarda no modo Testes do tooltip de stat block — mesmo formato
// de TokenStatBlockAcaoUI.cs (Button + TMP_Text(s)), só que pra TesteStatBlock (nome + modificador
// numérico) em vez de AcaoStatBlock (nome + descrição). Clicar rola 1d20 + modificador (ver
// TokenStatBlockTooltipUI.OnClicarTeste).
public class TokenStatBlockTesteUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private TMP_Text modificadorText;
    [SerializeField] private Button botao;

    public void Configurar(TesteStatBlock teste, Action<TesteStatBlock> aoClicar)
    {
        if (teste == null) return;

        if (nomeText != null)
            nomeText.text = teste.nome;

        if (modificadorText != null)
            modificadorText.text = teste.modificador >= 0 ? $"+{teste.modificador}" : teste.modificador.ToString();

        if (botao != null)
        {
            botao.onClick.RemoveAllListeners();
            botao.onClick.AddListener(() => aoClicar?.Invoke(teste));
        }
    }
}
