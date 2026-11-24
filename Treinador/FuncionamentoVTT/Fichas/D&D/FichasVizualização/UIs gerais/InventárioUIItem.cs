using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class InventarioUIItem : MonoBehaviour
{
    [Header("Cabeçalho")]
    public TMP_Text nomeText;
    public TMP_Text categoriaText;
    public TMP_Text pesoText; // formatado "individual/total", ex: "15w/30w"
    public TMP_InputField quantidadeInput;
    public Toggle equipadoToggle;

    [Header("Controles")]
    public Button btnExpandirDetalhes;
    public Button btnColapsarDetalhes;
    public GameObject conteudoDetalhes;

    [Header("Conteúdo Expansível - Detalhes")]
    public TMP_InputField caInput;
    public TMP_InputField danoInput;
    public TMP_InputField curaInput;
    public TMP_InputField descricaoCompletaInput;

    // ca, dano e cura vêm nulos/vazios quando não se aplicam ao item (ficam ocultos na UI).
    public void Setup(
        string nome,
        string categoria,
        string pesoFormatado,
        int quantidade,
        bool equipado,
        Action<bool> onEquipadoMudou = null,
        Action<int> onQuantidadeMudou = null,
        string ca = null,
        string dano = null,
        string cura = null,
        string descricaoCompleta = null)
    {
        if (nomeText != null) nomeText.text = nome;
        if (categoriaText != null) categoriaText.text = categoria;
        if (pesoText != null) pesoText.text = pesoFormatado;

        if (quantidadeInput != null)
        {
            quantidadeInput.onEndEdit.RemoveAllListeners();
            quantidadeInput.SetTextWithoutNotify(Mathf.Max(1, quantidade).ToString());
            quantidadeInput.onEndEdit.AddListener((valorTexto) =>
            {
                if (!int.TryParse(valorTexto, out int valor) || valor < 1) valor = 1;
                quantidadeInput.SetTextWithoutNotify(valor.ToString());
                onQuantidadeMudou?.Invoke(valor);
            });
        }

        if (equipadoToggle != null)
        {
            equipadoToggle.onValueChanged.RemoveAllListeners();
            equipadoToggle.SetIsOnWithoutNotify(equipado);
            equipadoToggle.onValueChanged.AddListener((val) => onEquipadoMudou?.Invoke(val));
        }

        AtivarSeTiverValor(caInput, ca);
        AtivarSeTiverValor(danoInput, dano);
        AtivarSeTiverValor(curaInput, cura);

        if (descricaoCompletaInput != null)
            descricaoCompletaInput.SetTextWithoutNotify(string.IsNullOrEmpty(descricaoCompleta) ? "Sem descrição." : descricaoCompleta);

        ConfigurarTruncamentoComReticencias(nomeText);
        ConfigurarTruncamentoComReticencias(categoriaText);
        ConfigurarTruncamentoComReticencias(pesoText);

        ConfigurarExpansaoDetalhes();
    }

    private static void AtivarSeTiverValor(TMP_InputField campo, string valor)
    {
        if (campo == null) return;
        bool temValor = !string.IsNullOrEmpty(valor);
        campo.SetTextWithoutNotify(temValor ? valor : "");
        campo.gameObject.SetActive(temValor);
    }

    // Faz o texto cortar com "..." em vez de estourar o layout quando não cabe na largura do campo.
    private void ConfigurarTruncamentoComReticencias(TMP_Text campo)
    {
        if (campo == null) return;

        campo.textWrappingMode = TextWrappingModes.NoWrap;
        campo.overflowMode = TextOverflowModes.Ellipsis;
    }

    /// <summary>
    /// Liga o autosave genérico dos detalhes (ca, dano, cura, descrição completa).
    /// Quantidade e equipado têm callbacks próprios no Setup, pois exigem lógica extra
    /// (recalcular peso total / slots de equipamento) além de salvar.
    /// </summary>
    public void RegistrarAutoSave(Action onAlterado)
    {
        RegistrarEndEdit(caInput, onAlterado);
        RegistrarEndEdit(danoInput, onAlterado);
        RegistrarEndEdit(curaInput, onAlterado);
        RegistrarEndEdit(descricaoCompletaInput, onAlterado);
    }

    private void RegistrarEndEdit(TMP_InputField campo, Action onAlterado)
    {
        if (campo == null) return;
        campo.onEndEdit.RemoveAllListeners();
        campo.onEndEdit.AddListener((_) => onAlterado?.Invoke());
    }

    public (string ca, string dano, string cura, string descricao) ObterDadosAtuais()
    {
        return (
            caInput ? caInput.text : "",
            danoInput ? danoInput.text : "",
            curaInput ? curaInput.text : "",
            descricaoCompletaInput ? descricaoCompletaInput.text : "");
    }

    private void ConfigurarExpansaoDetalhes()
    {
        if (conteudoDetalhes != null) conteudoDetalhes.SetActive(false);
        if (btnColapsarDetalhes != null) btnColapsarDetalhes.gameObject.SetActive(false);
        if (btnExpandirDetalhes != null) btnExpandirDetalhes.gameObject.SetActive(true);

        if (btnExpandirDetalhes != null)
        {
            btnExpandirDetalhes.onClick.RemoveAllListeners();
            btnExpandirDetalhes.onClick.AddListener(() =>
            {
                if (conteudoDetalhes != null) conteudoDetalhes.SetActive(true);
                btnExpandirDetalhes.gameObject.SetActive(false);
                if (btnColapsarDetalhes != null) btnColapsarDetalhes.gameObject.SetActive(true);
            });
        }

        if (btnColapsarDetalhes != null)
        {
            btnColapsarDetalhes.onClick.RemoveAllListeners();
            btnColapsarDetalhes.onClick.AddListener(() =>
            {
                if (conteudoDetalhes != null) conteudoDetalhes.SetActive(false);
                btnColapsarDetalhes.gameObject.SetActive(false);
                if (btnExpandirDetalhes != null) btnExpandirDetalhes.gameObject.SetActive(true);
            });
        }
    }
}
