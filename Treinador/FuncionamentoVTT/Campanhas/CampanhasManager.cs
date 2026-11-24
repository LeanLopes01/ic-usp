using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SimpleFileBrowser;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Hub de uma campanha aberta.
///
/// A "aba de botões gerais" (botoesTopicos) é uma lista de elementos configuráveis no Inspector —
/// cada elemento liga um botão a um painel próprio (GameObject) com seu próprio
/// CampanhaTopicEditorUI (hoje só "npcs" → Criar NPC). Adicionar um recurso novo no futuro
/// (locais, itens, etc.) não exige código novo aqui: só um item a mais na lista com seu painel
/// dedicado (e uma entrada nos switches de catálogo/pasta abaixo, se o tópico precisar de campos
/// próprios).
///
/// Clicar no botão de um tópico funciona como um toggle: se o painel dele já está aberto, fecha;
/// caso contrário, fecha qualquer outro painel de tópico aberto e chama Setup() no painel clicado
/// (sempre uma sessão nova, em branco — ver CampanhaTopicEditorUI). Um único botão "Salvar"
/// persiste o tópico atualmente aberto (inclusive imagens).
/// </summary>
public class CampanhasManager : MonoBehaviour
{
    [Serializable]
    private class TopicoBotao
    {
        public string topicId;
        public Button botao;
        public GameObject painel;
        public CampanhaTopicEditorUI editor;
    }

    [Header("Criação")]
    [SerializeField] private CampanhasCreationUI campanhasCreationUI;

    [Header("Painel da Campanha")]
    [SerializeField] private GameObject painelCampanha;
    [SerializeField] private TMP_Text   textoNomeCampanha;

    [Header("Navegação")]
    [Tooltip("Necessário pra abrir a campanha corretamente mesmo vindo de uma tela diferente (ex: " +
        "tela inicial, via CampanhaLoaderUI.campanhaLoaderTelaInicial em CampanhasCreationUI) — " +
        "SetActive direto no painel não basta: ToggleObjects desfaz isso sozinho no próximo frame se " +
        "a tela dele não for a 'atual' (ver comentário em ToggleObjects.NavegarParaTela).")]
    [SerializeField] private ToggleObjects toggleObjects;
    [Tooltip("Tela de nível superior (registrada em ToggleObjects.objectsToToggle) que hospeda " +
        "painelCampanha — pode ser o próprio painelCampanha, se ele já for uma tela própria, ou a " +
        "tela maior (ex: a mesma de CampanhasCreationUI) que o contém como painel filho.")]
    [SerializeField] private GameObject telaCampanhaAberta;

    [Header("Background")]
    [Tooltip("Mesmo GameObject configurado como backgroundGeral em ToggleObjects — desativado ao entrar na edição de campanha (esse campo é privado em ToggleObjects, por isso a referência própria aqui).")]
    [SerializeField] private GameObject backgroundGeral;

    [Header("Aba de Botões Gerais")]
    [SerializeField] private List<TopicoBotao> botoesTopicos = new List<TopicoBotao>();

    [Header("Mapa")]
    [Tooltip("Painel de battle map — separado de botoesTopicos porque não segue o ciclo Setup/Salvar dos outros tópicos (tem sua própria persistência, disparada pelas ações de importar/vetorizar).")]
    [SerializeField] private Button botaoMapa;
    [SerializeField] private GameObject mapaPainel;
    [SerializeField] private CampanhaMapManager mapManager;
    [SerializeField] private CampanhaMapGaleriaUI mapGaleria;
    [SerializeField] private CampanhaMapImportUI mapImportUI;

    [Header("Fog Manual (ver Salas/Grid/FogRect.cs, FogEditorTools.cs)")]
    [Tooltip("Abre/fecha o content com os botões Desenhar/Apagar de fog manual — o grid/mapa vive em " +
        "world-space e renderiza independente de qual painel de UI está visível no momento (mesmo " +
        "raciocínio do comentário em AbrirCampanhaCoroutine), então este toggle não precisa do painel " +
        "de Mapa aberto pra fazer sentido.")]
    [SerializeField] private Button botaoFerramentasFog;
    [SerializeField] private GameObject painelFerramentasFog;
    [Tooltip("Arrastar no grid desenha um retângulo de fog manual. Referência DIRETA (não singleton — " +
        "diferente de ReguaMedicaoManager, um FogEditorTools também existe em Salas pro MapManager de " +
        "lá; um singleton estático destruiria um dos dois se ambos existissem na mesma cena, ver " +
        "mesmo raciocínio de WallEditorTools não usar singleton). Arraste aqui o FogEditorTools desta " +
        "cena, configurado com mapManagerBehaviour apontando pro CampanhaMapManager.")]
    [SerializeField] private FogEditorTools fogEditorTools;
    [SerializeField] private Button botaoFogDesenhar;
    [Tooltip("Clicar num retângulo de fog manual já desenhado apaga ele inteiro.")]
    [SerializeField] private Button botaoFogApagar;

