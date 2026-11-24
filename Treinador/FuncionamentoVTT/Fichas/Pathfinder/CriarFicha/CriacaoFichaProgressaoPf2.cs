using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using TMPro;

public enum TipoResumoEtapaPf2
{
    Manual,
    Ancestral,
    Classe,
    Historico,
    Atributos,
    Talentos,
    Equipamento,
    NomeENivel,
    Nenhum
}

[System.Serializable]
public class EtapaCriacaoPf2
{
    [Header("Base")]
    public string nomeDaEtapa;
    public GameObject painel;
    public Button botaoContinuar;

    [Header("Resumo do item de progressão")]
    public string tituloNoProgresso;
    public TipoResumoEtapaPf2 tipoResumo = TipoResumoEtapaPf2.Manual;
    [TextArea(2, 5)]
    public string detalhesManuais;

    [NonSerialized] public ItemProgressoCriacaoDndUI itemUIInstanciado;
}

public class CriacaoFichaProgressaoPf2 : MonoBehaviour
{
    [Header("Configuração das Etapas")]
    public List<EtapaCriacaoPf2> etapas;

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

    private int indiceEtapaAtual = 0;
    private int maiorEtapaAlcancada = 0;
    private readonly HashSet<Button> botoesNavegacaoRegistrados = new HashSet<Button>();

    private void OnEnable()
    {
        if (GlobalFichaData.fichasDndId > 0)
        {
            int pid = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
            string sid = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
            if (LocalFichaStorage.TryGetFichaSnapshot(sid, pid,
                GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, out LocalFichaData snap))
            {
                FichaLoaderUI.CarregarRestauracaoDoSnapshot(snap);
            }
        }
    }

    private void OnDisable()
    {
        TentarSalvarAutomaticamente();
    }

    private void Start()
    {
        if (etapas == null || etapas.Count == 0)
        {
            Debug.LogWarning("[CriacaoFichaProgressaoPf2] Nenhuma etapa configurada.");
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

        TentarCarregarDadosPresalvos();
        GlobalFichaData.precisaRestaurarFluxo = false;
        MostrarEtapa(0);
    }

    public void AvancarEtapa()
    {
        if (etapas == null || etapas.Count == 0) return;

        indiceEtapaAtual = Mathf.Clamp(indiceEtapaAtual, 0, etapas.Count - 1);

        if (indiceEtapaAtual < etapas.Count - 1)
        {
            indiceEtapaAtual++;
            if (indiceEtapaAtual > maiorEtapaAlcancada)
                maiorEtapaAlcancada = indiceEtapaAtual;

            MostrarEtapa(indiceEtapaAtual);
        }
        else
        {
            AtualizarItensDeProgresso(indiceEtapaAtual, true);
            Debug.Log("<color=green>[Pf2] Criação de Ficha Finalizada!</color> Salvando dados...");
        }
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
            Debug.LogWarning("[Pf2] Você precisa terminar a etapa atual antes de ir para essa!");
        }
    }

    private void MostrarEtapa(int indiceParaMostrar)
    {
        if (etapas == null || etapas.Count == 0) return;

        indiceParaMostrar = Mathf.Clamp(indiceParaMostrar, 0, etapas.Count - 1);

        for (int i = 0; i < etapas.Count; i++)
        {
            bool ehAtual = (i == indiceParaMostrar);
            if (etapas[i].painel != null)
                etapas[i].painel.SetActive(ehAtual);

            Button botaoNav = etapas[i].itemUIInstanciado != null ? etapas[i].itemUIInstanciado.botaoNavegacao : null;
            if (botaoNav != null)
                botaoNav.interactable = (i <= maiorEtapaAlcancada);
        }

        AtivarHierarquiaDoPainelAtual(etapas[indiceParaMostrar].painel);

        if (botaoContinuarGlobal != null)
        {
            botaoContinuarGlobal.gameObject.SetActive(true);
            botaoContinuarGlobal.interactable = true;
        }

        AtualizarItensDeProgresso(indiceParaMostrar);
        AtualizarTextoProgressoAtual(indiceParaMostrar);
        indiceEtapaAtual = indiceParaMostrar;
    }

