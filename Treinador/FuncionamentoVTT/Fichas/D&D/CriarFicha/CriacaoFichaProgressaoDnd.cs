using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;

public enum TipoResumoEtapaDnd
{
    Manual,
    Atributos,
    Raca,
    Antecedente,
    Classe,
    NomeENivel,
    Itens,
    Magias,
    Talentos,
    Nenhum
}

[System.Serializable]
public class EtapaCriacaoDnd
{
    [Header("Base")]
    public string nomeDaEtapa;
    public GameObject painel;
    public Button botaoContinuar;

    [Header("Resumo do item de progressao")]
    public string tituloNoProgresso;
    public TipoResumoEtapaDnd tipoResumo = TipoResumoEtapaDnd.Manual;
    [TextArea(2, 5)]
    public string detalhesManuais;

    [NonSerialized] public ItemProgressoCriacaoDndUI itemUIInstanciado;
}

public class CriacaoFichaProgressaoDnd : MonoBehaviour
{
    [Serializable]
    private class SubDataDetalhes
    {
        public string name;
    }

    [Serializable]
    private class DetalhesPrincipaisSnapshot
    {
        public SubDataDetalhes subrace;
        public string subrace_name;
    }

    [Serializable]
    private class RacaInfoSnapshot
    {
        public string subrace_name;
    }

    // GrimorioWrapper/GrimorioSaveItem agora são as classes canônicas e públicas de
    // LocalFichaStorage.cs — não são mais declaradas aqui.

    [Header("Configuração das Etapas")]
    public List<EtapaCriacaoDnd> etapas;

    [Header("Prefab de progressão")]
    [SerializeField] private Transform containerProgressao;
    [SerializeField] private GameObject prefabItemProgressao;
    [SerializeField] private bool limparContainerAntesDeCriar = true;

    [Header("Progresso Atual (opcional)")]
    [SerializeField] private TMP_Text textoProgressoAtual;

    [Header("Botão Continuar (opcional)")]
    [SerializeField] private Button botaoContinuarGlobal;

    [Header("Seletor de Imagem do Token (opcional)")]
    [SerializeField] private TokenImageSelector tokenImageSelector;

    [Header("Painéis de Info (troca conforme etapa)")]
    [Tooltip("Painel de info padrão — exibido em Atributos, Raça, Classe, Antecedente, etc.")]
    [SerializeField] private GameObject painelInfoPadrao;
    [Tooltip("Painel de info específico — exibido apenas nas etapas de Equipamento e Magia.")]
    [SerializeField] private GameObject painelInfoEquipamentoMagia;
    [Tooltip("Painel fixo de Atributos (AttributeManager, agora fora da sequência de etapas) — " +
        "mesma regra de visibilidade de painelInfoPadrao: some nas etapas de Equipamento/Magia " +
        "pra não sobrepor painelInfoEquipamentoMagia, aparece em todas as outras.")]
    [SerializeField] private GameObject painelAtributos;

    private int indiceEtapaAtual = 0;
    private int maiorEtapaAlcancada = 0;
    private int magiasPreparadas = 0;
    private readonly HashSet<Button> botoesNavegacaoRegistrados = new HashSet<Button>();
    private int _ultimaFichaIdCarregada = -1;

    private void OnEnable()
    {
        // A tela de criação (e o FichaSaveManager singleton que a acompanha) pode ter escondido
        // o botão "Finalizar" ao concluir uma ficha anterior nesta mesma sessão. Reexibe sempre
        // que esta tela volta a ficar ativa, seja para uma ficha nova ou para editar uma existente.
        FichaSaveManager.Instance?.ResetarBotaoFinalizarESalvar();

        TratarTrocaDeSessao();

        AttributeManager.OnAtributosGlobaisMudaram += OnAtributosGlobaisMudaram;
        EquipmentSelector.OnOuroMudou += RefreshProgressaoUI;

        // Gatilho independente do próprio ciclo OnEnable/OnDisable — mesma correção já aplicada em
        // SpellSelector/ClassSelector/RaceSelector/FichaGeralManager pro bug de GameObject "sempre
        // ativo": este objeto hospeda o ClassSelector (que precisa ficar sempre ativo, ver
        // RaceSelector.cs), então OnEnable pode não redisparar de forma confiável ao trocar de
        // ficha. Sem isto, indiceEtapaAtual ficava travado na última aba vista da ficha ANTERIOR
        // (ex: Classe) — a ficha nova nunca voltava a mostrar Atributos/Raça, e como a aba nunca
        // trocava, os painéis dessas etapas nunca recarregavam pra refletir o reset de
        // GlobalFichaData (a raça/atributos zerados pelo reset central nunca eram re-exibidos).
        GlobalFichaData.OnSessaoDeFichaIniciada += OnSessaoDeFichaIniciadaHandler;
    }

    private void OnSessaoDeFichaIniciadaHandler()
    {
        if (!gameObject.activeInHierarchy) return;
        TratarTrocaDeSessao();
    }

