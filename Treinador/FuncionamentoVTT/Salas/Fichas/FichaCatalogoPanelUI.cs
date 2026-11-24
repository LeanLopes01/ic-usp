using UnityEngine;

/// <summary>
/// Painel genérico de visualização rápida de ficha.
/// Instancia um FichaCatalogoItemUI (prefab) que contém token, nome, cabeçalho e detalhes expansíveis.
/// </summary>
public class FichaCatalogoPanelUI : MonoBehaviour
{
    [Header("Painel")]
    [SerializeField] private GameObject painel;

    [Header("Item")]
    [SerializeField] private Transform itemContent;
    [SerializeField] private GameObject prefabFichaItem;

    [Header("Dependências")]
    [SerializeField] private FichaSyncManager fichaSyncManager;

    private int _fichaGeneralId = -1;
    private int _fichaDndId     = -1;
    private string _ownerSteamId;
    private FichaCatalogoItemUI _itemAtivo;

    private void Awake()
    {
        if (painel != null) painel.SetActive(false);
        if (fichaSyncManager == null)
            fichaSyncManager = FindFirstObjectByType<FichaSyncManager>();
    }

    public bool EstaAberto => painel != null && painel.activeSelf;

    public void ToggleParaFicha(FichaData ficha, string ownerSteamId = null)
    {
        if (ficha == null) return;

        string meuSteamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        ownerSteamId ??= meuSteamId;

        bool mesmaTela = EstaAberto
            && _fichaDndId     == ficha.fichas_dnd_id
            && _fichaGeneralId == ficha.fichas_general_id
            && _ownerSteamId   == ownerSteamId;

        if (mesmaTela) { FecharPainel(); return; }

        AbrirParaFicha(ficha, ownerSteamId);
    }

    public void AbrirParaFicha(FichaData ficha, string ownerSteamId = null)
    {
        if (ficha == null) return;

        string meuSteamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        ownerSteamId ??= meuSteamId;

        _fichaGeneralId = ficha.fichas_general_id;
        _fichaDndId     = ficha.fichas_dnd_id;
        _ownerSteamId   = ownerSteamId;

        if (painel != null) painel.SetActive(true);

        LimparItem();

        if (prefabFichaItem == null || itemContent == null) return;

        GameObject go = UIUtils.Spawn(prefabFichaItem, itemContent);
        _itemAtivo = go.GetComponent<FichaCatalogoItemUI>();

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;

        // 1. Minha própria ficha — resolve via storage local (comportamento original, sem regressão).
        if (LocalFichaStorage.TryGetFichaSnapshot(meuSteamId, playerId,
                ficha.fichas_general_id, ficha.fichas_dnd_id, out LocalFichaData snapshot))
        {
            _itemAtivo?.Setup(snapshot);
            return;
        }

        // 2. Ficha de outro jogador — resolve via cache P2P (FichaSyncManager), populado quando o
        // dono abriu a aba ou quando eu entrei na sala. Ver Bug D.
        if (fichaSyncManager == null)
            fichaSyncManager = FindFirstObjectByType<FichaSyncManager>();
        if (fichaSyncManager != null &&
            fichaSyncManager.TryObterFichaRemota(ownerSteamId, ficha.fichas_dnd_id, out LocalFichaData remota))
        {
            _itemAtivo?.Setup(remota);
            return;
        }

        // 3. Degradação: só os 4 campos de metadado já sincronizados via lobby data.
        _itemAtivo?.SetupFallback(ficha);
    }

    public void FecharPainel()
    {
        if (painel != null) painel.SetActive(false);
    }

    private void LimparItem()
    {
        if (_itemAtivo != null) { Destroy(_itemAtivo.gameObject); _itemAtivo = null; }
    }
}