    private void CriarItensDeProgressao()
    {
        if (containerProgressao == null || prefabItemProgressao == null)
        {
            Debug.LogWarning("[CriacaoFichaProgressaoPf2] Faltam referências do container ou do prefab de progressão.");
            return;
        }

        if (limparContainerAntesDeCriar)
        {
            for (int i = containerProgressao.childCount - 1; i >= 0; i--)
                Destroy(containerProgressao.GetChild(i).gameObject);
        }

        for (int i = 0; i < etapas.Count; i++)
        {
            EtapaCriacaoPf2 etapa = etapas[i];
            if (etapa == null) continue;

            GameObject itemGO = UIUtils.Spawn(prefabItemProgressao, containerProgressao, false);
            itemGO.SetActive(true);

            ItemProgressoCriacaoDndUI itemUI = itemGO.GetComponent<ItemProgressoCriacaoDndUI>();
            if (itemUI == null)
            {
                Debug.LogWarning("[CriacaoFichaProgressaoPf2] Prefab não possui ItemProgressoCriacaoDndUI.");
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
            if (botao == null || botoesNavegacaoRegistrados.Contains(botao)) continue;

            int indiceCapturado = i;
            botao.onClick.RemoveAllListeners();
            botao.onClick.AddListener(() => PularParaEtapa(indiceCapturado));
            botoesNavegacaoRegistrados.Add(botao);
        }
    }

    private void AtualizarItensDeProgresso(int indiceAtual, bool marcarAtualComoConcluida = false)
    {
        for (int i = 0; i < etapas.Count; i++)
        {
            EtapaCriacaoPf2 etapa = etapas[i];
            if (etapa == null || etapa.itemUIInstanciado == null) continue;

            bool concluida = i < indiceAtual || (marcarAtualComoConcluida && i == indiceAtual);
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

    private string ObterTituloDaEtapa(EtapaCriacaoPf2 etapa)
    {
        if (etapa == null) return string.Empty;
        return string.IsNullOrWhiteSpace(etapa.tituloNoProgresso) ? etapa.nomeDaEtapa : etapa.tituloNoProgresso;
    }

    private string MontarResumoDaEtapa(EtapaCriacaoPf2 etapa)
    {
        if (etapa == null) return string.Empty;

        switch (etapa.tipoResumo)
        {
            case TipoResumoEtapaPf2.Ancestral:
                return MontarResumoAncestral();
            case TipoResumoEtapaPf2.Classe:
                return MontarResumoClasse();
            case TipoResumoEtapaPf2.Historico:
                return MontarResumoHistorico();
            case TipoResumoEtapaPf2.Atributos:
                return MontarResumoAtributos();
            case TipoResumoEtapaPf2.NomeENivel:
                return MontarResumoNomeNivel();
            case TipoResumoEtapaPf2.Talentos:
                return "Talentos configurados";
            case TipoResumoEtapaPf2.Equipamento:
                return "Equipamento configurado";
            case TipoResumoEtapaPf2.Nenhum:
                return string.Empty;
            case TipoResumoEtapaPf2.Manual:
            default:
                return etapa.detalhesManuais ?? string.Empty;
        }
    }

    private string MontarResumoAncestral()
    {
        return string.IsNullOrWhiteSpace(GlobalFichaData.racaPersonagem)
            ? "Ancestral não definido"
            : GlobalFichaData.racaPersonagem;
    }

    private string MontarResumoClasse()
    {
        return string.IsNullOrWhiteSpace(GlobalFichaData.classePersonagem)
            ? "Classe ainda não selecionada."
            : GlobalFichaData.classePersonagem;
    }

    private string MontarResumoHistorico()
    {
        return GlobalFichaData.currentAntecedenteId > 0
            ? "Histórico selecionado"
            : "Histórico ainda não selecionado.";
    }

    private string MontarResumoAtributos()
    {
        bool semDados = GlobalFichaData.forca == 0 && GlobalFichaData.destreza == 0 &&
                        GlobalFichaData.constituicao == 0 && GlobalFichaData.inteligencia == 0 &&
                        GlobalFichaData.sabedoria == 0 && GlobalFichaData.carisma == 0;

        if (semDados) return "Aguardando definição dos atributos.";

        return string.Format("{0} FOR, {1} DEX, {2} CON, {3} INT, {4} SAB, {5} CAR",
            GlobalFichaData.forca, GlobalFichaData.destreza, GlobalFichaData.constituicao,
            GlobalFichaData.inteligencia, GlobalFichaData.sabedoria, GlobalFichaData.carisma);
    }

    private string MontarResumoNomeNivel()
    {
        string nome = string.IsNullOrWhiteSpace(GlobalFichaData.nomePersonagem) ? "Sem nome" : GlobalFichaData.nomePersonagem;
        int nivel = GlobalFichaData.levelPersonagem > 0 ? GlobalFichaData.levelPersonagem : 1;
        return nome + " (Nível " + nivel + ")";
    }

    private void AtualizarTextoProgressoAtual(int indice)
    {
        if (textoProgressoAtual == null || etapas == null || etapas.Count == 0) return;

        int indiceExibicao = Mathf.Clamp(indice + 1, 1, etapas.Count);
        string nomeEtapa = etapas[Mathf.Clamp(indice, 0, etapas.Count - 1)]?.nomeDaEtapa;
        if (string.IsNullOrWhiteSpace(nomeEtapa))
            nomeEtapa = "Etapa " + indiceExibicao;

        textoProgressoAtual.text = string.Format("{0}/{1} - {2}", indiceExibicao, etapas.Count, nomeEtapa);
    }

    private void AtivarHierarquiaDoPainelAtual(GameObject painelAtual)
    {
        if (painelAtual == null) return;

        Transform atual = painelAtual.transform;
        while (atual != null)
        {
            if (!atual.gameObject.activeSelf)
                atual.gameObject.SetActive(true);
            atual = atual.parent;
        }
    }

    public void RefreshProgressaoUI()
    {
        if (etapas != null && etapas.Count > 0)
            AtualizarItensDeProgresso(indiceEtapaAtual);
    }

    public void AbrirSeletorImagem()
    {
        if (tokenImageSelector != null)
        {
            tokenImageSelector.gameObject.SetActive(true);
            tokenImageSelector.AbrirSelecionarImagem();
        }
    }

    private void TentarCarregarDadosPresalvos()
    {
        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        if (!LocalFichaStorage.TryGetFichaSnapshot(steamId, playerId,
            GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, out LocalFichaData snapshot) || snapshot == null)
        {
            Debug.Log("[CriacaoFichaProgressaoPf2] Nenhum snapshot. Iniciando nova criação.");
            return;
        }

        Debug.Log($"[CriacaoFichaProgressaoPf2] Carregando ficha pré-salva: {snapshot.name_pc}");

        GlobalFichaData.nomePersonagem  = snapshot.name_pc;
        GlobalFichaData.levelPersonagem = snapshot.level_pc;
        GlobalFichaData.racaPersonagem   = snapshot.raca?.raca_nome ?? "";
        GlobalFichaData.classePersonagem = snapshot.classes?.PrimaryClass()?.class_name ?? "";

        if (snapshot.atributos.forca > 0 || snapshot.atributos.destreza > 0)
        {
            GlobalFichaData.forca         = snapshot.atributos.forca;
            GlobalFichaData.destreza      = snapshot.atributos.destreza;
            GlobalFichaData.constituicao  = snapshot.atributos.constituicao;
            GlobalFichaData.inteligencia  = snapshot.atributos.inteligencia;
            GlobalFichaData.sabedoria     = snapshot.atributos.sabedoria;
            GlobalFichaData.carisma       = snapshot.atributos.carisma;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.token_image_path))
            GlobalFichaData.imagemTokenPath = snapshot.token_image_path;

        int etapaParaCarregar = 0;
        if (snapshot.raca != null && !string.IsNullOrEmpty(snapshot.raca.raca_id))
            etapaParaCarregar = 1;
        if (snapshot.classes != null && snapshot.classes.Count > 0)
            etapaParaCarregar = 2;
        if (!string.IsNullOrWhiteSpace(snapshot.background_id))
            etapaParaCarregar = 3;

        maiorEtapaAlcancada = Mathf.Max(maiorEtapaAlcancada, etapaParaCarregar);
    }

    private void TentarSalvarAutomaticamente()
    {
        Debug.Log("[CriacaoFichaProgressaoPf2] Tela desativada â€” tentando auto-save...");

        FichaSaveManager saveManager = FindObjectOfType<FichaSaveManager>();
        if (saveManager != null)
            saveManager.SalvarAutomaticamente();
        else
            Debug.LogWarning("[CriacaoFichaProgressaoPf2] FichaSaveManager não encontrado.");
    }
}
