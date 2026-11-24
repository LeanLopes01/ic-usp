using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Marcador de UMA fonte de luz já plantada — instanciado por LightEditorTools.AtualizarMarcadoresExistentes
/// em cada posição de LightSource (ver IGerenciadorDeParedes.ObterFontesDeLuzEmMundo): uma Image+Button
/// "simbolizando onde está a tocha" (pedido do usuário) que revela um Slider ao clicar, pra reajustar
/// o raio dessa luz especificamente, sem precisar apagar e replantar.
///
/// Precisa de um Canvas próprio (no mesmo GameObject ou num filho) pra funcionar como UI posicionada
/// no mundo do mapa — Awake FORÇA esse Canvas pra World Space + garante um GraphicRaycaster, mesmo se
/// o prefab tiver sido montado com outro render mode (um Canvas novo no Unity nasce em Screen Space -
/// Overlay por padrão, que ignora completamente a posição de mundo do GameObject — se o marcador
/// "não aparece em lugar nenhum" e não é clicável, é isso). Mesmo raciocínio defensivo de
/// TokenButtonUI.Awake garantindo seu próprio CanvasGroup, independente do que o prefab já tiver.
///
/// O slider aplica AO VIVO a cada tick (onValueChanged — ver IGerenciadorDeParedes.
/// DefinirRaioDaFonteDeLuz), mas só persiste/propaga pra rede ao SOLTAR (PointerUp — ver
/// PersistirRaioDaFonteDeLuz) — mesmo motivo dos sliders de Iluminação em BarraFerramentasManager só
/// persistirem ao fechar o content: salvar/propagar a cada frame de arrasto seria caro à toa. Slider
/// não expõe um evento público de "soltou o botão", então o PointerUp é ligado em código via
/// EventTrigger — mesmo padrão já usado em BarraFerramentasManager.ConfigurarTooltipHover.
/// </summary>
public class LightMarcadorUI : MonoBehaviour
{
    [SerializeField] private Canvas canvasRaiz;
    [SerializeField] private Button botao;
    [Tooltip("Container do slider — começa OCULTO, revelado ao clicar em botao (ver AlternarPainelSlider).")]
    [SerializeField] private GameObject painelSlider;
    [SerializeField] private Slider sliderRaio;
    [Tooltip("Opcional — mostra o raio atual em metros.")]
    [SerializeField] private TMP_Text textoRaio;

    public string Id { get; private set; }

    private Action<string, float> aoAlterarRaioAoVivo;
    private Action<string, float> aoConfirmarRaio;
    private bool atualizandoSlider;
    private bool pointerUpConfigurado;

    private void Awake()
    {
        // ATENÇÃO: '??=' NÃO funciona de forma confiável em referências UnityEngine.Object — um campo
        // [SerializeField] deixado em "None" no Inspector não é um null de verdade em nível de CLR (é
        // um objeto "fake null" que só é igual a null através do operator== sobrecarregado da Unity,
        // que '??'/'??=' NÃO invocam). Por isso todo o encadeamento abaixo usa 'if (x == null) x = ...'
        // explícito — com '??=' aqui, canvasRaiz nunca era reatribuído mesmo vindo "None" do prefab, e
        // canvasRaiz.renderMode (linha abaixo) lançava UnassignedReferenceException.
        if (canvasRaiz == null) canvasRaiz = GetComponent<Canvas>();
        if (canvasRaiz == null) canvasRaiz = GetComponentInChildren<Canvas>(true);
        // prefabMarcadorLuz (LighMarcadorPrefab) foi montado SEM nenhum Canvas próprio — sem um aqui,
        // o marcador só renderiza por HERDAR o Canvas ancestral de LightEditorTools.transform em tempo
        // de execução, cujo worldCamera/sortingOrder nunca são configurados pra este marcador
        // especificamente (só Configurar() faz isso, e só quando canvasRaiz != null). Isso explica
        // "aparece na posição certa mas não é clicável": a posição vem de transform.position (world,
        // funciona com qualquer Canvas WorldSpace ancestral), mas o clique depende do worldCamera
        // certo estar setado NESTE Canvas — que nunca existia. Cria um se faltar, mesmo raciocínio
        // defensivo do GraphicRaycaster logo abaixo. NÃO adicione um Canvas manualmente no prefab — o
        // script cuida disso sozinho; um Canvas adicionado à mão nasce em Screen Space - Overlay (o
        // padrão da Unity) e, sem ninguém forçando WorldSpace nele, cobre a tela inteira.
        bool criouCanvasNovo = canvasRaiz == null;
        if (criouCanvasNovo) canvasRaiz = gameObject.AddComponent<Canvas>();

        // Ver comentário da classe — força World Space + garante um GraphicRaycaster
        // independente de como o prefab foi montado no Editor.
        canvasRaiz.renderMode = RenderMode.WorldSpace;
        canvasRaiz.overrideSorting = true; // só tem efeito se acabar aninhado sob outro Canvas; inofensivo se não estiver

        if (canvasRaiz.GetComponent<GraphicRaycaster>() == null)
            canvasRaiz.gameObject.AddComponent<GraphicRaycaster>();

        // TEMP debug (ver VTTLog.ativoBoard) — investigando marcador de luz sumindo/cobrindo a tela.
        VTTLog.Board($"[LightMarcadorUI] Awake '{gameObject.name}': canvasNovo={criouCanvasNovo}, " +
            $"renderMode={canvasRaiz.renderMode}, localScale={transform.localScale}, " +
            $"pai={(transform.parent != null ? transform.parent.name : "null")}");
    }

