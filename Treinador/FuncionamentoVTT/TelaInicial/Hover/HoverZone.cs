using UnityEngine;
using UnityEngine.EventSystems;

// Estado compartilhado entre a zona do botão fixo e a zona do painel: um contador de
// quantas dessas duas áreas o mouse está sobre no momento, para o fechamento com atraso
// (ToggleObjects.FecharHoverComAtraso) poder confirmar se o mouse realmente saiu de
// ambas antes de esconder o painel.
public class HoverZoneState
{
    public int refCount;
    public Coroutine fecharCoroutine;
}

// Anexado em runtime (via AddComponent) tanto no botão fixo quanto no painelHover de cada
// BotaoHoverConfig — ver ToggleObjects.ConfigurarBotoesHover.
public class HoverZone : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public ToggleObjects manager;
    public RectTransform painel;
    public HoverZoneState estado;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (estado.fecharCoroutine != null)
        {
            manager.StopCoroutine(estado.fecharCoroutine);
            estado.fecharCoroutine = null;
        }

        estado.refCount++;
        manager.IniciarAnimacaoHover(painel, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        estado.refCount = Mathf.Max(0, estado.refCount - 1);

        // Para qualquer corrotina de fechamento já agendada antes de marcar uma nova — sem isso, um
        // segundo OnPointerExit (ex: sair de botaoFixo e, logo em seguida, sair de painelHover, sem
        // um OnPointerEnter cancelando a primeira no meio) sobrescreve a referência e deixa a
        // primeira corrotina órfã: ela continua rodando sem ninguém poder pará-la, e pode fechar o
        // painel bem no meio de um hover que já devia ter sido cancelado.
        if (estado.fecharCoroutine != null)
            manager.StopCoroutine(estado.fecharCoroutine);

        estado.fecharCoroutine = manager.StartCoroutine(manager.FecharHoverComAtraso(painel, estado));
    }
}
