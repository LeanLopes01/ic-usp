using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TelaResumoManager : MonoBehaviour
{
    public Transform contentScrollView;
    public GameObject resumoItemPrefab;

    [Header("Opcional: Separador de Nível")]
    public GameObject nivelSeparadorPrefab;

    // Rastreia a qual ficha o resumo exibido pertence
    private int contextoFichaId = -1;

    private void OnEnable()
    {
        // Se a ficha ativa mudou, invalida contexto local para forçar reconstrução limpa
        int fichaAtualId = GlobalFichaData.fichasDndId;
        if (contextoFichaId != fichaAtualId)
            contextoFichaId = fichaAtualId;

        ConstruirResumo();
    }

    private void OnDisable()
    {
        // Invalida contexto ao sair para que a próxima abertura sempre rebuilde
        contextoFichaId = -1;
    }

    public void ConstruirResumo()
    {
        foreach (Transform child in contentScrollView)
            Destroy(child.gameObject);

        var listaOrdenada = FichaLogManager.ObterHistoricoOrdenado();

        if (listaOrdenada.Count == 0)
        {
            // Estado vazio: exibe aviso informativo
            if (nivelSeparadorPrefab != null)
            {
                GameObject aviso = Instantiate(nivelSeparadorPrefab, contentScrollView);
                var avisoText = aviso.GetComponentInChildren<TMP_Text>();
                if (avisoText != null)
                    avisoText.text = "Nenhuma escolha registrada ainda.\nComplete as abas de Raça, Classe e Antecedente.";
            }
        }
        else
        {
            var agrupadoPorNivel = listaOrdenada.GroupBy(x => x.nivel);

            foreach (var grupo in agrupadoPorNivel)
            {
                if (nivelSeparadorPrefab != null)
                {
                    GameObject sepGo = Instantiate(nivelSeparadorPrefab, contentScrollView);
                    sepGo.GetComponentInChildren<TMP_Text>().text = $"--- NÍVEL {grupo.Key} ---";
                }

                var agrupadoPorCategoria = grupo.GroupBy(x => x.categoria);

                foreach (var catGroup in agrupadoPorCategoria)
                {
                    string textoCompleto = "";
                    foreach (var log in catGroup)
                        textoCompleto += $"• {log.conteudo}\n";

                    GameObject itemGo = Instantiate(resumoItemPrefab, contentScrollView);
                    ResumoItemUI uiItem = itemGo.GetComponent<ResumoItemUI>();
                    uiItem.Setup(catGroup.Key, textoCompleto.TrimEnd());
                }
            }
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentScrollView.GetComponent<RectTransform>());
    }
}