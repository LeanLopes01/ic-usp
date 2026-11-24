using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using SimpleFileBrowser;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Formulário de criação de campanha (nome, sistema, imagem, descrição) — mesmo padrão do
/// formulário "Criar Sala" (Salas/TelaSalaUIManager.cs), porém 100% local: sem lobby/Steam.
/// A listagem das campanhas já salvas vive em CampanhaLoaderUI (mesmo padrão de FichaLoaderUI/
/// SistemasListManager — prefab + IndicadorPaginasUI dedicados), assinada aqui só pra saber quando o
/// usuário clicou pra abrir uma. Tanto criar quanto reabrir disparam OnCampanhaAberta.
/// </summary>
public class CampanhasCreationUI : MonoBehaviour
{
    [Header("Painel de Criação")]
    [Tooltip("GameObject do formulário de criação — ativado sempre que esta tela é exibida (ver OnEnable) e fechado externamente ao abrir uma campanha (ver CampanhasManager.AbrirCampanha).")]
    [SerializeField] private GameObject painelCriarCampanha;

    [Header("Formulário")]
    [SerializeField] private TMP_InputField inputNomeCampanha;
    [SerializeField] private TMP_Dropdown   dropdownSistemaCampanha;
    [SerializeField] private TMP_InputField inputDescricaoCampanha;

    [Header("Imagem")]
    [SerializeField] private Button     botaoSelecionarImagem;
    [SerializeField] private GameObject previewImagemGO;
    [SerializeField] private Image      previewImagem;

    [Header("Ações")]
    [SerializeField] private Button    botaoConfirmarCriacao;
    [SerializeField] private TMP_Text  feedbackText;

    [Header("Listagem de Campanhas")]
    [SerializeField] private CampanhaLoaderUI campanhaLoader;
    [Tooltip("Instância SEPARADA de CampanhaLoaderUI que vive na tela inicial (fora desta tela) — " +
        "clicar numa campanha por lá dispara o MESMO fluxo de abertura (AbrirCampanhaSnapshot), sem " +
        "precisar passar visualmente por aqui primeiro. Ver também CampanhasManager.telaCampanhaAberta " +
        "— é o que garante que a tela certa apareça mesmo vindo de fora desta.")]
    [SerializeField] private CampanhaLoaderUI campanhaLoaderTelaInicial;

    /// <summary>Disparado tanto ao criar uma campanha nova quanto ao abrir uma já existente da lista.</summary>
    public event Action<string, string, string> OnCampanhaAberta; // campanhaId, nome, sistema

    private readonly List<string> _sistemaFolderNames = new List<string>();
    private string     _imagemSelecionadaPath = "";
    private Texture2D  _texturaPreviewRuntime;
    private Sprite     _spritePreviewRuntime;

    private void Awake()
    {
        Religar(botaoConfirmarCriacao,   OnClickConfirmarCriacao);
        Religar(botaoSelecionarImagem,   OnClickSelecionarImagem);

        if (campanhaLoader != null)
            campanhaLoader.OnCampanhaClicada += AbrirCampanhaSnapshot;

        if (campanhaLoaderTelaInicial != null)
            campanhaLoaderTelaInicial.OnCampanhaClicada += AbrirCampanhaSnapshot;
    }

    private void OnEnable()
    {
        // Esta tela pode ser reaberta pelo ToggleObjects (menu/tabs) depois de ter sido fechada
        // externamente por CampanhasManager.FecharPainelCriacao ao abrir uma campanha — sempre
        // volta a mostrar o formulário de criação ao entrar de novo na tela.
        SetAtivo(painelCriarCampanha, true);

        PopularDropdownSistemas();
        SetFeedback(string.Empty);
        campanhaLoader?.RecarregarLista();
    }

    private void OnDestroy()
    {
        LimparPreviewRuntime();

        if (campanhaLoader != null)
            campanhaLoader.OnCampanhaClicada -= AbrirCampanhaSnapshot;

        if (campanhaLoaderTelaInicial != null)
            campanhaLoaderTelaInicial.OnCampanhaClicada -= AbrirCampanhaSnapshot;
    }

    /// <summary>Fecha o painel de criação — chamado externamente (ex: CampanhasManager.AbrirCampanha)
    /// ao sair do fluxo de criação/listagem para editar a campanha.</summary>
    public void FecharPainelCriacao()
    {
        SetAtivo(painelCriarCampanha, false);
    }

    // ── Listagem de campanhas (ver CampanhaLoaderUI) ─────────────────────────