    /// <summary>
    /// Detecta se a ficha ativa mudou (ou se é um "subir de nível" na mesma ficha) e, se sim,
    /// reseta o progresso visual e recarrega dados pré-salvos — chamado tanto por OnEnable() quanto
    /// por OnSessaoDeFichaIniciadaHandler(), pra funcionar independente de qual dos dois disparar.
    /// </summary>
    private void TratarTrocaDeSessao()
    {
        bool fichaIdMudou = _ultimaFichaIdCarregada != GlobalFichaData.fichasDndId;

        if (fichaIdMudou)
        {
            Debug.Log($"[CriacaoFichaProgressaoDnd] Sessão trocou — ficha mudou ({_ultimaFichaIdCarregada} → {GlobalFichaData.fichasDndId}). Resetando progresso.");
            _ultimaFichaIdCarregada = GlobalFichaData.fichasDndId;
            indiceEtapaAtual = 0;
            maiorEtapaAlcancada = 0;
        }

        // Carrega restauração ANTES dos OnEnable dos filhos (Unity garante: pai antes dos filhos)
        if (GlobalFichaData.fichasDndId > 0)
        {
            int pid = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
            string sid = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
            if (LocalFichaStorage.TryGetFichaSnapshot(sid, pid,
                GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, out LocalFichaData snap))
            {
                Debug.Log($"[CriacaoFichaProgressaoDnd] Snapshot carregado para fichaId={GlobalFichaData.fichasDndId} nome='{snap.name_pc}'");
                FichaLoaderUI.CarregarRestauracaoDoSnapshot(snap);
            }
        }

        // Se Start já rodou (etapas criadas) e a ficha mudou, recarrega progresso visual
        if (fichaIdMudou && etapas != null && etapas.Count > 0)
        {
            Debug.Log("[CriacaoFichaProgressaoDnd] Recarregando dados pré-salvos após troca de ficha.");
            TentarCarregarDadosPresalvos();
            MostrarEtapa(indiceEtapaAtual);
        }

        // Subir Nível: mesma ficha, navega direto para a etapa de Classe com restauração completa
        // para que ClassSelector recarregue dados
        if (!fichaIdMudou && GlobalFichaData.modoSubirNivel && etapas != null && etapas.Count > 0)
        {
            GlobalFichaData.modoSubirNivel = false;

            int indiceClasse = etapas.FindIndex(e => e != null && e.tipoResumo == TipoResumoEtapaDnd.Classe);
            if (indiceClasse < 0) indiceClasse = 2;

            indiceEtapaAtual     = indiceClasse;
            maiorEtapaAlcancada  = Mathf.Max(maiorEtapaAlcancada, etapas.Count - 1);

            Debug.Log($"[CriacaoFichaProgressaoDnd] Subir Nível — indo para etapa de Classe (índice {indiceClasse})");
            StartCoroutine(IniciarComRestauracaoCoroutine(indiceClasse));
        }
    }

    private void OnDisable()
    {
        GlobalFichaData.OnSessaoDeFichaIniciada -= OnSessaoDeFichaIniciadaHandler;
        AttributeManager.OnAtributosGlobaisMudaram -= OnAtributosGlobaisMudaram;
        EquipmentSelector.OnOuroMudou -= RefreshProgressaoUI;

        // Auto-save quando a tela de criação for desativada
        TentarSalvarAutomaticamente();
    }

    private void Start()
    {
        if (etapas == null || etapas.Count == 0)
        {
            Debug.LogWarning("[CriacaoFichaProgressaoDnd] Nenhuma etapa configurada.");
            return;
        }

        CriarItensDeProgressao();
        RegistrarBotoesDaEtapa();

        if (botaoContinuarGlobal != null)
        {
            botaoContinuarGlobal.gameObject.SetActive(true);
            botaoContinuarGlobal.interactable = true;
            botaoContinuarGlobal.onClick.RemoveListener(AvancarEtapa);
            botaoContinuarGlobal.onClick.AddListener(AvancarEtapa);
        }
        else
        {
            Debug.LogWarning("[CriacaoFichaProgressaoDnd] Botão Continuar Global não foi configurado.");
        }

        // Tentar carregar dados pré-salvos (para fichas em continuação)
        // Isso também define indiceEtapaAtual com base no PlayerPrefs/snapshot
        TentarCarregarDadosPresalvos();

        // Se há uma ficha sendo restaurada, ativa todos os painéis por 1 frame
        // para que os seletores inativos recebam OnEnable e restaurem seus dados
        if (GlobalFichaData.fichasDndId > 0 && GlobalFichaData.precisaRestaurarFluxo)
        {
            StartCoroutine(IniciarComRestauracaoCoroutine(indiceEtapaAtual));
        }
        else
        {
            GlobalFichaData.precisaRestaurarFluxo = false;
            MostrarEtapa(indiceEtapaAtual);
        }
    }

    private IEnumerator IniciarComRestauracaoCoroutine(int indiceAlvo)
    {
        Debug.Log($"[CriacaoFichaProgressaoDnd] IniciarComRestauracao — ativando todos os painéis por 1 frame (indiceAlvo={indiceAlvo})");

        // Ativa todos os painéis para que os seletores acordem e restaurem via OnEnable
        foreach (var etapa in etapas)
            if (etapa?.painel != null) etapa.painel.SetActive(true);

        yield return new WaitForEndOfFrame();

        // Limpa a flag — todos os OnEnable dos seletores já rodaram
        GlobalFichaData.precisaRestaurarFluxo = false;

        // Navega para a última aba onde o jogador estava
        Debug.Log($"[CriacaoFichaProgressaoDnd] IniciarComRestauracao — navegando para etapa {indiceAlvo}");
        MostrarEtapa(indiceAlvo);
    }

    public void AvancarEtapa()
    {
        if (etapas == null || etapas.Count == 0) return;

        if (indiceEtapaAtual < 0 || indiceEtapaAtual >= etapas.Count)
        {
            indiceEtapaAtual = Mathf.Clamp(indiceEtapaAtual, 0, etapas.Count - 1);
        }

        if (indiceEtapaAtual < etapas.Count - 1)
        {
            indiceEtapaAtual++;
            if (indiceEtapaAtual > maiorEtapaAlcancada)
            {
                maiorEtapaAlcancada = indiceEtapaAtual;
            }

            MostrarEtapa(indiceEtapaAtual);
        }
        else
        {
            AtualizarItensDeProgresso(indiceEtapaAtual, true);
            Debug.Log("<color=green>Criação de Ficha Finalizada!</color> Salvando dados...");
        }
    }

