using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Painel de tooltip (prefab) mostrado ao passar o mouse sobre um CampanhaTokenDraggable — versão
/// enxuta de Salas/Grid/Tooltips/TokenStatBlockTooltipUI.cs: só modo Hover (some sozinho ao menor
/// movimento do mouse), sem Atacar/Curar/seleção de alvo (não fazem sentido em preparação offline).
///
/// Em vez de converter pra uma TokenStatBlockData estruturada, monta o conteúdo direto a partir do
/// EntryData do NPC usando NpcEntryFormatter — a mesma formatação já usada por CampanhaNpcCardUI —
/// como um único bloco de texto rico, então não precisa de sub-prefabs por linha.
/// </summary>
public class CampanhaTokenTooltipUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Textos")]
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private TMP_Text corpoText;

    [Tooltip("Quantos pixels o mouse precisa se mover pra considerar 'o jogador moveu o mouse' e esconder o painel.")]
    [SerializeField, Min(0f)] private float limiarMovimentoMouse = 4f;

    private RectTransform rectTransform;
    private CampanhaTokenTooltipManager manager;
    private Vector2 posicaoMouseAoAbrir;

    private void Awake()
    {
        rectTransform = transform as RectTransform;

        CanvasGroup cg = GetComponent<CanvasGroup>();
        if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    public void Configurar(EntryData dados, CampanhaTokenTooltipManager managerRef)
    {
        if (dados == null) return;

        manager = managerRef;

        if (nomeText != null)
        {
            string nome = NpcEntryFormatter.ObterNome(dados);
            nomeText.text = string.IsNullOrWhiteSpace(nome) ? "NPC sem nome" : nome;
        }

        if (corpoText != null)
            corpoText.text = MontarCorpo(dados);

        posicaoMouseAoAbrir = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        RectTransformCornerPositioner.PosicionarNoCantoDoPonto(rectTransform, posicaoMouseAoAbrir);
    }

    private static string MontarCorpo(EntryData dados)
    {
        var sb = new StringBuilder();

        foreach (FieldValue fv in dados.Fields)
        {
            if (fv?.Definition == null) continue;
            if (fv.Definition.FieldId == Dnd5eNpcFieldCatalog.NomeField.FieldId) continue;
            if (fv.Definition.FieldId == Dnd5eNpcFieldCatalog.RetratoField.FieldId) continue;
            if (!NpcEntryFormatter.TemValor(fv)) continue;

            if (fv.Definition.Type == FieldType.StringList || fv.Definition.Type == FieldType.ObjectList)
            {
                sb.AppendLine($"<b>{fv.Definition.Label}:</b>");
                foreach (string linha in NpcEntryFormatter.ObterLinhas(fv))
                    sb.AppendLine($"  • {linha}");
            }
            else
            {
                sb.AppendLine($"<b>{fv.Definition.Label}:</b> {NpcEntryFormatter.FormatarValorTexto(fv)}");
            }
        }

        return sb.Length > 0 ? sb.ToString() : "Sem campos preenchidos.";
    }

    private void Update()
    {
        if (manager == null || Mouse.current == null) return;

        Vector2 posicaoAtual = Mouse.current.position.ReadValue();
        if (Vector2.Distance(posicaoAtual, posicaoMouseAoAbrir) > limiarMovimentoMouse)
            manager.Esconder();
    }

    public void OnPointerEnter(PointerEventData eventData) => manager?.NotificarMouseEntrouNoPainel();

    public void OnPointerExit(PointerEventData eventData) => manager?.NotificarMouseSaiuDoPainel();
}
