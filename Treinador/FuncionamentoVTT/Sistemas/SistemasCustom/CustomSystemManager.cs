using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using SimpleFileBrowser;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Orquestrador único da Forja de Sistemas. TopicEditorUI foi inutilizado — toda a lógica que ele
/// tinha foi trazida pra cá, reestruturada em 3 níveis:
///   1) Sistema (nome auto-save + imagem) + lista de tópicos.
///   2) Clicar "Editar" num tópico abre a lista de entradas (arquivos XML) daquele tópico, com
///      botão "Adicionar" e "Estrutura".
///   3) Cada tópico tem sua própria ESTRUTURA (schema_{topico}.xml, salvo na pasta do sistema) —
///      uma lista de campos (Label/ID/Tipo) definida pelo próprio criador do sistema no "Criador
///      de Estruturas" (painelSchema), em vez do catálogo fixo hardcoded que o TopicEditorUI usava.
///      Toda entrada do tópico (nova ou existente) usa automaticamente TODOS os campos da estrutura
///      dele + id/name fixos — não existe mais escolha de campo POR entrada (toggle), a estrutura é
///      por TÓPICO.
/// </summary>
public class CustomSystemManager : MonoBehaviour
{
    [Header("Dados do Sistema (auto-save)")]
    [SerializeField] private TMP_InputField inputNomeSistema;
    [SerializeField] private Button         botaoSelecionarImagem;
    [SerializeField] private Image          imagemSistemaPreview;

    [Header("Navegação")]
    [SerializeField] private Button btnVoltarGerenciamento;

    [Header("Painel de Ajuda")]
    [SerializeField] private GameObject painelAjuda;
    [SerializeField] private Button     btnAjuda;

    [Header("Lista de Tópicos / Entradas — content compartilhado")]
    [Tooltip("Um ÚNICO content compartilhado entre a lista de tópicos e a lista de entradas do " +
        "tópico aberto — nunca mostra as duas ao mesmo tempo. Clicar um tópico desliza a lista de " +
        "tópicos pra fora (esquerda) e repovoa com as entradas dele, que entram deslizando da " +
        "direita (ver TransicaoDeslizantePagina — mesmo efeito de SistemasListManager/FichaLoaderUI); " +
        "o botão Voltar faz o inverso. Precisa ser um RectTransform pra animar (senão só troca os " +
        "itens sem slide).")]
    [SerializeField] private Transform  listaContainer;
    [SerializeField] private GameObject prefabTopicoItem;  // precisa de TopicoListItemUI
    [SerializeField] private GameObject prefabEntradaItem; // precisa de EntradaListItemUI
    [Tooltip("Visível só no modo Tópicos (ver AtualizarVisibilidadeControlesDaLista).")]
    [SerializeField] private Button     botaoAdicionarTopico;
    [Tooltip("Os 4 campos abaixo são visíveis só no modo Entradas (tópico aberto).")]
    [SerializeField] private TMP_Text   textoTopicoAtual;
    [SerializeField] private Button     botaoAdicionarEntrada;
    [SerializeField] private Button     botaoEditarEstrutura;
    [SerializeField] private Button     botaoVoltarTopicos;

    [Header("Motor de Dados — Acesso (ver Mds/SistemasModulares/MotorDeDados)")]
    [Tooltip("Painel de nível de SISTEMA (irmão do Nível 1, não aninhado num tópico) — propriedades " +
        "do personagem, tipo de rolagem ativo e ações de teste são do sistema inteiro, não de um " +
        "tópico específico. Salvo em motor-de-dados.xml na raiz do sistema (ver CarregadorMotorDeDados).")]
    [SerializeField] private Button     botaoAbrirMotorDeDados;
    [SerializeField] private GameObject painelMotorDeDados;
    [SerializeField] private Button     botaoFecharMotorDeDados;

    [Header("Motor de Dados — Propriedades")]
    [SerializeField] private Transform      propriedadesContainer;
    [SerializeField] private GameObject     prefabPropriedadeItem; // precisa de PropriedadeListItemUI
    [SerializeField] private TMP_InputField inputNovaPropriedadeLabel;
    [Tooltip("0 = Número fixo (usa inputNovaPropriedadeValor como valorBase), 1 = Fórmula (usa " +
        "inputNovaPropriedadeValor como texto de fórmula, ex: floor(({for}-10)/2)).")]
    [SerializeField] private TMP_Dropdown   dropdownNovaPropriedadeTipo;
    [SerializeField] private TMP_InputField inputNovaPropriedadeValor;
    [SerializeField] private Button         botaoAdicionarPropriedade;

    [Header("Motor de Dados — Tipo de Rolagem do Sistema")]
    [Tooltip("0 = linear_check (d20 padrão de D&D), 1 = dice_pool (pool de d10 de Vampire) — só UM " +
        "tipo ativo por sistema nesta fase (ver MotorDeDados.RegistrarResolvedor pra mais tipos no futuro).")]
    [SerializeField] private TMP_Dropdown   dropdownTipoRolagem;
    [SerializeField] private TMP_InputField inputLimiarSucessoPool; // visível só se dice_pool
    [SerializeField] private TMP_InputField inputParMaximoPool;     // visível só se dice_pool
    [SerializeField] private Transform      grausContainer;
    [SerializeField] private GameObject     prefabGrauItem; // precisa de GrauResultadoListItemUI
    [SerializeField] private TMP_InputField inputNovoGrauId;
    [SerializeField] private TMP_InputField inputNovoGrauCondicao;
    [SerializeField] private Button         botaoAdicionarGrau;

    [Header("Motor de Dados — Ações de Teste")]
    [SerializeField] private Transform      acoesContainer;
    [SerializeField] private GameObject     prefabAcaoItem; // precisa de AcaoRolagemListItemUI
    [SerializeField] private TMP_InputField inputNovaAcaoNome;
    [SerializeField] private Button         botaoAdicionarAcao;

    [Header("Motor de Dados — Painel de Teste")]
    [Tooltip("EstadoPersonagem de RASCUNHO — nunca é salvo em motor-de-dados.xml, só as definições " +
        "(propriedades/tipo de rolagem/ações) são persistidas.")]
    [SerializeField] private GameObject     painelTesteRolagem;
    [SerializeField] private TMP_Text       textoAcaoEmTeste;
    [SerializeField] private Transform      propriedadesTesteContainer;
    [SerializeField] private GameObject     prefabPropriedadeTesteItem; // precisa de PropriedadeTesteItemUI
    [SerializeField] private Button         botaoRolarTeste;
    [SerializeField] private Button         botaoFecharTesteRolagem;
    [SerializeField] private TMP_Text       textoResultadoTeste;

    [Header("Nível 3a — Criador de Estruturas (Schema do tópico)")]
    [SerializeField] private GameObject     painelSchema;
    [SerializeField] private Transform      schemaContainer;
    [SerializeField] private GameObject     prefabSchemaCampoItem; // precisa de SchemaCampoItemUI
    [SerializeField] private TMP_InputField inputNovoCampoLabel;
    [SerializeField] private TMP_Dropdown   dropdownNovoCampoTipo; // Texto / Número / Texto Longo / Lista
    [SerializeField] private Button         botaoAdicionarCampoSchema;
    [SerializeField] private Button         botaoFecharSchema;

    [Header("Nível 3b — Editor da Entrada Selecionada")]
    [SerializeField] private GameObject painelEditorEntrada;
    [SerializeField] private Transform  entryFieldsContainer;
    [SerializeField] private GameObject prefabEntryPanel;
    [SerializeField] private Button     botaoSalvarEntrada;
    [SerializeField] private Button     botaoFecharEditorEntrada;

    // Tipos expostos no Criador de Estruturas — subconjunto do FieldType completo: o suficiente
    // pra a maioria dos campos de sistema (texto curto/longo, número, lista). Attribute/UnitValue/
    // ObjectList/Group/Image continuam existindo no enum (usados pelo catálogo antigo hardcoded),
    // só não são oferecidos aqui pro criador — exigiriam UI/parâmetros extras (unidade, sub-campos
    // de objeto) fora do escopo de "Label + ID + Tipo" pedido. "Fórmula" também não existe ainda —
    // depende do motor de expressões (AvaliadorDeExpressao, ver Mds/SistemasModulares) que ainda
    // não está ligado à ficha de personagem.
    private static readonly (string Label, FieldType Tipo)[] TiposDisponiveis =
    {
        ("Texto",       FieldType.Text),
        ("Número",      FieldType.Number),
        ("Texto Longo", FieldType.TextArea),
        ("Lista",       FieldType.StringList),
    };

    // Campos sempre presentes em toda entrada de qualquer tópico, nunca definidos pelo criador
    // (Passo C do pedido) — reaproveita as MESMAS definições do catálogo antigo pra manter
    // compatibilidade com dados já salvos por versões anteriores da Forja.
    private static readonly XmlFieldDef CampoFixoId   = XmlFieldDef.Catalog.Find(f => f.FieldId == "attr-id");
    private static readonly XmlFieldDef CampoFixoNome = XmlFieldDef.Catalog.Find(f => f.FieldId == "name");

    // ── Estado — Sistema/Tópicos ────────────────────────────────────────────────

    private string       _sistemaId;
    private string       _sistemaPath;
    private string       _imagemSistemaPath;
    private List<string> _topicos = new List<string>();
    private readonly List<TopicoListItemUI> _itensTopico = new List<TopicoListItemUI>();
    private int  _topicoSelecionadoIndex = -1;
    private bool _modoEdicao;

    // ── Estado — Entradas do tópico aberto ──────────────────────────────────────

    private string           _topicoAtualId;
    private string           _pastaTopicoAtual;
    private List<EntryData>  _entradasDoTopico = new List<EntryData>();
    private readonly List<EntradaListItemUI> _itensEntrada = new List<EntradaListItemUI>();
    private int _entradaSelecionadaIndex = -1;
    private EntryPanelUI _entryPanelAtivo;