    /// <summary>
    /// Avança para a próxima etapa automaticamente, mas SOMENTE se a etapa atualmente exibida for do
    /// tipo informado — sinal diagnóstico usado por seletores (hoje só ClassSelector) que sabem quando
    /// terminaram de vez (ex: fila de passos esvaziada). O guard evita:
    ///  1) avançar a etapa errada se disparar durante o "esquenta" de 1 frame de
    ///     IniciarComRestauracaoCoroutine (todos os painéis ativos ao mesmo tempo) — mesmo se disparar
    ///     nessa janela, o MostrarEtapa(indiceAlvo) no fim daquela corrotina sempre roda por último e
    ///     prevalece, então o destino da restauração nunca é corrompido;
    ///  2) disparar duas vezes seguidas pro mesmo término — depois do primeiro avanço bem-sucedido,
    ///     indiceEtapaAtual deixa de apontar pra 'tipo', então uma segunda chamada vira no-op.
    /// Reaproveita AvancarEtapa()/MostrarEtapa() — não duplica lógica de navegação.
    /// </summary>
    public void AvancarSeEtapaAtualFor(TipoResumoEtapaDnd tipo)
    {
        if (etapas == null || etapas.Count == 0) return;
        if (indiceEtapaAtual < 0 || indiceEtapaAtual >= etapas.Count) return;

        if (etapas[indiceEtapaAtual].tipoResumo != tipo)
        {
            Debug.Log($"[CriacaoFichaProgressaoDnd] AvancarSeEtapaAtualFor({tipo}) ignorado — etapa atual é '{etapas[indiceEtapaAtual].tipoResumo}' (índice {indiceEtapaAtual}).");
            return;
        }

        Debug.Log($"[CriacaoFichaProgressaoDnd] Auto-avançando de {tipo} pra próxima etapa (fila de passos concluída).");
        AvancarEtapa();
    }

    public void PularParaEtapa(int indiceAlvo)
    {
        if (etapas == null || etapas.Count == 0) return;

        if (indiceAlvo <= maiorEtapaAlcancada)
        {
            indiceEtapaAtual = indiceAlvo;
            MostrarEtapa(indiceEtapaAtual);
        }
        else
        {
            Debug.LogWarning("Você precisa terminar a etapa atual antes de ir para essa!");
        }
    }

    private void MostrarEtapa(int indiceParaMostrar)
    {
        if (etapas == null || etapas.Count == 0) return;

        indiceParaMostrar = Mathf.Clamp(indiceParaMostrar, 0, etapas.Count - 1);
        GameObject painelAtual = etapas[indiceParaMostrar].painel;

        for (int i = 0; i < etapas.Count; i++)
        {
            bool ehAEtapaAtual = (i == indiceParaMostrar);
            if (etapas[i].painel != null)
            {
                etapas[i].painel.SetActive(ehAEtapaAtual);
            }

            Button botaoNavegacao = etapas[i].itemUIInstanciado != null ? etapas[i].itemUIInstanciado.botaoNavegacao : null;
            if (botaoNavegacao != null)
            {
                botaoNavegacao.interactable = (i <= maiorEtapaAlcancada);
            }
        }

        AtivarHierarquiaDoPainelAtual(painelAtual);

        if (botaoContinuarGlobal != null)
        {
            botaoContinuarGlobal.gameObject.SetActive(true);
            botaoContinuarGlobal.interactable = true;
        }

        // Alterna o painel de info conforme o tipo da etapa atual
        TipoResumoEtapaDnd tipoAtual = etapas[indiceParaMostrar].tipoResumo;
        bool ehEquipamentoOuMagia = tipoAtual == TipoResumoEtapaDnd.Itens
                                 || tipoAtual == TipoResumoEtapaDnd.Magias;
        painelInfoPadrao?.SetActive(!ehEquipamentoOuMagia);
        painelInfoEquipamentoMagia?.SetActive(ehEquipamentoOuMagia);
        // AttributeManager saiu da sequência de etapas e ficou fixo na tela — mas continua
        // sobrepondo painelInfoEquipamentoMagia nas etapas de Itens/Magias, então some junto com
        // painelInfoPadrao nessas duas e volta a aparecer em todas as outras.
        painelAtributos?.SetActive(!ehEquipamentoOuMagia);

        // Garante que o catálogo de magias reflita a classe já escolhida/salva ao chegar nesta
        // etapa — mesmo raciocínio de RaceSelector.ForcarRecarregamento() (AutoSaveAttributesManager)
        // já usado nesta sessão: sem isto, SpellSelector podia ficar travado no estado "sem classe
        // ainda" carregado pela ativação prévia de 1 frame em IniciarComRestauracaoCoroutine.
        if (tipoAtual == TipoResumoEtapaDnd.Magias)
        {
            SpellSelector spellSelector = FindObjectOfType<SpellSelector>(true);
            spellSelector?.ForcarRecarregamento();
        }

        AtualizarItensDeProgresso(indiceParaMostrar);
        AtualizarTextoProgressoAtual(indiceParaMostrar);
        indiceEtapaAtual = indiceParaMostrar;

        // Persiste a aba atual para restaurar na próxima sessão
        if (GlobalFichaData.fichasDndId > 0)
            PlayerPrefs.SetInt($"Ficha_{GlobalFichaData.fichasDndId}_UltimaAba", indiceEtapaAtual);
    }

    private void CriarItensDeProgressao()
    {
        if (containerProgressao == null || prefabItemProgressao == null)
        {
            Debug.LogWarning("[CriacaoFichaProgressaoDnd] Faltam referências do container ou do prefab de progressão.");
            return;
        }

        if (limparContainerAntesDeCriar)
        {
            for (int i = containerProgressao.childCount - 1; i >= 0; i--)
            {
                Destroy(containerProgressao.GetChild(i).gameObject);
            }
        }

        for (int i = 0; i < etapas.Count; i++)
        {
            EtapaCriacaoDnd etapa = etapas[i];
            if (etapa == null)
            {
                continue;
            }

            GameObject itemGO = UIUtils.Spawn(prefabItemProgressao, containerProgressao, false);
            itemGO.SetActive(true);

            ItemProgressoCriacaoDndUI itemUI = itemGO.GetComponent<ItemProgressoCriacaoDndUI>();
            if (itemUI == null)
            {
                Debug.LogWarning("[CriacaoFichaProgressaoDnd] O prefab de progressão não possui o componente ItemProgressoCriacaoDndUI.");
                continue;
            }

            etapa.itemUIInstanciado = itemUI;
            int indiceCapturado = i;
            itemUI.Configurar(
                ObterTituloDaEtapa(etapa),
                MontarResumoDaEtapa(etapa),
                false,
                () => PularParaEtapa(indiceCapturado)
            );
            itemUI.DefinirInteratividade(false);
            itemUI.DefinirConcluida(false);
        }
    }

