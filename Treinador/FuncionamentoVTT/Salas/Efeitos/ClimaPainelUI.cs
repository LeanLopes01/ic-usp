using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Painel de controle do clima (Chuva/Neve/Nenhum) — mesma API de GridAjustePanelUI
/// (EstaAbertoPainel/AbrirPainel/FecharPainel), usada por BarraFerramentasManager.ToggleClima(), que
/// é o ÚNICO responsável por abrir/fechar este painel (sem botão de toggle próprio aqui, pra não
/// duplicar esse controle). Sliders ficam interativos só pro Mestre (checagem local, cosmética — a
/// autoridade real está toda em EfeitoClimaManager).
/// </summary>
public class ClimaPainelUI : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private EfeitoClimaManager climaManager;
    [SerializeField] private SteamLobbyManager steamLobbyManager;
    [SerializeField] private GameObject painel;
    [SerializeField] private PainelToggleAnimado animacaoPainel;

    [Header("Seletor de Clima")]
    [SerializeField] private Button botaoNenhum;
    [SerializeField] private Button botaoChuva;
    [SerializeField] private Button botaoNeve;
    [SerializeField] private GameObject destaqueNenhum;
    [SerializeField] private GameObject destaqueChuva;
    [SerializeField] private GameObject destaqueNeve;

    [Header("Controles (refletem o efeito atualmente selecionado)")]
    [SerializeField] private ClimaSliderControleUI controleTamanho;
    [SerializeField] private ClimaSliderControleUI controleAngulo;
    [SerializeField] private ClimaSliderControleUI controleQuantidade;
    [SerializeField] private ClimaSliderControleUI controleVelocidadeQueda;
    [SerializeField] private ClimaSliderControleUI controleVento;
    [SerializeField] private ClimaSliderControleUI controleDuracaoVida;
    [SerializeField] private ClimaSliderControleUI controleRuidoX;
    [SerializeField] private ClimaSliderControleUI controleRuidoY;

    private bool SouMestre() =>
        Steamworks.SteamClient.IsValid && steamLobbyManager?.SalaAtual?.IsOwnedBy(Steamworks.SteamClient.SteamId) == true;

    private void Awake()
    {
        if (climaManager == null) climaManager = FindFirstObjectByType<EfeitoClimaManager>();
        if (steamLobbyManager == null) steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();
        if (animacaoPainel == null && painel != null) animacaoPainel = painel.GetComponent<PainelToggleAnimado>();

        ConfigurarBotao(botaoNenhum, () => climaManager?.DefinirTipoClima(TipoEfeitoClima.Nenhum));
        ConfigurarBotao(botaoChuva,  () => climaManager?.DefinirTipoClima(TipoEfeitoClima.Chuva));
        ConfigurarBotao(botaoNeve,   () => climaManager?.DefinirTipoClima(TipoEfeitoClima.Neve));

        if (climaManager != null)
        {
            controleTamanho?.Configurar("Tamanho", climaManager.ObterTamanhoMin(), climaManager.ObterTamanhoMax(),
                climaManager.DefinirTamanho, v => $"{v:0.00}");
            controleAngulo?.Configurar("Ângulo", climaManager.ObterAnguloMin(), climaManager.ObterAnguloMax(),
                climaManager.DefinirAngulo, v => $"{v:0}°");
            controleQuantidade?.Configurar("Quantidade", climaManager.ObterQuantidadeMin(), climaManager.ObterQuantidadeMax(),
                climaManager.DefinirQuantidade, v => $"{v:0}");
            controleVelocidadeQueda?.Configurar("Velocidade de Queda", climaManager.ObterVelocidadeQuedaMin(), climaManager.ObterVelocidadeQuedaMax(),
                climaManager.DefinirVelocidadeQueda, v => $"{v:0.0}");
            controleVento?.Configurar("Vento", climaManager.ObterVentoMin(), climaManager.ObterVentoMax(),
                climaManager.DefinirVento, v => $"{v:0.0}");
            controleDuracaoVida?.Configurar("Duração de Vida", climaManager.ObterDuracaoVidaMin(), climaManager.ObterDuracaoVidaMax(),
                climaManager.DefinirDuracaoVida, v => $"{v:0.0}s");
            controleRuidoX?.Configurar("Ruído X", climaManager.ObterRuidoMin(), climaManager.ObterRuidoMax(),
                climaManager.DefinirRuidoX, v => $"{v:0.00}");
            controleRuidoY?.Configurar("Ruído Y", climaManager.ObterRuidoMin(), climaManager.ObterRuidoMax(),
                climaManager.DefinirRuidoY, v => $"{v:0.00}");
        }
    }

    private void OnEnable()
    {
        if (climaManager != null) climaManager.OnClimaAlterado += AtualizarUI;
        AtualizarUI();
    }

    private void OnDisable()
    {
        if (climaManager != null) climaManager.OnClimaAlterado -= AtualizarUI;
    }

    public bool EstaAbertoPainel()
    {
        if (animacaoPainel != null) return animacaoPainel.EstaAberto();
        return painel != null && painel.activeSelf;
    }

    public void AbrirPainel()
    {
        if (animacaoPainel != null) animacaoPainel.Abrir();
        else if (painel != null) painel.SetActive(true);
        AtualizarUI();
    }

    public void FecharPainel()
    {
        if (animacaoPainel != null) animacaoPainel.Fechar();
        else if (painel != null) painel.SetActive(false);
    }

    public void AtualizarUI()
    {
        if (climaManager == null) return;

        bool souMestre = SouMestre();
        TipoEfeitoClima tipo = climaManager.ObterTipoAtual();

        SetInteractable(botaoNenhum, souMestre);
        SetInteractable(botaoChuva, souMestre);
        SetInteractable(botaoNeve, souMestre);
        if (destaqueNenhum != null) destaqueNenhum.SetActive(tipo == TipoEfeitoClima.Nenhum);
        if (destaqueChuva != null) destaqueChuva.SetActive(tipo == TipoEfeitoClima.Chuva);
        if (destaqueNeve != null) destaqueNeve.SetActive(tipo == TipoEfeitoClima.Neve);

        ClimaConfig cfg = climaManager.ObterConfigAtual();
        bool temEfeito = cfg != null;
        AtualizarControle(controleTamanho, temEfeito ? cfg.tamanho : (float?)null, souMestre);
        AtualizarControle(controleAngulo, temEfeito ? cfg.angulo : (float?)null, souMestre);
        AtualizarControle(controleQuantidade, temEfeito ? cfg.quantidade : (float?)null, souMestre);
        AtualizarControle(controleVelocidadeQueda, temEfeito ? cfg.velocidadeQueda : (float?)null, souMestre);
        AtualizarControle(controleVento, temEfeito ? cfg.vento : (float?)null, souMestre);
        AtualizarControle(controleDuracaoVida, temEfeito ? cfg.duracaoVida : (float?)null, souMestre);
        AtualizarControle(controleRuidoX, temEfeito ? cfg.ruidoX : (float?)null, souMestre);
        AtualizarControle(controleRuidoY, temEfeito ? cfg.ruidoY : (float?)null, souMestre);
    }

    private static void AtualizarControle(ClimaSliderControleUI controle, float? valor, bool souMestre)
    {
        if (controle == null) return;
        controle.gameObject.SetActive(valor.HasValue);
        if (valor.HasValue) controle.AtualizarValor(valor.Value, souMestre);
    }

    private static void SetInteractable(Button b, bool v) { if (b != null) b.interactable = v; }
    private static void ConfigurarBotao(Button b, UnityEngine.Events.UnityAction a) { if (b == null) return; b.onClick.RemoveListener(a); b.onClick.AddListener(a); }
}