    // true = listaContainer está mostrando as entradas do tópico aberto; false = mostrando a lista
    // de tópicos. Guia tanto a visibilidade dos controles (AtualizarVisibilidadeControlesDaLista)
    // quanto a direção do slide (ver AbrirTopico/VoltarParaListaTopicos).
    private bool _visualizandoEntradas;
    private Coroutine _transicaoListaCoroutine;

    // ── Estado — Estrutura (schema) do tópico aberto ────────────────────────────

    private List<XmlFieldDef> _schemaTopicoAtual = new List<XmlFieldDef>();

    // ── Estado — Motor de Dados (nível de sistema, ver Mds/SistemasModulares/MotorDeDados) ──────

    private DefinicaoMotorDeDados _motorDeDados = new DefinicaoMotorDeDados();
    private readonly List<PropriedadeListItemUI> _itensPropriedade = new List<PropriedadeListItemUI>();
    // Só UM tipo de rolagem ativo por sistema nesta fase — referência dentro de
    // _motorDeDados.tiposDeRolagem, nunca null quando o painel Motor de Dados está aberto.
    private DefinicaoTipoRolagem _tipoRolagemAtivo;
    private readonly List<GrauResultadoListItemUI> _itensGrau = new List<GrauResultadoListItemUI>();
    private readonly List<AcaoRolagemListItemUI> _itensAcao = new List<AcaoRolagemListItemUI>();

    // EstadoPersonagem de RASCUNHO do painel de teste — recriado sob demanda (ver AbrirPainelTeste),
    // nunca persistido em disco.
    private EstadoPersonagem _estadoTeste;
    private DefinicaoAcaoRolagem _acaoEmTeste;
    private readonly List<PropriedadeTesteItemUI> _itensPropriedadeTeste = new List<PropriedadeTesteItemUI>();

    // Nomes injetados como pseudo-propriedades transitórias por MotorDeDados durante uma rolagem
    // (ver MotorDeDados.ResolverLinearCheck/ResolverDicePool) — uma Propriedade nova não pode usar
    // nenhum destes ids, senão a rolagem pisaria no valor real do personagem.
    private static readonly HashSet<string> IdsReservadosPropriedade = new HashSet<string>
    {
        "total", "d1", "maior", "menor", "cd", "modifier", "sucessos", "pares_max", "pool_size", "threshold",
    };

    public event Action OnSistemaSalvo;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Sem esse botão atribuído no Inspector, não sobra NENHUM jeito de sair do modo de edição —
        // Religar() ignora silenciosamente uma referência nula, e nada mais no código chama
        // VoltarParaGerenciamento()/dispara OnSistemaSalvo (confirmado: é o único caminho de volta).
        if (btnVoltarGerenciamento == null)
            Debug.LogWarning("[CustomSystemManager] btnVoltarGerenciamento não atribuído no Inspector — " +
                "não há como sair do modo de edição do Compêndio depois de entrar nele. Arraste o botão " +
                "de voltar correspondente no Inspector deste componente.");

        Religar(btnVoltarGerenciamento, VoltarParaGerenciamento);
        Religar(btnAjuda, AbrirAjuda);
        Religar(botaoAdicionarTopico, BotaoAdicionarTopico);
        Religar(botaoSelecionarImagem, AbrirSeletorDeImagem);

        Religar(botaoAdicionarEntrada, ClicouAdicionarEntrada);
        Religar(botaoEditarEstrutura, AbrirEstrutura);
        Religar(botaoVoltarTopicos, VoltarParaListaTopicos);

        Religar(botaoAdicionarCampoSchema, AdicionarCampoAoSchema);
        Religar(botaoFecharSchema, FecharEstrutura);

        Religar(botaoSalvarEntrada, SalvarEntradaAtual);
        Religar(botaoFecharEditorEntrada, FecharEditorDeEntrada);

        Religar(botaoAbrirMotorDeDados, AbrirMotorDeDados);
        Religar(botaoFecharMotorDeDados, FecharMotorDeDados);
        Religar(botaoAdicionarPropriedade, AdicionarPropriedade);
        Religar(botaoAdicionarGrau, AdicionarGrau);
        Religar(botaoAdicionarAcao, AdicionarAcao);
        Religar(botaoRolarTeste, ExecutarTesteDeRolagem);
        Religar(botaoFecharTesteRolagem, FecharPainelTeste);

        if (dropdownTipoRolagem != null)
        {
            dropdownTipoRolagem.onValueChanged.RemoveListener(AoTipoRolagemAlterado);
            dropdownTipoRolagem.onValueChanged.AddListener(AoTipoRolagemAlterado);
        }
        if (inputLimiarSucessoPool != null)
        {
            inputLimiarSucessoPool.onEndEdit.RemoveListener(AoLimiarOuParMaximoEditado);
            inputLimiarSucessoPool.onEndEdit.AddListener(AoLimiarOuParMaximoEditado);
        }
        if (inputParMaximoPool != null)
        {
            inputParMaximoPool.onEndEdit.RemoveListener(AoLimiarOuParMaximoEditado);
            inputParMaximoPool.onEndEdit.AddListener(AoLimiarOuParMaximoEditado);
        }

        if (inputNomeSistema != null)
        {
            inputNomeSistema.onEndEdit.RemoveListener(AoNomeSistemaEditado);
            inputNomeSistema.onEndEdit.AddListener(AoNomeSistemaEditado);
        }