    private void RegistrarBotoesDaEtapa()
    {
        botoesNavegacaoRegistrados.Clear();

        for (int i = 0; i < etapas.Count; i++)
        {
            ItemProgressoCriacaoDndUI itemUI = etapas[i] != null ? etapas[i].itemUIInstanciado : null;
            Button botao = itemUI != null ? itemUI.botaoNavegacao : null;
            if (botao == null || botoesNavegacaoRegistrados.Contains(botao))
            {
                continue;
            }

            int indiceCapturado = i;
            botao.onClick.RemoveAllListeners();
            botao.onClick.AddListener(() => PularParaEtapa(indiceCapturado));
            botoesNavegacaoRegistrados.Add(botao);
        }
    }

    private void AtualizarItensDeProgresso(int indiceAtual, bool marcarEtapaAtualComoConcluida = false)
    {
        for (int i = 0; i < etapas.Count; i++)
        {
            EtapaCriacaoDnd etapa = etapas[i];
            if (etapa == null || etapa.itemUIInstanciado == null)
            {
                continue;
            }

            bool concluida = i < indiceAtual || (marcarEtapaAtualComoConcluida && i == indiceAtual);
            bool liberada = i <= maiorEtapaAlcancada;

            etapa.itemUIInstanciado.Configurar(
                ObterTituloDaEtapa(etapa),
                MontarResumoDaEtapa(etapa),
                concluida,
                null
            );
            etapa.itemUIInstanciado.DefinirInteratividade(liberada);
            etapa.itemUIInstanciado.DefinirConcluida(concluida);
        }
    }

