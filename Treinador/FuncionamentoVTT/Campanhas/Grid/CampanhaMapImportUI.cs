using System;
using System.Collections.Generic;
using System.IO;
using SimpleFileBrowser;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Formulário de importação de mapa — mesmo padrão de Salas/Grid/MapCreatePopupUI.cs (seleção de
/// imagem via SimpleFileBrowser), com um Toggle extra "Vetorizar automaticamente" (ligado por
/// padrão). A vetorização em si (CampanhaMapaVetorizador) roda em CampanhaMapManager.VetorizarMapa,
/// não aqui — o GameObject deste formulário é escondido/reaberto o tempo todo (Configurar/EditarMapa/
/// OnClickFechar), e uma coroutine de vários frames rodando aqui seria morta no meio se o usuário
/// navegasse pra outro painel antes dela terminar, perdendo o resultado antes do SaveOrUpdate final.
/// </summary>
public class CampanhaMapImportUI : MonoBehaviour
{
    [Header("Formulário")]
    [SerializeField] private TMP_InputField inputNomeMapa;
    [SerializeField] private Button botaoSelecionarImagem;
    [SerializeField] private GameObject previewImagemGO;
    [SerializeField] private Image previewImagem;
    [SerializeField] private Toggle toggleVetorizar;
    [SerializeField] private Button botaoConfirmar;
    [SerializeField] private Button botaoFechar;
    [SerializeField] private TMP_Text feedbackText;

    /// <summary>Disparado após um mapa ser importado e salvo — CampanhasManager usa isso pra recarregar a galeria.</summary>
    public event Action OnMapaCriado;

    private CampanhaMapManager _mapManager;
    private string _campanhaId;
    private string _imagemSelecionadaPath = "";
    private Texture2D _texturaPreviewRuntime;
    private Sprite _spritePreviewRuntime;
    private CampanhaMapaData _mapaEmEdicao;

    private void Awake()
    {
        Religar(botaoSelecionarImagem, OnClickSelecionarImagem);
        Religar(botaoConfirmar, OnClickConfirmar);
        Religar(botaoFechar, OnClickFechar);
        if (toggleVetorizar != null) toggleVetorizar.isOn = true; // ligado por padrão, como pedido
    }

    private void OnDestroy() => LimparPreviewRuntime();

    // Reseta o estado mas mantém o formulário FECHADO por padrão — ele só deve aparecer quando
    // explicitamente pedido (AbrirParaNovoMapa ou EditarMapa), não simplesmente por entrar no painel
    // de Mapa (isso incluía o caminho de "Carregar" uma cena existente, que não deveria mostrar o
    // formulário de importação junto).
    public void Configurar(CampanhaMapManager mapManager, string campanhaId)
    {
        _mapManager = mapManager;
        _campanhaId = campanhaId;
        _mapaEmEdicao = null;
        LimparFormulario();
        gameObject.SetActive(false);
    }

    /// <summary>Chamado pelo botão "Mapa" (CampanhasManager.AlternarMapa) — abre o formulário em
    /// branco pra importar uma cena nova.</summary>
    public void AbrirParaNovoMapa()
    {
        _mapaEmEdicao = null;
        LimparFormulario();
        gameObject.SetActive(true);
    }

    /// <summary>Chamado pelo botão "Editar" de um CampanhaMapItemUI — pré-preenche o formulário com
    /// os dados dessa cena; ao confirmar, atualiza a mesma entrada (mesmo mapaId) em vez de criar
    /// uma nova (ver OnClickConfirmar).</summary>
    public void EditarMapa(CampanhaMapaData mapa)
    {
        if (mapa == null) return;
        _mapaEmEdicao = mapa;
        gameObject.SetActive(true);

        if (inputNomeMapa != null) inputNomeMapa.text = mapa.nome;
        if (toggleVetorizar != null) toggleVetorizar.isOn = mapa.vetorizado;

        _imagemSelecionadaPath = mapa.imagemPath;
        AtualizarPreview(mapa.imagemPath);
        SetFeedback(string.Empty);
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
                AtualizarPreview(_imagemSelecionadaPath);
            },
            null,
            FileBrowser.PickMode.Files, false, null, null, "Selecione uma Imagem", "Selecionar");
    }

    private void AtualizarPreview(string caminho)
    {
        if (previewImagemGO != null) previewImagemGO.SetActive(false);
        if (previewImagem == null || string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho)) return;

        byte[] bytes = File.ReadAllBytes(caminho);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes))
        {
            Destroy(tex);
            SetFeedback("Falha ao carregar a imagem selecionada.", true);
            return;
        }

        LimparPreviewRuntime();
        _texturaPreviewRuntime = tex;
        _spritePreviewRuntime = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
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

    // ── Fechar ────────────────────────────────────────────────────────────────

    // Mesmo comportamento do botão fechar de Salas/Grid/MapCreatePopupUI.cs (lá delegado a
    // MapManager.FecharPopupCriarMapa) — aqui o formulário é autocontido, então desativa o próprio
    // GameObject diretamente.
    private void OnClickFechar() => gameObject.SetActive(false);

    // ── Confirmação ───────────────────────────────────────────────────────────

    private void OnClickConfirmar()
    {
        if (string.IsNullOrWhiteSpace(_imagemSelecionadaPath))
        {
            SetFeedback("Selecione uma imagem antes de confirmar.", true);
            return;
        }

        if (string.IsNullOrWhiteSpace(_campanhaId))
        {
            SetFeedback("Nenhuma campanha aberta.", true);
            return;
        }

        string nome = inputNomeMapa != null && !string.IsNullOrWhiteSpace(inputNomeMapa.text)
            ? inputNomeMapa.text.Trim()
            : "Mapa";

        bool editando = _mapaEmEdicao != null;
        string mapaId = editando ? _mapaEmEdicao.mapaId : Guid.NewGuid().ToString("N");
        bool imagemMudou = !editando || _imagemSelecionadaPath != _mapaEmEdicao.imagemPath;

        string imagemPersistida = imagemMudou
            ? CampanhaMapasStorage.EnsureImagePersisted(_campanhaId, _imagemSelecionadaPath, mapaId)
            : _mapaEmEdicao.imagemPath;

        bool vetorizar = toggleVetorizar == null || toggleVetorizar.isOn;

        CampanhaMapaData mapa = editando ? _mapaEmEdicao : new CampanhaMapaData { mapaId = mapaId };
        mapa.nome = nome;
        mapa.imagemPath = imagemPersistida;

        // Imagem nova (ou mapa novo): recalcula o grid pela proporção da imagem e descarta paredes
        // antigas (foram detectadas em cima de outra imagem, não fazem mais sentido).
        if (imagemMudou)
        {
            ConfigurarGridPorAspecto(mapa, _texturaPreviewRuntime);
            mapa.paredes = new List<WallLine>();
            mapa.vetorizado = false;
        }

        // Toggle desligado: garante que a cena não fique marcada como vetorizada com paredes de
        // uma vetorização anterior que o usuário não quer mais.
        if (!vetorizar)
        {
            mapa.vetorizado = false;
            mapa.paredes = new List<WallLine>();
        }

        CampanhaMapasStorage.SaveOrUpdate(_campanhaId, mapa);
        _mapManager?.CarregarMapa(mapa);

        VTTLog.Forja($"Mapa '{nome}' {(editando ? "atualizado" : "importado")} na campanha '{_campanhaId}'.");

        // Só vetoriza se pedido e ainda não estiver vetorizado com a imagem atual (evita reprocessar
        // do zero uma cena editada que não mudou de imagem nem desligou o toggle). Roda em
        // CampanhaMapManager (ver comentário na classe) — sobrevive mesmo se este formulário fechar.
        if (vetorizar && !mapa.vetorizado)
            _mapManager?.VetorizarMapa(mapa);

        _mapaEmEdicao = null;
        LimparFormulario();
        OnMapaCriado?.Invoke();
    }

    // Mesma lógica de Salas/Grid/MapManager.ConfigurarGridInicialPorAspecto: dimensiona
    // gridColunas/gridLinhas a partir da proporção da imagem, mantendo uma "dimensão base" de 20
    // células no lado maior — importa aqui porque a vetorização por célula depende do grid do mapa
    // bater com a proporção real da imagem (senão as células ficam esticadas/distorcidas).
    private static void ConfigurarGridPorAspecto(CampanhaMapaData mapa, Texture2D textura)
    {
        if (mapa == null || textura == null || textura.height <= 0) return;

        float aspect = (float)textura.width / textura.height;
        const int dimensaoBase = 20;

        if (aspect >= 1f)
        {
            mapa.gridColunas = dimensaoBase;
            mapa.gridLinhas = Mathf.Max(1, Mathf.RoundToInt(dimensaoBase / aspect));
        }
        else
        {
            mapa.gridLinhas = dimensaoBase;
            mapa.gridColunas = Mathf.Max(1, Mathf.RoundToInt(dimensaoBase * aspect));
        }
    }

    private void LimparFormulario()
    {
        if (inputNomeMapa != null) inputNomeMapa.text = "";
        _imagemSelecionadaPath = "";
        LimparPreviewRuntime();
        if (previewImagemGO != null) previewImagemGO.SetActive(false);
        SetFeedback(string.Empty);
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
}
