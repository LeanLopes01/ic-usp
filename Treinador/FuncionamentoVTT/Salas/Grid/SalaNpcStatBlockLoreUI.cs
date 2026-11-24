using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Painel de visualização SOMENTE-LEITURA do stat block + lore de um NPC de Campanha, aberto pela
/// opção "Lore" do menu de atalho de um MapTokenDraggable com NpcIdCampanha (ver
/// MapManager.MostrarLoreDoNpc). Diferente de Campanhas/CampanhaNpcLoreUI.cs (editor completo, com
/// catálogo de itens anexáveis) — autoria de NPC continua exclusiva de Campanhas; aqui só EXIBE o
/// EntryData inteiro (todos os campos preenchidos) como um único bloco de texto rico, mesma
/// formatação usada pelo hover-tooltip de Campanhas (ver CampanhaTokenTooltipUI.MontarCorpo).
/// </summary>
public class SalaNpcStatBlockLoreUI : MonoBehaviour
{
    [Header("Cabeçalho")]
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private Image retratoImage;
    [SerializeField] private Sprite retratoFallback;

    [Header("Corpo (stat block + lore, somente leitura)")]
    [SerializeField] private TMP_Text corpoText;

    [Header("Ações")]
    [SerializeField] private Button botaoFechar;

    private Texture2D _texturaCarregada;
    private Sprite _spriteCarregado;

    private void Awake()
    {
        if (botaoFechar != null)
        {
            botaoFechar.onClick.RemoveAllListeners();
            botaoFechar.onClick.AddListener(Fechar);
        }
    }

    public void Configurar(EntryData dados)
    {
        if (dados == null) return;

        if (nomeText != null)
        {
            string nome = NpcEntryFormatter.ObterNome(dados);
            nomeText.text = string.IsNullOrWhiteSpace(nome) ? "NPC sem nome" : nome;
        }

        CarregarRetrato(NpcEntryFormatter.ObterRetratoPath(dados));

        if (corpoText != null)
            corpoText.text = MontarCorpo(dados);

        gameObject.SetActive(true);
    }

    public void Fechar() => gameObject.SetActive(false);

    // Mesma formatação de CampanhaTokenTooltipUI.MontarCorpo — todos os campos preenchidos do NPC
    // (stat block mecânico + lore + itens anexados) num único bloco de texto rico, sem sub-prefabs
    // por linha (não precisa de interação — é só leitura).
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

    private void CarregarRetrato(string imagePath)
    {
        if (retratoImage == null) return;
        LimparRetratoRuntime();

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            retratoImage.sprite = retratoFallback;
            retratoImage.preserveAspect = true;
            return;
        }

        byte[] bytes = File.ReadAllBytes(imagePath);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes))
        {
            Destroy(tex);
            retratoImage.sprite = retratoFallback;
            retratoImage.preserveAspect = true;
            return;
        }

        _texturaCarregada = tex;
        _spriteCarregado = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        retratoImage.sprite = _spriteCarregado;
        retratoImage.preserveAspect = true;
    }

    private void LimparRetratoRuntime()
    {
        if (_spriteCarregado != null) Destroy(_spriteCarregado);
        if (_texturaCarregada != null) Destroy(_texturaCarregada);
        _spriteCarregado = null;
        _texturaCarregada = null;
    }

    private void OnDestroy() => LimparRetratoRuntime();
}
