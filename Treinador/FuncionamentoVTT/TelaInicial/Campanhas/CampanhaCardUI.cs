using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CampanhaCardUI : MonoBehaviour
{
    [Header("Visuais")]
    [SerializeField] private Image bannerCampanha;
    [SerializeField] private GameObject tagBuscandoJogadores;

    [Header("Textos")]
    [SerializeField] private TMP_Text tituloText;
    [SerializeField] private TMP_Text mestreText;
    [SerializeField] private TMP_Text jogadoresText;
    [SerializeField] private TMP_Text sistemaText;

    [Header("Ações")]
    [SerializeField] private Button acaoButton;

    private Action onAcaoClick;

    public void Configurar(CampanhaData campanha, Action onClicarAcao)
    {
        if (campanha == null)
            return;

        onAcaoClick = onClicarAcao;

        if (bannerCampanha != null && campanha.banner != null)
            bannerCampanha.sprite = campanha.banner;

        if (tituloText != null)
            tituloText.text = campanha.titulo;

        if (mestreText != null)
            mestreText.text = $"Mestre: {campanha.mestre}";

        if (jogadoresText != null)
            jogadoresText.text = $"{campanha.jogadoresAtuais}/{campanha.maxJogadores} Jogadores";

        if (sistemaText != null)
            sistemaText.text = campanha.sistema;

        if (tagBuscandoJogadores != null)
            tagBuscandoJogadores.SetActive(campanha.jogadoresAtuais < campanha.maxJogadores);

        if (acaoButton != null)
        {
            acaoButton.onClick.RemoveAllListeners();
            acaoButton.onClick.AddListener(() => onAcaoClick?.Invoke());
        }
    }
}
