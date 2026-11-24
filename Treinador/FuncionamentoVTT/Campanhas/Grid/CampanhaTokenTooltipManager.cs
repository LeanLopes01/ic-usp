using UnityEngine;

/// <summary>
/// Singleton do tooltip de NPC no grid de Campanhas — versão enxuta de
/// Salas/Grid/Tooltips/TokenStatBlockTooltipManager.cs, só modo Hover.
/// </summary>
public class CampanhaTokenTooltipManager : MonoBehaviour
{
    public static CampanhaTokenTooltipManager Instance { get; private set; }

    [Header("Prefab do painel (com CampanhaTokenTooltipUI)")]
    [SerializeField] private CampanhaTokenTooltipUI prefabTooltip;

    [Header("Container (Canvas raiz onde o tooltip é instanciado)")]
    [SerializeField] private Transform containerRaiz;

    private CampanhaTokenTooltipUI _instanciaAtiva;
    private bool _mouseSobrePainel;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Mostrar(EntryData npc)
    {
        if (npc == null || prefabTooltip == null) return;

        Esconder();

        Transform pai = containerRaiz != null ? containerRaiz : transform;
        _instanciaAtiva = Instantiate(prefabTooltip, pai);
        _mouseSobrePainel = false;
        _instanciaAtiva.Configurar(npc, this);
    }

    public void Esconder()
    {
        if (_instanciaAtiva != null)
            Destroy(_instanciaAtiva.gameObject);
        _instanciaAtiva = null;
        _mouseSobrePainel = false;
    }

    internal void NotificarMouseEntrouNoPainel() => _mouseSobrePainel = true;

    internal void NotificarMouseSaiuDoPainel()
    {
        _mouseSobrePainel = false;
        Esconder();
    }
}
