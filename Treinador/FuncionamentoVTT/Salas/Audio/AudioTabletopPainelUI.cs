using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Uma categoria (aba) do painel Tabletop Audio — botão + painel + content, mesma forma de
/// ConteudoSubAbaSlot (BarraFerramentasManager), mas independente dele: Audio não compartilha
/// população de dados com Conteudo/MapManager/SalaAbasFichasManager, então não há razão pra viver lá.</summary>
[Serializable]
public class CategoriaAudioSlot
{
    public string nomeExibicao;          // rótulo do tooltip hover, ex: "Ambientes Naturais"
    public TipoCategoriaAudio categoria;

    [Header("UI")]
    public Button botaoToggle;
    public GameObject painel;
    public Transform content;
    public GameObject destaque;          // opcional — indicador visual de aba selecionada
}

/// <summary>
/// Painel "Tabletop Audio" — mesma API de ClimaPainelUI (EstaAbertoPainel/AbrirPainel/FecharPainel),
/// usada por BarraFerramentasManager.ToggleAudio(), que é o ÚNICO responsável por abrir/fechar este
/// painel (sem botão de toggle próprio aqui). Dentro, 13 categorias com exclusão mútua entre si
/// (segundo nível, análogo a ToggleSubAbaConteudo, mas isolado nesta classe).
/// </summary>
public class AudioTabletopPainelUI : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private AudioTabletopManager audioManager;
    [SerializeField] private SteamLobbyManager steamLobbyManager;
    [SerializeField] private GameObject painel;
    [SerializeField] private PainelToggleAnimado animacaoPainel;

    [Header("Prefab do item de som (compartilhado por todas as categorias)")]
    [SerializeField] private GameObject prefabItemSom; // precisa ter SomTabletopItemUI

    [Header("Categorias (13 entradas, uma por TipoCategoriaAudio)")]
    [SerializeField] private List<CategoriaAudioSlot> categorias = new List<CategoriaAudioSlot>();

    private CategoriaAudioSlot categoriaAberta;
    // Itens instanciados da categoria ATUALMENTE aberta — atualizados em vigor por índice quando um
    // volume muda, sem destruir/recriar (ver AoVolumeAlterado); só PopularCategoria destrói/recria,
    // e só ao trocar de aba.
    private readonly Dictionary<int, SomTabletopItemUI> itensAbertos = new Dictionary<int, SomTabletopItemUI>();

    private bool SouMestre() =>
        Steamworks.SteamClient.IsValid && steamLobbyManager?.SalaAtual?.IsOwnedBy(Steamworks.SteamClient.SteamId) == true;

    private void Awake()
    {
        if (audioManager == null) audioManager = FindFirstObjectByType<AudioTabletopManager>();
        if (steamLobbyManager == null) steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();
        if (animacaoPainel == null && painel != null) animacaoPainel = painel.GetComponent<PainelToggleAnimado>();

        foreach (CategoriaAudioSlot slot in categorias)
        {
            CategoriaAudioSlot capturado = slot;
            if (capturado.botaoToggle == null) continue;
            capturado.botaoToggle.onClick.RemoveListener(() => ToggleCategoria(capturado));
            capturado.botaoToggle.onClick.AddListener(() => ToggleCategoria(capturado));

            ConfigurarTooltipHover(capturado.botaoToggle, capturado.nomeExibicao);
        }
    }

    private void OnEnable()
    {
        if (audioManager != null) audioManager.OnVolumeAlterado += AoVolumeAlterado;
    }

    private void OnDisable()
    {
        if (audioManager != null) audioManager.OnVolumeAlterado -= AoVolumeAlterado;
    }

    public bool EstaAbertoPainel() =>
        animacaoPainel != null ? animacaoPainel.EstaAberto() : (painel != null && painel.activeSelf);

    public void AbrirPainel()
    {
        if (animacaoPainel != null) animacaoPainel.Abrir();
        else if (painel != null) painel.SetActive(true);
        // Não força repopulação aqui — cada categoria só popula quando clicada (ToggleCategoria),
        // pra não instanciar as 13 galerias de uma vez ao simplesmente abrir o hub.
    }

    public void FecharPainel()
    {
        if (animacaoPainel != null) animacaoPainel.Fechar();
        else if (painel != null) painel.SetActive(false);
        // A categoria interna aberta NÃO é fechada aqui — reabrir o hub volta direto na mesma
        // categoria, já populada.
    }

    // ── Exclusão mútua interna (13 categorias) ──────────────────────────────

    private void ToggleCategoria(CategoriaAudioSlot slot)
    {
        if (slot == null) return;
        bool jaAberta = categoriaAberta == slot && slot.painel != null && slot.painel.activeSelf;

        FecharTodasCategoriasInterno();
        if (jaAberta) return;

        if (slot.painel != null) slot.painel.SetActive(true);
        if (slot.content != null) slot.content.gameObject.SetActive(true);
        if (slot.destaque != null) slot.destaque.SetActive(true);
        categoriaAberta = slot;
        PopularCategoria(slot);
    }

    private void FecharTodasCategoriasInterno()
    {
        foreach (CategoriaAudioSlot slot in categorias)
        {
            if (slot.content != null) slot.content.gameObject.SetActive(false);
            if (slot.painel != null) slot.painel.SetActive(false);
            if (slot.destaque != null) slot.destaque.SetActive(false);
        }
        categoriaAberta = null;
        itensAbertos.Clear();
    }

    // ── População (destrói+recria — só ao trocar de categoria, nunca por volume) ─

    private void PopularCategoria(CategoriaAudioSlot slot)
    {
        if (slot?.content == null || prefabItemSom == null || audioManager == null) return;

        for (int i = slot.content.childCount - 1; i >= 0; i--)
            Destroy(slot.content.GetChild(i).gameObject);
        itensAbertos.Clear();

        IReadOnlyList<SomTabletopEntry> biblioteca = audioManager.ObterBiblioteca();
        bool souMestre = SouMestre();

        for (int i = 0; i < biblioteca.Count; i++)
        {
            SomTabletopEntry entrada = biblioteca[i];
            if (entrada == null || entrada.categoria != slot.categoria) continue;

            GameObject go = UIUtils.Spawn(prefabItemSom, slot.content, false);
            SomTabletopItemUI item = go.GetComponent<SomTabletopItemUI>();
            if (item == null) { Debug.LogWarning("[AudioTabletop] Prefab não possui SomTabletopItemUI."); continue; }

            int indice = i; // captura por valor
            item.Configurar(entrada, indice, audioManager.ObterVolume(indice), souMestre,
                (idx, vol) => audioManager.DefinirVolume(idx, vol));
            itensAbertos[indice] = item;
        }

        if (slot.content is RectTransform rt)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
    }

    // ── Atualização ao vivo (rede ou local) sem reconstruir a galeria ───────

    private void AoVolumeAlterado(int indice)
    {
        if (categoriaAberta == null) return; // nenhuma categoria visível agora — nada a fazer

        if (indice < 0) { PopularCategoria(categoriaAberta); return; } // reset em massa

        if (itensAbertos.TryGetValue(indice, out SomTabletopItemUI item) && item != null)
            item.AtualizarValor(audioManager.ObterVolume(indice), SouMestre());
        // índice de outra categoria não instanciada agora — reflete quando o usuário abrir aquela aba
    }

    // Liga hover PointerEnter/Exit numa aba de categoria pro tooltip compartilhado de
    // BarraFerramentasManager (MostrarNomeHover/EsconderNomeHover são públicos precisamente pra
    // permitir esse tipo de uso self-contained) — mesmo mecanismo do ConfigurarTooltipHover privado
    // de BarraFerramentasManager, replicado aqui porque este painel é intencionalmente independente.
    private static void ConfigurarTooltipHover(Button botao, string nome)
    {
        if (botao == null || string.IsNullOrWhiteSpace(nome)) return;

        RectTransform rect = botao.transform as RectTransform;
        if (rect == null) return;

        EventTrigger trigger = botao.GetComponent<EventTrigger>();
        if (trigger != null && trigger.triggers.Exists(e => e.eventID == EventTriggerType.PointerEnter))
            return; // já configurado numa passada anterior de Awake

        if (trigger == null) trigger = botao.gameObject.AddComponent<EventTrigger>();

        EventTrigger.Entry entrar = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        // Cima: as abas de categoria ficam numa fileira perto do topo do painel, mesmo lado preferido
        // das sub-abas do Conteudo (ver DirecaoTooltip em BarraFerramentasManager).
        entrar.callback.AddListener(_ => BarraFerramentasManager.Instancia?.MostrarNomeHover(nome, rect, DirecaoTooltip.Cima));
        trigger.triggers.Add(entrar);

        EventTrigger.Entry sair = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        sair.callback.AddListener(_ => BarraFerramentasManager.Instancia?.EsconderNomeHover());
        trigger.triggers.Add(sair);
    }
}
