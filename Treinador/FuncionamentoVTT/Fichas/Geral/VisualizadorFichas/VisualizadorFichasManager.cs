using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Orquestra o visualizador de fichas agrupado por sistema.
/// Cada grupo gera um SistemaCardUI instanciado dentro de sistemaContainer.
///
/// Setup no Inspector:
///   sistemaCardPrefab → prefab com SistemaCardUI
///   fichaCardPrefab   → prefab com FichaCardMiniUI
///   sistemaContainer  → Content do ScrollView vertical
/// </summary>
public class VisualizadorFichasManager : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject sistemaCardPrefab;

    [Header("Container")]
    public Transform sistemaContainer;

    [Header("Telas (redirecionamento)")]
    public GameObject telaCriacaoFichaDnd;
    public GameObject telaDisplayFichas;
    public GameObject telaFichaDnd;

    private Coroutine _rotina;

    // ─────────────────────────────────────────────────────────────────
    private void OnEnable()
    {
        if (DataManager.Instance != null)
            DataManager.Instance.OnDataUpdated += OnDataAtualizado;

        if (gameObject.activeInHierarchy)
            IniciarCarregamento();
    }

    private void OnDisable()
    {
        if (DataManager.Instance != null)
            DataManager.Instance.OnDataUpdated -= OnDataAtualizado;

        if (_rotina != null) { StopCoroutine(_rotina); _rotina = null; }
    }

    // ─────────────────────────────────────────────────────────────────
    private void IniciarCarregamento()
    {
        if (_rotina != null) StopCoroutine(_rotina);
        _rotina = StartCoroutine(RotinaCarregamento());
    }

    private IEnumerator RotinaCarregamento()
    {
        yield return new WaitForEndOfFrame();

        if (DataManager.Instance == null)
        {
            PopupManager.Instance?.MostrarErro("Erro", "Gerenciador de dados não encontrado.");
            yield break;
        }

        if (DataManager.Instance.HasData)
        {
            Popular(DataManager.Instance.GetFichasCopy());
        }
        else
        {
            PopupManager.Instance?.MostrarCarregamento("Carregando", "Buscando seus personagens...");
            int pid = PlayerSession.Instance != null ? PlayerSession.Instance.playerId : 1;
            DataManager.Instance.StartFetch(pid, false);
        }

        _rotina = null;
    }

    private void OnDataAtualizado(FichaData[] novasFichas)
    {
        // Fecha o popup de carregamento sempre que os dados chegarem, mesmo que este painel não
        // esteja ativo no momento — do contrário o popup (compartilhado globalmente via
        // PopupManager) fica preso na tela indefinidamente (mesmo motivo documentado em
        // FichaLoaderUI.OnDataUpdated).
        PopupManager.Instance?.Fechar();
        if (!gameObject.activeInHierarchy) return;
        if (novasFichas == null || novasFichas.Length == 0) return;
        Popular(novasFichas);
    }

    // ─────────────────────────────────────────────────────────────────
    private void Popular(FichaData[] fichas)
    {
        if (sistemaContainer == null || sistemaCardPrefab == null) return;

        // Limpa sistema cards antigos
        foreach (Transform c in sistemaContainer) Destroy(c.gameObject);

        int    pid = DataManager.Instance != null ? DataManager.Instance.GetCurrentId()      : 0;
        string sid = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        // Agrupa fichas por sistema_nome (vem do snapshot local)
        var grupos = new Dictionary<string, List<FichaCardData>>(StringComparer.OrdinalIgnoreCase);

        foreach (FichaData f in fichas)
        {
            if (f == null) continue;

            string sistemaNome = "Outros";
            string imagemPath  = "";

            if (LocalFichaStorage.TryGetFichaSnapshot(sid, pid, f.fichas_general_id, f.fichas_dnd_id, out LocalFichaData snap))
            {
                if (!string.IsNullOrWhiteSpace(snap.sistema_nome))
                    sistemaNome = snap.sistema_nome;
                imagemPath = snap.token_image_path ?? "";
            }

            if (!grupos.ContainsKey(sistemaNome))
                grupos[sistemaNome] = new List<FichaCardData>();

            grupos[sistemaNome].Add(new FichaCardData
            {
                nome       = f.name_pc,
                classe     = f.class_pc,
                generalId  = f.fichas_general_id,
                dndId      = f.fichas_dnd_id,
                level      = f.level_pc,
                imagemPath = imagemPath
            });
        }

        // Instancia um SistemaCardUI por sistema
        foreach (var kvp in grupos)
        {
            GameObject go   = Instantiate(sistemaCardPrefab, sistemaContainer);
            var        card = go.GetComponent<SistemaCardUI>();
            if (card == null) continue;

            card.Setup(kvp.Key, kvp.Value, TratarCliqueFicha);
        }

        Canvas.ForceUpdateCanvases();
        if (sistemaContainer is RectTransform rt)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    // ─────────────────────────────────────────────────────────────────
    private void TratarCliqueFicha(int generalId, int dndId, string nome, int level, bool incompleta)
    {
        // Salva ficha aberta antes de trocar
        if (GlobalFichaData.fichasDndId > 0 && GlobalFichaData.fichasDndId != dndId)
        {
            FichaSaveManager.Instance?.SalvarLocalSincronamente();
            GlobalFichaData.inventarioJson  = null;
            GlobalFichaData.magiasJson      = null;
            GlobalFichaData.slotsMagiasJson = "";
            EquipmentSelector.PrecisaCarregarEquipamentos = true;
            SpellSelector.PrecisaCarregarMagias           = true;
        }

        GlobalFichaData.fichasGeneralId = generalId;
        GlobalFichaData.fichasDndId     = dndId;

        if (incompleta)
        {
            int    pid = DataManager.Instance != null ? DataManager.Instance.GetCurrentId()      : 0;
            string sid = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
            LocalFichaStorage.TryGetFichaSnapshot(sid, pid, generalId, dndId, out LocalFichaData snap);

            PopupManager.Instance?.MostrarEscolha(
                "Ficha Incompleta",
                "Esta ficha ainda está em criação. Como deseja continuar?",
                labelOpcao1: "Continuar Criação",
                onOpcao1: () =>
                {
                    // Portão central: reseta tudo primeiro, incondicionalmente, e só então
                    // restaura por cima do estado limpo (ver mesma correção espelhada em
                    // FichaLoaderUI.TratarCliqueFicha).
                    GlobalFichaData.IniciarSessaoParaFicha(generalId, dndId);
                    if (snap != null) FichaLoaderUI.CarregarRestauracaoDoSnapshot(snap);
                    telaDisplayFichas?.SetActive(false);
                    telaFichaDnd?.SetActive(false);
                    telaCriacaoFichaDnd?.SetActive(true);
                },
                labelOpcao2: "Abrir Ficha",
                onOpcao2: () =>
                {
                    GlobalFichaData.IniciarSessaoParaFicha(generalId, dndId);
                    GlobalFichaData.nomePersonagem  = nome;
                    GlobalFichaData.levelPersonagem = level;
                    telaDisplayFichas?.SetActive(false);
                    telaCriacaoFichaDnd?.SetActive(false);
                    telaFichaDnd?.SetActive(true);
                }
            );
        }
        else
        {
            GlobalFichaData.IniciarSessaoParaFicha(generalId, dndId);
            GlobalFichaData.nomePersonagem  = nome;
            GlobalFichaData.levelPersonagem = level;
            FichaLogManager.IniciarParaFicha(dndId);
            telaDisplayFichas?.SetActive(false);
            telaCriacaoFichaDnd?.SetActive(false);
            telaFichaDnd?.SetActive(true);
        }
    }
}
