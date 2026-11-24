using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI; // Necessário para manipular a Image

/// <summary>
/// Helper mínimo injetado automaticamente em cada objectsButtons pelo UITooltipTrigger.
/// Não é necessário adicionar manualmente — o manager cuida disso em Start().
/// </summary>
public class UITooltipButtonHelper : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private int              _index;
    private UITooltipTrigger _manager;
    
    // Variáveis para o efeito visual de highlight
    private Image _bg;
    private Color _corOriginal;

    private void Awake()
    {
        _bg = GetComponent<Image>();
        if (_bg != null)
        {
            _corOriginal = _bg.color;
            VTTLog.UI($"TooltipHelper.Awake '{gameObject.name}' — Image encontrada, cor original: {_corOriginal}");
        }
        else
        {
            VTTLog.UIWarn($"TooltipHelper.Awake '{gameObject.name}' — nenhum componente Image encontrado.");
        }
    }

    public void Inicializar(int index, UITooltipTrigger manager)
    {
        _index   = index;
        _manager = manager;
        VTTLog.UI($"TooltipHelper inicializado em '{gameObject.name}' — índice: {index}, manager: {(manager != null ? manager.gameObject.name : "NULL")}");
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        VTTLog.UI($"TooltipHelper.PointerEnter item {_index} ('{gameObject.name}'). Manager existe? {_manager != null}");
        _manager?.MostrarTooltip(_index);

        if (_bg != null)
        {
            _bg.color = new Color(0.9f, 0.9f, 0.9f, 1f);
            VTTLog.UI($"TooltipHelper: highlight aplicado em '{gameObject.name}'.");
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        VTTLog.UI($"TooltipHelper.PointerExit item {_index} ('{gameObject.name}').");
        _manager?.EsconderTooltip();

        if (_bg != null)
        {
            _bg.color = _corOriginal;
            VTTLog.UI($"TooltipHelper: cor restaurada em '{gameObject.name}'.");
        }
    }
}