    /// <summary>Chamado por LightEditorTools logo após instanciar o prefab. minMetros/maxMetros
    /// espelham o mesmo range de LightEditorTools.sliderRaioLuz, pra o slider de edição ter os mesmos
    /// limites do slider de criação. camera/sortingOrder configuram o Canvas pra renderizar por cima
    /// de mapa/grid/fog e ser clicável pela câmera certa (ver LightEditorTools._camera).</summary>
    public void Configurar(string id, float raioMetrosAtual, float minMetros, float maxMetros,
        Camera camera, int sortingOrder,
        Action<string, float> aoAlterarRaioAoVivo, Action<string, float> aoConfirmarRaio)
    {
        Id = id;
        this.aoAlterarRaioAoVivo = aoAlterarRaioAoVivo;
        this.aoConfirmarRaio = aoConfirmarRaio;

        if (canvasRaiz != null)
        {
            canvasRaiz.worldCamera = camera;
            canvasRaiz.sortingOrder = sortingOrder;
        }

        // TEMP debug (ver VTTLog.ativoBoard) — investigando marcador de luz sumindo/cobrindo a tela.
        VTTLog.Board($"[LightMarcadorUI] Configurar '{gameObject.name}' id={id}: " +
            $"camera={(camera != null ? camera.name : "NULL")}, sortingOrder={sortingOrder}, " +
            $"canvasRaiz.worldCamera={(canvasRaiz != null && canvasRaiz.worldCamera != null ? canvasRaiz.worldCamera.name : "NULL")}, " +
            $"rectSize={(GetComponent<RectTransform>() != null ? GetComponent<RectTransform>().rect.size.ToString() : "?")}, " +
            $"localScale={transform.localScale}, posMundo={transform.position}");

        if (painelSlider != null) painelSlider.SetActive(false);

        if (botao != null)
        {
            botao.onClick.RemoveListener(AlternarPainelSlider);
            botao.onClick.AddListener(AlternarPainelSlider);
        }

        if (sliderRaio != null)
        {
            sliderRaio.minValue = minMetros;
            sliderRaio.maxValue = maxMetros;
            sliderRaio.wholeNumbers = false;

            atualizandoSlider = true;
            sliderRaio.value = raioMetrosAtual;
            atualizandoSlider = false;

            sliderRaio.onValueChanged.RemoveListener(OnSliderAlterado);
            sliderRaio.onValueChanged.AddListener(OnSliderAlterado);

            ConfigurarPointerUp(sliderRaio.gameObject);
        }
        AtualizarTexto(raioMetrosAtual);
    }

    // Clicar no botão (ícone da tocha) revela/esconde o painel do slider — "um slider que vai ser
    // ativo quando clicar nele" (pedido do usuário).
    private void AlternarPainelSlider()
    {
        if (painelSlider != null) painelSlider.SetActive(!painelSlider.activeSelf);
    }

    private void OnSliderAlterado(float valor)
    {
        AtualizarTexto(valor);
        if (atualizandoSlider) return; // Configurar setando o valor inicial não deve contar como edição
        aoAlterarRaioAoVivo?.Invoke(Id, valor);
    }

    private void AtualizarTexto(float metros)
    {
        if (textoRaio != null) textoRaio.text = $"{metros:0.0}m";
    }

    private void ConfigurarPointerUp(GameObject alvo)
    {
        if (pointerUpConfigurado) return; // Configurar pode rodar mais de uma vez no mesmo marcador (pool)
        pointerUpConfigurado = true;

        EventTrigger trigger = alvo.GetComponent<EventTrigger>();
        if (trigger == null) trigger = alvo.AddComponent<EventTrigger>();

        EventTrigger.Entry entrada = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        entrada.callback.AddListener(_ => { if (sliderRaio != null) aoConfirmarRaio?.Invoke(Id, sliderRaio.value); });
        trigger.triggers.Add(entrada);
    }
}