    [Header("Fontes de Luz (ver Salas/Grid/LightSource.cs, LightEditorTools.cs)")]
    [Tooltip("Abre/fecha o content com os botões Adicionar/Remover de fontes de luz — mesmo " +
        "raciocínio de botaoFerramentasFog (grid em world-space, não precisa do painel de Mapa aberto).")]
    [SerializeField] private Button botaoFerramentasLuz;
    [SerializeField] private GameObject painelFerramentasLuz;
    [Tooltip("Planta uma fonte de luz no ponto clicado, com o raio do slider dentro do próprio " +
        "LightEditorTools (não fica aqui — ver LightEditorTools.sliderRaioLuz). Referência DIRETA " +
        "(não singleton — mesmo raciocínio de fogEditorTools: um LightEditorTools também existe em " +
        "Salas pro MapManager de lá). Arraste aqui o LightEditorTools desta cena, configurado com " +
        "mapManagerBehaviour apontando pro CampanhaMapManager.")]
    [SerializeField] private LightEditorTools lightEditorTools;
    [SerializeField] private Button botaoLuzAdicionar;
    [Tooltip("Clicar perto de uma fonte de luz já plantada remove ela inteira.")]
    [SerializeField] private Button botaoLuzApagar;
    [Tooltip("Liga/desliga a VISIBILIDADE dos marcadores das luzes já plantadas (ver " +
        "LightEditorTools.AlternarVisibilidadeMarcadores) — independente de Adicionar/Apagar estarem " +
        "ativos, só pra conferir onde as tochas estão.")]
    [SerializeField] private Button botaoLuzAlternarVisibilidade;

    [Header("Lista de Mapas")]
    [Tooltip("Painel separado só com a galeria de mapas salvos (distinto do painel de Mapa/grid acima) — ocupa o mesmo espaço visual do painel de NPCs, por isso entra na mesma exclusão mútua.")]
    [SerializeField] private Button botaoListaMapas;
    [SerializeField] private GameObject listaMapasPainel;
    [SerializeField] private CampanhaMapGaleriaUI listaMapasGaleria;

    [Header("Lista de NPCs")]
    [Tooltip("Painel separado só com a galeria de NPCs salvos (distinto do painel de criação/edição do tópico \"npcs\") — mesmo espaço visual, mesma exclusão mútua. Abre sozinho quando o usuário termina de salvar um NPC.")]
    [SerializeField] private Button botaoListaNpcs;
    [SerializeField] private GameObject listaNpcsPainel;
    [SerializeField] private CampanhaNpcGaleriaUI listaNpcsGaleria;
    [Tooltip("Botão dentro do painel de Lista de NPCs que abre o formulário de criação em branco — " +
        "mesmo destino do botão \"npcs\" na Aba de Botões Gerais (ObterCatalogoDoTopico/ObterPastaDoTopico), " +
        "só que acessível direto de dentro da lista, sem precisar fechá-la primeiro.")]
    [SerializeField] private Button botaoAdicionarNpc;

    [Header("Importar NPCs")]
    [Tooltip("Abre o explorador de arquivos (mesmo plugin de TelaSalaUIManager) em modo PASTA — cada " +
        "arquivo .xml/.json reconhecido dentro dela vira um NPC novo (ver CampanhaNpcImporter).")]
    [SerializeField] private Button botaoImportarNpcs;

    [Header("Lore de NPC")]
    [Tooltip("Painel de lore de um NPC, separado do stat block — só é aberto a partir do botão \"Lore\" de um card na Lista de NPCs (não tem botão próprio na aba geral), por isso precisa de um botão \"Voltar\" explícito.")]
    [SerializeField] private GameObject npcLorePainel;
    [SerializeField] private CampanhaNpcLoreUI npcLoreEditor;
    [SerializeField] private Button botaoFecharLore;

    [Header("Ações")]
    [SerializeField] private Button botaoSalvar;

    // ── Estado ────────────────────────────────────────────────────────────────