    private string ObterTituloDaEtapa(EtapaCriacaoDnd etapa)
    {
        if (etapa == null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(etapa.tituloNoProgresso)
            ? etapa.nomeDaEtapa
            : etapa.tituloNoProgresso;
    }

    private string MontarResumoDaEtapa(EtapaCriacaoDnd etapa)
    {
        if (etapa == null)
        {
            return string.Empty;
        }

        switch (etapa.tipoResumo)
        {
            case TipoResumoEtapaDnd.Atributos:
                return MontarResumoAtributos();
            case TipoResumoEtapaDnd.Raca:
                return MontarResumoRaca();
            case TipoResumoEtapaDnd.Antecedente:
                return MontarResumoAntecedente();
            case TipoResumoEtapaDnd.Classe:
                return MontarResumoClasse();
            case TipoResumoEtapaDnd.NomeENivel:
                return MontarResumoNomeNivel();
            case TipoResumoEtapaDnd.Itens:
                return MontarResumoItens();
            case TipoResumoEtapaDnd.Magias:
                return MontarResumoMagias();
            case TipoResumoEtapaDnd.Talentos:
                return MontarResumoTalentos();
            case TipoResumoEtapaDnd.Nenhum:
                return string.Empty;
            case TipoResumoEtapaDnd.Manual:
            default:
                return etapa.detalhesManuais ?? string.Empty;
        }
    }

    private string MontarResumoAtributos()
    {
        int forca = GlobalFichaData.forca;
        int destreza = GlobalFichaData.destreza;
        int constituicao = GlobalFichaData.constituicao;
        int inteligencia = GlobalFichaData.inteligencia;
        int sabedoria = GlobalFichaData.sabedoria;
        int carisma = GlobalFichaData.carisma;

        Debug.Log($"[CriacaoFichaProgressaoDnd] MontarResumoAtributos - Valores de GlobalFichaData: FOR={forca}, DEX={destreza}, CON={constituicao}, INT={inteligencia}, SAB={sabedoria}, CAR={carisma}");

        // Se todos os atributos estão em 10 (padrão vazio), tentar carregar de LocalFichaStorage
        bool todosDez = forca == 10 && destreza == 10 && constituicao == 10 && 
                        inteligencia == 10 && sabedoria == 10 && carisma == 10;
        
        if (todosDez)
        {
            // Tentar carregar de LocalFichaStorage
            int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
            string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
            
            if (LocalFichaStorage.TryLoadAttributes(
                steamId,
                playerId,
                GlobalFichaData.fichasGeneralId,
                GlobalFichaData.fichasDndId,
                out int forcaSalva,
                out int destrezaSalva,
                out int constituicaoSalva,
                out int inteligenciaSalva,
                out int sabedoriaSalva,
                out int carismaSalva))
            {
                // Se carregou com sucesso, usar esses valores
                forca = forcaSalva;
                destreza = destrezaSalva;
                constituicao = constituicaoSalva;
                inteligencia = inteligenciaSalva;
                sabedoria = sabedoriaSalva;
                carisma = carismaSalva;
                
                // Também atualizar GlobalFichaData para sincronizar
                GlobalFichaData.forca = forca;
                GlobalFichaData.destreza = destreza;
                GlobalFichaData.constituicao = constituicao;
                GlobalFichaData.inteligencia = inteligencia;
                GlobalFichaData.sabedoria = sabedoria;
                GlobalFichaData.carisma = carisma;
                
                Debug.Log($"[CriacaoFichaProgressaoDnd] ✓ Atributos recarregados de LocalFichaStorage no resumo: FOR={forca}, DEX={destreza}, CON={constituicao}, INT={inteligencia}, SAB={sabedoria}, CAR={carisma}");
            }
        }

        bool semDados = forca == 0 && destreza == 0 && constituicao == 0 && inteligencia == 0 && sabedoria == 0 && carisma == 0;
        if (semDados)
        {
            Debug.LogWarning("[CriacaoFichaProgressaoDnd] MontarResumoAtributos: Todos atributos estão em 0!");
            return "Aguardando definição dos atributos.";
        }

        // Calcular totais incluindo bônus de raça + classe + antecedente armazenados em GlobalFichaData
        int forcaTotal = forca + GlobalFichaData.bonusRacaDict["FOR"] + GlobalFichaData.bonusClasseDict["FOR"] + GlobalFichaData.bonusAntecedenteDict["FOR"] + GlobalFichaData.bonusTalentoDict["FOR"];
        int destrezaTotal = destreza + GlobalFichaData.bonusRacaDict["DEX"] + GlobalFichaData.bonusClasseDict["DEX"] + GlobalFichaData.bonusAntecedenteDict["DEX"] + GlobalFichaData.bonusTalentoDict["DEX"];
        int constituicaoTotal = constituicao + GlobalFichaData.bonusRacaDict["CON"] + GlobalFichaData.bonusClasseDict["CON"] + GlobalFichaData.bonusAntecedenteDict["CON"] + GlobalFichaData.bonusTalentoDict["CON"];
        int inteligenciaTotal = inteligencia + GlobalFichaData.bonusRacaDict["INT"] + GlobalFichaData.bonusClasseDict["INT"] + GlobalFichaData.bonusAntecedenteDict["INT"] + GlobalFichaData.bonusTalentoDict["INT"];
        int sabedoriaTotal = sabedoria + GlobalFichaData.bonusRacaDict["SAB"] + GlobalFichaData.bonusClasseDict["SAB"] + GlobalFichaData.bonusAntecedenteDict["SAB"] + GlobalFichaData.bonusTalentoDict["SAB"];
        int carismaTotal = carisma + GlobalFichaData.bonusRacaDict["CAR"] + GlobalFichaData.bonusClasseDict["CAR"] + GlobalFichaData.bonusAntecedenteDict["CAR"] + GlobalFichaData.bonusTalentoDict["CAR"];

        Debug.Log($"[CriacaoFichaProgressaoDnd] Bônus aplicados - Raça: FOR={GlobalFichaData.bonusRacaDict["FOR"]}, DEX={GlobalFichaData.bonusRacaDict["DEX"]} | Classe: FOR={GlobalFichaData.bonusClasseDict["FOR"]}");

        string resultado = string.Format("{0} FOR, {1} DEX, {2} CON, {3} INT, {4} SAB, {5} CAR", 
            forcaTotal, destrezaTotal, constituicaoTotal, inteligenciaTotal, sabedoriaTotal, carismaTotal);
        
        Debug.Log($"[CriacaoFichaProgressaoDnd] MontarResumoAtributos FINAL: {resultado}");
        return resultado;
    }

    private string MontarResumoRaca()
    {
        string raca = string.IsNullOrWhiteSpace(GlobalFichaData.racaPersonagem)
            ? "Raça não definida"
            : GlobalFichaData.racaPersonagem;

        string subraca = ObterNomeSubracaDosDetalhes();
        if (!string.IsNullOrWhiteSpace(subraca))
        {
            return raca + ", " + subraca;
        }

        return raca;
    }

    private string MontarResumoAntecedente()
    {
        if (GlobalFichaData.currentAntecedenteId > 0)
        {
            return "Antecedente selecionado";
        }

        return "Antecedente ainda não selecionado.";
    }

    private string MontarResumoItens()
    {
        // ResolveInstanciaPrincipal() em vez de FindObjectOfType sozinho — mesma ambiguidade entre
        // as duas instâncias de EquipmentSelector da cena (ver EquipmentSelector.cs); aqui só afeta
        // este resumo exibido em tela, mas usar a mesma instância que o resto do fluxo evita
        // mostrar dados de uma tela de equipamento que não é a que o jogador está usando.
        EquipmentSelector equipSelector = EquipmentSelector.ResolveInstanciaPrincipal();
        if (equipSelector == null)
        {
            return "Inventário não carregado.";
        }

        float pesoTotal = equipSelector.ObterPesoTotal();
        float ouro = equipSelector.ObterOuroTotal();

        return $"Peso: {pesoTotal:0.##} lb | Ouro: {ouro:0.##} GP";
    }

    private string MontarResumoMagias()
    {
        magiasPreparadas = 0;
        string slotsJson = ObterSlotsMagiaJsonAtual();
        Dictionary<int, int> slotsPorNivel = ExtrairSlotsPorNivel(slotsJson);

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        if (LocalFichaStorage.TryGetFichaSnapshot(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            out LocalFichaData snapshot) && snapshot?.magias_json?.magias != null)
        {
            GrimorioWrapper wrapper = snapshot.magias_json;
            magiasPreparadas = wrapper.magias.Count(m => m != null && m.preparada);
            Debug.Log($"[CriacaoFichaProgressaoDnd] Grimório do snapshot. Total={wrapper.magias.Count} Preparadas={magiasPreparadas} slotsJson='{slotsJson}'");
        }

        Debug.Log($"[CriacaoFichaProgressaoDnd] MontarResumoMagias. slotsJson='{slotsJson}' slotsCount={slotsPorNivel.Count} magiasPreparadas={magiasPreparadas}");

        if (slotsPorNivel.Count > 0)
        {
            int totalSlots = slotsPorNivel.Values.Sum();
            string slotsResumo = string.Join(" | ", slotsPorNivel.OrderBy(s => s.Key).Select(s => $"Nv {s.Key}: {s.Value}"));
            string preparados = magiasPreparadas > 0 ? $" | Preparadas: {magiasPreparadas}" : string.Empty;
            return $"Slots: {slotsResumo} | Total: {totalSlots}{preparados}";
        }

        if (magiasPreparadas > 0)
        {
            return $"Slots não encontrados | Magias preparadas: {magiasPreparadas}";
        }

        if (string.IsNullOrWhiteSpace(GlobalFichaData.classInfoJson) || GlobalFichaData.classInfoJson == "{}")
        {
            return "Nenhuma magia disponível.";
        }

        return "Classe com capacidade de magia - Consulte detalhes.";
    }

    private string ObterSlotsMagiaJsonAtual()
    {
        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        if (LocalFichaStorage.TryGetFichaSnapshot(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            out LocalFichaData snapshot) && snapshot != null && !string.IsNullOrWhiteSpace(snapshot.slots_magias_json))
        {
            return snapshot.slots_magias_json;
        }

        return GlobalFichaData.slotsMagiasJson ?? string.Empty;
    }

    private Dictionary<int, int> ExtrairSlotsPorNivel(string slotsJson)
    {
        Dictionary<int, int> slots = new Dictionary<int, int>();
        if (string.IsNullOrWhiteSpace(slotsJson))
        {
            Debug.Log("[CriacaoFichaProgressaoDnd] ExtrairSlotsPorNivel: slotsJson vazio.");
            return slots;
        }

        System.Text.RegularExpressions.MatchCollection matches = System.Text.RegularExpressions.Regex.Matches(slotsJson, "\\\"(?<lvl>\\d+)\\\"\\s*:\\s*(?<qtd>\\d+)");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (!match.Success) continue;
            if (int.TryParse(match.Groups["lvl"].Value, out int nivel) && int.TryParse(match.Groups["qtd"].Value, out int quantidade))
            {
                slots[nivel] = quantidade;
            }
        }

        Debug.Log($"[CriacaoFichaProgressaoDnd] ExtrairSlotsPorNivel -> '{slotsJson}' => {string.Join(", ", slots.OrderBy(x => x.Key).Select(x => $"Nv{x.Key}={x.Value}"))}");

        return slots;
    }

