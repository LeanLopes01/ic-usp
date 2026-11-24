using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Coloque este componente no GameObject do painel que deve aparecer ao passar o mouse
/// sobre o botão gatilho. O painel fica visível enquanto o mouse estiver sobre o botão
/// OU sobre o próprio painel; some ao sair de ambos.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class HoverPainel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Button botaoGatilho;
    [SerializeField, Min(0f)] private float duracaoFade = 0.15f;

    private CanvasGroup _group;
    private bool _mouseNoBotao;
    private bool _mouseNoPainel;
    private Coroutine _fade;

    private void Awake()
    {
        _group = GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;
        gameObject.SetActive(false);

        if (botaoGatilho != null)
        {
            var trigger = botaoGatilho.GetComponent<EventTrigger>()
                       ?? botaoGatilho.gameObject.AddComponent<EventTrigger>();
            AdicionarEntrada(trigger, EventTriggerType.PointerEnter, _ => AoBotaoEntrar());
            AdicionarEntrada(trigger, EventTriggerType.PointerExit,  _ => AoBotaoSair());
        }
    }

    // ── Eventos do botão ────────────────────────────────────────────────────────

    private void AoBotaoEntrar()
    {
        _mouseNoBotao = true;
        Mostrar();
    }

    private void AoBotaoSair()
    {
        _mouseNoBotao = false;

        // O EventTrigger de PointerExit fica no botão (sempre ativo), não no painel — com o mouse
        // se movendo rápido, esse evento pode chegar depois que Ocultar() já desativou o painel
        // (fade concluído entre um Entrar/Sair e outro). Nesse caso não há nada para verificar:
        // o painel já está escondido, e StartCoroutine num GameObject inativo lança exceção.
        if (!gameObject.activeInHierarchy) return;
        StartCoroutine(VerificarFechar());
    }

    // ── Eventos do painel (IPointerEnter/Exit) ──────────────────────────────────

    public void OnPointerEnter(PointerEventData _) => _mouseNoPainel = true;

    public void OnPointerExit(PointerEventData _)
    {
        _mouseNoPainel = false;
        if (!gameObject.activeInHierarchy) return;
        StartCoroutine(VerificarFechar());
    }

    // ── Lógica de mostrar/ocultar ────────────────────────────────────────────────

    private void Mostrar()
    {
        gameObject.SetActive(true);
        _group.interactable = true;
        _group.blocksRaycasts = true;
        IniciarFade(1f);
    }

    private void Ocultar()
    {
        _group.interactable = false;
        _group.blocksRaycasts = false;
        IniciarFade(0f, onFim: () => gameObject.SetActive(false));
    }

    private IEnumerator VerificarFechar()
    {
        yield return null; // aguarda um frame para o próximo PointerEnter registrar
        if (!_mouseNoBotao && !_mouseNoPainel)
            Ocultar();
    }

    private void IniciarFade(float alvo, System.Action onFim = null)
    {
        if (_fade != null) StopCoroutine(_fade);
        _fade = StartCoroutine(RotinaFade(alvo, onFim));
    }

    private IEnumerator RotinaFade(float alvo, System.Action onFim)
    {
        float origem = _group.alpha;
        float elapsed = 0f;

        while (elapsed < duracaoFade)
        {
            elapsed += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Lerp(origem, alvo, Mathf.Clamp01(elapsed / duracaoFade));
            yield return null;
        }

        _group.alpha = alvo;
        onFim?.Invoke();
        _fade = null;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static void AdicionarEntrada(EventTrigger et, EventTriggerType tipo,
                                          UnityEngine.Events.UnityAction<BaseEventData> cb)
    {
        var entrada = new EventTrigger.Entry { eventID = tipo };
        entrada.callback.AddListener(cb);
        et.triggers.Add(entrada);
    }
}
