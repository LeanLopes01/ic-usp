using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MonstroCatalogoItemUI : MonoBehaviour
{
    [Header("Cabeçalho")]
    [SerializeField] private Image imagemToken;
    [SerializeField] private TMP_Text textoNome;
    [SerializeField] private TMP_Text textoMeta;

    [Header("Stat Block (expansível)")]
    [SerializeField] private GameObject painelStatBlock;
    [SerializeField] private TMP_Text textoStatBlock;
    [SerializeField] private Button botaoExpandir;

    [Header("Adicionar ao Tabuleiro")]
    [SerializeField] private Button botaoAdicionarAoMapa;

    private bool expandido;
    private MonstroDado dadoAtual;
    private MapManager mapManager;

    public void Setup(MonstroDado dado, Sprite token, MapManager mapManagerRef = null)
    {
        dadoAtual  = dado;
        mapManager = mapManagerRef;

        if (imagemToken != null)
        {
            imagemToken.sprite = token;
            imagemToken.enabled = token != null;
        }

        if (textoNome != null)
            textoNome.text = dado.nome;

        if (textoMeta != null)
            textoMeta.text = dado.meta;

        if (textoStatBlock != null)
            textoStatBlock.text = MontarStatBlock(dado);

        expandido = false;
        if (painelStatBlock != null)
            painelStatBlock.SetActive(false);

        if (botaoExpandir != null)
        {
            botaoExpandir.onClick.RemoveAllListeners();
            botaoExpandir.onClick.AddListener(ToggleStatBlock);
        }

        if (botaoAdicionarAoMapa != null)
        {
            botaoAdicionarAoMapa.onClick.RemoveAllListeners();
            botaoAdicionarAoMapa.onClick.AddListener(AdicionarAoMapa);
            botaoAdicionarAoMapa.interactable = mapManager != null;
        }
    }

    // Empurra o monstro pra galeria de tokens do mapa (mesmo catálogo que os tokens de ficha usam)
    // já linkado pelo id — dali o mestre arrasta pro tabuleiro como qualquer outro token.
    private void AdicionarAoMapa()
    {
        if (mapManager == null || dadoAtual == null) return;

        string caminhoToken = MonstroCatalogoLoader.ObterCaminhoTokenPorId(dadoAtual.id);
        mapManager.AdicionarTokenAoCatalogo(dadoAtual.nome, caminhoToken, monstroId: dadoAtual.id);
    }

    private void ToggleStatBlock()
    {
        expandido = !expandido;
        if (painelStatBlock != null)
            painelStatBlock.SetActive(expandido);
    }

    private static string MontarStatBlock(MonstroDado d)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(d.ca))          sb.AppendLine($"<b>CA:</b> {d.ca}");
        if (!string.IsNullOrWhiteSpace(d.hp))          sb.AppendLine($"<b>PV:</b> {d.hp}");
        if (!string.IsNullOrWhiteSpace(d.velocidade))  sb.AppendLine($"<b>Velocidade:</b> {d.velocidade}");

        bool temAtributo = !string.IsNullOrWhiteSpace(d.str) || !string.IsNullOrWhiteSpace(d.dex);
        if (temAtributo)
        {
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(d.str))   sb.Append($"<b>FOR</b> {d.str}  ");
            if (!string.IsNullOrWhiteSpace(d.dex))   sb.Append($"<b>DES</b> {d.dex}  ");
            if (!string.IsNullOrWhiteSpace(d.con))   sb.Append($"<b>CON</b> {d.con}  ");
            if (!string.IsNullOrWhiteSpace(d.intel)) sb.Append($"<b>INT</b> {d.intel}  ");
            if (!string.IsNullOrWhiteSpace(d.wis))   sb.Append($"<b>SAB</b> {d.wis}  ");
            if (!string.IsNullOrWhiteSpace(d.cha))   sb.Append($"<b>CAR</b> {d.cha}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(d.pericias))  sb.AppendLine($"<b>Perícias:</b> {d.pericias}");
        if (!string.IsNullOrWhiteSpace(d.sentidos))  sb.AppendLine($"<b>Sentidos:</b> {d.sentidos}");
        if (!string.IsNullOrWhiteSpace(d.idiomas))   sb.AppendLine($"<b>Idiomas:</b> {d.idiomas}");
        if (!string.IsNullOrWhiteSpace(d.desafio))   sb.AppendLine($"<b>Desafio:</b> {d.desafio}");

        if (d.acoes != null && d.acoes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("<b>AÇÕES</b>");
            foreach (MonstroAcao a in d.acoes)
                sb.AppendLine($"<b>{a.nome}.</b> {a.descricao.Trim()}");
        }

        return sb.ToString().Trim();
    }
}