    private string     _campanhaId;
    private string     _campanhaPath;
    private string     _nomeCampanha;
    private string     _sistemaCampanha;
    private TopicoBotao _topicoAberto;
    private bool        _mapaAberto;
    private bool        _listaMapasAberta;
    private bool        _listaNpcsAberta;
    private bool        _loreNpcAberta;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (toggleObjects == null)
            toggleObjects = FindFirstObjectByType<ToggleObjects>();

        Religar(botaoSalvar, SalvarTopicoAtual);
        Religar(botaoMapa, AlternarMapa);
        Religar(botaoFerramentasFog, AlternarPainelFerramentasFog);
        Religar(botaoFogDesenhar, () => fogEditorTools?.SelecionarFerramenta(FogToolType.Desenhar));
        Religar(botaoFogApagar,   () => fogEditorTools?.SelecionarFerramenta(FogToolType.Apagar));
        Religar(botaoFerramentasLuz, AlternarPainelFerramentasLuz);
        Religar(botaoLuzAdicionar, () => lightEditorTools?.SelecionarFerramenta(LightToolType.Adicionar));
        Religar(botaoLuzApagar,    () => lightEditorTools?.SelecionarFerramenta(LightToolType.Remover));
        Religar(botaoLuzAlternarVisibilidade, () => lightEditorTools?.AlternarVisibilidadeMarcadores());
        Religar(botaoListaMapas, AlternarListaMapas);
        Religar(botaoListaNpcs, AlternarListaNpcs);
        Religar(botaoAdicionarNpc, AbrirCriacaoDeNpc);
        Religar(botaoImportarNpcs, OnClickImportarNpcs);
        Religar(botaoFecharLore, AbrirListaNpcsForcado);

        if (npcLoreEditor != null)
            npcLoreEditor.OnSaved += AbrirListaNpcsForcado;

        foreach (TopicoBotao tb in botoesTopicos)
        {
            if (tb == null || tb.botao == null) continue;
            TopicoBotao capturado = tb;
            Religar(tb.botao, () => AlternarTopico(capturado));

            // Só "npcs" tem um painel de listagem próprio pra voltar após salvar (ver
            // CampanhaNpcGaleriaUI/listaNpcsPainel) — tópicos futuros que não tiverem isso
            // simplesmente não assinam OnSaved aqui.
            if (tb.topicId == "npcs" && tb.editor != null)
                tb.editor.OnSaved += AoNpcSalvo;
        }

        if (campanhasCreationUI != null)
            campanhasCreationUI.OnCampanhaAberta += AbrirCampanha;

        if (mapImportUI != null)
            mapImportUI.OnMapaCriado += AtualizarGaleriaDeMapas;

