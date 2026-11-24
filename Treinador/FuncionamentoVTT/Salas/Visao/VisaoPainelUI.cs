// Painel "Content de Visão" — slider GLOBAL (aplica a todos os tokens de jogador de uma vez) + um
// slider INDIVIDUAL por token (prefab VisaoTokenSliderSlot). Mesma API de ClimaPainelUI/
// AudioTabletopPainelUI (EstaAbertoPainel/AbrirPainel/FecharPainel), usada por
// BarraFerramentasManager.ToggleVisao(), que é o ÚNICO responsável por abrir/fechar este painel.
// Só-Mestre: nenhum jogador comum deveria conseguir mexer no raio de visão de OUTRO token —
// TokenButtonUI já restringe o slider individual do PRÓPRIO token do jogador; este painel cobre o
// caso do Mestre ajustando qualquer token.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class VisaoPainelUI : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private MapManager mapManager;
    [SerializeField] private SteamLobbyManager steamLobbyManager;
    [SerializeField] private GameObject painel;
    [SerializeField] private PainelToggleAnimado animacaoPainel;

    [Header("Slider Global (aplica a TODOS os tokens de jogador de uma vez)")]
    [SerializeField] private Slider sliderGlobal;
    [SerializeField] private TMPro.TMP_Text textoGlobal;

    [Header("Slots individuais (um prefab VisaoTokenSliderSlot por token de jogador)")]
    [SerializeField] private GameObject prefabSlotToken;
    [SerializeField] private Transform containerSlots;

    private const float MetrosPorQuadrado = 1.5f;

    private readonly List<VisaoTokenSliderSlot> _slotsAtivos = new List<VisaoTokenSliderSlot>();
    private bool _atualizandoSliderGlobal;

    // Mesmo padrão duplicado 6x no projeto (BarraFerramentasManager, EfeitoClimaManager, ClimaPainelUI
    // etc) — dono do lobby Steam == Mestre da sala.
    private bool SouMestre() =>
        Steamworks.SteamClient.IsValid && steamLobbyManager?.SalaAtual?.IsOwnedBy(Steamworks.SteamClient.SteamId) == true;

    private void Awake()
    {
        if (mapManager == null) mapManager = FindFirstObjectByType<MapManager>();
        if (steamLobbyManager == null) steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();
        if (animacaoPainel == null && painel != null) animacaoPainel = painel.GetComponent<PainelToggleAnimado>();

        if (sliderGlobal != null)
        {
            sliderGlobal.minValue = MetrosPorQuadrado;
            sliderGlobal.maxValue = MetrosPorQuadrado * 40f;
            sliderGlobal.wholeNumbers = false;
            sliderGlobal.onValueChanged.RemoveListener(OnSliderGlobalAlterado);
            sliderGlobal.onValueChanged.AddListener(OnSliderGlobalAlterado);
        }
    }

    public bool EstaAbertoPainel() =>
        animacaoPainel != null ? animacaoPainel.EstaAberto() : (painel != null && painel.activeSelf);

    public void AbrirPainel()
    {
        if (animacaoPainel != null) animacaoPainel.Abrir();
        else if (painel != null) painel.SetActive(true);
        // Sempre repopula ao abrir — diferente do hub de Áudio (que só popula categoria clicada), aqui
        // a lista inteira de tokens pode ter mudado (token novo, removido, dono reatribuído) desde a
        // última vez que o painel foi aberto.
        PopularSlots();
    }

    public void FecharPainel()
    {
        if (animacaoPainel != null) animacaoPainel.Fechar();
        else if (painel != null) painel.SetActive(false);
    }

    // ── População (destrói+recria a cada abertura) ──────────────────────────

    private void PopularSlots()
    {
        if (containerSlots == null || prefabSlotToken == null || mapManager == null) return;

        for (int i = containerSlots.childCount - 1; i >= 0; i--)
            Destroy(containerSlots.GetChild(i).gameObject);
        _slotsAtivos.Clear();

        bool souMestre = SouMestre();
        List<MapTokenDraggable> tokens = mapManager.ObterTokensDeJogadorNoMapa();

        foreach (MapTokenDraggable token in tokens)
        {
            GameObject go = UIUtils.Spawn(prefabSlotToken, containerSlots, false);
            VisaoTokenSliderSlot slot = go.GetComponent<VisaoTokenSliderSlot>();
            if (slot == null)
            {
                Debug.LogWarning("[VisaoPainelUI] prefabSlotToken não possui VisaoTokenSliderSlot.");
                continue;
            }

            slot.Configurar(token, mapManager, souMestre);
            _slotsAtivos.Add(slot);
        }

        if (sliderGlobal != null) sliderGlobal.interactable = souMestre;
        if (textoGlobal != null) textoGlobal.text = string.Empty;

        if (containerSlots is RectTransform rt)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
    }

    // ── Slider global ────────────────────────────────────────────────────────

    private void OnSliderGlobalAlterado(float metros)
    {
        if (_atualizandoSliderGlobal || mapManager == null) return;

        if (textoGlobal != null) textoGlobal.text = $"{metros:0.0}m";

        // Aplica a todos de uma vez, notificando rede/persistindo UMA VEZ SÓ no final (ver comentário
        // em MapManager.DefinirRaioVisaoGlobalParaTodosOsTokens) — não floodar a rede a cada tick de
        // arrasto deste slider.
        mapManager.DefinirRaioVisaoGlobalParaTodosOsTokens(metros);

        // Reflete o novo valor em cada slot individual já instanciado, sem reconstruir a galeria.
        bool souMestre = SouMestre();
        foreach (VisaoTokenSliderSlot slot in _slotsAtivos)
            slot.AtualizarValor(metros, souMestre);
    }
}
