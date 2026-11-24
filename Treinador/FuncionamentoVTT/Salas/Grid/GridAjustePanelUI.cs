using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GridAjustePanelUI : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private MapManager mapManager;
    [SerializeField] private Button botaoTogglePainel;
    [SerializeField] private GameObject painel;
    [SerializeField] private PainelToggleAnimado animacaoPainel;

    [Header("Controles")]
    [SerializeField] private Button botaoDiminuir;
    [SerializeField] private Button botaoAumentar;
    [SerializeField] private Slider sliderTamanhoGrid;
    [SerializeField] private TMP_Text textoValorAtual;

    private bool atualizandoSlider;

    private void Awake()
    {
        if (mapManager == null)
            mapManager = FindFirstObjectByType<MapManager>();

        if (animacaoPainel == null && painel != null)
            animacaoPainel = painel.GetComponent<PainelToggleAnimado>();

        ConfigurarBotao(botaoTogglePainel, TogglePainel);
        ConfigurarBotao(botaoDiminuir, DiminuirGrid);
        ConfigurarBotao(botaoAumentar, AumentarGrid);

        if (sliderTamanhoGrid != null)
        {
            sliderTamanhoGrid.onValueChanged.RemoveListener(OnSliderAlterado);
            sliderTamanhoGrid.onValueChanged.AddListener(OnSliderAlterado);
        }
    }

    private void Start()
    {
        AtualizarUI();
    }

    private void OnDestroy()
    {
        RemoverBotao(botaoTogglePainel, TogglePainel);
        RemoverBotao(botaoDiminuir, DiminuirGrid);
        RemoverBotao(botaoAumentar, AumentarGrid);

        if (sliderTamanhoGrid != null)
            sliderTamanhoGrid.onValueChanged.RemoveListener(OnSliderAlterado);
    }

    public bool EstaAbertoPainel()
    {
        if (animacaoPainel != null)
            return animacaoPainel.EstaAberto();
        return painel != null && painel.activeSelf;
    }

    public void AbrirPainel()
    {
        if (animacaoPainel != null)
            animacaoPainel.Abrir();
        else if (painel != null)
            painel.SetActive(true);
    }

    public void FecharPainel()
    {
        if (animacaoPainel != null)
            animacaoPainel.Fechar();
        else if (painel != null)
            painel.SetActive(false);
    }

    public void TogglePainel()
    {
        if (EstaAbertoPainel())
            FecharPainel();
        else
            AbrirPainel();
    }

    public void DiminuirGrid()
    {
        if (mapManager == null)
            return;

        mapManager.DiminuirTamanhoGrid();
        AtualizarUI();
    }

    public void AumentarGrid()
    {
        if (mapManager == null)
            return;

        mapManager.AumentarTamanhoGrid();
        AtualizarUI();
    }

    public void AtualizarUI()
    {
        if (mapManager == null)
            return;

        float atual = mapManager.ObterMultiplicadorTamanhoGrid();
        float minimo = mapManager.ObterMultiplicadorMinimoGrid();
        float maximo = mapManager.ObterMultiplicadorMaximoGrid();

        if (sliderTamanhoGrid != null)
        {
            atualizandoSlider = true;
            sliderTamanhoGrid.minValue = minimo;
            sliderTamanhoGrid.maxValue = maximo;
            sliderTamanhoGrid.value = atual;
            atualizandoSlider = false;
        }

        if (textoValorAtual != null)
            textoValorAtual.text = $"{mapManager.ObterMultiplicadorTamanhoGrid() * 100f:0}%";
    }

    private void OnSliderAlterado(float novoValor)
    {
        if (atualizandoSlider || mapManager == null)
            return;

        mapManager.DefinirMultiplicadorTamanhoGrid(novoValor);
        AtualizarUI();
    }

    private static void ConfigurarBotao(Button botao, UnityEngine.Events.UnityAction acao)
    {
        if (botao == null)
            return;

        botao.onClick.RemoveListener(acao);
        botao.onClick.AddListener(acao);
    }

    private static void RemoverBotao(Button botao, UnityEngine.Events.UnityAction acao)
    {
        if (botao == null)
            return;

        botao.onClick.RemoveListener(acao);
    }
}