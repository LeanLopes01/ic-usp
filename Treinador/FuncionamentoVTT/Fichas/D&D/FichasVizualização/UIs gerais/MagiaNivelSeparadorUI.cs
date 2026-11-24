using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;

public class MagiaNivelSeparadorUI : MonoBehaviour
{
    [Header("Referências de UI")]
    public TMP_Text nivelTxt;
    public TMP_InputField espacosAtuaisInput;
    public TMP_Text espacosTotaisTxt;
    public TMP_InputField cdSalvaguardaInput;
    public TMP_InputField bonusAtaqueInput;
    public Button btnExpandirColapsar;
    public TMP_Text iconeExpandirColapsarTxt;

    [Header("Conteúdo Expansível - Magias do Nível")]
    // Container onde as magias desse nível são instanciadas. Fica aqui (e não solto em
    // magiasContent) para o botão expandir/colapsar poder animar só esse grupo, sem afetar os outros.
    public RectTransform conteudoMagiasNivel;
    public GameObject magiaItemPrefab; // Prefab de MagiaUIItem — o Manager pede pra este separador instanciar, em vez de guardar o prefab ele mesmo.

    private int espacosTotaisAtuais;

    private void Awake()
    {
        // Garante o componente no GameObject EXATO de cada campo (não no pai, onde este script
        // está) — colocar TMP_TruncarComReticencias manualmente no objeto errado no Editor faz
        // ele ficar inerte sem erro nenhum, porque GetComponent não busca em filhos.
        GarantirTruncamento(nivelTxt);
        GarantirTruncamento(espacosTotaisTxt);
        GarantirTruncamento(cdSalvaguardaInput);
        GarantirTruncamento(bonusAtaqueInput);
    }

    private static void GarantirTruncamento(Component alvo)
    {
        if (alvo == null) return;
        if (alvo.GetComponent<TMP_TruncarComReticencias>() == null)
            alvo.gameObject.AddComponent<TMP_TruncarComReticencias>();
    }

    /// <summary>
    /// Instancia uma magia dentro do content deste grupo. Quem chama ainda é responsável por
    /// chamar Setup/RegistrarAutoSave no MagiaUIItem do objeto retornado.
    /// </summary>
    public GameObject InstanciarItemMagia()
    {
        if (magiaItemPrefab == null || conteudoMagiasNivel == null) return null;

        GameObject go = Instantiate(magiaItemPrefab, conteudoMagiasNivel, false);
        go.SetActive(true);
        return go;
    }

    public void Setup(
        string nivelLabel,
        int espacosAtuais,
        int espacosTotais,
        string cdSalvaguarda,
        string bonusAtaque,
        bool expandidoInicial,
        Action<int> onEspacosAtuaisAlterado,
        Action<string> onCdSalvaguardaEditado,
        Action<string> onBonusAtaqueEditado,
        Action<bool> onToggleExpandir)
    {
        espacosTotaisAtuais = espacosTotais;

        if (nivelTxt != null) nivelTxt.text = nivelLabel;
        if (espacosAtuaisInput != null) espacosAtuaisInput.SetTextWithoutNotify(espacosAtuais.ToString());
        if (espacosTotaisTxt != null) espacosTotaisTxt.text = espacosTotais.ToString();
        if (cdSalvaguardaInput != null) cdSalvaguardaInput.SetTextWithoutNotify(cdSalvaguarda);
        if (bonusAtaqueInput != null) bonusAtaqueInput.SetTextWithoutNotify(bonusAtaque);

        if (espacosAtuaisInput != null)
        {
            espacosAtuaisInput.onEndEdit.RemoveAllListeners();
            espacosAtuaisInput.onEndEdit.AddListener((valorTexto) =>
            {
                int.TryParse(valorTexto, out int valor);
                valor = Mathf.Clamp(valor, 0, espacosTotaisAtuais);
                espacosAtuaisInput.SetTextWithoutNotify(valor.ToString());
                onEspacosAtuaisAlterado?.Invoke(valor);
            });
        }

        if (cdSalvaguardaInput != null)
        {
            cdSalvaguardaInput.onEndEdit.RemoveAllListeners();
            cdSalvaguardaInput.onEndEdit.AddListener((valorTexto) => onCdSalvaguardaEditado?.Invoke(valorTexto));
        }

        if (bonusAtaqueInput != null)
        {
            bonusAtaqueInput.onEndEdit.RemoveAllListeners();
            bonusAtaqueInput.onEndEdit.AddListener((valorTexto) => onBonusAtaqueEditado?.Invoke(valorTexto));
        }

        // Estado inicial aplicado direto (sem animação) — o Setup roda toda vez que a lista é
        // reconstruída (busca, filtro, etc.), e só o clique no botão deve animar a transição.
        AtualizarIconeExpandir(expandidoInicial);
        UIAnimacao.AplicarExpansaoInstantanea(conteudoMagiasNivel, expandidoInicial);

        if (btnExpandirColapsar != null)
        {
            bool expandidoAtual = expandidoInicial;
            RectTransform listaParaReconstruir = transform.parent as RectTransform;

            btnExpandirColapsar.onClick.RemoveAllListeners();
            btnExpandirColapsar.onClick.AddListener(() =>
            {
                expandidoAtual = !expandidoAtual;
                AtualizarIconeExpandir(expandidoAtual);
                UIAnimacao.AnimarExpansao(this, conteudoMagiasNivel, expandidoAtual, listaParaReconstruir);
                onToggleExpandir?.Invoke(expandidoAtual);
            });
        }
    }

    private void AtualizarIconeExpandir(bool expandido)
    {
        if (iconeExpandirColapsarTxt != null) iconeExpandirColapsarTxt.text = expandido ? "▼" : "▶";
    }
}
