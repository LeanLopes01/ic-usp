using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;

public class FichaSaveManager : MonoBehaviour
{
    public static FichaSaveManager Instance { get; private set; }

    [Header("Referências dos Seletores (Abas)")]
    public RaceSelector raceSelector;
    public ClassSelector classSelector;
    public BackgroundSelector backgroundSelector;
    public AttributeManager attributeManager;
    public ProficiencySelector proficiencySelector;
    
    // --- NOVOS SELETORES ADICIONADOS ---
    public EquipmentSelector equipmentSelector;
    public SpellSelector spellSelector;

    [Header("UI Global")]
    public Button botaoSalvarTudo;
    public TMP_Text feedbackGlobalText;

    [Header("Navegação ao Finalizar")]
    public bool abrirTelaFichaAoFinalizar = true;
    public GameObject telaCriacaoFichaDnd;
    public GameObject telaFichaDnd;

    // Flag: indica que o popup de confirmação aberto é para finalizar a ficha
    private bool _aguardandoConfirmacaoFinalizar = false;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (proficiencySelector == null)
            proficiencySelector = FindObjectOfType<ProficiencySelector>(true);

        if (botaoSalvarTudo)
            botaoSalvarTudo.onClick.AddListener(OnClickSalvarTudo);
    }

    /// <summary>
    /// Reexibe o botão "Finalizar"/feedback global escondidos ao concluir a criação anterior.
    /// FichaSaveManager é um singleton persistente na tela de criação — sem isso, o botão
    /// continua invisível ao criar uma nova ficha ou reabrir uma para edição na mesma sessão.
    /// Chamado por CriacaoFichaProgressaoDnd.OnEnable() sempre que a tela de criação é reaberta.
    /// </summary>
    public void ResetarBotaoFinalizarESalvar()
    {
        if (botaoSalvarTudo != null)
        {
            botaoSalvarTudo.gameObject.SetActive(true);
            botaoSalvarTudo.interactable = true;
        }
        feedbackGlobalText?.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        PopupUI.AoConfirmar += OnPopupConfirmado;
        PopupUI.AoCancelar  += OnPopupCancelado;
    }

    private void OnDisable()
    {
        PopupUI.AoConfirmar -= OnPopupConfirmado;
        PopupUI.AoCancelar  -= OnPopupCancelado;

        // Auto-save ao sair da tela de criação (save síncrono, sem UI)
        if (GlobalFichaData.fichasDndId > 0)
            SalvarLocalSincronamente();
    }

    /// <summary>
    /// Salva o snapshot JSON da ficha de forma síncrona — sem corrotinas nem UI.
    /// Usado pelo OnDisable para persistir o estado atual ao sair da tela de criação.
    /// </summary>
    public void SalvarLocalSincronamente()
    {
        if (GlobalFichaData.fichasDndId <= 0) return;

        // Salva dados de classe se o ClassSelector tiver uma seleção ativa
        classSelector?.SalvarDadosClasseSincronamente();

        // Exporta o snapshot JSON completo para disco
        ExportarJsonCompletoFicha(out _);
        PersistirImagemTokenNoSnapshot();
    }

    // Chamado pelo evento estático do PopupUI quando qualquer confirmação ocorre
    private void OnPopupConfirmado()
    {
        if (!_aguardandoConfirmacaoFinalizar) return;
        _aguardandoConfirmacaoFinalizar = false;
        StartCoroutine(ProcessoSalvarTudo());
    }

    private void OnPopupCancelado()
    {
        _aguardandoConfirmacaoFinalizar = false;
    }

    private void OnClickSalvarTudo()
    {
        if (GlobalFichaData.fichasDndId <= 0)
        {
            PopupManager.Instance?.MostrarErro("Ficha não encontrada", "Nenhuma ficha foi criada ainda.");
            return;
        }

        var incompletos = VerificarItensIncompletos();
        string mensagem = incompletos.Count > 0
            ? $"Os seguintes itens ainda não foram preenchidos:\n\n• {string.Join("\n• ", incompletos)}\n\nDeseja finalizar mesmo assim?"
            : "Todos os itens estão preenchidos.\nDeseja finalizar e salvar a ficha?";
        string labelSim = incompletos.Count > 0 ? "Finalizar mesmo assim" : "Finalizar";

        _aguardandoConfirmacaoFinalizar = true;
        // onSim/onNao apenas fecham o popup; o evento AoConfirmar/AoCancelar faz o restante
        PopupManager.Instance?.MostrarConfirmacao(
            "Finalizar criação", mensagem,
            onSim: null,
            onNao: null,
            labelSim: labelSim,
            labelNao: "Voltar"
        );
    }

    private System.Collections.Generic.List<string> VerificarItensIncompletos()
    {
        var lista = new System.Collections.Generic.List<string>();

        if (string.IsNullOrWhiteSpace(GlobalFichaData.racaPersonagem) || GlobalFichaData.racaPersonagem == "-")
            lista.Add("Raça não selecionada");

        if (string.IsNullOrWhiteSpace(GlobalFichaData.classePersonagem) || GlobalFichaData.classePersonagem == "-")
            lista.Add("Classe não selecionada");

        if (GlobalFichaData.currentAntecedenteId == 0)
            lista.Add("Antecedente não selecionado");

        bool semAtributos = GlobalFichaData.forca == 0 && GlobalFichaData.destreza == 0
                         && GlobalFichaData.constituicao == 0 && GlobalFichaData.inteligencia == 0
                         && GlobalFichaData.sabedoria == 0 && GlobalFichaData.carisma == 0;
        if (semAtributos)
            lista.Add("Atributos não definidos");

        return lista;
    }

    private IEnumerator ProcessoSalvarTudo()
    {
        if (botaoSalvarTudo != null) botaoSalvarTudo.interactable = false;

        const int totalPassos = 8;
        int passo = 0;

        PopupManager.Instance?.MostrarCarregamento("Salvando ficha", "Iniciando...");
        yield return null; // aguarda o popup renderizar

        // 1. Atributos
        passo++;
        PopupManager.Instance?.AtualizarProgresso(passo / (float)totalPassos, "Salvando atributos...");
        if (attributeManager != null)
            yield return StartCoroutine(attributeManager.SalvarAtributosPublico());

        // 2. Raça
        passo++;
        PopupManager.Instance?.AtualizarProgresso(passo / (float)totalPassos, "Salvando raça...");
        if (raceSelector != null)
            yield return StartCoroutine(raceSelector.SalvarRacaPublico());

        // 3. Classe
        // Resolve a MESMA instância viva que o jogador realmente usou pra escolher a classe — mesmo
        // raciocínio do passo 5 (EquipmentSelector.Instance): a cena pode ter mais de um ClassSelector
        // (confirmado em Desktop Principal.unity: um ativo, outro desativado, sobra de uma versão
        // antiga da tela de etapas) e a referência fixa do Inspector já apontou pro errado, fazendo
        // este passo salvar/reimportar equipamento de classe a partir de um estado vazio — apagando
        // os itens de classe do inventário no Finalizar mesmo com tudo certo durante a criação.
        passo++;
        PopupManager.Instance?.AtualizarProgresso(passo / (float)totalPassos, "Salvando classe...");
        ClassSelector classSelAtivo = ClassSelector.Instance != null ? ClassSelector.Instance : (classSelector != null ? classSelector : FindObjectOfType<ClassSelector>(true));
        if (classSelAtivo != null)
            yield return StartCoroutine(classSelAtivo.SalvarClassePublico());

        // 4. Antecedente
        passo++;
        PopupManager.Instance?.AtualizarProgresso(passo / (float)totalPassos, "Salvando antecedentes...");
        if (backgroundSelector != null)
            yield return StartCoroutine(backgroundSelector.SalvarAntecedentePublico());

        // 5. Inventário
        // Resolve a MESMA instância viva que ClassSelector/BackgroundSelector usaram para
        // importar os itens, via ResolveInstanciaPrincipal() — nunca a instância delegada
        // (embutida no painel "Adicionar Item" de FichaGeralManager), cujo RotinaSalvarInventario
        // é sempre um no-op. Achado real: com o FindObjectOfType sem filtro daqui, este passo podia
        // "salvar com sucesso" (nenhum erro, log normal) usando a instância delegada — itens de
        // classe/antecedente/compra apareciam certinho durante a criação, mas o inventário final
        // da ficha ia pro disco vazio, porque a delegada nunca escreve nada (ver
        // EquipmentSelector.ResolveInstanciaPrincipal).
        passo++;
        PopupManager.Instance?.AtualizarProgresso(passo / (float)totalPassos, "Salvando inventário...");
        EquipmentSelector equipSelAtivo = EquipmentSelector.ResolveInstanciaPrincipal();
        if (equipSelAtivo != null)
        {
            VTTLog.Save($"[DIAG] FichaSaveManager passo 5: salvando inventário via instanceId={equipSelAtivo.GetInstanceID()}");
            yield return StartCoroutine(equipSelAtivo.RotinaSalvarInventario());
        }

        // 6. Proficiências
        passo++;
        PopupManager.Instance?.AtualizarProgresso(passo / (float)totalPassos, "Salvando proficiências...");
        if (proficiencySelector != null)
            yield return StartCoroutine(proficiencySelector.SalvarProficienciasComMerge());

        // 7. Magias
        // Mesmo raciocínio dos passos 3 e 5: confirmado em cena real que spellSelector nunca foi
        // arrastado no Inspector (fileID 0) — sem o fallback pra Instance, este passo inteiro era
        // pulado silenciosamente no Finalizar, mesmo com o grimório certo durante a criação.
        passo++;
        PopupManager.Instance?.AtualizarProgresso(passo / (float)totalPassos, "Salvando grimório...");
        SpellSelector spellSelAtivo = SpellSelector.Instance != null ? SpellSelector.Instance : (spellSelector != null ? spellSelector : FindObjectOfType<SpellSelector>(true));
        if (spellSelAtivo != null)
            yield return StartCoroutine(spellSelAtivo.RotinaSalvarMagias());

        // Atualizar todos os FichaDisplayUI com dados frescos antes de exportar e navegar.
        // Isso garante que perícias, salvaguardas e proficiências reflitam os saves acima.
        foreach (var d in FindObjectsByType<FichaDisplayUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (d != null) d.AtualizarTudo();
        yield return null;

        // 8. Exportar JSON completo (lê snapshot atualizado pelos saves anteriores)
        passo++;
        PopupManager.Instance?.AtualizarProgresso(passo / (float)totalPassos, "Gerando arquivo completo...");
        bool exportOk = ExportarJsonCompletoFicha(out string outputPath);

        if (!exportOk)
        {
            PopupManager.Instance?.MostrarErro("Erro ao exportar", "Não foi possível gerar o arquivo completo da ficha.");
            if (botaoSalvarTudo != null) botaoSalvarTudo.interactable = true;
            yield break;
        }

        PersistirImagemTokenNoSnapshot();
        Debug.Log($"[FichaSaveManager] JSON completo exportado em: {outputPath}");

        botaoSalvarTudo?.gameObject.SetActive(false);
        feedbackGlobalText?.gameObject.SetActive(false);

        // Fecha loading e navega diretamente — sem depender de callback de popup
        PopupManager.Instance?.Fechar();
        yield return null;

        if (abrirTelaFichaAoFinalizar) AbrirTelaFichaDnd();
    }

    private void MostrarFeedback(string msg, bool erro)
    {
        if (feedbackGlobalText)
        {
            feedbackGlobalText.text = msg;
            feedbackGlobalText.color = erro ? Color.red : Color.green;
            feedbackGlobalText.gameObject.SetActive(true);
        }
    }

    private bool ExportarJsonCompletoFicha(out string outputPath)
    {
        outputPath = string.Empty;

        try
        {
            int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
            string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

            LocalFichaData snapshot;
            bool hasSnapshot = LocalFichaStorage.TryGetFichaSnapshot(
                steamId, playerId,
                GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId,
                out snapshot);

            string sistemaNome = ResolveSystemName(snapshot);
            string levelDetalhe = ComputeLevelPcDetalhe();
            string multiclasseJson = ComputeMulticlasseJson(snapshot, hasSnapshot);

            bool ok = LocalFichaStorage.UpdateExportFields(
                steamId, playerId,
                GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId,
                ficha =>
                {
                    ficha.exported_at_utc = DateTime.UtcNow.ToString("o");
                    ficha.sistema_nome = sistemaNome;
                    if (!string.IsNullOrWhiteSpace(GlobalFichaData.nomePersonagem))
                        ficha.name_pc = GlobalFichaData.nomePersonagem;
                    if (GlobalFichaData.levelPersonagem > 0)
                        ficha.level_pc = GlobalFichaData.levelPersonagem;
                    ficha.level_pc_detalhe = levelDetalhe;
                    ficha.vida_pc = GlobalFichaData.vidaPersonagem;
                    ficha.deslocamento = GlobalFichaData.deslocamento;
                    if (!string.IsNullOrWhiteSpace(GlobalFichaData.detalhesPrincipaisJson))
                        ficha.detalhes_principais_json = GlobalFichaData.detalhesPrincipaisJson;
                    // multiclasse_json removido — informação já está em ficha.classes
                    if (GlobalFichaData.inventarioJson != null)
                        ficha.inventario_json = GlobalFichaData.inventarioJson;
                    if (!string.IsNullOrWhiteSpace(GlobalFichaData.slotsMagiasJson))
                        ficha.slots_magias_json = GlobalFichaData.slotsMagiasJson;
                    if (GlobalFichaData.magiasJson != null)
                        ficha.magias_json = GlobalFichaData.magiasJson;
                    ficha.vida_atual = GlobalFichaData.vidaAtual;
                    if (!string.IsNullOrWhiteSpace(GlobalFichaData.caminhoImagemToken))
                        ficha.token_image_path = GlobalFichaData.caminhoImagemToken;
                });

            outputPath = LocalFichaStorage.TryGetFichaFilePath(GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId) ?? "";
            return ok;
        }
        catch (Exception ex)
        {
            Debug.LogError("[FichaSaveManager] Falha ao exportar JSON completo: " + ex.Message);
            return false;
        }
    }

    private static string ComputeLevelPcDetalhe()
    {
        if (!GlobalFichaData.modoMulticlasse
            || GlobalFichaData.multiclassesSelecionadas == null
            || GlobalFichaData.multiclassesSelecionadas.Count <= 1)
            return "";

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < GlobalFichaData.multiclassesSelecionadas.Count; i++)
        {
            if (i > 0) sb.Append(" / ");
            var m = GlobalFichaData.multiclassesSelecionadas[i];
            sb.Append(m.className).Append(' ').Append(m.level);
        }
        return sb.ToString();
    }

    private static string ComputeMulticlasseJson(LocalFichaData snapshot, bool hasSnapshot)
    {
        if (GlobalFichaData.multiclassesSelecionadas != null && GlobalFichaData.multiclassesSelecionadas.Count > 0)
        {
            var wrapper = new MulticlassDataWrapper { classes = GlobalFichaData.multiclassesSelecionadas };
            return JsonUtility.ToJson(wrapper);
        }
        // multiclasse_json removido — dados já em ficha.classes[].level
        return "{}";
    }

    private void PersistirImagemTokenNoSnapshot()
    {
        if (string.IsNullOrWhiteSpace(GlobalFichaData.caminhoImagemToken)) return;

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        
        // Copia a imagem para o diretório de fichas
        string caminhoImagemOrigem = GlobalFichaData.caminhoImagemToken;
        string caminhoImagemDestino = CopiarImagemParaPastaFichas(caminhoImagemOrigem);
        
        // Se a cópia foi bem-sucedida, salva o novo caminho
        if (!string.IsNullOrWhiteSpace(caminhoImagemDestino))
        {
            LocalFichaStorage.SaveTokenImagePath(
                steamId,
                playerId,
                GlobalFichaData.fichasGeneralId,
                GlobalFichaData.fichasDndId,
                caminhoImagemDestino
            );
            Debug.Log($"[FichaSaveManager] Caminho da imagem persistido: {caminhoImagemDestino}");
        }
        else
        {
            Debug.LogWarning("[FichaSaveManager] Falha ao copiar imagem para pasta de fichas.");
        }
    }
    
    private string CopiarImagemParaPastaFichas(string caminhoOrigem)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(caminhoOrigem) || !File.Exists(caminhoOrigem))
            {
                Debug.LogWarning($"[FichaSaveManager] Imagem não encontrada: {caminhoOrigem}");
                return "";
            }
            
            // Define pasta de destino local da ficha
            string folderImagens = Path.Combine(LocalFichaStorage.BasePath, "tokens");
            
            Directory.CreateDirectory(folderImagens);
            
            // Define nome do arquivo de imagem
            string nomePersonagem = string.IsNullOrWhiteSpace(GlobalFichaData.nomePersonagem) ? "SemNome" : GlobalFichaData.nomePersonagem;
            string extensao = Path.GetExtension(caminhoOrigem);
            string nomeArquivoImagem = SanitizePathName(nomePersonagem) + "_token" + extensao;
            
            string caminhoDestino = Path.Combine(folderImagens, nomeArquivoImagem);

            // Origem e destino são o mesmo arquivo — já está no lugar certo, nada a fazer
            if (string.Equals(Path.GetFullPath(caminhoOrigem), Path.GetFullPath(caminhoDestino), StringComparison.OrdinalIgnoreCase))
                return caminhoDestino;

            // Copia o arquivo
            File.Copy(caminhoOrigem, caminhoDestino, true);
            
            Debug.Log($"[FichaSaveManager] Imagem copiada com sucesso para: {caminhoDestino}");
            return caminhoDestino;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[FichaSaveManager] Erro ao copiar imagem: {ex.Message}");
            return "";
        }
    }

    private void AbrirTelaFichaDnd()
    {
        if (telaCriacaoFichaDnd != null) telaCriacaoFichaDnd.SetActive(false);
        if (telaFichaDnd != null) telaFichaDnd.SetActive(true);

        DataManager.Instance?.RefreshCurrentData(true);

        // Atualiza todos os blocos de resumo visíveis/invisíveis da cena destino.
        FichaDisplayUI[] displays = FindObjectsByType<FichaDisplayUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var d in displays)
        {
            if (d != null) d.AtualizarTudo();
        }
    }

    private string ResolveSystemName(LocalFichaData snapshot)
    {
        string fromGlobal = GlobalFichaData.sistemaNome;
        if (!string.IsNullOrWhiteSpace(fromGlobal)) return fromGlobal;

        if (snapshot != null)
        {
            if (snapshot.sistema_id == 1) return "D&D";
            if (snapshot.sistema_id == 2) return "Tormenta";
            if (snapshot.sistema_id == 3) return "Cyberpunk";
        }

        return "D&D";
    }

    private string ResolveCharacterName(LocalFichaData snapshot)
    {
        if (!string.IsNullOrWhiteSpace(GlobalFichaData.nomePersonagem)) return GlobalFichaData.nomePersonagem;
        if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.name_pc)) return snapshot.name_pc;
        return "Ficha_Sem_Nome";
    }

    private string SanitizePathName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "SemNome";
        string value = raw.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
            value = value.Replace(invalid[i], '_');

        value = value.Replace('.', '_');
        return string.IsNullOrWhiteSpace(value) ? "SemNome" : value;
    }

    /// <summary>
    /// Salva automaticamente todos os dados da ficha sem aguardar confirmação do usuário
    /// Útil para auto-save quando a tela de criação é desativada
    /// </summary>
    public void SalvarAutomaticamente()
    {
        // Verificar se há uma ficha em criação
        if (GlobalFichaData.fichasDndId <= 0)
        {
            Debug.LogWarning("[FichaSaveManager] SalvarAutomaticamente: Nenhuma ficha_dnd_id ativa.");
            return;
        }

        Debug.Log("[FichaSaveManager] Iniciando auto-save...");

        // Executar a corrotina de salvamento normalmente
        StartCoroutine(ProcessoSalvarTudo());
    }

}