    private void AbrirCampanhaSnapshot(CampanhaLocalSnapshot snapshot)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.campanhaId))
        {
            SetFeedback("Campanha inválida.", true);
            return;
        }

        OnCampanhaAberta?.Invoke(snapshot.campanhaId, snapshot.nome, snapshot.sistema);
    }

    // ── Sistema ───────────────────────────────────────────────────────────────

    private void PopularDropdownSistemas()
    {
        if (dropdownSistemaCampanha == null) return;

        _sistemaFolderNames.Clear();
        dropdownSistemaCampanha.ClearOptions();

        string baseDir = Path.Combine(Application.persistentDataPath, "sistemas");
        var opcoes = new List<string>();

        if (Directory.Exists(baseDir))
        {
            foreach (string dir in Directory.GetDirectories(baseDir))
            {
                string folderName = Path.GetFileName(dir);
                _sistemaFolderNames.Add(folderName);
                opcoes.Add(PrettyPrintFolderName(folderName));
            }
        }

        if (opcoes.Count == 0)
        {
            _sistemaFolderNames.Add("quinta-edicao-srd");
            opcoes.Add("Quinta Edição (D&D SRD)");
        }

        dropdownSistemaCampanha.AddOptions(opcoes);
        dropdownSistemaCampanha.value = 0;
        dropdownSistemaCampanha.RefreshShownValue();
    }

    private string ObterSistemaSelecionado()
    {
        if (dropdownSistemaCampanha == null) return string.Empty;
        int idx = dropdownSistemaCampanha.value;
        return idx >= 0 && idx < _sistemaFolderNames.Count ? _sistemaFolderNames[idx] : string.Empty;
    }

    private static string PrettyPrintFolderName(string folderName)
    {
        string[] parts = folderName.Split('-');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
        }
        return string.Join(" ", parts);
    }

    // ── Imagem ────────────────────────────────────────────────────────────────

    private void OnClickSelecionarImagem()
    {
        FileBrowser.SetFilters(true, new FileBrowser.Filter("Imagens", ".jpg", ".png", ".jpeg"));
        FileBrowser.ShowLoadDialog(
            (paths) =>
            {
                if (paths == null || paths.Length == 0) return;
                _imagemSelecionadaPath = paths[0];
                AtualizarPreviewImagem(_imagemSelecionadaPath);
            },
            null,
            FileBrowser.PickMode.Files, false, null, null, "Selecione uma Imagem", "Selecionar");
    }

    private void AtualizarPreviewImagem(string caminho)
    {
        if (previewImagemGO != null) previewImagemGO.SetActive(false);
        if (previewImagem == null || string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho)) return;

        byte[] bytes = File.ReadAllBytes(caminho);
        var novaTextura = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!novaTextura.LoadImage(bytes))
        {
            Destroy(novaTextura);
            SetFeedback("Falha ao carregar a imagem selecionada.", true);
            return;
        }

        LimparPreviewRuntime();

        _texturaPreviewRuntime = novaTextura;
        _spritePreviewRuntime = Sprite.Create(
            _texturaPreviewRuntime,
            new Rect(0f, 0f, _texturaPreviewRuntime.width, _texturaPreviewRuntime.height),
            new Vector2(0.5f, 0.5f),
            100f);

        previewImagem.sprite = _spritePreviewRuntime;
        if (previewImagemGO != null) previewImagemGO.SetActive(true);
    }

    private void LimparPreviewRuntime()
    {
        if (previewImagem != null) previewImagem.sprite = null;
        if (_spritePreviewRuntime != null) Destroy(_spritePreviewRuntime);
        if (_texturaPreviewRuntime != null) Destroy(_texturaPreviewRuntime);
        _spritePreviewRuntime = null;
        _texturaPreviewRuntime = null;
    }

    // ── Confirmação ───────────────────────────────────────────────────────────

    private void OnClickConfirmarCriacao()
    {
        string nome = inputNomeCampanha != null ? inputNomeCampanha.text.Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(nome))
        {
            SetFeedback("Digite um nome para a campanha.", true);
            return;
        }

        string sistema = ObterSistemaSelecionado();
        string descricao = inputDescricaoCampanha != null ? inputDescricaoCampanha.text.Trim() : string.Empty;

        string campanhaId = GerarIdUnico(nome);
        Directory.CreateDirectory(CampanhasLocalStorage.CampaignFolderPath(campanhaId));
        Directory.CreateDirectory(CampanhasLocalStorage.NpcsFolderPath(campanhaId));

        string imagemPersistida = CampanhasLocalStorage.EnsureImagePersisted(_imagemSelecionadaPath, campanhaId);

        CampanhasLocalStorage.SaveOrUpdate(new CampanhaLocalSnapshot
        {
            campanhaId = campanhaId,
            nome       = nome,
            sistema    = sistema,
            descricao  = descricao,
            imagemPath = imagemPersistida,
        });

        VTTLog.Forja($"Campanha '{nome}' criada em '{CampanhasLocalStorage.CampaignFolderPath(campanhaId)}'.");
        PostHogTracker.TrackCampanhaCriada(sistema);

        LimparFormulario();
        campanhaLoader?.RecarregarLista();
        OnCampanhaAberta?.Invoke(campanhaId, nome, sistema);
    }

    private void LimparFormulario()
    {
        if (inputNomeCampanha != null) inputNomeCampanha.text = "";
        if (inputDescricaoCampanha != null) inputDescricaoCampanha.text = "";
        _imagemSelecionadaPath = "";
        AtualizarPreviewImagem(null);
        SetFeedback(string.Empty);
    }

    private static string GerarIdUnico(string nome)
    {
        string baseId = FormatarId(nome);
        if (string.IsNullOrWhiteSpace(baseId)) baseId = "campanha";

        string id = baseId;
        int sufixo = 2;
        while (Directory.Exists(CampanhasLocalStorage.CampaignFolderPath(id)))
        {
            id = $"{baseId}-{sufixo}";
            sufixo++;
        }
        return id;
    }

    private static string FormatarId(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        texto = texto.Trim().ToLowerInvariant().Replace(" ", "-");
        texto = Regex.Replace(texto, @"[^a-z0-9\-]", string.Empty);
        texto = Regex.Replace(texto, @"-{2,}", "-");
        return texto.Trim('-');
    }

    // ── Utilitários ───────────────────────────────────────────────────────────

    private void SetFeedback(string mensagem, bool erro = false)
    {
        if (feedbackText == null) return;
        feedbackText.text = mensagem;
        feedbackText.color = erro ? Color.red : Color.white;
    }

    private static void Religar(Button btn, Action acao)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => acao());
    }

    private static void SetAtivo(GameObject go, bool ativo)
    {
        if (go != null) go.SetActive(ativo);
    }
}
