using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Painel de progresso mostrado ao cliente enquanto recebe o mapa via P2P.
/// Ligar ao NetworkSyncManager via SerializeField.
/// </summary>
public class DownloadMapUI : MonoBehaviour
{
    [SerializeField] private GameObject    painelRaiz;
    [SerializeField] private Slider        barraProgresso;
    [SerializeField] private TextMeshProUGUI txtPercentagem;

    private void Awake()
    {
        if (painelRaiz == null) painelRaiz = gameObject;
        painelRaiz.SetActive(false);
    }

    public void MostrarUI()
    {
        painelRaiz.SetActive(true);
        if (barraProgresso != null) barraProgresso.value = 0f;
        if (txtPercentagem  != null) txtPercentagem.text  = "0%";
    }

    public void AtualizarProgresso(int chunksRecebidos, int totalChunks)
    {
        if (totalChunks <= 0) return;
        float t = (float)chunksRecebidos / totalChunks;

        if (barraProgresso != null) barraProgresso.value = t;
        if (txtPercentagem  != null) txtPercentagem.text  = $"{t * 100f:0}%";
    }

    public void EsconderUI()
    {
        painelRaiz.SetActive(false);
    }
}