    private string MontarResumoTalentos()
    {
        if (TalentosSelector.talentosEscolhidosNestaSessao == null || TalentosSelector.talentosEscolhidosNestaSessao.Count == 0)
        {
            return "Nenhum talento selecionado.";
        }

        // Tentar obter nomes dos talentos através do TalentosSelector
        TalentosSelector talentosSelector = FindObjectOfType<TalentosSelector>();
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        
        foreach (string idTalento in TalentosSelector.talentosEscolhidosNestaSessao)
        {
            // Tentar obter nome do talento via reflecção
            string nomeTalento = ObterNomeTalento(idTalento, talentosSelector);
            
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(nomeTalento);
        }

        return sb.ToString();
    }

    private string ObterNomeTalento(string idTalento, TalentosSelector talentosSelector)
    {
        // Se não temos o ID, retornar vazio
        if (string.IsNullOrWhiteSpace(idTalento))
            return string.Empty;

        // Se temos referência ao seletor, procurar via reflecção/acesso direto
        if (talentosSelector != null)
        {
            try
            {
                // Tentar acessar a lista de talentos via reflecção
                var field = talentosSelector.GetType().GetField("listaDeTalentos", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    var listaDeTalentos = field.GetValue(talentosSelector) as List<TalentosSelector.TalentoDB>;
                    if (listaDeTalentos != null)
                    {
                        var talento = listaDeTalentos.FirstOrDefault(t => t.id_talento == idTalento);
                        if (talento != null && !string.IsNullOrWhiteSpace(talento.nome_talento))
                        {
                            return talento.nome_talento;
                        }
                    }
                }
            }
            catch { }
        }

        // Fallback: retornar o ID se não conseguir o nome
        return idTalento;
    }

    private string MontarResumoClasse()
    {
        if (!string.IsNullOrWhiteSpace(GlobalFichaData.classePersonagem))
        {
            // Tente obter a subclasse se existir
            string subclasse = ObterSubclasseDoSnapshot();
            if (!string.IsNullOrWhiteSpace(subclasse))
            {
                return GlobalFichaData.classePersonagem + " (" + subclasse + ")";
            }
            return GlobalFichaData.classePersonagem;
        }

        return "Classe ainda não selecionada.";
    }

    private string ObterSubclasseDoSnapshot()
    {
        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        if (!LocalFichaStorage.TryGetFichaSnapshot(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            out LocalFichaData snapshot))
        {
            return string.Empty;
        }

        // Lê subclasse diretamente de snapshot.classes (novo formato)
        var primary = snapshot?.classes?.PrimaryClass();
        if (primary != null && !string.IsNullOrWhiteSpace(primary.subclass_name))
            return primary.subclass_name;

        return string.Empty;
    }

    private string MontarResumoNomeNivel()
    {
        string nome = string.IsNullOrWhiteSpace(GlobalFichaData.nomePersonagem) ? "Sem nome" : GlobalFichaData.nomePersonagem;
        int nivel = GlobalFichaData.levelPersonagem > 0 ? GlobalFichaData.levelPersonagem : 1;
        return nome + " (Nível " + nivel + ")";
    }

    private string ObterNomeSubracaDosDetalhes()
    {
        string porDetalhes = ObterSubracaDoDetalhesPrincipais();
        if (!string.IsNullOrWhiteSpace(porDetalhes))
        {
            return porDetalhes;
        }

        return ObterSubracaDoSnapshotLocal();
    }