        if (toggleObjects != null)
            toggleObjects.OnTelaAlterada += OnTelaAlterada;
    }

    /// <summary>Desativa o grid da Campanha (câmera própria em world-space) ao navegar pra fora da
    /// tela da Campanha — sem isso ele fica ativo pra sempre depois da primeira abertura, podendo
    /// renderizar simultaneamente com o grid de uma Sala aberta em paralelo.</summary>
    private void OnTelaAlterada(GameObject novaTela)
    {
        if (telaCampanhaAberta == null || novaTela == telaCampanhaAberta) return;
        SetAtivo(mapManager != null ? mapManager.gameObject : null, false);
    }

    private void OnDestroy()
    {
        if (campanhasCreationUI != null)
            campanhasCreationUI.OnCampanhaAberta -= AbrirCampanha;

        if (mapImportUI != null)
            mapImportUI.OnMapaCriado -= AtualizarGaleriaDeMapas;

        if (toggleObjects != null)
            toggleObjects.OnTelaAlterada -= OnTelaAlterada;

        if (npcLoreEditor != null)
            npcLoreEditor.OnSaved -= AbrirListaNpcsForcado;

        foreach (TopicoBotao tb in botoesTopicos)
            if (tb != null && tb.topicId == "npcs" && tb.editor != null)
                tb.editor.OnSaved -= AoNpcSalvo;
    }

    private void OnEnable()
    {
        SetAtivo(painelCampanha, false);
    }

    // ── Abertura de campanha ──────────────────────────────────────────────────

    public void AbrirCampanha(string campanhaId, string nome, string sistema)
    {
        StartCoroutine(AbrirCampanhaCoroutine(campanhaId, nome, sistema));
    }

    private IEnumerator AbrirCampanhaCoroutine(string campanhaId, string nome, string sistema)
    {
        PopupManager.Instance?.MostrarCarregamento("Abrindo campanha", nome);
        yield return null;

        _campanhaId      = campanhaId;
        _campanhaPath    = CampanhasLocalStorage.CampaignFolderPath(campanhaId);
        _nomeCampanha    = nome;
        _sistemaCampanha = sistema;
        _topicoAberto    = null;
        _mapaAberto      = false;
        _listaMapasAberta = false;
        _listaNpcsAberta  = false;
        _loreNpcAberta    = false;

        if (textoNomeCampanha != null)
            textoNomeCampanha.text = nome;

        // NavegarParaTela ANTES do SetAtivo direto — pode estar vindo de uma tela diferente (ex:
        // tela inicial, via CampanhaLoaderUI.campanhaLoaderTelaInicial) onde a tela de
        // painelCampanha nunca foi mostrada; um SetActive sozinho seria desfeito no próximo frame
        // pelo ToggleObjects se ele não souber que essa é a tela "atual" agora.
        if (telaCampanhaAberta != null)
            toggleObjects?.NavegarParaTela(telaCampanhaAberta);

        SetAtivo(painelCampanha, true);
        SetAtivo(backgroundGeral, false);
        campanhasCreationUI?.FecharPainelCriacao();

        foreach (TopicoBotao tb in botoesTopicos)
            if (tb != null) SetAtivo(tb.painel, false);

        SetAtivo(mapaPainel, false);
        SetAtivo(painelFerramentasFog, false);
        fogEditorTools?.Desativar();
        SetAtivo(painelFerramentasLuz, false);
        lightEditorTools?.Desativar();
        SetAtivo(listaMapasPainel, false);
        SetAtivo(listaNpcsPainel, false);
        SetAtivo(npcLorePainel, false);

        // O grid/mapa vive em world-space (fora do Canvas) — precisa do próprio GameObject ativo
        // pra renderizar (câmera, LineRenderers etc.), independente do painel de UI mapaPainel
        // estar visível ou não no momento.
        SetAtivo(mapManager != null ? mapManager.gameObject : null, true);
        mapManager?.DefinirCampanhaId(campanhaId);
        mapManager?.DefinirSistemaCampanha(sistema);
        // Recarrega automaticamente a última cena que estava aberta nessa campanha (se houver) — sem
        // isso, reabrir a campanha deixava o grid vazio até o usuário clicar em "Carregar" de novo.
        mapManager?.CarregarUltimoMapaAtivo();

        PopupManager.Instance?.Fechar();
    }

    // ── Tópicos ───────────────────────────────────────────────────────────────

    // Clicar no botão de um tópico alterna: se o painel dele já está aberto, fecha; senão, fecha
    // o painel do tópico anterior (ou do mapa, se houver) e abre este com uma sessão nova (em branco).
    private void AlternarTopico(TopicoBotao tb)
    {
        if (tb == null || tb.painel == null) return;

        if (_topicoAberto == tb)
        {
            SetAtivo(tb.painel, false);
            _topicoAberto = null;
            return;
        }

        FecharMapa();
        FecharListaMapas();
        FecharListaNpcs();
        FecharLoreDoNpc();
        if (_topicoAberto != null)
            SetAtivo(_topicoAberto.painel, false);

        _topicoAberto = tb;
        SetAtivo(tb.painel, true);
        tb.editor?.Setup(tb.topicId, ObterPastaDoTopico(tb.topicId), ObterCatalogoDoTopico(tb.topicId));
    }

    // ── Mapa ──────────────────────────────────────────────────────────────────

    private void AlternarMapa()
    {
        if (mapaPainel == null) return;

        if (_mapaAberto)
        {
            FecharMapa();
            return;
        }

        // Só o botão "Mapa" revela o formulário de importação em branco (pra criar uma cena nova) —
        // AbrirPainelMapa() sozinho (usado também por "Carregar"/"Editar" na Lista de Mapas) deixa o
        // formulário fechado por padrão (ver CampanhaMapImportUI.Configurar). AbrirParaNovoMapa()
        // precisa rodar DEPOIS de Configurar() (ver aposAbrir) — Configurar() reseta o formulário
        // pra um estado limpo, então rodar antes apagaria o que AbrirParaNovoMapa acabou de preparar.
        AbrirPainelMapa(aposAbrir: () => mapImportUI?.AbrirParaNovoMapa());
    }

    // Força a abertura do painel de grid/import (fecha tópico e lista de mapas se estiverem
    // abertos) — usado tanto pelo botão "Mapa" quanto ao selecionar um item na Lista de Mapas.
    // Mostra popup de progresso (a galeria le todos os mapas salvos do disco) — por isso é
    // corrotina: qualquer efeito que precise rodar DEPOIS do painel estar pronto (ex: abrir o
    // formulário de edição de um mapa específico) precisa vir via aposAbrir, não como uma chamada
    // solta logo em seguida — senão rodaria ANTES de Configurar()/AtualizarGaleriaDeMapas(), que é a
    // ordem errada (ver comentário em AlternarMapa).
    private void AbrirPainelMapa(Action aposAbrir = null)
    {
        if (mapaPainel == null) return;
        StartCoroutine(AbrirPainelMapaCoroutine(aposAbrir));
    }

    private IEnumerator AbrirPainelMapaCoroutine(Action aposAbrir)
    {
        PopupManager.Instance?.MostrarCarregamento("Carregando mapas", "Lendo mapas salvos da campanha...");
        yield return null;

        FecharListaMapas();
        FecharListaNpcs();
        FecharLoreDoNpc();
        if (_topicoAberto != null)
        {
            SetAtivo(_topicoAberto.painel, false);
            _topicoAberto = null;
        }

        _mapaAberto = true;
        SetAtivo(mapaPainel, true);
        mapImportUI?.Configurar(mapManager, _campanhaId);
        AtualizarGaleriaDeMapas();

        aposAbrir?.Invoke();

        PopupManager.Instance?.Fechar();
    }

    private void FecharMapa()
    {
        if (!_mapaAberto) return;
        _mapaAberto = false;
        SetAtivo(mapaPainel, false);
    }

    // ── Fog Manual (ver Salas/Grid/FogRect.cs, FogEditorTools.cs) ────────────

    /// <summary>Abre/fecha o content com os botões Desenhar/Apagar de fog manual — toggle
    /// independente (não mexe em _mapaAberto/tópicos/listas), já que o grid renderiza em world-space
    /// o tempo todo, mesmo com outro painel de UI em foco. Fechar sempre desativa a ferramenta ativa
    /// (Desenhar/Apagar) — sem isso o modo ficaria ligado sem nenhum botão visível pra desligar,
    /// mesmo raciocínio de BarraFerramentasManager.ToggleContentFerramentaMapa pra régua/fog.</summary>
    private void AlternarPainelFerramentasFog()
    {
        if (painelFerramentasFog == null) return;

        bool vaiFechar = painelFerramentasFog.activeSelf;
        painelFerramentasFog.SetActive(!vaiFechar);
        if (vaiFechar) fogEditorTools?.Desativar();
    }

    /// <summary>Abre/fecha o content com os botões Adicionar/Remover de fontes de luz — mesmo
    /// raciocínio de AlternarPainelFerramentasFog.</summary>
    private void AlternarPainelFerramentasLuz()
    {
        if (painelFerramentasLuz == null) return;

        bool vaiFechar = painelFerramentasLuz.activeSelf;
        painelFerramentasLuz.SetActive(!vaiFechar);
        if (vaiFechar) lightEditorTools?.Desativar();
    }

    private void AtualizarGaleriaDeMapas()
    {
        mapGaleria?.Carregar(_campanhaId, mapManager, aoEditar: (mapa) => mapImportUI?.EditarMapa(mapa));
    }

    // ── Lista de Mapas (painel separado) ─────────────────────────────────────

    private void AlternarListaMapas()
    {
        if (listaMapasPainel == null) return;

        if (_listaMapasAberta)
        {
            FecharListaMapas();
            return;
        }

        StartCoroutine(AlternarListaMapasCoroutine());
    }

    private IEnumerator AlternarListaMapasCoroutine()
    {
        PopupManager.Instance?.MostrarCarregamento("Carregando mapas", "Lendo mapas salvos da campanha...");
        yield return null;

        FecharMapa();
        FecharListaNpcs();
        FecharLoreDoNpc();
        if (_topicoAberto != null)
        {
            SetAtivo(_topicoAberto.painel, false);
            _topicoAberto = null;
        }

        _listaMapasAberta = true;
        SetAtivo(listaMapasPainel, true);
        // "Carregar" aqui troca a cena no mapManager (com seu próprio popup — ver
        // CampanhaMapGaleriaUI.onCarregar/CampanhaMapManager.TrocarMapaComProgresso) e troca pro
        // painel do grid; "Editar" troca pro painel do grid mas abre o formulário pré-preenchido em
        // vez de trocar a cena ativa — só troca de fato quando o usuário confirmar a edição.
        listaMapasGaleria?.Carregar(_campanhaId, mapManager,
            aoSelecionar: (mapa) => AbrirPainelMapa(),
            aoEditar: (mapa) => AbrirPainelMapa(aposAbrir: () => mapImportUI?.EditarMapa(mapa)));

        PopupManager.Instance?.Fechar();
    }

    private void FecharListaMapas()
    {
        if (!_listaMapasAberta) return;
        _listaMapasAberta = false;
        SetAtivo(listaMapasPainel, false);
    }

    // ── Lista de NPCs (painel separado) ──────────────────────────────────────

    private void AlternarListaNpcs()
    {
        if (listaNpcsPainel == null) return;

        if (_listaNpcsAberta)
        {
            FecharListaNpcs();
            return;
        }

        AbrirListaNpcsForcado();
    }

    // Força a abertura da listagem de NPCs (fecha mapa/lista de mapas/tópico se estiverem
    // abertos) — usado tanto pelo botão "Lista de NPCs" quanto automaticamente ao terminar de
    // salvar um NPC (ver AoNpcSalvo, assinado em OnSaved). Mostra popup de progresso — a listagem
    // agora também lê os NPCs do SISTEMA ativo (ver CampanhaSistemaNpcLoader), que pode significar
    // ler dezenas/centenas de arquivos XML a cada abertura, não só os poucos NPCs da campanha.
    private void AbrirListaNpcsForcado()
    {
        if (listaNpcsPainel == null) return;
        StartCoroutine(AbrirListaNpcsForcadoCoroutine());
    }

    private IEnumerator AbrirListaNpcsForcadoCoroutine()
    {
        PopupManager.Instance?.MostrarCarregamento("Carregando NPCs", "Lendo NPCs da campanha e do sistema...");
        yield return null;

        FecharMapa();
        FecharListaMapas();
        FecharLoreDoNpc();
        if (_topicoAberto != null)
        {
            SetAtivo(_topicoAberto.painel, false);
            _topicoAberto = null;
        }

        _listaNpcsAberta = true;
        SetAtivo(listaNpcsPainel, true);
        AtualizarListaNpcs();

        PopupManager.Instance?.Fechar();
    }

    private void FecharListaNpcs()
    {
        if (!_listaNpcsAberta) return;
        _listaNpcsAberta = false;
        SetAtivo(listaNpcsPainel, false);
    }

    private void AtualizarListaNpcs()
    {
        listaNpcsGaleria?.Carregar(
            CampanhasLocalStorage.NpcsFolderPath(_campanhaId),
            _sistemaCampanha,
            AbrirNpcParaEdicao,
            mapManager != null ? (Action<EntryData, Vector2>)mapManager.TentarSoltarTokenNoMapa : null,
            AbrirLoreDoNpc);
    }

    // Botão "Adicionar" dentro do painel de Lista de NPCs — atalho pro mesmo destino do botão
    // "npcs" da Aba de Botões Gerais (AlternarTopico já cuida de fechar a lista e abrir o
    // formulário em branco, mesmo comportamento de clicar o botão geral com a lista aberta).
    private void AbrirCriacaoDeNpc()
    {
        TopicoBotao npcsTopico = botoesTopicos.Find(tb => tb != null && tb.topicId == "npcs");
        if (npcsTopico == null) return;
        AlternarTopico(npcsTopico);
    }

    // ── Importar NPCs de uma pasta ───────────────────────────────────────────
    // Mesmo plugin/padrão de Salas/TelaSalaUIManager.OnClickSelecionarImagemSala, só que pra ESCOLHER
    // A PASTA em vez de um arquivo único. FileBrowser.PickMode.Folders (usado antes) filtra TODO
    // arquivo da listagem por design do plugin (ver FileBrowser.FileSystemEntryMatchesFilters — "if
    // (m_pickerMode == PickMode.Folders) return false" pra qualquer item que não seja pasta), então
    // a tela ficava "travada" sem mostrar os .xml/.json de dentro — usa FilesAndFolders em vez disso:
    // continua deixando escolher a pasta, mas os arquivos aparecem na listagem pra conferência visual.

    private void OnClickImportarNpcs()
    {
        if (string.IsNullOrWhiteSpace(_campanhaId))
        {
            VTTLog.ForjaError("[CampanhasManager] Importar NPCs: nenhuma campanha aberta.");
            return;
        }

        GarantirAtalhoDownloads();

        FileBrowser.ShowLoadDialog(
            (caminhos) =>
            {
                if (caminhos == null || caminhos.Length == 0) return;
                StartCoroutine(ImportarNpcsDeCaminhoCoroutine(caminhos[0]));
            },
            null,
            FileBrowser.PickMode.FilesAndFolders, false, null, null, "Selecione a pasta OU o arquivo (.xml/.json) com os NPCs", "Importar");
    }

    // Atalho "Downloads" na barra lateral do explorador — FileBrowser.AddQuickLink já ignora
    // sozinho se o atalho já existe ou se a pasta não existe (não precisa de guarda própria aqui).
    // %USERPROFILE%\Downloads não tem um Environment.SpecialFolder próprio no .NET usado pela Unity,
    // por isso monta o caminho a partir da pasta do usuário.
    private static void GarantirAtalhoDownloads()
    {
        string pastaDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        FileBrowser.AddQuickLink("Downloads", pastaDownloads);
    }

    // Aceita tanto uma pasta (escaneia .xml/.json dentro dela) quanto um arquivo único — o
    // explorador mostra arquivos também (PickMode.FilesAndFolders), então clicar direto num
    // .json/.xml em vez de navegar até a pasta e confirmar ela também precisa funcionar. Cada
    // arquivo/entrada reconhecida vira um NPC novo — ou substitui um existente com o mesmo nome/id
    // (ver DynamicXmlBuilder.ResolverEntryId), sem mexer nos NPCs já salvos que não fazem parte
    // deste import. Conversão via CampanhaNpcImporter (melhor esforço — casa campos pelo catálogo,
    // ignora o que não reconhece).
    private IEnumerator ImportarNpcsDeCaminhoCoroutine(string caminhoOrigem)
    {
        if (!Directory.Exists(caminhoOrigem) && !File.Exists(caminhoOrigem))
        {
            VTTLog.ForjaError($"[CampanhasManager] Importar NPCs: '{caminhoOrigem}' não existe.");
            yield break;
        }

        PopupManager.Instance?.MostrarCarregamento("Importando NPCs", Path.GetFileName(caminhoOrigem));
        yield return null;

        List<XmlFieldDef> catalogo = Dnd5eNpcFieldCatalog.AllFields;
        List<EntryData> entradas = CampanhaNpcImporter.ImportarDeCaminho(caminhoOrigem, catalogo, "npcs", out int falhas);

        if (entradas.Count == 0)
        {
            PopupManager.Instance?.Fechar();
            VTTLog.ForjaError($"[CampanhasManager] Nenhum NPC importado de '{caminhoOrigem}' (esperado .xml ou .json reconhecível).");
            yield break;
        }

        // Marca a origem pra o filtro "Importados" de CampanhaNpcGaleriaUI conseguir distinguir
        // deste NPC de um criado à mão pelo tópico "npcs" — ver Dnd5eNpcFieldCatalog.OrigemField.
        foreach (EntryData entrada in entradas)
            entrada.Fields.Add(new FieldValue { Definition = Dnd5eNpcFieldCatalog.OrigemField, TextValue = "imported" });

        string pastaDestino = CampanhasLocalStorage.NpcsFolderPath(_campanhaId);
        DynamicXmlBuilder.SaveEntriesAsXml(entradas, pastaDestino, "npcs");

        PopupManager.Instance?.Fechar();
        AbrirListaNpcsForcado(); // mesmo comportamento de "salvou um NPC" — mostra a lista atualizada, com seu próprio popup

        string resumo = falhas > 0
            ? $"{entradas.Count} NPC(s) importado(s), {falhas} arquivo(s) com falha."
            : $"{entradas.Count} NPC(s) importado(s) com sucesso.";
        VTTLog.Forja($"[CampanhasManager] Import de NPCs de '{caminhoOrigem}': {resumo}");
    }

    // ── Lore de NPC (painel separado, sem botão próprio na aba geral) ────────

    /// <summary>Chamado pelo botão "Lore" de um card na Lista de NPCs (ver CampanhaNpcCardUI/
    /// CampanhaNpcGaleriaUI) — abre o painel de lore com o EntryData completo dessa entrada.</summary>
    public void AbrirLoreDoNpc(EntryData data)
    {
        if (data == null || npcLorePainel == null) return;

        FecharMapa();
        FecharListaMapas();
        FecharListaNpcs();
        if (_topicoAberto != null)
        {
            SetAtivo(_topicoAberto.painel, false);
            _topicoAberto = null;
        }

        _loreNpcAberta = true;
        SetAtivo(npcLorePainel, true);
        npcLoreEditor?.Setup(data, CampanhasLocalStorage.NpcsFolderPath(_campanhaId), "npcs");
    }

    private void FecharLoreDoNpc()
    {
        if (!_loreNpcAberta) return;
        // Autosave silencioso ao navegar pra outro painel — sem isso, texto/itens editados na lore
        // e ainda não salvos explicitamente eram descartados ao sair do painel.
        npcLoreEditor?.SalvarSeCarregado();
        _loreNpcAberta = false;
        SetAtivo(npcLorePainel, false);
    }

    // Assina CampanhaTopicEditorUI.OnSaved do tópico "npcs" — depois de salvar com sucesso, troca
    // automaticamente pro painel de listagem em vez de deixar o formulário de criação aberto.
    private void AoNpcSalvo()
    {
        AbrirListaNpcsForcado();
    }

    /// <summary>Chamado por CampanhaMapManager.PedirEdicaoDoNpc (botão "Editar" no menu de atalho de
    /// um token do grid) — abre (ou reaproveita) o tópico "npcs" e carrega essa entrada nos campos.</summary>
    public void AbrirNpcParaEdicao(EntryData data)
    {
        if (data == null) return;

        TopicoBotao npcsTopico = botoesTopicos.Find(tb => tb != null && tb.topicId == "npcs");
        if (npcsTopico == null || npcsTopico.painel == null || npcsTopico.editor == null) return;

        FecharMapa();
        FecharListaMapas();
        FecharListaNpcs();
        FecharLoreDoNpc();

        if (_topicoAberto != npcsTopico)
        {
            if (_topicoAberto != null) SetAtivo(_topicoAberto.painel, false);
            _topicoAberto = npcsTopico;
            SetAtivo(npcsTopico.painel, true);
            npcsTopico.editor.Setup(npcsTopico.topicId, ObterPastaDoTopico(npcsTopico.topicId), ObterCatalogoDoTopico(npcsTopico.topicId));
        }

        npcsTopico.editor.EditarEntrada(data);
    }

    private List<XmlFieldDef> ObterCatalogoDoTopico(string topicId)
    {
        switch (topicId)
        {
            case "npcs": return Dnd5eNpcFieldCatalog.Fields;
            default:
                VTTLog.ForjaError($"[CampanhasManager] Topic Id '{topicId}' não reconhecido — confira o campo " +
                    "'Topic Id' na lista 'Aba de Botões Gerais' (deve ser exatamente 'npcs', minúsculo e sem espaço).");
                return new List<XmlFieldDef>();
        }
    }

    private string ObterPastaDoTopico(string topicId)
    {
        switch (topicId)
        {
            case "npcs": return CampanhasLocalStorage.NpcsFolderPath(_campanhaId);
            default:     return Path.Combine(_campanhaPath, topicId);
        }
    }

    // ── Salvar ────────────────────────────────────────────────────────────────

    private void SalvarTopicoAtual()
    {
        // Botão "Salvar" compartilhado — se o painel de Lore estiver aberto, salva ele (e volta pra
        // listagem, via OnSaved); senão cai no fluxo normal do editor de stat block.
        if (_loreNpcAberta)
        {
            npcLoreEditor?.Salvar();
            return;
        }

        _topicoAberto?.editor?.SalvarTudo(PersistirImagemDeEntrada);
    }

    // ── Persistência de imagens de entrada (ex: retrato de NPC) ─────────────────

    private string PersistirImagemDeEntrada(EntryData entry, FieldValue fv)
    {
        string topicId = _topicoAberto?.topicId;
        string destino = topicId switch
        {
            "npcs" => CampanhasLocalStorage.NpcsImagesPath(_campanhaId),
            _      => Path.Combine(_campanhaPath, topicId ?? "geral", "imagens"),
        };

        string hint = DynamicXmlBuilder.ResolverEntryId(entry, 0);
        return CampanhasLocalStorage.PersistImageIfPossible(fv.TextValue, destino, hint);
    }

    // ── Utilitários ───────────────────────────────────────────────────────────

    private static void SetAtivo(GameObject go, bool ativo)
    {
        if (go != null) go.SetActive(ativo);
    }

    private static void Religar(Button btn, Action acao)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => acao());
    }
}
