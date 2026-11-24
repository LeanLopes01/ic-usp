using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Uma linha clicável dentro do tooltip de stat block — usada tanto pra ações/ataques quanto pra
// magias preparadas. Prefab: Button (só pelo visual de hover/pressed) + TMP_Text(s) filhos.
//
// Clique ESQUERDO abre a linha de mira (rola ataque/dano só depois de confirmar o alvo — ver
// SelecaoAlvoManager). Clique DIREITO só mostra a descrição no chat, sem entrar em modo de mira —
// por isso dois callbacks em vez de um Action único (era só Button.onClick, que não distingue botão).
public class TokenStatBlockAcaoUI : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private TMP_Text descricaoText;
    [SerializeField] private Button botao; // mantido só pelo feedback visual de hover/pressed do Button

    private AcaoStatBlock _acao;
    private Action<AcaoStatBlock> _aoClicarEsquerdo;
    private Action<AcaoStatBlock> _aoClicarDireito;

    public void Configurar(AcaoStatBlock acao, Action<AcaoStatBlock> aoClicarEsquerdo, Action<AcaoStatBlock> aoClicarDireito)
    {
        if (acao == null) return;

        _acao = acao;
        _aoClicarEsquerdo = aoClicarEsquerdo;
        _aoClicarDireito = aoClicarDireito;

        if (nomeText != null)
            nomeText.text = acao.nome;

        if (descricaoText != null)
            descricaoText.text = acao.descricao;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_acao == null) return;

        if (eventData.button == PointerEventData.InputButton.Left)
            _aoClicarEsquerdo?.Invoke(_acao);
        else if (eventData.button == PointerEventData.InputButton.Right)
            _aoClicarDireito?.Invoke(_acao);
    }
}