        ConfigurarDropdownTipos();
        ConfigurarDropdownTipoRolagem();
        ConfigurarDropdownTipoPropriedade();
    }

    private void OnEnable()
    {
        if (!_modoEdicao)
        {
            SetAtivo(painelAjuda, false);
            LimparParaNovoSistema();
        }
    }

    private void LimparParaNovoSistema()
    {
        _sistemaId              = null;
        _sistemaPath            = null;
        _imagemSistemaPath      = null;
        _topicoSelecionadoIndex = -1;
        _topicos.Clear();

        if (inputNomeSistema != null) inputNomeSistema.text = string.Empty;

        AtualizarPreviewImagem(null);
        ResetarParaListaDeTopicos(); // já reconstrói a lista de tópicos — ver comentário no método
        CarregarMotorDeDadosDoSistema();
        FecharMotorDeDados();
    }

    // ── Edição de sistema existente ───────────────────────────────────────────

    public void CarregarSistemaExistente(string sistemaPath, string nomeExibicao)
    {
        _modoEdicao  = true;
        _sistemaId   = Path.GetFileName(sistemaPath);
        _sistemaPath = sistemaPath;

        if (inputNomeSistema != null) inputNomeSistema.text = nomeExibicao;

        _topicos.Clear();
        if (Directory.Exists(sistemaPath))
        {
            string[] subpastas = Directory.GetDirectories(sistemaPath);
            Array.Sort(subpastas, StringComparer.OrdinalIgnoreCase);
            foreach (string pasta in subpastas)
            {
                string nomePasta = Path.GetFileName(pasta);
                // "dados" é a pasta interna de metadados (regras_jogando.json) — ver
                // SystemManager.DadosFolderName — nunca é um tópico de verdade.
                if (string.Equals(nomePasta, SystemManager.DadosFolderName, StringComparison.OrdinalIgnoreCase))
                    continue;
                _topicos.Add(nomePasta);
            }
        }

        _imagemSistemaPath = EncontrarImagemExistente(sistemaPath);
        AtualizarPreviewImagem(_imagemSistemaPath);

        SetAtivo(painelAjuda, false);
        _topicoSelecionadoIndex = -1;
        ResetarParaListaDeTopicos(); // já reconstrói a lista de tópicos — ver comentário no método
        CarregarMotorDeDadosDoSistema();
        FecharMotorDeDados();
    }

    // ── Navegação ─────────────────────────────────────────────────────────────

    public void VoltarParaGerenciamento()
    {
        ResetarParaListaDeTopicos();
        FecharMotorDeDados();
        _modoEdicao = false;

        // Dispara pro assinante de edição (SystemManager.EditarSistema) E também navega direto —
        // no modo de CRIAÇÃO ninguém assina esse evento, então sem a chamada direta o usuário
        // ficaria preso aqui (mesma robustez de duplo caminho que o botão "Voltar" original já tinha).
        OnSistemaSalvo?.Invoke();
        SystemManager.Instance?.VoltarParaListaSistemas();
    }

    // ── Ajuda ─────────────────────────────────────────────────────────────────

    public void AbrirAjuda()  => SetAtivo(painelAjuda, painelAjuda != null && !painelAjuda.activeSelf);
    public void FecharAjuda() => SetAtivo(painelAjuda, false);

    // ── Nome do sistema (auto-save) ───────────────────────────────────────────

    private void AoNomeSistemaEditado(string nomeDigitado)
    {
        string nome = nomeDigitado?.Trim();
        if (string.IsNullOrWhiteSpace(nome)) return; // não salva nome vazio — só ignora

        GarantirPastaDoSistema(nome);
        SalvarMetadados(nome);
    }

    // A pasta (e o ID dela) só é criada UMA VEZ, na primeira vez que um nome válido é digitado —
    // renomear depois só atualiza o campo "nome" dentro do metadata (ver SalvarMetadados), nunca
    // move/renomeia a pasta em si (os tópicos já salvos apontam pra ela pelo ID antigo).
    private void GarantirPastaDoSistema(string nome)
    {
        if (!string.IsNullOrWhiteSpace(_sistemaPath) && Directory.Exists(_sistemaPath)) return;

        _sistemaId   = FormatarId(nome);
        _sistemaPath = Path.Combine(Application.persistentDataPath, SystemManager.SistemasFolderName, _sistemaId);
        Directory.CreateDirectory(_sistemaPath);
    }

    private void SalvarMetadados(string nome)
    {
        if (string.IsNullOrWhiteSpace(_sistemaPath)) return;

        string pastaDados = Path.Combine(_sistemaPath, SystemManager.DadosFolderName);
        Directory.CreateDirectory(pastaDados);
        string caminhoMeta = Path.Combine(pastaDados, SystemManager.MetadadosFileName);

        SistemaMetadados meta = null;
        if (File.Exists(caminhoMeta))
        {
            try { meta = JsonUtility.FromJson<SistemaMetadados>(File.ReadAllText(caminhoMeta)); }
            catch (Exception e) { VTTLog.ForjaError($"Metadados existentes inválidos, recriando: {e.Message}"); }
        }
        meta ??= new SistemaMetadados { versao = "1.0", autor = "" };

        meta.nome = nome;
        if (string.IsNullOrWhiteSpace(meta.descricao))
            meta.descricao = "Sistema personalizado local.";

        File.WriteAllText(caminhoMeta, JsonUtility.ToJson(meta, prettyPrint: true));
    }

    // ── Imagem do sistema ─────────────────────────────────────────────────────

    private void AbrirSeletorDeImagem()
    {
        FileBrowser.SetFilters(true, new FileBrowser.Filter("Imagens", ".jpg", ".png", ".jpeg", ".webp"));
        FileBrowser.ShowLoadDialog(
            (caminhos) =>
            {
                if (caminhos == null || caminhos.Length == 0) return;
                PersistirImagemDoSistema(caminhos[0]);
            },
            null,
            FileBrowser.PickMode.Files, false, null, null, "Selecione a Imagem do Sistema", "Selecionar");
    }

    private void PersistirImagemDoSistema(string caminhoOrigem)
    {
        if (string.IsNullOrWhiteSpace(_sistemaId) || string.IsNullOrWhiteSpace(_sistemaPath))
        {
            PopupManager.Instance?.MostrarErro("Nome do sistema obrigatório", "Defina o nome do sistema antes de escolher uma imagem.");
            return;
        }

        try
        {
            // Remove qualquer "*-token.*" anterior — SystemManager.CarregarTokenSistema pega só o
            // PRIMEIRO arquivo que casar o padrão na raiz da pasta, então um token antigo sobrando
            // confundiria a busca (poderia carregar o antigo em vez do novo).
            foreach (string antigo in Directory.GetFiles(_sistemaPath, "*-token.*", SearchOption.TopDirectoryOnly))
                File.Delete(antigo);

            string extensao = Path.GetExtension(caminhoOrigem);
            string destino  = Path.Combine(_sistemaPath, $"{_sistemaId}-token{extensao}");
            File.Copy(caminhoOrigem, destino, overwrite: true);

            _imagemSistemaPath = destino;
            AtualizarPreviewImagem(destino);
        }
        catch (Exception e)
        {
            VTTLog.ForjaError($"Falha ao copiar imagem do sistema: {e.Message}");
        }
    }

    private static string EncontrarImagemExistente(string pastaSistema)
    {
        if (!Directory.Exists(pastaSistema)) return null;
        string[] candidatos = Directory.GetFiles(pastaSistema, "*-token.*", SearchOption.TopDirectoryOnly);
        return candidatos.Length > 0 ? candidatos[0] : null;
    }

    private void AtualizarPreviewImagem(string caminhoArquivo)
    {
        if (imagemSistemaPreview == null) return;

        if (string.IsNullOrWhiteSpace(caminhoArquivo) || !File.Exists(caminhoArquivo))
        {
            imagemSistemaPreview.sprite  = null;
            imagemSistemaPreview.enabled = false;
            return;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(caminhoArquivo);
            var textura = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!textura.LoadImage(bytes)) { Destroy(textura); return; }

            imagemSistemaPreview.sprite  = Sprite.Create(textura, new Rect(0, 0, textura.width, textura.height), new Vector2(0.5f, 0.5f), 100f);
            imagemSistemaPreview.enabled = true;
        }
        catch (Exception e)
        {
            VTTLog.ForjaError($"Falha ao carregar preview da imagem do sistema: {e.Message}");
        }
    }

    // ── Nível 1 — Tópicos ────────────────────────────────────────────────────────

    private void BotaoAdicionarTopico()
    {
        if (string.IsNullOrWhiteSpace(_sistemaId) || string.IsNullOrWhiteSpace(_sistemaPath))
        {
            PopupManager.Instance?.MostrarErro("Nome do sistema obrigatório", "Defina o nome do sistema antes de adicionar um tópico.");
            return;
        }

        PopupManager.Instance?.MostrarEdicao(
            "Novo Tópico", "Nome do tópico (ex: raça, classe):", "",
            onConfirmar: ConfirmarNovoTopico,
            placeholder: "raça",
            labelConfirmar: "Adicionar");
    }

    private void ConfirmarNovoTopico(string textoDigitado)
    {
        string id = FormatarId(textoDigitado);
        if (string.IsNullOrWhiteSpace(id))
        {
            PopupManager.Instance?.MostrarErro("Tópico inválido", "Digite um nome de tópico válido (ex: raça, classe).");
            return;
        }

        if (_topicos.Contains(id))
        {
            PopupManager.Instance?.MostrarErro("Tópico já existe", $"O tópico '{id}' já foi adicionado.");
            return;
        }

        Directory.CreateDirectory(Path.Combine(_sistemaPath, id));
        _topicos.Add(id);

        ReconstruirListaTopicos();
    }

    private void ReconstruirListaTopicos()
    {
        if (listaContainer == null || prefabTopicoItem == null) return;

        for (int i = listaContainer.childCount - 1; i >= 0; i--)
            Destroy(listaContainer.GetChild(i).gameObject);
        _itensTopico.Clear();

        for (int i = 0; i < _topicos.Count; i++)
        {
            int    indice   = i; // captura por valor — evita o clássico bug de closure sobre variável de loop
            string topicoId = _topicos[i];

            GameObject       go   = UIUtils.Spawn(prefabTopicoItem, listaContainer);
            TopicoListItemUI item = go.GetComponent<TopicoListItemUI>();
            if (item == null)
            {
                Debug.LogWarning("[CustomSystemManager] prefabTopicoItem não possui TopicoListItemUI.");
                continue;
            }

            item.Configurar(topicoId,
                () => SelecionarTopico(indice),
                () => AbrirTopico(indice));
            _itensTopico.Add(item);
        }

        ForcarRebuildLayout(listaContainer);
    }

    // "Selecionar" só guarda qual tópico está em foco (sem destaque visual, ver TopicoListItemUI) —
    // quem realmente abre a lista de entradas é o botão "Editar" (ver AbrirTopico). Mantido separado
    // por pedido explícito do usuário (dois botões distintos por item).
    private void SelecionarTopico(int indice)
    {
        _topicoSelecionadoIndex = indice;
    }

    // ── Nível 2 — Entradas (arquivos XML) do tópico aberto ──────────────────────
    //
    // listaContainer é COMPARTILHADO com a lista de tópicos acima — abrir/fechar um tópico nunca
    // ativa/desativa um painel separado, só troca o CONTEÚDO desse mesmo content com um slide (ver
    // IniciarTransicaoDeLista) e alterna quais botões/textos ficam visíveis (ver
    // AtualizarVisibilidadeControlesDaLista).

    private void AbrirTopico(int indice)
    {
        if (indice < 0 || indice >= _topicos.Count) return;
        StartCoroutine(AbrirTopicoRotina(indice));
    }

    // Corrotina só pra poder mostrar o popup de carregamento ANTES do trabalho síncrono pesado
    // (CarregarSchemaDoTopico/DescobrirCamposNaoDeclarados/LoadEntriesFromFolder abrem e fazem parse
    // de TODO arquivo .xml do tópico, duas vezes — pode ser bastante coisa num tópico grande tipo
    // monstros/magias do SRD). Sem o yield logo após mostrar o popup, ele NUNCA chegaria a ser
    // desenhado na tela antes do bloqueio — Unity só apresenta um frame depois que todo o Update/
    // corrotinas dele terminam, então "mostrar popup" seguido direto de um método pesado no MESMO
    // frame nunca aparece; precisa ceder pelo menos 1 frame pro Unity desenhar o popup primeiro.
    // O próprio overlay do popup (raycast target) já bloqueia cliques na lista de tópicos enquanto
    // isso roda, então não precisa de nenhuma trava extra contra reentrância.
    private IEnumerator AbrirTopicoRotina(int indice)
    {
        SelecionarTopico(indice);

        string topicoId = _topicos[indice];
        PopupManager.Instance?.MostrarCarregamento("Abrindo Tópico", $"Lendo arquivos de \"{topicoId.Replace("-", " ")}\"...");
        yield return null;

        _topicoAtualId    = topicoId;
        _pastaTopicoAtual = Path.Combine(_sistemaPath, _topicoAtualId);

        CarregarSchemaDoTopico();
        DescobrirCamposNaoDeclarados();
        _entradasDoTopico = DynamicXmlBuilder.LoadEntriesFromFolder(_pastaTopicoAtual, _topicoAtualId, CamposCompletosDoTopico());

        PopupManager.Instance?.Fechar();

        if (textoTopicoAtual != null)
            textoTopicoAtual.text = _topicoAtualId.Replace("-", " ").ToUpperInvariant();

        FecharEditorDeEntrada();
        FecharEstrutura();

        _visualizandoEntradas = true;
        AtualizarVisibilidadeControlesDaLista();
        IniciarTransicaoDeLista(direcao: 1, ReconstruirListaEntradas);
    }

    /// <summary>Botão "Voltar" — sai da lista de entradas e volta a mostrar os tópicos, sempre com o
    /// slide inverso de AbrirTopico. Só reage se já estiver vendo entradas (evita reanimar à toa se
    /// clicado duas vezes rápido). Não confundir com ResetarParaListaDeTopicos (sem animação, usado
    /// só na inicialização/troca de sistema, nunca em resposta a um clique do usuário).</summary>
    private void VoltarParaListaTopicos()
    {
        if (!_visualizandoEntradas) return;

        FecharEditorDeEntrada();
        FecharEstrutura();
        _topicoAtualId    = null;
        _pastaTopicoAtual = null;
        _entradasDoTopico.Clear();
        _schemaTopicoAtual.Clear();

        _visualizandoEntradas = false;
        AtualizarVisibilidadeControlesDaLista();
        IniciarTransicaoDeLista(direcao: -1, ReconstruirListaTopicos);
    }

    /// <summary>Reset INSTANTÂNEO (sem slide) pro modo Tópicos — usado só ao entrar na Forja
    /// (LimparParaNovoSistema/CarregarSistemaExistente) ou ao sair dela (VoltarParaGerenciamento),
    /// nunca em resposta a um clique do usuário dentro da tela (esses sempre passam por
    /// VoltarParaListaTopicos, que anima).</summary>
    private void ResetarParaListaDeTopicos()
    {
        if (_transicaoListaCoroutine != null)
        {
            StopCoroutine(_transicaoListaCoroutine);
            _transicaoListaCoroutine = null;
        }
        if (listaContainer is RectTransform rt) rt.anchoredPosition = Vector2.zero;

        FecharEditorDeEntrada();
        FecharEstrutura();
        _topicoAtualId    = null;
        _pastaTopicoAtual = null;
        _entradasDoTopico.Clear();
        _schemaTopicoAtual.Clear();

        _visualizandoEntradas = false;
        AtualizarVisibilidadeControlesDaLista();
        ReconstruirListaTopicos();
    }

    // TransicaoDeslizantePagina.Executar já cobre listaContainer == null internamente (só troca os
    // itens sem animar), mas ele exige um RectTransform — sem isso (Transform genérico), cai no
    // mesmo fallback de "troca direto, sem slide".
    private void IniciarTransicaoDeLista(int direcao, Action repovoar)
    {
        if (_transicaoListaCoroutine != null) StopCoroutine(_transicaoListaCoroutine);

        if (listaContainer is RectTransform rt)
            _transicaoListaCoroutine = StartCoroutine(TransicaoDeslizantePagina.Executar(rt, direcao, () => repovoar()));
        else
            repovoar();
    }

    private void AtualizarVisibilidadeControlesDaLista()
    {
        if (botaoAdicionarTopico != null) botaoAdicionarTopico.gameObject.SetActive(!_visualizandoEntradas);

        if (textoTopicoAtual != null) textoTopicoAtual.gameObject.SetActive(_visualizandoEntradas);
        if (botaoAdicionarEntrada != null) botaoAdicionarEntrada.gameObject.SetActive(_visualizandoEntradas);
        if (botaoEditarEstrutura != null) botaoEditarEstrutura.gameObject.SetActive(_visualizandoEntradas);
        if (botaoVoltarTopicos != null) botaoVoltarTopicos.gameObject.SetActive(_visualizandoEntradas);
    }

    private void ReconstruirListaEntradas()
    {
        if (listaContainer == null || prefabEntradaItem == null) return;

        for (int i = listaContainer.childCount - 1; i >= 0; i--)
            Destroy(listaContainer.GetChild(i).gameObject);
        _itensEntrada.Clear();

        for (int i = 0; i < _entradasDoTopico.Count; i++)
        {
            int indice = i; // captura por valor
            GameObject go = UIUtils.Spawn(prefabEntradaItem, listaContainer);
            EntradaListItemUI item = go.GetComponent<EntradaListItemUI>();
            if (item == null)
            {
                Debug.LogWarning("[CustomSystemManager] prefabEntradaItem não possui EntradaListItemUI.");
                continue;
            }

            item.Configurar(ObterNomeExibicaoEntrada(_entradasDoTopico[i], i), () => AbrirEntradaExistente(indice));
            _itensEntrada.Add(item);
        }

        ForcarRebuildLayout(listaContainer);
    }

    private string ObterNomeExibicaoEntrada(EntryData entrada, int indice)
    {
        FieldValue campoNome = entrada.Fields.Find(fv => fv.Definition.FieldId == "name");
        return !string.IsNullOrWhiteSpace(campoNome?.TextValue)
            ? campoNome.TextValue
            : $"{_topicoAtualId} #{indice + 1}";
    }

    // ── Nível 3a — Criador de Estruturas (schema do tópico) ─────────────────────
    //
    // Passo A/B do pedido: em vez do catálogo fixo hardcoded (XmlFieldDef.Catalog) + toggles por
    // entrada, cada tópico tem sua PRÓPRIA lista de campos definida pelo criador do sistema, salva
    // em "schema_{topico}.xml" na pasta do sistema (não dentro da pasta do tópico) e recarregada
    // sempre que o tópico é aberto. Toda entrada do tópico usa TODOS os campos da estrutura dele —
    // não existe mais escolha de campo por entrada.

    private void AbrirEstrutura()
    {
        if (string.IsNullOrWhiteSpace(_topicoAtualId)) return;
        ReconstruirListaSchema();
        SetAtivo(painelSchema, true);
    }

    private void FecharEstrutura() => SetAtivo(painelSchema, false);

    private string CaminhoArquivoSchema(string topicId) =>
        Path.Combine(_sistemaPath, $"schema_{topicId}.xml");

    private void CarregarSchemaDoTopico()
    {
        _schemaTopicoAtual = CarregarSchemaDeArquivo(CaminhoArquivoSchema(_topicoAtualId));
    }

    // DynamicXmlBuilder.ParseFields só reconhece uma tag do XML se ela já estiver no catálogo
    // passado pra ele — qualquer tag "estranha" (ex: dados salvos antes da Estrutura existir, ou
    // vindos de outra ferramenta) é IGNORADA na leitura, nem chega a virar FieldValue. Isso varre os
    // XMLs de verdade do tópico e adiciona à Estrutura EM MEMÓRIA (com melhor esforço de tipo)
    // qualquer tag que ainda não seja conhecida, ANTES de carregar as entradas — sem isso, o dado
    // desconhecido nunca chegaria a aparecer em lugar nenhum, só ficaria "perdido" dentro do arquivo.
    //
    // NÃO persiste em disco sozinho (nada de SalvarSchemaDoTopicoNoDisco aqui) — abrir um tópico é
    // navegação, não edição, e sistemas nunca autorados pela Forja (ex: o SRD embutido, cujos
    // tópicos vêm prontos do StreamingAssets) não deveriam ganhar um schema_<topico>.xml no disco só
    // por terem sido visitados. A persistência real só acontece quando o próprio usuário edita a
    // Estrutura de propósito (ver AdicionarCampoAoSchema/RemoverCampoDoSchema) — até lá, os campos
    // inferidos continuam disponíveis nesta sessão (edição/visualização funcionam normalmente), só
    // não sobrevivem a fechar e reabrir o tópico sem terem sido confirmados na Estrutura.
    private void DescobrirCamposNaoDeclarados()
    {
        if (!Directory.Exists(_pastaTopicoAtual)) return;

        // Auto-cura de schema_<topico>.xml salvo por uma versão anterior que não persistia
        // SubFields/xmlItemTag (ver CarregarSchemaDeArquivo) — um Group/ObjectList carregado do
        // disco com SubFields.Count == 0 está "conhecido" pelo id, mas estruturalmente vazio; sem
        // isto, o loop abaixo pula essa tag pra sempre (já é "conhecida") e a estrutura rica nunca
        // volta a aparecer sozinha. Remove essas entradas rasas ANTES de montar tagsConhecidas, pra
        // serem redescobertas do zero como se fossem novas.
        List<XmlFieldDef> vaziosParaReprocessar = _schemaTopicoAtual
            .Where(f => (f.Type == FieldType.Group || f.Type == FieldType.ObjectList) && f.SubFields.Count == 0)
            .ToList();
        if (vaziosParaReprocessar.Count > 0)
        {
            foreach (XmlFieldDef vazio in vaziosParaReprocessar)
                VTTLog.Forja($"[Estrutura] Campo '{vazio.XmlTag}' de '{_topicoAtualId}' estava salvo sem " +
                    "sub-campos (schema de versão anterior) — redescobrindo a estrutura completa.");
            _schemaTopicoAtual.RemoveAll(f => vaziosParaReprocessar.Contains(f));
        }

        var tagsConhecidas = new HashSet<string>(CamposCompletosDoTopico().Select(f => f.XmlTag));

        foreach (string arquivo in Directory.GetFiles(_pastaTopicoAtual, "*.xml"))
        {
            XmlDocument doc = new XmlDocument();
            try { doc.Load(arquivo); }
            catch (Exception e)
            {
                VTTLog.ForjaError($"Falha ao ler '{arquivo}' pra descobrir campos: {e.Message}");
                continue;
            }

            XmlElement root = doc.DocumentElement;
            if (root == null) continue;

            foreach (XmlNode node in root.ChildNodes)
            {
                if (node is not XmlElement elemento) continue;
                if (tagsConhecidas.Contains(elemento.Name)) continue;

                XmlFieldDef inferido = InferirCampoDeElemento(elemento);
                _schemaTopicoAtual.Add(inferido);
                tagsConhecidas.Add(elemento.Name);

                VTTLog.Forja($"[Estrutura] Campo '{elemento.Name}' encontrado em '{Path.GetFileName(arquivo)}' " +
                    $"não estava na Estrutura de '{_topicoAtualId}' — reconhecido nesta sessão como " +
                    $"\"{inferido.Label}\" ({inferido.Type}), sem salvar em disco (ver comentário acima).");
            }
        }
    }

    // Melhor esforço, sem garantias — o usuário pode entrar na Estrutura e ajustar o tipo/label
    // depois se a inferência errar. Regras, na ordem:
    //   1. Sem filhos (folha) → número se o conteúdo for puramente numérico, senão texto/texto longo.
    //   2. Filhos todos com a MESMA tag → é uma repetição de itens → SEMPRE ObjectList, nunca
    //      StringList — mesmo pra item de texto puro (sem atributo/sub-tag nenhum), que vira uma
    //      sub-entrada com um único campo de texto curto (ver XmlFieldDef.MarcadorTextoProprio em
    //      InferirSubCampos). Cada item ganha sua PRÓPRIA sub-entrada removível em FieldInputUI/
    //      SubEntryUI (um input de texto curto por item), nunca uma caixa multiline compartilhada —
    //      pedido explícito: "cada opção é o input field de texto curto".
    //   3. Filhos com tags DIFERENTES entre si → um objeto único aninhado (Group) — antes isso caía
    //      errado no caso 1 (folha), concatenando todo o texto descendente num Text/TextArea único.
    private static XmlFieldDef InferirCampoDeElemento(XmlElement elemento)
    {
        string id    = elemento.Name;
        string label = ObterLabelAPartirDeTag(id);

        var filhosElementos = new List<XmlElement>();
        foreach (XmlNode filho in elemento.ChildNodes)
            if (filho is XmlElement el) filhosElementos.Add(el);

        if (filhosElementos.Count == 0)
        {
            string texto = elemento.InnerText ?? "";
            if (!string.IsNullOrWhiteSpace(texto) &&
                double.TryParse(texto, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                return new XmlFieldDef(id, label, id, FieldType.Number, "");

            return new XmlFieldDef(id, label, id, texto.Length > 80 ? FieldType.TextArea : FieldType.Text, "");
        }

        if (TodosOsFilhosTemMesmaTag(elemento, out string itemTag))
        {
            // Amostra usada pra construir os sub-campos = o item mais "rico" da lista (com atributo
            // ou sub-elemento, se algum tiver), não sempre o primeiro — senão um item com atributo
            // (ex: <item id="Ouro">15</item> numa lista onde os outros itens são texto puro) perderia
            // esse atributo por não fazer parte da amostra usada pra descobrir os sub-campos.
            XmlElement amostra = filhosElementos.Find(TemSubEstrutura) ?? filhosElementos[0];
            return new XmlFieldDef(id, label, id, FieldType.ObjectList, "",
                xmlItemTag: itemTag, subFields: InferirSubCampos(amostra));
        }

        return new XmlFieldDef(id, label, id, FieldType.Group, "", subFields: InferirSubCampos(elemento));
    }

    // Tem atributo(s) e/ou pelo menos um filho elemento — usado só pra escolher a amostra mais rica
    // dentro de uma lista de itens repetidos (ver InferirCampoDeElemento).
    private static bool TemSubEstrutura(XmlElement elemento)
    {
        if (elemento.Attributes.Count > 0) return true;
        foreach (XmlNode filho in elemento.ChildNodes)
            if (filho is XmlElement) return true;
        return false;
    }

    // Sub-campos de um Group/ObjectList — recursivo de propósito: um filho ELEMENTO delega pra
    // InferirCampoDeElemento (mesma classificação leaf/lista/grupo do topo), então um Group cujo
    // filho é ELE MESMO uma lista repetida (ex: <equipment><choice>...</choice><fixed>...</fixed>
    // </equipment> — "choice" e "fixed" são filhos de equipment, mas cada um tem sua própria lista
    // de <option>/<item>) é reconhecido corretamente como tal, em vez de ser achatado num texto só
    // concatenando tudo (bug antigo: só extraía InnerText de cada filho, sem checar se o filho em
    // si era uma lista — "opções" e "fixed" apareciam como um blob de texto só, sem separação).
    private static List<XmlFieldDef> InferirSubCampos(XmlElement amostra)
    {
        var subCampos = new List<XmlFieldDef>();

        foreach (XmlAttribute atributo in amostra.Attributes)
            subCampos.Add(new XmlFieldDef(atributo.Name, ObterLabelAPartirDeTag(atributo.Name), atributo.Name, FieldType.Attribute, ""));

        bool temFilhoElemento = false;
        foreach (XmlNode filho in amostra.ChildNodes)
        {
            if (filho is not XmlElement el) continue;
            temFilhoElemento = true;
            subCampos.Add(InferirCampoDeElemento(el));
        }

        // Elemento-folha (sem NENHUM filho elemento) com texto próprio não-vazio — cobre tanto
        // <option id="...">Uma sagrao sagrada</option> (atributo + texto, vira o SEGUNDO campo, lado
        // a lado com o atributo) quanto <item>Uma veste religiosa</item> puro (sem atributo nenhum,
        // já que a lista inteira agora é sempre ObjectList — vira o ÚNICO campo do item, um simples
        // texto curto). Sem isto, o texto some da leitura por completo: nada acima captura "o
        // InnerText do próprio elemento" (só filhos ELEMENTO viram sub-campo). FieldInputUI/
        // SubEntryUI já são 100% genéricos por XmlFieldDef.Type, não precisam de nenhuma mudança pra
        // isso funcionar.
        string textoProprio = (amostra.InnerText ?? "").Trim();
        if (!temFilhoElemento && !string.IsNullOrWhiteSpace(textoProprio))
        {
            subCampos.Add(new XmlFieldDef("texto", "Texto", XmlFieldDef.MarcadorTextoProprio,
                textoProprio.Length > 80 ? FieldType.TextArea : FieldType.Text, ""));
        }

        return subCampos;
    }

    private static bool TodosOsFilhosTemMesmaTag(XmlElement elemento, out string tagComum)
    {
        tagComum = null;
        foreach (XmlNode filho in elemento.ChildNodes)
        {
            if (filho is not XmlElement el) return false;
            if (tagComum == null) tagComum = el.Name;
            else if (el.Name != tagComum) return false;
        }
        return tagComum != null;
    }

    private static string ObterLabelAPartirDeTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return tag;
        string comEspacos = tag.Replace("-", " ").Replace("_", " ");
        return char.ToUpperInvariant(comEspacos[0]) + comEspacos.Substring(1);
    }

    // Recursivo de propósito: Group/ObjectList (ver InferirCampoDeElemento/InferirSubCampos) têm
    // SubFields — sem ler os <Campo> aninhados de volta, um campo do tipo Group/ObjectList recarrega
    // sempre VAZIO (SubFields.Count == 0), mesmo que a estrutura tenha sido inferida corretamente da
    // primeira vez. Foi exatamente esse bug que fazia traços de personalidade/opções desaparecerem
    // ao reabrir um tópico depois de a Estrutura ter sido salva uma vez.
    private static List<XmlFieldDef> CarregarSchemaDeArquivo(string caminho)
    {
        var campos = new List<XmlFieldDef>();
        if (!File.Exists(caminho)) return campos;

        try
        {
            var doc = new XmlDocument();
            doc.Load(caminho);

            XmlElement raiz = doc.DocumentElement;
            if (raiz == null || raiz.Name != "EsquemaTopico") return campos;

            foreach (XmlNode node in raiz.ChildNodes)
                if (node is XmlElement el && el.Name == "Campo")
                {
                    XmlFieldDef campo = InterpretarElementoCampoSalvo(el);
                    if (campo != null) campos.Add(campo);
                }
        }
        catch (Exception e)
        {
            VTTLog.ForjaError($"Falha ao ler estrutura '{caminho}': {e.Message}");
        }
        return campos;
    }

    private static XmlFieldDef InterpretarElementoCampoSalvo(XmlElement el)
    {
        string id    = el.Attributes["id"]?.Value;
        string label = el.Attributes["label"]?.Value;
        string tipo  = el.Attributes["tipo"]?.Value;
        if (string.IsNullOrWhiteSpace(id) || !Enum.TryParse(tipo, out FieldType tipoCampo)) return null;

        // xmlTag normalmente é igual ao id (todo campo inferido por InferirCampoDeElemento/
        // InferirSubCampos usa o mesmo valor pros dois), MAS diverge pro sub-campo especial de
        // "texto próprio" (id="texto", xmlTag="#text" — ver XmlFieldDef.MarcadorTextoProprio) — sem
        // ler esse atributo de volta, o marcador se perderia no primeiro salvar+recarregar do schema.
        // Ausente (schema salvo por uma versão anterior) cai pro id, mesmo comportamento de antes.
        string xmlTag = el.Attributes["xmlTag"]?.Value;
        if (string.IsNullOrWhiteSpace(xmlTag)) xmlTag = id;

        string itemTag = el.Attributes["itemTag"]?.Value;
        if (string.IsNullOrWhiteSpace(itemTag) && tipoCampo == FieldType.StringList) itemTag = "item";

        var subCampos = new List<XmlFieldDef>();
        foreach (XmlNode filho in el.ChildNodes)
            if (filho is XmlElement subEl && subEl.Name == "Campo")
            {
                XmlFieldDef sub = InterpretarElementoCampoSalvo(subEl);
                if (sub != null) subCampos.Add(sub);
            }

        return new XmlFieldDef(id, label ?? id, xmlTag, tipoCampo, "", xmlItemTag: itemTag, subFields: subCampos);
    }

    private void SalvarSchemaDoTopicoNoDisco()
    {
        if (string.IsNullOrWhiteSpace(_sistemaPath) || string.IsNullOrWhiteSpace(_topicoAtualId)) return;

        var doc = new XmlDocument();
        XmlElement raiz = doc.CreateElement("EsquemaTopico");
        raiz.SetAttribute("topico", _topicoAtualId);
        doc.AppendChild(raiz);

        foreach (XmlFieldDef campo in _schemaTopicoAtual)
            raiz.AppendChild(ConstruirElementoCampoParaSalvar(doc, campo));

        doc.Save(CaminhoArquivoSchema(_topicoAtualId));
    }

    // Espelho recursivo de InterpretarElementoCampoSalvo — persiste xmlItemTag e SubFields
    // (aninhando <Campo> dentro de <Campo>), senão a leitura de volta perde a estrutura.
    private static XmlElement ConstruirElementoCampoParaSalvar(XmlDocument doc, XmlFieldDef campo)
    {
        XmlElement el = doc.CreateElement("Campo");
        el.SetAttribute("id", campo.FieldId);
        el.SetAttribute("label", campo.Label);
        el.SetAttribute("tipo", campo.Type.ToString());
        el.SetAttribute("xmlTag", campo.XmlTag); // pode divergir do id — ver InterpretarElementoCampoSalvo
        if (!string.IsNullOrWhiteSpace(campo.XmlItemTag))
            el.SetAttribute("itemTag", campo.XmlItemTag);

        foreach (XmlFieldDef sub in campo.SubFields)
            el.AppendChild(ConstruirElementoCampoParaSalvar(doc, sub));

        return el;
    }

    private void AdicionarCampoAoSchema()
    {
        string label = inputNovoCampoLabel != null ? inputNovoCampoLabel.text?.Trim() : null;
        if (string.IsNullOrWhiteSpace(label))
        {
            PopupManager.Instance?.MostrarErro("Nome do campo obrigatório", "Digite um nome de exibição pro campo (ex: Dado de Vida).");
            return;
        }

        string id = FormatarId(label);
        if (string.IsNullOrWhiteSpace(id) || id == CampoFixoId?.FieldId || id == CampoFixoNome?.FieldId)
        {
            PopupManager.Instance?.MostrarErro("Nome de campo inválido", "Escolha outro nome — esse é reservado ('id'/'nome') ou inválido.");
            return;
        }
        if (_schemaTopicoAtual.Any(f => f.FieldId == id))
        {
            PopupManager.Instance?.MostrarErro("Campo já existe", $"Já existe um campo com o ID '{id}'.");
            return;
        }

        FieldType tipo = ObterTipoSelecionadoNoDropdown();
        _schemaTopicoAtual.Add(new XmlFieldDef(id, label, id, tipo, "",
            xmlItemTag: tipo == FieldType.StringList ? "item" : null));
        SalvarSchemaDoTopicoNoDisco();

        if (inputNovoCampoLabel != null) inputNovoCampoLabel.text = string.Empty;
        ReconstruirListaSchema();
    }

    private void RemoverCampoDoSchema(int indice)
    {
        if (indice < 0 || indice >= _schemaTopicoAtual.Count) return;

        _schemaTopicoAtual.RemoveAt(indice);
        SalvarSchemaDoTopicoNoDisco();
        ReconstruirListaSchema();
    }

    private void ReconstruirListaSchema()
    {
        if (schemaContainer == null || prefabSchemaCampoItem == null) return;

        for (int i = schemaContainer.childCount - 1; i >= 0; i--)
            Destroy(schemaContainer.GetChild(i).gameObject);

        for (int i = 0; i < _schemaTopicoAtual.Count; i++)
        {
            int indice = i; // captura por valor
            XmlFieldDef campo = _schemaTopicoAtual[i];

            GameObject go = UIUtils.Spawn(prefabSchemaCampoItem, schemaContainer);
            SchemaCampoItemUI item = go.GetComponent<SchemaCampoItemUI>();
            if (item == null)
            {
                Debug.LogWarning("[CustomSystemManager] prefabSchemaCampoItem não possui SchemaCampoItemUI.");
                continue;
            }

            item.Configurar(campo.Label, ObterNomeExibicaoTipo(campo.Type), () => RemoverCampoDoSchema(indice));
        }

        ForcarRebuildLayout(schemaContainer);
    }

    private void ConfigurarDropdownTipos()
    {
        if (dropdownNovoCampoTipo == null) return;
        dropdownNovoCampoTipo.ClearOptions();
        dropdownNovoCampoTipo.AddOptions(TiposDisponiveis.Select(t => t.Label).ToList());
    }

    private FieldType ObterTipoSelecionadoNoDropdown()
    {
        if (dropdownNovoCampoTipo == null) return FieldType.Text;
        int indice = Mathf.Clamp(dropdownNovoCampoTipo.value, 0, TiposDisponiveis.Length - 1);
        return TiposDisponiveis[indice].Tipo;
    }

    private static string ObterNomeExibicaoTipo(FieldType tipo)
    {
        foreach ((string Label, FieldType Tipo) par in TiposDisponiveis)
            if (par.Tipo == tipo) return par.Label;
        return tipo.ToString();
    }

    // Campos fixos (id/nome) + tudo que o criador definiu na estrutura do tópico — usado tanto pra
    // ler entradas já salvas do disco (AbrirTopico) quanto pra montar o editor de qualquer entrada
    // (nova ou existente, ver AbrirEditorDeEntrada).
    private List<XmlFieldDef> CamposCompletosDoTopico()
    {
        var campos = new List<XmlFieldDef>();
        if (CampoFixoId   != null) campos.Add(CampoFixoId);
        if (CampoFixoNome != null) campos.Add(CampoFixoNome);
        campos.AddRange(_schemaTopicoAtual);
        return campos;
    }

    // ── Nível 3b — Editor da entrada selecionada ────────────────────────────────

    private void ClicouAdicionarEntrada()
    {
        if (string.IsNullOrWhiteSpace(_topicoAtualId)) return;

        if (_schemaTopicoAtual.Count == 0)
        {
            PopupManager.Instance?.MostrarErro("Estrutura não definida",
                "Defina os campos deste tópico na Estrutura antes de adicionar uma entrada.");
            AbrirEstrutura();
            return;
        }

        var novaEntrada = new EntryData { TopicTag = _topicoAtualId };
        _entradasDoTopico.Add(novaEntrada);
        int indice = _entradasDoTopico.Count - 1;

        ReconstruirListaEntradas();
        AbrirEditorDeEntrada(indice);
    }

    private void AbrirEntradaExistente(int indice)
    {
        if (indice < 0 || indice >= _entradasDoTopico.Count) return;

        AbrirEditorDeEntrada(indice);
        _entryPanelAtivo?.LoadData(_entradasDoTopico[indice]);
    }

    private void AbrirEditorDeEntrada(int indice)
    {
        FecharEstrutura();
        _entradaSelecionadaIndex = indice;

        if (entryFieldsContainer != null)
            for (int i = entryFieldsContainer.childCount - 1; i >= 0; i--)
                Destroy(entryFieldsContainer.GetChild(i).gameObject);

        _entryPanelAtivo = null;
        if (prefabEntryPanel != null && entryFieldsContainer != null)
        {
            GameObject go = UIUtils.Spawn(prefabEntryPanel, entryFieldsContainer);
            _entryPanelAtivo = go.GetComponent<EntryPanelUI>();
            _entryPanelAtivo?.Setup(indice, _topicoAtualId, CamposCompletosDoTopico(), _ => RemoverEntradaAtual());
        }

        SetAtivo(painelEditorEntrada, true);
        ForcarRebuildLayout(entryFieldsContainer);
    }

    private void SalvarEntradaAtual()
    {
        if (_entryPanelAtivo == null || _entradaSelecionadaIndex < 0 || _entradaSelecionadaIndex >= _entradasDoTopico.Count)
            return;

        _entradasDoTopico[_entradaSelecionadaIndex] = _entryPanelAtivo.GetEntryData();
        SalvarEntradasDoTopicoNoDisco();
        ReconstruirListaEntradas(); // o "nome" exibido pode ter mudado
        VTTLog.Forja($"Entrada #{_entradaSelecionadaIndex + 1} de '{_topicoAtualId}' salva.");
    }

    private void RemoverEntradaAtual()
    {
        if (_entradaSelecionadaIndex < 0 || _entradaSelecionadaIndex >= _entradasDoTopico.Count) return;

        _entradasDoTopico.RemoveAt(_entradaSelecionadaIndex);
        SalvarEntradasDoTopicoNoDisco();

        FecharEditorDeEntrada();
        ReconstruirListaEntradas();
    }

    private void SalvarEntradasDoTopicoNoDisco()
    {
        if (string.IsNullOrWhiteSpace(_pastaTopicoAtual)) return;
        DynamicXmlBuilder.SaveEntriesAsXml(_entradasDoTopico, _pastaTopicoAtual, _topicoAtualId);
    }

    private void FecharEditorDeEntrada()
    {
        SetAtivo(painelEditorEntrada, false);
        _entradaSelecionadaIndex = -1;
        _entryPanelAtivo = null;
    }

    // ── Motor de Dados (nível de sistema — ver Mds/SistemasModulares/MotorDeDados) ──────────────
    //
    // Painel irmão do Nível 1 (não aninhado num tópico): propriedades do personagem, o tipo de
    // rolagem ativo do sistema (só UM nesta fase — linear_check OU dice_pool) e ações de teste.
    // Persistido em motor-de-dados.xml na raiz do sistema (CarregadorMotorDeDados), auto-save a
    // cada mutação, mesmo padrão de SalvarSchemaDoTopicoNoDisco.

    private void AbrirMotorDeDados()
    {
        if (string.IsNullOrWhiteSpace(_sistemaId) || string.IsNullOrWhiteSpace(_sistemaPath))
        {
            PopupManager.Instance?.MostrarErro("Nome do sistema obrigatório", "Defina o nome do sistema antes de configurar o Motor de Dados.");
            return;
        }

        ReconstruirListaPropriedades();
        AtualizarDropdownTipoRolagem();
        ReconstruirListaGraus();
        ReconstruirListaAcoes();
        FecharPainelTeste();
        SetAtivo(painelMotorDeDados, true);
    }

    private void FecharMotorDeDados()
    {
        FecharPainelTeste();
        SetAtivo(painelMotorDeDados, false);
    }

    private string CaminhoArquivoMotorDeDados() => Path.Combine(_sistemaPath, CarregadorMotorDeDados.NomeArquivo);

    private void CarregarMotorDeDadosDoSistema()
    {
        _motorDeDados = !string.IsNullOrWhiteSpace(_sistemaPath)
            ? CarregadorMotorDeDados.CarregarComSuporteJson(CaminhoArquivoMotorDeDados())
            : new DefinicaoMotorDeDados();

        _tipoRolagemAtivo = _motorDeDados.tiposDeRolagem.Values.FirstOrDefault();
        _estadoTeste = null; // recriado sob demanda (ver AbrirPainelTeste) a partir da definição recém-carregada
    }

    private void SalvarMotorDeDadosNoDisco()
    {
        if (string.IsNullOrWhiteSpace(_sistemaPath)) return;
        CarregadorMotorDeDados.Salvar(_motorDeDados, CaminhoArquivoMotorDeDados());
    }

    // ── Propriedades ─────────────────────────────────────────────────────────

    private void AdicionarPropriedade()
    {
        string label = inputNovaPropriedadeLabel != null ? inputNovaPropriedadeLabel.text?.Trim() : null;
        if (string.IsNullOrWhiteSpace(label))
        {
            PopupManager.Instance?.MostrarErro("Nome da propriedade obrigatório", "Digite um nome de exibição (ex: Força).");
            return;
        }

        string id = FormatarIdPropriedade(label);
        if (string.IsNullOrWhiteSpace(id))
        {
            PopupManager.Instance?.MostrarErro("Nome inválido", "Escolha outro nome pra propriedade.");
            return;
        }
        if (IdsReservadosPropriedade.Contains(id))
        {
            PopupManager.Instance?.MostrarErro("Nome reservado", $"'{id}' é usado internamente pelo motor de dados (resultado de rolagem) — escolha outro nome.");
            return;
        }
        if (_motorDeDados.propriedades.Any(p => p.id == id))
        {
            PopupManager.Instance?.MostrarErro("Propriedade já existe", $"Já existe uma propriedade com o ID '{id}'.");
            return;
        }

        bool ehFormula = dropdownNovaPropriedadeTipo != null && dropdownNovaPropriedadeTipo.value == 1;
        string valorTexto = inputNovaPropriedadeValor != null ? inputNovaPropriedadeValor.text?.Trim() : "";

        var propriedade = new DefinicaoPropriedade { id = id, label = label };
        if (ehFormula)
            propriedade.formula = valorTexto;
        else
            propriedade.valorBase = float.TryParse(valorTexto, NumberStyles.Float, CultureInfo.InvariantCulture, out float valor) ? valor : 0f;

        _motorDeDados.propriedades.Add(propriedade);
        SalvarMotorDeDadosNoDisco();

        if (inputNovaPropriedadeLabel != null) inputNovaPropriedadeLabel.text = string.Empty;
        if (inputNovaPropriedadeValor != null) inputNovaPropriedadeValor.text = string.Empty;
        ReconstruirListaPropriedades();
    }

    private void RemoverPropriedade(int indice)
    {
        if (indice < 0 || indice >= _motorDeDados.propriedades.Count) return;

        _motorDeDados.propriedades.RemoveAt(indice);
        SalvarMotorDeDadosNoDisco();
        ReconstruirListaPropriedades();
    }

    private void ReconstruirListaPropriedades()
    {
        if (propriedadesContainer == null || prefabPropriedadeItem == null) return;

        for (int i = propriedadesContainer.childCount - 1; i >= 0; i--)
            Destroy(propriedadesContainer.GetChild(i).gameObject);
        _itensPropriedade.Clear();

        for (int i = 0; i < _motorDeDados.propriedades.Count; i++)
        {
            int indice = i; // captura por valor
            GameObject go = UIUtils.Spawn(prefabPropriedadeItem, propriedadesContainer);
            PropriedadeListItemUI item = go.GetComponent<PropriedadeListItemUI>();
            if (item == null)
            {
                Debug.LogWarning("[CustomSystemManager] prefabPropriedadeItem não possui PropriedadeListItemUI.");
                continue;
            }

            item.Configurar(_motorDeDados.propriedades[i], () => RemoverPropriedade(indice));
            _itensPropriedade.Add(item);
        }

        ForcarRebuildLayout(propriedadesContainer);
    }

    // ── Tipo de Rolagem do Sistema ───────────────────────────────────────────

    private void ConfigurarDropdownTipoRolagem()
    {
        if (dropdownTipoRolagem == null) return;
        dropdownTipoRolagem.ClearOptions();
        dropdownTipoRolagem.AddOptions(new List<string> { "d20 Linear (linear_check)", "Pool de Dados (dice_pool)" });
    }

    private void ConfigurarDropdownTipoPropriedade()
    {
        if (dropdownNovaPropriedadeTipo == null) return;
        dropdownNovaPropriedadeTipo.ClearOptions();
        dropdownNovaPropriedadeTipo.AddOptions(new List<string> { "Número fixo", "Fórmula" });
    }

    private void AtualizarDropdownTipoRolagem()
    {
        int indice = _tipoRolagemAtivo != null && _tipoRolagemAtivo.id == "dice_pool" ? 1 : 0;

        if (dropdownTipoRolagem != null) dropdownTipoRolagem.SetValueWithoutNotify(indice);
        AtualizarVisibilidadeCamposDicePool();

        if (inputLimiarSucessoPool != null)
            inputLimiarSucessoPool.SetTextWithoutNotify(_tipoRolagemAtivo != null ? _tipoRolagemAtivo.limiarSucesso.ToString() : "8");
        if (inputParMaximoPool != null)
            inputParMaximoPool.SetTextWithoutNotify(_tipoRolagemAtivo != null ? _tipoRolagemAtivo.parMaximo.ToString() : "10");
    }

    // Trocar o dropdown não apaga a configuração do outro tipo — cada um fica guardado em
    // _motorDeDados.tiposDeRolagem por id, então voltar pro tipo anterior recupera os graus salvos.
    private void AoTipoRolagemAlterado(int indice)
    {
        string idNovo = indice == 1 ? "dice_pool" : "linear_check";

        if (!_motorDeDados.tiposDeRolagem.TryGetValue(idNovo, out DefinicaoTipoRolagem tipo))
        {
            tipo = new DefinicaoTipoRolagem
            {
                id = idNovo,
                dado = idNovo == "dice_pool" ? 10 : 20,
                limiarSucesso = idNovo == "dice_pool" ? 8 : 0,
                parMaximo = idNovo == "dice_pool" ? 10 : 0,
            };
            _motorDeDados.tiposDeRolagem[idNovo] = tipo;
        }

        _tipoRolagemAtivo = tipo;
        SalvarMotorDeDadosNoDisco();

        AtualizarVisibilidadeCamposDicePool();
        if (inputLimiarSucessoPool != null) inputLimiarSucessoPool.SetTextWithoutNotify(tipo.limiarSucesso.ToString());
        if (inputParMaximoPool != null) inputParMaximoPool.SetTextWithoutNotify(tipo.parMaximo.ToString());
        ReconstruirListaGraus();
        ReconstruirListaAcoes(); // parâmetros visíveis por ação dependem do tipo ativo
    }

    private void AtualizarVisibilidadeCamposDicePool()
    {
        bool ehDicePool = _tipoRolagemAtivo != null && _tipoRolagemAtivo.id == "dice_pool";
        if (inputLimiarSucessoPool != null) inputLimiarSucessoPool.gameObject.SetActive(ehDicePool);
        if (inputParMaximoPool != null) inputParMaximoPool.gameObject.SetActive(ehDicePool);
    }

    private void AoLimiarOuParMaximoEditado(string _)
    {
        if (_tipoRolagemAtivo == null) return;

        if (inputLimiarSucessoPool != null && int.TryParse(inputLimiarSucessoPool.text, out int limiar))
            _tipoRolagemAtivo.limiarSucesso = limiar;
        if (inputParMaximoPool != null && int.TryParse(inputParMaximoPool.text, out int parMaximo))
            _tipoRolagemAtivo.parMaximo = parMaximo;

        SalvarMotorDeDadosNoDisco();
    }

    // ── Graus de Resultado (do tipo de rolagem ativo) ───────────────────────────

    private void AdicionarGrau()
    {
        if (_tipoRolagemAtivo == null)
        {
            PopupManager.Instance?.MostrarErro("Tipo de rolagem não definido", "Escolha um tipo de rolagem antes de adicionar um grau de resultado.");
            return;
        }

        string id = FormatarId(inputNovoGrauId != null ? inputNovoGrauId.text : null);
        string condicao = inputNovoGrauCondicao != null ? inputNovoGrauCondicao.text?.Trim() : null;

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(condicao))
        {
            PopupManager.Instance?.MostrarErro("Grau inválido", "Preencha o nome do grau (ex: critico) e a condição (ex: {d1} == 20).");
            return;
        }
        if (_tipoRolagemAtivo.graus.Any(g => g.id == id))
        {
            PopupManager.Instance?.MostrarErro("Grau já existe", $"Já existe um grau com o ID '{id}'.");
            return;
        }

        _tipoRolagemAtivo.graus.Add(new DefinicaoGrauResultado { id = id, condicao = condicao });
        SalvarMotorDeDadosNoDisco();

        if (inputNovoGrauId != null) inputNovoGrauId.text = string.Empty;
        if (inputNovoGrauCondicao != null) inputNovoGrauCondicao.text = string.Empty;
        ReconstruirListaGraus();
    }

    private void RemoverGrau(int indice)
    {
        if (_tipoRolagemAtivo == null || indice < 0 || indice >= _tipoRolagemAtivo.graus.Count) return;

        _tipoRolagemAtivo.graus.RemoveAt(indice);
        SalvarMotorDeDadosNoDisco();
        ReconstruirListaGraus();
    }

    private void ReconstruirListaGraus()
    {
        if (grausContainer == null || prefabGrauItem == null) return;

        for (int i = grausContainer.childCount - 1; i >= 0; i--)
            Destroy(grausContainer.GetChild(i).gameObject);
        _itensGrau.Clear();

        if (_tipoRolagemAtivo == null) { ForcarRebuildLayout(grausContainer); return; }

        for (int i = 0; i < _tipoRolagemAtivo.graus.Count; i++)
        {
            int indice = i;
            GameObject go = UIUtils.Spawn(prefabGrauItem, grausContainer);
            GrauResultadoListItemUI item = go.GetComponent<GrauResultadoListItemUI>();
            if (item == null)
            {
                Debug.LogWarning("[CustomSystemManager] prefabGrauItem não possui GrauResultadoListItemUI.");
                continue;
            }

            item.Configurar(_tipoRolagemAtivo.graus[i], () => RemoverGrau(indice));
            _itensGrau.Add(item);
        }

        ForcarRebuildLayout(grausContainer);
    }

    // ── Ações de Teste ───────────────────────────────────────────────────────

    private void AdicionarAcao()
    {
        if (_tipoRolagemAtivo == null)
        {
            PopupManager.Instance?.MostrarErro("Tipo de rolagem não definido", "Escolha um tipo de rolagem antes de adicionar uma ação.");
            return;
        }

        string nome = inputNovaAcaoNome != null ? inputNovaAcaoNome.text?.Trim() : null;
        if (string.IsNullOrWhiteSpace(nome))
        {
            PopupManager.Instance?.MostrarErro("Nome da ação obrigatório", "Digite um nome (ex: Ataque com Espada).");
            return;
        }

        string id = FormatarId(nome);
        if (string.IsNullOrWhiteSpace(id) || _motorDeDados.acoes.Any(a => a.id == id))
        {
            PopupManager.Instance?.MostrarErro("Ação inválida", string.IsNullOrWhiteSpace(id)
                ? "Escolha outro nome pra ação."
                : $"Já existe uma ação com o ID '{id}'.");
            return;
        }

        var acao = new DefinicaoAcaoRolagem { id = id, nome = nome, idTipoRolagem = _tipoRolagemAtivo.id };
        _motorDeDados.acoes.Add(acao);
        SalvarMotorDeDadosNoDisco();

        if (inputNovaAcaoNome != null) inputNovaAcaoNome.text = string.Empty;
        ReconstruirListaAcoes();
    }

    private void RemoverAcao(int indice)
    {
        if (indice < 0 || indice >= _motorDeDados.acoes.Count) return;

        _motorDeDados.acoes.RemoveAt(indice);
        SalvarMotorDeDadosNoDisco();
        ReconstruirListaAcoes();
    }

    // Cada AcaoRolagemListItemUI escreve DIRETO no DefinicaoAcaoRolagem que recebeu em Configurar
    // (mesma referência guardada em _motorDeDados.acoes) — este callback só precisa persistir.
    private void AoAcaoEditada() => SalvarMotorDeDadosNoDisco();

    private void ReconstruirListaAcoes()
    {
        if (acoesContainer == null || prefabAcaoItem == null) return;

        for (int i = acoesContainer.childCount - 1; i >= 0; i--)
            Destroy(acoesContainer.GetChild(i).gameObject);
        _itensAcao.Clear();

        for (int i = 0; i < _motorDeDados.acoes.Count; i++)
        {
            DefinicaoAcaoRolagem acao = _motorDeDados.acoes[i];
            GameObject go = UIUtils.Spawn(prefabAcaoItem, acoesContainer);
            AcaoRolagemListItemUI item = go.GetComponent<AcaoRolagemListItemUI>();
            if (item == null)
            {
                Debug.LogWarning("[CustomSystemManager] prefabAcaoItem não possui AcaoRolagemListItemUI.");
                continue;
            }

            int indice = i; // captura por valor
            item.Configurar(acao, _tipoRolagemAtivo?.id, () => RemoverAcao(indice), () => AbrirPainelTeste(acao), AoAcaoEditada);
            _itensAcao.Add(item);
        }

        ForcarRebuildLayout(acoesContainer);
    }

    // ── Painel de Teste (rola de verdade contra um EstadoPersonagem de rascunho) ─────────────────

    private void AbrirPainelTeste(DefinicaoAcaoRolagem acao)
    {
        if (acao == null || _tipoRolagemAtivo == null) return;

        // Reaproveitado entre aberturas na mesma sessão de Forja — trocas manuais que o mestre fez
        // pra testar (ex: subir "for" pra ver se bate a CD) não se perdem só por fechar e reabrir.
        if (_estadoTeste == null)
            _estadoTeste = CarregadorMotorDeDados.CriarEstadoInicial(_motorDeDados);

        _acaoEmTeste = acao;
        if (textoAcaoEmTeste != null) textoAcaoEmTeste.text = acao.nome;
        if (textoResultadoTeste != null) textoResultadoTeste.text = string.Empty;

        ReconstruirPropriedadesDeTeste();
        SetAtivo(painelTesteRolagem, true);
    }

    private void FecharPainelTeste()
    {
        SetAtivo(painelTesteRolagem, false);
        _acaoEmTeste = null;
    }

    private void ReconstruirPropriedadesDeTeste()
    {
        if (propriedadesTesteContainer == null || prefabPropriedadeTesteItem == null || _estadoTeste == null) return;

        for (int i = propriedadesTesteContainer.childCount - 1; i >= 0; i--)
            Destroy(propriedadesTesteContainer.GetChild(i).gameObject);
        _itensPropriedadeTeste.Clear();

        foreach (PropriedadePersonagem propriedade in _estadoTeste.PropriedadesBase.Values)
        {
            GameObject go = UIUtils.Spawn(prefabPropriedadeTesteItem, propriedadesTesteContainer);
            PropriedadeTesteItemUI item = go.GetComponent<PropriedadeTesteItemUI>();
            if (item == null)
            {
                Debug.LogWarning("[CustomSystemManager] prefabPropriedadeTesteItem não possui PropriedadeTesteItemUI.");
                continue;
            }

            string label = _motorDeDados.propriedades.Find(p => p.id == propriedade.id)?.label ?? propriedade.id;
            item.Configurar(label, propriedade, _estadoTeste, AoValorDeTesteEditado);
            _itensPropriedadeTeste.Add(item);
        }

        ForcarRebuildLayout(propriedadesTesteContainer);
    }

    // EstadoPersonagem.DefinirValorBase já disparou o recálculo em cascata — só precisa refletir na
    // UI (propriedades derivadas por fórmula podem ter mudado de valor).
    private void AoValorDeTesteEditado() => ReconstruirPropriedadesDeTeste();

    private void ExecutarTesteDeRolagem()
    {
        if (_acaoEmTeste == null || _tipoRolagemAtivo == null || _estadoTeste == null) return;

        ResultadoRolagemMotor resultado = MotorDeDados.Rolar(_acaoEmTeste, _tipoRolagemAtivo, _estadoTeste);
        if (resultado == null)
        {
            if (textoResultadoTeste != null) textoResultadoTeste.text = "Falha ao rolar — confira o tipo de rolagem.";
            return;
        }

        if (textoResultadoTeste != null) textoResultadoTeste.text = FormatarResultadoTeste(resultado);
        VTTLog.Dice($"[Forja] Teste '{_acaoEmTeste.nome}' — dados=[{string.Join(",", resultado.dados)}] total={resultado.total} grau={resultado.grau}");
    }

    private static string FormatarResultadoTeste(ResultadoRolagemMotor resultado)
    {
        string dados = string.Join(", ", resultado.dados);
        return $"Dados: [{dados}]\nTotal: {resultado.total:0.##}\nGrau: {resultado.grau}";
    }

    // IDs de propriedade viram token {id} avaliado por AvaliadorDeExpressao (regex
    // [a-zA-Z0-9_]+) — precisa de underscore, NÃO hífen, diferente de FormatarId (usado pra
    // tópicos/ações/graus, que nunca viram token de fórmula).
    private static string FormatarIdPropriedade(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        texto = texto.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
        texto = Regex.Replace(texto, @"[^a-z0-9_]", string.Empty);
        texto = Regex.Replace(texto, @"_{2,}", "_");
        return texto.Trim('_');
    }

    // ── Utilitários ───────────────────────────────────────────────────────────

    private static string FormatarId(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        texto = texto.Trim().ToLowerInvariant().Replace(" ", "-");
        texto = Regex.Replace(texto, @"[^a-z0-9\-]", string.Empty);
        texto = Regex.Replace(texto, @"-{2,}", "-");
        return texto.Trim('-');
    }

    private static void SetAtivo(GameObject go, bool ativo)
    {
        if (go != null) go.SetActive(ativo);
    }

    // Filhos instanciados em runtime dentro de um Layout Group não recalculam tamanho/posição
    // sozinhos — sem isso, o Content View Group fica com os itens sobrepostos/espremidos até algo
    // (ex: redimensionar a janela) forçar um rebuild. Mesmo padrão já usado em
    // BarraFerramentasManager.PopularGenerico.
    private static void ForcarRebuildLayout(Transform t)
    {
        if (t is not RectTransform rt) return;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    private static void Religar(Button btn, Action acao)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => acao());
    }
}
