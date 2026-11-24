using UnityEngine;
using TMPro;

// Arraste este componente em qualquer TMP_Text (rótulo comum) ou TMP_InputField (campo de
// resumo/somente leitura) para que o texto corte com "..." quando não couber na largura
// disponível, em vez de quebrar linha ou estourar o layout.
[ExecuteAlways]
[DisallowMultipleComponent]
public class TMP_TruncarComReticencias : MonoBehaviour
{
    private TMP_Text texto;
    private TMP_InputField inputField;
    private string textoCompleto;      // último valor "real" conhecido
    private string ultimoTextoEscrito; // o que nós mesmos escrevemos na malha (TMP_Text puro)
    private bool editando;

    private void Awake()
    {
        ResolverReferencias();
    }

    private void OnEnable()
    {
        if (inputField != null)
        {
            inputField.onSelect.AddListener(OnCampoSelecionado);
            inputField.onDeselect.AddListener(OnCampoDeselecionado);
        }

        AplicarTruncamento();
    }

    private void OnDisable()
    {
        if (inputField != null)
        {
            inputField.onSelect.RemoveListener(OnCampoSelecionado);
            inputField.onDeselect.RemoveListener(OnCampoDeselecionado);
        }
    }

    private void Update()
    {
        if (texto == null || editando) return;

        // Detecta o valor de origem mudando por fora (ex: FichaUI.SetFicha alterando o nome, ou
        // um Setup() de prefab reaproveitado com dados novos) e retrunca a partir do valor novo.
        if (inputField != null)
        {
            if (inputField.text != textoCompleto)
            {
                textoCompleto = inputField.text;
                AplicarTruncamento();
            }
        }
        else if (texto.text != ultimoTextoEscrito)
        {
            textoCompleto = texto.text;
            AplicarTruncamento();
        }
    }

    // Enquanto o usuário edita o campo, mostra o valor completo em vez do truncado.
    private void OnCampoSelecionado(string _)
    {
        editando = true;
        if (texto != null) texto.text = textoCompleto;
    }

    private void OnCampoDeselecionado(string valorEditado)
    {
        editando = false;
        textoCompleto = valorEditado;
        AplicarTruncamento();
    }

    private void ResolverReferencias()
    {
        if (texto != null) return;

        inputField = GetComponent<TMP_InputField>();
        texto = inputField != null ? inputField.textComponent : GetComponent<TMP_Text>();

        if (texto == null) return;

        textoCompleto = inputField != null ? inputField.text : texto.text;
    }

    public void AplicarTruncamento()
    {
        ResolverReferencias();
        if (texto == null || editando) return;

        texto.textWrappingMode = TextWrappingModes.NoWrap;

        // Overflow, não Ellipsis: o modo Ellipsis do TMP, neste projeto, acaba ocultando a linha
        // inteira em vez de só cortar quando a largura ainda não está totalmente resolvida — mesmo
        // bug já identificado e contornado em FichaGeralManager.AplicarTextoClasseSubclasse. Reproduz
        // aqui o mesmo corte manual validado lá, em vez de depender do Ellipsis nativo.
        texto.overflowMode = TextOverflowModes.Overflow;

        string resultado = ObterTextoTruncadoParaLargura(texto, textoCompleto);
        ultimoTextoEscrito = resultado;

        // Escreve direto na malha (texto.text), nunca via inputField.text/SetTextWithoutNotify —
        // isso alteraria o valor real armazenado no campo, não só a exibição.
        texto.text = resultado;
    }

    private static string ObterTextoTruncadoParaLargura(TMP_Text texto, string textoCompleto)
    {
        if (string.IsNullOrEmpty(textoCompleto)) return textoCompleto;

        float largura = texto.rectTransform.rect.width;
        if (largura <= 0f) return textoCompleto; // largura ainda não resolvida (objeto inativo) — mostra completo por ora

        if (texto.GetPreferredValues(textoCompleto).x <= largura) return textoCompleto;

        string cortado = textoCompleto;
        while (cortado.Length > 1)
        {
            cortado = cortado.Substring(0, cortado.Length - 1);
            string candidato = cortado.TrimEnd() + "...";
            if (texto.GetPreferredValues(candidato).x <= largura) return candidato;
        }
        return cortado + "...";
    }
}