    private string ObterSubracaDoDetalhesPrincipais()
    {
        if (string.IsNullOrWhiteSpace(GlobalFichaData.detalhesPrincipaisJson))
        {
            return string.Empty;
        }

        try
        {
            DetalhesPrincipaisSnapshot detalhes = JsonUtility.FromJson<DetalhesPrincipaisSnapshot>(GlobalFichaData.detalhesPrincipaisJson);
            if (detalhes == null)
            {
                return string.Empty;
            }

            if (detalhes.subrace != null && !string.IsNullOrWhiteSpace(detalhes.subrace.name))
            {
                return detalhes.subrace.name;
            }

            if (!string.IsNullOrWhiteSpace(detalhes.subrace_name))
            {
                return detalhes.subrace_name;
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private string ObterSubracaDoSnapshotLocal()
    {
        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        if (!LocalFichaStorage.TryGetFichaSnapshot(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            out LocalFichaData snapshot))
        {
            return string.Empty;
        }

        // Lê subraca diretamente de snapshot.raca (novo formato)
        return snapshot?.raca?.subraca_nome ?? string.Empty;
    }

    private void AtualizarTextoProgressoAtual(int indice)
    {
        if (textoProgressoAtual == null || etapas == null || etapas.Count == 0)
        {
            return;
        }

        int indiceExibicao = Mathf.Clamp(indice + 1, 1, etapas.Count);
        string nomeEtapa = etapas[Mathf.Clamp(indice, 0, etapas.Count - 1)]?.nomeDaEtapa;
        if (string.IsNullOrWhiteSpace(nomeEtapa))
        {
            nomeEtapa = "Etapa " + indiceExibicao;
        }

        textoProgressoAtual.text = string.Format("{0}/{1} - {2}", indiceExibicao, etapas.Count, nomeEtapa);
    }

    private void AtivarHierarquiaDoPainelAtual(GameObject painelAtual)
    {
        if (painelAtual == null) return;

        Transform atual = painelAtual.transform;
        while (atual != null)
        {
            if (!atual.gameObject.activeSelf)
            {
                atual.gameObject.SetActive(true);
            }
            atual = atual.parent;
        }
    }

    private void OnAtributosGlobaisMudaram(Dictionary<string, int> _)
    {
        if (etapas == null || etapas.Count == 0)
        {
            return;
        }

        AtualizarItensDeProgresso(indiceEtapaAtual);
    }

    /// <summary>
    /// Ativa o seletor de imagem do token
    /// </summary>
    public void AtivarSeletorImagem()
    {
        if (tokenImageSelector != null)
        {
            tokenImageSelector.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[CriacaoFichaProgressaoDnd] TokenImageSelector não foi atribuído no inspector.");
        }
    }

    /// <summary>
    /// Desativa o seletor de imagem do token
    /// </summary>
    public void DeativarSeletorImagem()
    {
        if (tokenImageSelector != null)
        {
            tokenImageSelector.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Abre automaticamente o diálogo de seleção de imagem
    /// </summary>
    public void AbrirSeletorImagem()
    {
        if (tokenImageSelector != null)
        {
            tokenImageSelector.gameObject.SetActive(true);
            tokenImageSelector.AbrirSelecionarImagem();
        }
        else
        {
            Debug.LogWarning("[CriacaoFichaProgressaoDnd] TokenImageSelector não foi atribuído no inspector.");
        }
    }

    /// <summary>
    /// Atualiza a UI de progressão (chamado por RaceSelector/ClassSelector após aplicar bônus)
    /// </summary>
    public void RefreshProgressaoUI()
    {
        Debug.Log($"[CriacaoFichaProgressaoDnd] RefreshProgressaoUI() chamado - Bônus atuais: Raça={string.Join(",", GlobalFichaData.bonusRacaDict.Values)}, Classe={string.Join(",", GlobalFichaData.bonusClasseDict.Values)}");
        
        if (etapas != null && etapas.Count > 0)
        {
            AtualizarItensDeProgresso(indiceEtapaAtual);
            Debug.Log("[CriacaoFichaProgressaoDnd] ✓ UI de Progressão atualizada via RefreshProgressaoUI()");
        }
        else
        {
            Debug.LogWarning("[CriacaoFichaProgressaoDnd] RefreshProgressaoUI() - etapas é null ou vazia!");
        }
    }

    /// <summary>
    /// Overload para atualizar a UI quando o ouro muda (callback do evento OnOuroMudou)
    /// </summary>
    public void RefreshProgressaoUI(float ouroAtualizado)
    {
        // Ignorar o valor do parâmetro, apenas usar como trigger para atualizar
        RefreshProgressaoUI();
    }

    /// <summary>
    /// Retorna o componente TokenImageSelector se disponível
    /// </summary>
    public TokenImageSelector ObterSeletorImagem()
    {
        if (tokenImageSelector == null)
        {
            Debug.LogWarning("[CriacaoFichaProgressaoDnd] ⚠ tokenImageSelector NÃO foi atribuído no Inspector!");
            Debug.LogWarning("[CriacaoFichaProgressaoDnd] Procure por CriacaoFichaProgressaoDnd no cenário e arraste TokenImageSelector para o campo");
        }
        else
        {
            Debug.Log("[CriacaoFichaProgressaoDnd] ✓ TokenImageSelector encontrado e disponível");
        }
        
        return tokenImageSelector;
    }

    /// <summary>
    /// Tenta carregar dados pré-salvos de uma ficha em criação
    /// Usado quando o usuário volta para continuar a criar uma ficha incompleta
    /// </summary>
    private void TentarCarregarDadosPresalvos()
    {
        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        
        LocalFichaData snapshot;
        if (!LocalFichaStorage.TryGetFichaSnapshot(steamId, playerId, GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, out snapshot))
        {
            Debug.Log("[CriacaoFichaProgressaoDnd] Nenhum snapshot encontrado. Iniciando nova criação.");
            return;
        }

        if (snapshot == null)
        {
            Debug.Log("[CriacaoFichaProgressaoDnd] Snapshot é nulo. Iniciando nova criação.");
            return;
        }

        Debug.Log($"[CriacaoFichaProgressaoDnd] Carregando ficha pré-salva: {snapshot.name_pc} (ID: {snapshot.fichas_dnd_id})");

        // Restaurar dados globais
        GlobalFichaData.nomePersonagem = snapshot.name_pc;
        GlobalFichaData.levelPersonagem = snapshot.level_pc;
        GlobalFichaData.racaPersonagem   = snapshot.raca?.raca_nome ?? "";
        GlobalFichaData.classePersonagem = snapshot.classes?.PrimaryClass()?.class_name ?? "";
        GlobalFichaData.metodoAtributos = snapshot.metodo_atributos;

        // Restaurar atributos - NÃO resetar para 10 se GlobalFichaData já tem valores (carregados pelo AttributeManager)
        if (snapshot.atributos.forca > 0 || snapshot.atributos.destreza > 0)
        {
            // Se snapshot tem dados, usar snapshot. Se snapshot é 0 MAS GlobalFichaData tem dados, manter GlobalFichaData.
            // Só usar 10 se AMBOS sejam 0 (atributos nunca foram rolados)
            GlobalFichaData.forca = snapshot.atributos.forca > 0 ? snapshot.atributos.forca : (GlobalFichaData.forca > 0 ? GlobalFichaData.forca : 10);
            GlobalFichaData.destreza = snapshot.atributos.destreza > 0 ? snapshot.atributos.destreza : (GlobalFichaData.destreza > 0 ? GlobalFichaData.destreza : 10);
            GlobalFichaData.constituicao = snapshot.atributos.constituicao > 0 ? snapshot.atributos.constituicao : (GlobalFichaData.constituicao > 0 ? GlobalFichaData.constituicao : 10);
            GlobalFichaData.inteligencia = snapshot.atributos.inteligencia > 0 ? snapshot.atributos.inteligencia : (GlobalFichaData.inteligencia > 0 ? GlobalFichaData.inteligencia : 10);
            GlobalFichaData.sabedoria = snapshot.atributos.sabedoria > 0 ? snapshot.atributos.sabedoria : (GlobalFichaData.sabedoria > 0 ? GlobalFichaData.sabedoria : 10);
            GlobalFichaData.carisma = snapshot.atributos.carisma > 0 ? snapshot.atributos.carisma : (GlobalFichaData.carisma > 0 ? GlobalFichaData.carisma : 10);
            
            Debug.Log($"[CriacaoFichaProgressaoDnd] Atributos restaurados (preservando GlobalFichaData se snapshot vazio): FOR={GlobalFichaData.forca}, DEX={GlobalFichaData.destreza}, CON={GlobalFichaData.constituicao}, INT={GlobalFichaData.inteligencia}, SAB={GlobalFichaData.sabedoria}, CAR={GlobalFichaData.carisma}");
        }

        // classInfoJson legado: derivado de classes[primary] se disponível
        if (snapshot.classes != null && snapshot.classes.Count > 0)
        {
            var pCl = snapshot.classes.PrimaryClass();
            if (pCl != null)
            {
                var det = new ClassSelector.DetalhesUpdate
                {
                    classe_info = new ClassSelector.ClasseInfoData
                    {
                        subclass_id = pCl.subclass_id ?? "", subclass_name = pCl.subclass_name ?? "",
                        saves = pCl.saves ?? new System.Collections.Generic.List<string>(),
                        skills = pCl.skills ?? new System.Collections.Generic.List<string>(),
                        armors = pCl.armors ?? new System.Collections.Generic.List<string>(),
                        weapons = pCl.weapons ?? new System.Collections.Generic.List<string>(),
                        equipment = pCl.equipment ?? new System.Collections.Generic.List<string>(),
                        asi_feats = pCl.asi_feats ?? new System.Collections.Generic.List<string>(),
                    }
                };
                GlobalFichaData.classInfoJson = JsonUtility.ToJson(det);
            }
        }

        if (!string.IsNullOrWhiteSpace(snapshot.background_info_json) && snapshot.background_info_json != "{}")
            GlobalFichaData.backgroundInfoJson = snapshot.background_info_json;

        // Restaurar vida — necessário para que SaveClassInfo não escreva vida_maxima = 0 ao editar
        if (snapshot.vida_maxima > 0)
            GlobalFichaData.vidaPersonagem = snapshot.vida_maxima.ToString();

        // Restaurar imagem do token se existir
        if (!string.IsNullOrWhiteSpace(snapshot.token_image_path))
            GlobalFichaData.imagemTokenPath = snapshot.token_image_path;

        // Determinar qual é a primeira etapa incompleta e navegar para lá
        int etapaParaCarregar = 0;
        
        // Verificar qual etapa pode ser pulada
        if (!string.IsNullOrWhiteSpace(snapshot.atributos_json) && snapshot.atributos_json != "{}")
            etapaParaCarregar = 1; // Próxima: Raça
        
        if (snapshot.raca != null && !string.IsNullOrEmpty(snapshot.raca.raca_id))
            etapaParaCarregar = 2; // Próxima: Classe

        if (snapshot.classes != null && snapshot.classes.Count > 0)
            etapaParaCarregar = 3; // Próxima: Antecedente
        
        if (!string.IsNullOrWhiteSpace(snapshot.background_id))
            etapaParaCarregar = 4; // Próxima: Resumo/Finalização

        if (GlobalFichaData.modoSubirNivel)
        {
            // Localiza dinamicamente a etapa de Classe para ir direto a ela
            int indiceClasse = etapas.FindIndex(e => e != null && e.tipoResumo == TipoResumoEtapaDnd.Classe);
            if (indiceClasse < 0) indiceClasse = 2; // fallback se não encontrar

            etapaParaCarregar = indiceClasse;
            maiorEtapaAlcancada = etapas.Count - 1; // libera navegação em todas as etapas
            GlobalFichaData.modoSubirNivel = false;

            Debug.Log($"[CriacaoFichaProgressaoDnd] Modo subir nível — indo para etapa de Classe (índice {indiceClasse})");
        }
        else
        {
            // Tenta recuperar a aba exata do PlayerPrefs (salvo durante sessão anterior)
            int savedTab = PlayerPrefs.GetInt($"Ficha_{GlobalFichaData.fichasDndId}_UltimaAba", etapaParaCarregar);
            etapaParaCarregar = Mathf.Max(etapaParaCarregar, savedTab);
            Debug.Log($"[CriacaoFichaProgressaoDnd] Carregando na etapa: {etapaParaCarregar} (PlayerPrefs={savedTab})");
        }

        // Atualizar maior etapa alcançada para permitir navegação
        maiorEtapaAlcancada = Mathf.Max(maiorEtapaAlcancada, etapaParaCarregar);
        indiceEtapaAtual    = etapaParaCarregar;
    }

    /// <summary>
    /// Tenta salvar automaticamente os dados da ficha quando a tela é desativada
    /// Garante que os dados não sejam perdidos se o usuário sair da criação
    /// </summary>
    private void TentarSalvarAutomaticamente()
    {
        if (GlobalFichaData.fichasDndId <= 0) return;

        // FichaSaveManager.OnDisable já chama SalvarLocalSincronamente(),
        // mas chamamos explicitamente aqui como garantia extra (ordem de OnDisable entre irmãos é indeterminada).
        // Usamos SalvarLocalSincronamente porque StartCoroutine não funciona em OnDisable de objetos sendo desativados.
        if (FichaSaveManager.Instance != null)
        {
            Debug.Log($"[CriacaoFichaProgressaoDnd] OnDisable — auto-save síncrono para fichaId={GlobalFichaData.fichasDndId}");
            FichaSaveManager.Instance.SalvarLocalSincronamente();
        }
        else
        {
            Debug.LogWarning("[CriacaoFichaProgressaoDnd] FichaSaveManager.Instance não encontrado — auto-save não executado.");
        }
    }
}
