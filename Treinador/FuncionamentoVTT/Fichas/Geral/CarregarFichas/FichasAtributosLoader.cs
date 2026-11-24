using UnityEngine;
using TMPro;

public class FichaAtributosLoader : MonoBehaviour
{
    [Header("Textos dos Atributos (VALORES)")]
    public TMP_Text forcaValorText;
    public TMP_Text destrezaValorText;
    public TMP_Text constituicaoValorText;
    public TMP_Text inteligenciaValorText;
    public TMP_Text sabedoriaValorText;
    public TMP_Text carismaValorText;

    [Header("Textos dos Modificadores")]
    public TMP_Text forcaModText;
    public TMP_Text destrezaModText;
    public TMP_Text constituicaoModText;
    public TMP_Text inteligenciaModText;
    public TMP_Text sabedoriaModText;
    public TMP_Text carismaModText;

    [Header("Feedback")]
    public TMP_Text feedbackText;
    public bool autoCarregar = true;

    void Start()
    {
        if (autoCarregar)
        {
            CarregarAtributos();
        }
    }

    void OnEnable()
    {
        if (autoCarregar)
        {
            CarregarAtributos();
        }
    }

    public void CarregarAtributos()
    {
        if (GlobalFichaData.fichasDndId <= 0)
        {
            MostrarFeedback("Nenhuma ficha carregada", true);
            return;
        }

        MostrarFeedback("Atributos locais carregados!", false);
        AtualizarUIAtributos(new AtributosResponse
        {
            atributos = new AtributosData
            {
                forca        = GlobalFichaData.forca,
                destreza     = GlobalFichaData.destreza,
                constituicao = GlobalFichaData.constituicao,
                inteligencia = GlobalFichaData.inteligencia,
                sabedoria    = GlobalFichaData.sabedoria,
                carisma      = GlobalFichaData.carisma
            }
        });
    }

    private void AtualizarUIAtributos(AtributosResponse atributos)
    {
        // Atualiza valores dos atributos
        SetText(forcaValorText, atributos.atributos.forca.ToString());
        SetText(destrezaValorText, atributos.atributos.destreza.ToString());
        SetText(constituicaoValorText, atributos.atributos.constituicao.ToString());
        SetText(inteligenciaValorText, atributos.atributos.inteligencia.ToString());
        SetText(sabedoriaValorText, atributos.atributos.sabedoria.ToString());
        SetText(carismaValorText, atributos.atributos.carisma.ToString());

        // Calcula e atualiza modificadores
        SetText(forcaModText, CalcularModificador(atributos.atributos.forca));
        SetText(destrezaModText, CalcularModificador(atributos.atributos.destreza));
        SetText(constituicaoModText, CalcularModificador(atributos.atributos.constituicao));
        SetText(inteligenciaModText, CalcularModificador(atributos.atributos.inteligencia));
        SetText(sabedoriaModText, CalcularModificador(atributos.atributos.sabedoria));
        SetText(carismaModText, CalcularModificador(atributos.atributos.carisma));

        // Atualiza também os dados globais (opcional)
        AtualizarDadosGlobais(atributos);
    }

    private string CalcularModificador(int valorAtributo)
    {
        int modificador = Mathf.FloorToInt((valorAtributo - 10) / 2.0f);
        return modificador >= 0 ? $"+{modificador}" : modificador.ToString();
    }

    private void AtualizarDadosGlobais(AtributosResponse atributos)
    {
        GlobalFichaData.forca = atributos.atributos.forca;
        GlobalFichaData.destreza = atributos.atributos.destreza;
        GlobalFichaData.constituicao = atributos.atributos.constituicao;
        GlobalFichaData.inteligencia = atributos.atributos.inteligencia;
        GlobalFichaData.sabedoria = atributos.atributos.sabedoria;
        GlobalFichaData.carisma = atributos.atributos.carisma;
    }

    private void SetText(TMP_Text textComponent, string value)
    {
        if (textComponent != null)
        {
            textComponent.text = value;
        }
    }

    private void MostrarFeedback(string mensagem, bool isError)
    {
        if (feedbackText != null)
        {
            feedbackText.text = mensagem;
            feedbackText.color = isError ? Color.red : Color.green;
            feedbackText.gameObject.SetActive(true);

            // Auto-esconder após 3 segundos
            StartCoroutine(EsconderFeedback(3f));
        }

        Debug.Log(isError ? $"ERRO: {mensagem}" : $"INFO: {mensagem}");
    }

    private System.Collections.IEnumerator EsconderFeedback(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (feedbackText != null)
            feedbackText.gameObject.SetActive(false);
    }

    // Método para forçar recarregamento (útil para botões)
    public void RecarregarAtributos()
    {
        CarregarAtributos();
    }

    private class AtributosResponse
    {
        public AtributosData atributos = new AtributosData();
    }
}