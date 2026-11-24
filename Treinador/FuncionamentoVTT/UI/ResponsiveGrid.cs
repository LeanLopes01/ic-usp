using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(GridLayoutGroup))]
[ExecuteInEditMode]
public class GridAdaptavelCentro : UIBehaviour
{
    private GridLayoutGroup gridLayout;
    private RectTransform rectTransform;

    [Header("Configurações")]
    [Tooltip("Número máximo de itens antes de pular para a linha de baixo")]
    public int maxColunas = 3;

    [Tooltip("Tamanho máximo (largura/altura) de cada célula, em pixels — nunca cresce além disso, mesmo com poucos itens e muito espaço sobrando.")]
    public float tamanhoMaximoCelula = 62f;

    protected override void Awake()
    {
        base.Awake();
        gridLayout = GetComponent<GridLayoutGroup>();
        rectTransform = GetComponent<RectTransform>();
        
        // Garante que o alinhamento base seja no centro
        gridLayout.childAlignment = TextAnchor.MiddleCenter;
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
    }

    // Esse exige o override pois faz parte do UIBehaviour
    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        AjustarTamanhoCelulas();
    }

    // CORREÇÃO: Removido o 'override' e o 'base.', pois este é um evento padrão da Unity
    private void OnTransformChildrenChanged()
    {
        AjustarTamanhoCelulas();
    }

    public void AjustarTamanhoCelulas()
    {
        if (gridLayout == null || rectTransform == null) return;

        int totalItens = transform.childCount;
        
        if (totalItens == 0) return;

        // 1. AJUSTE DINÂMICO DE COLUNAS (Para manter 1 ou 2 itens no centro perfeito)
        int colunasAtuais = Mathf.Min(totalItens, maxColunas);
        gridLayout.constraintCount = colunasAtuais;

        // 2. CÁLCULO DE DIMINUIÇÃO DAS CÉLULAS PARA CABER NO ESPAÇO MÁXIMO
        // Descobre quantas linhas existem no momento (arredondando para cima)
        int linhas = Mathf.CeilToInt((float)totalItens / maxColunas);

        // Pega o tamanho real disponível do painel subtraindo as margens (padding)
        float larguraDisponivel = rectTransform.rect.width - gridLayout.padding.left - gridLayout.padding.right;
        float alturaDisponivel = rectTransform.rect.height - gridLayout.padding.top - gridLayout.padding.bottom;

        // Desconta os espaços entre os itens (spacing) — pelas colunas/linhas REAIS de agora, não o
        // máximo possível, senão 1 item já reserva espaço de spacing pras outras 2 colunas vazias.
        float espacoSpacingX = gridLayout.spacing.x * (colunasAtuais - 1);
        float espacoSpacingY = gridLayout.spacing.y * (Mathf.Max(1, linhas) - 1);

        // A largura é dividida pelas colunas REAIS de agora (não maxColunas) — com 1 item só,
        // colunasAtuais é 1, então a célula pode crescer até ocupar a largura toda (antes disso ficava
        // sempre do tamanho de "1/maxColunas", ficando pequena demais com poucos itens).
        float larguraCelula = (larguraDisponivel - espacoSpacingX) / colunasAtuais;

        // A altura vai encolhendo conforme mais linhas são criadas
        float alturaCelula = (alturaDisponivel - espacoSpacingY) / linhas;

        // 3. APLICAÇÃO DO TAMANHO
        // Pegamos o menor valor (Mathf.Min) entre largura/altura disponíveis E o teto configurado —
        // garante quadrado perfeito, nunca vaza pelas laterais/por baixo, e nunca cresce além do
        // tamanho máximo mesmo com pouquíssimos itens e muito espaço sobrando.
        float tamanhoFinal = Mathf.Min(larguraCelula, alturaCelula, tamanhoMaximoCelula);

        gridLayout.cellSize = new Vector2(tamanhoFinal, tamanhoFinal);
    }
}