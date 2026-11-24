using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using System;

[DisallowMultipleComponent]
public class FichaCreator : MonoBehaviour
{
    [Header("Dropdowns")]
    public TMP_Dropdown sistemaDropdown;
    public TMP_Dropdown levelDropdown;

    [Header("Inputs")]
    public TMP_InputField nomeInput;

    [Header("Atributos")]
    public TMP_Dropdown atributosDropdown;

    [Header("UI (opcionais)")]
    public TMP_Text feedbackText;
    public Button   criarButton;
    public Toggle   multiclasseToggle;

    [Header("Telas")]
    public GameObject TelaFichas;
    public GameObject TelaCriarFichasDnd;
    public GameObject TelaCriarFichasCyberpunk;
    public GameObject TelaCriarFichasDesbravador;

    [Header("Config")]
    public float feedbackDuration = 3f;

    private Coroutine feedbackCoroutine;
    private FichaEditorManager editorManager;

    private void Start()
    {
        editorManager = FichaEditorManager.Instance;

        if (feedbackText != null && feedbackText.gameObject.activeSelf)
            feedbackText.gameObject.SetActive(false);

        if (criarButton != null)
        {
            criarButton.onClick.RemoveAllListeners();
            criarButton.onClick.AddListener(CriarFicha);
        }

        if (multiclasseToggle != null)
        {
            multiclasseToggle.onValueChanged.RemoveAllListeners();
            multiclasseToggle.onValueChanged.AddListener(ativo => GlobalFichaData.modoMulticlasse = ativo);
        }
    }

    private void OnEnable()
    {
        if (editorManager == null)
            editorManager = FichaEditorManager.Instance;

        if (editorManager != null && editorManager.IsEditMode)
        {
            // Modo Edição: preenche os campos
            if (nomeInput != null)
                nomeInput.text = GlobalFichaData.nomePersonagem;

            if (levelDropdown != null)
            {
                int levelIndex = -1;
                for (int i = 0; i < levelDropdown.options.Count; i++)
                {
                    string[] parts = levelDropdown.options[i].text.Split(' ');
                    foreach (var part in parts)
                    {
                        if (int.TryParse(part, out int parsedLevel) && parsedLevel == GlobalFichaData.levelPersonagem)
                        {
                            levelIndex = i;
                            break;
                        }
                    }
                    if (levelIndex != -1) break;
                }

                if (levelIndex != -1)
                {
                    levelDropdown.value = levelIndex;
                }
                else
                {
                    levelDropdown.value = 0; // Fallback
                }
                levelDropdown.RefreshShownValue();
            }
        }
        else
        {
            // Modo Criação: limpa os campos e ativa multiclasse por padrão
            if (nomeInput != null) nomeInput.text = "";
            if (levelDropdown != null) { levelDropdown.value = 0; levelDropdown.RefreshShownValue(); }
            GlobalFichaData.modoMulticlasse = true;
        }

        // Sincroniza o toggle com o estado atual
        if (multiclasseToggle != null)
            multiclasseToggle.SetIsOnWithoutNotify(GlobalFichaData.modoMulticlasse);
    }

    public void CriarFicha()
    {
        StartCoroutine(RotinaCriarFicha());
    }

    private IEnumerator RotinaCriarFicha()
    {
        if (editorManager != null && editorManager.IsEditMode)
        {
            yield return StartCoroutine(RotinaUpdateFichaDndLocal());
            yield break;
        }

        try
        {
            if (sistemaDropdown == null || sistemaDropdown.options.Count == 0)
            {
                PopupManager.Instance?.MostrarErro("Erro de configuração", "Dropdown de sistema não encontrado.");
                yield break;
            }

            string rawText = sistemaDropdown.options[sistemaDropdown.value].text;
            Debug.Log($"[FichaCreator] Texto selecionado no dropdown: '{rawText}'");
            GlobalFichaData.sistemaNome = string.IsNullOrWhiteSpace(rawText) ? "D&D" : rawText.Trim();

            int idSistema = MapSistemaToId(rawText);
            Debug.Log($"[FichaCreator] ID sistema detectado: {idSistema}");

            if (idSistema == 0)
            {
                PopupManager.Instance?.MostrarErro("Sistema inválido", $"Sistema não reconhecido: '{rawText}'");
                yield break;
            }

            if (!ValidateFieldsForSystem(idSistema))
                yield break;
        }
        catch (Exception ex)
        {
            PopupManager.Instance?.MostrarErro("Erro inesperado", ex.Message);
            Debug.LogException(ex);
            yield break;
        }

        // Mostra loading e aguarda um frame para renderizar antes do save síncrono
        PopupManager.Instance?.MostrarCarregamento("Criando ficha", "Salvando personagem...");
        yield return null;

        try
        {
            string rawText = sistemaDropdown.options[sistemaDropdown.value].text;
            int idSistema = MapSistemaToId(rawText);

            if (idSistema == 1)
            {
                string nomePersonagem = nomeInput != null ? nomeInput.text.Trim() : "";
                int level = 1;

                if (levelDropdown != null && levelDropdown.options.Count > 0)
                {
                    string levelText = levelDropdown.options[levelDropdown.value].text;
                    foreach (string part in levelText.Split(' '))
                        if (int.TryParse(part, out int parsed)) { level = parsed; break; }
                }

                string metodoAtributos = atributosDropdown != null && atributosDropdown.options.Count > 0
                    ? atributosDropdown.options[atributosDropdown.value].text : "";

                int playerId = DataManager.Instance.GetCurrentId();
                string steamId = DataManager.Instance.GetCurrentSteamId();
                Debug.Log("[FichaCreator] ID Final usado para criar: " + playerId);

                SaveFichaDndLocal(nomePersonagem, level, playerId, steamId, metodoAtributos);
            }
            else if (idSistema == 4)
            {
                SaveFichaGenerica(nomeInput, levelDropdown, 4, "Cyberpunk", TelaCriarFichasCyberpunk);
            }
            else if (idSistema == 5)
            {
                SaveFichaGenerica(nomeInput, levelDropdown, 5, "Desbravador 2e SRD", TelaCriarFichasDesbravador);
            }
            else
            {
                PopupManager.Instance?.MostrarErro("Sistema não implementado", $"O sistema ID {idSistema} ainda não foi implementado.");
            }
        }
        catch (Exception ex)
        {
            PopupManager.Instance?.MostrarErro("Erro ao criar ficha", ex.Message);
            Debug.LogException(ex);
        }
    }

    private int MapSistemaToId(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;

        string clean = s.ToLower().Replace(" ", "").Replace("&", "").Replace("-", "");

        // D&D / Quinta Edição
        if (clean.Contains("dnd") || clean.Contains("dungeons") || clean.Contains("dragon") || clean == "dd"
            || clean.Contains("quinta") || clean.Contains("5e") || clean.Contains("5edicao") || clean.Contains("5edi"))
            return 1;

        // Pathfinder / Desbravador 2e
        if (clean.Contains("desbravador") || clean.Contains("pathfinder") || clean.Contains("pf2"))
            return 5;

        // Cyberpunk
        if (clean.Contains("cyber") || clean.Contains("2077"))
            return 4;

        return 0;
    }

    private bool ValidateFieldsForSystem(int idSistema)
    {
        Debug.Log($"[FichaCreator] ValidateFields — idSistema={idSistema}, nomeInput={(nomeInput == null ? "NULL" : $"'{nomeInput.text}'")}");

        if (nomeInput == null || string.IsNullOrWhiteSpace(nomeInput.text))
        {
            string sistema = idSistema == 1 ? "D&D" : idSistema == 4 ? "Cyberpunk" : idSistema == 5 ? "Pathfinder" : "do personagem";
            PopupManager.Instance?.MostrarErro("Campo obrigatório", $"Preencha o nome {sistema}.");
            return false;
        }
        return true;
    }

    private IEnumerator RotinaUpdateFichaDndLocal()
    {
        PopupManager.Instance?.MostrarCarregamento("Atualizando ficha", "Salvando alterações...");
        yield return null;

        try
        {
            string nomePersonagem = nomeInput != null ? nomeInput.text.Trim() : "";
            int level = 1;
            if (levelDropdown != null && levelDropdown.options.Count > 0)
            {
                string levelText = levelDropdown.options[levelDropdown.value].text;
                foreach (string part in levelText.Split(' '))
                    if (int.TryParse(part, out int p)) { level = p; break; }
            }

            int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
            string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

            bool updated = LocalFichaStorage.UpdateDndFichaIdentity(
                steamId, playerId,
                editorManager.CurrentGeneralId, editorManager.CurrentDndId,
                nomePersonagem, level
            );

            if (!updated)
            {
                PopupManager.Instance?.MostrarErro("Ficha não encontrada", "Não foi possível localizar a ficha para atualização.");
                yield break;
            }

            GlobalFichaData.nomePersonagem = nomePersonagem;
            GlobalFichaData.levelPersonagem = level;

            PopupManager.Instance?.MostrarSucesso("Ficha atualizada", "As alterações foram salvas com sucesso.",
                onOk: () => DataManager.Instance?.RefreshCurrentData(true));
        }
        catch (Exception ex)
        {
            PopupManager.Instance?.MostrarErro("Erro ao atualizar", ex.Message);
            Debug.LogException(ex);
        }
    }

    private void ShowFeedback(string msg, bool isError)
    {
        if (feedbackCoroutine != null) StopCoroutine(feedbackCoroutine);
        if (feedbackText != null)
        {
            feedbackText.gameObject.SetActive(!string.IsNullOrEmpty(msg));
            feedbackText.text = msg;
            feedbackText.color = isError ? Color.red : Color.green;
            if (!string.IsNullOrEmpty(msg) && feedbackDuration > 0)
                feedbackCoroutine = StartCoroutine(HideFeedbackAfterDelay(feedbackDuration));
        }
        else Debug.Log(msg);
    }

    private IEnumerator HideFeedbackAfterDelay(float d) { yield return new WaitForSeconds(d); if(feedbackText) feedbackText.gameObject.SetActive(false); }

    private void SaveFichaDndLocal(string nome, int level, int playerId, string steamId, string metodoAtributos)
    {
        try
        {
            // Salva ficha anterior sincronamente antes de limpar o estado
            if (GlobalFichaData.fichasDndId > 0)
            {
                Debug.Log($"[FichaCreator] Salvando ficha anterior (id={GlobalFichaData.fichasDndId}) antes de criar nova...");
                FichaSaveManager.Instance?.SalvarLocalSincronamente();
            }

            LocalFichaData localFicha = LocalFichaStorage.SaveFicha(
                steamId, playerId, 1, nome, level,
                metodoAtributos: metodoAtributos);

            // Portão central: reseta TODOS os dados da ficha anterior do GlobalFichaData,
            // incondicionalmente, e só então aponta para a ficha recém-criada — mesmo portão
            // usado por todo o resto do fluxo de criação/edição de ficha.
            Debug.Log("[FichaCreator] Resetando GlobalFichaData para nova ficha...");
            GlobalFichaData.IniciarSessaoParaFicha(localFicha.fichas_general_id, localFicha.fichas_dnd_id);
            GlobalFichaData.metodoAtributos = metodoAtributos;
            GlobalFichaData.nomePersonagem  = nome;
            GlobalFichaData.levelPersonagem = level;
            GlobalFichaData.modoMulticlasse = true; // sempre ON para nova ficha

            // Força os seletores de itens/magias a recarregarem do disco ao abrir
            EquipmentSelector.PrecisaCarregarEquipamentos = true;
            SpellSelector.PrecisaCarregarMagias           = true;

            Debug.Log($"[FichaCreator] Nova ficha criada: generalId={localFicha.fichas_general_id} dndId={localFicha.fichas_dnd_id} nome='{nome}' nivel={level}");

            FichaLogManager.IniciarParaFicha(localFicha.fichas_dnd_id);

            PopupManager.Instance?.Fechar();
            ShowFeedback("Ficha D&D salva localmente com sucesso!", false);
            SetScreenVisibility(TelaFichas, false);
            SetScreenVisibility(TelaCriarFichasDnd, true);

            DataManager.Instance?.RefreshCurrentData(true);

            PostHogTracker.TrackFichaCriada("D&D 5e");
        }
        catch (Exception ex)
        {
            PopupManager.Instance?.MostrarErro("Erro ao salvar ficha", ex.Message);
            Debug.LogException(ex);
        }
    }

    private void SaveFichaGenerica(TMP_InputField nomeInputField, TMP_Dropdown levelDrop, int sistemaId, string sistemaNome, GameObject telaDestino)
    {
        try
        {
            string nomePersonagem = nomeInputField != null ? nomeInputField.text.Trim() : "";
            int level = 1;

            if (levelDrop != null && levelDrop.options.Count > 0)
            {
                string levelText = levelDrop.options[levelDrop.value].text;
                foreach (string part in levelText.Split(' '))
                {
                    if (int.TryParse(part, out int parsed)) { level = parsed; break; }
                }
            }

            int playerId = DataManager.Instance.GetCurrentId();
            string steamId = DataManager.Instance.GetCurrentSteamId();

            LocalFichaData localFicha = LocalFichaStorage.SaveFicha(
                steamId, playerId, sistemaId, nomePersonagem, level);

            // Portão central: mesmo raciocínio de SaveFichaDnd acima — sem isto, atributos/raça/
            // classe etc. da ficha D&D (ou de outro sistema) que estava aberta antes continuavam em
            // GlobalFichaData, vazando pra esta ficha nova.
            GlobalFichaData.IniciarSessaoParaFicha(localFicha.fichas_general_id, localFicha.fichas_dnd_id);
            GlobalFichaData.nomePersonagem  = nomePersonagem;
            GlobalFichaData.levelPersonagem = level;
            GlobalFichaData.sistemaNome     = sistemaNome;

            FichaLogManager.IniciarParaFicha(localFicha.fichas_dnd_id);

            PopupManager.Instance?.Fechar();
            ShowFeedback($"Ficha {sistemaNome} salva localmente com sucesso!", false);
            SetScreenVisibility(TelaFichas, false);
            SetScreenVisibility(telaDestino, true);

            DataManager.Instance?.RefreshCurrentData(true);

            PostHogTracker.TrackFichaCriada(sistemaNome);
        }
        catch (Exception ex)
        {
            PopupManager.Instance?.MostrarErro($"Erro ao salvar {sistemaNome}", ex.Message);
            Debug.LogException(ex);
        }
    }

    private void SetScreenVisibility(GameObject target, bool visible)
    {
        if (target == null) return;

        target.SetActive(visible);

        if (target.TryGetComponent<CanvasGroup>(out CanvasGroup group))
        {
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        if (visible && target.transform is RectTransform rect)
        {
            rect.localScale = Vector3.one;
        }
    }

}

public static class GlobalFichaData
{
    public static string sistemaNome = "Quinta Edição";
    public static string metodoAtributos;
    public static int fichasGeneralId = 0;
    public static int fichasDndId = 0;
    
    public static string nomePersonagem = "";
    public static int levelPersonagem = 1;
    public static string vidaPersonagem = "0";
    public static string racaPersonagem = "";
    public static string classePersonagem = "";

    // IDs para edição (Faltavam estes aqui)
    public static int currentRacaId = 0;
    public static int currentClassId = 0;
    public static int currentAntecedenteId = 0;
    public static string currentSubclassId = "";

    // IDs de restauração: populados ao reabrir ficha incompleta
    public static string raceIdRestauracao = "";
    public static string subraceIdRestauracao = "";
    public static string classIdRestauracao = "";
    public static string subclassIdRestauracao = "";
    public static string backgroundIdRestauracao = "";

    // Sinaliza que os seletores devem reiniciar o fluxo mesmo com fluxoIniciado=true
    public static bool precisaRestaurarFluxo = false;

    // Sinaliza que o fluxo foi aberto via "Subir Nível" — deve ir direto para a etapa de Classe
    public static bool modoSubirNivel = false;

    public static int forca, destreza, constituicao, inteligencia, sabedoria, carisma;
    public static int deslocamento, vidaMaxima, vidaAtual;

    public static string detalhesPrincipaisJson = "{}";
    public static string classInfoJson = "{}";
    public static string backgroundInfoJson = "{}";
    // Objeto direto (não mais string escapada) — ver InventoryWrapper/GrimorioWrapper em
    // LocalFichaStorage.cs. Começam null de propósito (igual a "" antes) — é o sinal que os
    // seletores usam pra saber "cache ainda não carregado, ler do disco" (ex:
    // EquipmentSelector.CarregarTudo); um objeto sempre-não-nulo perderia esse sinal, já que um
    // inventário/grimório genuinamente vazio também "pareceria" vazio. slotsMagiasJson fica string
    // (é um dicionário de verdade, JsonUtility não serializa Dictionary<K,V>).
    public static InventoryWrapper inventarioJson = null;
    public static string slotsMagiasJson = "{}";
    public static GrimorioWrapper magiasJson = null;
    
    // Imagem/Token do personagem
    public static string caminhoImagemToken = "";
    public static string imagemTokenPath = "";

    // BÔNUS DE ATRIBUTOS - armazenados durante criação para exibição em progressão
    // Inicializa automaticamente
    public static Dictionary<string, int> bonusRacaDict = new Dictionary<string, int> { 
        { "FOR", 0 }, { "DEX", 0 }, { "CON", 0 }, { "INT", 0 }, { "SAB", 0 }, { "CAR", 0 } 
    };
    public static Dictionary<string, int> bonusClasseDict = new Dictionary<string, int> { 
        { "FOR", 0 }, { "DEX", 0 }, { "CON", 0 }, { "INT", 0 }, { "SAB", 0 }, { "CAR", 0 } 
    };
    public static Dictionary<string, int> bonusAntecedenteDict = new Dictionary<string, int> {
        { "FOR", 0 }, { "DEX", 0 }, { "CON", 0 }, { "INT", 0 }, { "SAB", 0 }, { "CAR", 0 }
    };

    // Bônus provenientes de talentos (stat_increase / bonus de atributo)
    public static Dictionary<string, int> bonusTalentoDict = new Dictionary<string, int> {
        { "FOR", 0 }, { "DEX", 0 }, { "CON", 0 }, { "INT", 0 }, { "SAB", 0 }, { "CAR", 0 }
    };

    // Modificadores de combate e passivas de talentos/antecedentes
    public static int  bonusAtaqueDistancia     = 0;
    public static int  bonusAtaqueMelee         = 0;
    public static int  bonusCA                  = 0;
    public static int  bonusIniciativa          = 0;
    public static bool temProficienciaIniciativa = false;
    public static int  ourosIniciais            = 0;

    // Vantagens condicionais registradas por talentos (para consulta posterior)
    public static System.Collections.Generic.List<VantagemCondicional> vantagensCondicionais =
        new System.Collections.Generic.List<VantagemCondicional>();

    // Magias concedidas por talentos (para consulta pelo SpellSelector)
    public static System.Collections.Generic.List<MagiaTalento> magiasTalentos =
        new System.Collections.Generic.List<MagiaTalento>();

    // Multiclasse
    public static bool modoMulticlasse = false;
    public static System.Collections.Generic.List<MulticlassData> multiclassesSelecionadas =
        new System.Collections.Generic.List<MulticlassData>();

    // Stat increases que dependem de escolha do jogador (ex: "STR_or_DEX")
    public static System.Collections.Generic.List<EscolhaStatIncrease> escolhasStatIncreasePendentes =
        new System.Collections.Generic.List<EscolhaStatIncrease>();

    [System.Serializable]
    public class VantagemCondicional
    {
        public string alvo;
        public string condicao;
        public string fonte;
    }

    [System.Serializable]
    public class MagiaTalento
    {
        public string alvo;   // "cantrips", "level_1_spells", etc.
        public int quantidade;
        public string fonte;
    }

    [System.Serializable]
    public class EscolhaStatIncrease
    {
        public string alvo;  // ex: "STR_or_DEX"
        public int valor;
        public string fonte;
    }

    public static void RegistrarVantagem(string alvo, string condicao, string fonte)
    {
        vantagensCondicionais.Add(new VantagemCondicional { alvo = alvo, condicao = condicao, fonte = fonte });
    }

    public static void RegistrarMagiaTalento(string alvo, int quantidade, string fonte)
    {
        magiasTalentos.Add(new MagiaTalento { alvo = alvo, quantidade = quantidade, fonte = fonte });
    }

    public static void RegistrarEscolhaStatIncrease(string alvo, int valor, string fonte)
    {
        escolhasStatIncreasePendentes.Add(new EscolhaStatIncrease { alvo = alvo, valor = valor, fonte = fonte });
    }

    public static void ResetSelectionIds()
    {
        fichasGeneralId = 0;
        fichasDndId = 0;
    }

    public static void ResetCharacterSnapshot()
    {
        sistemaNome = "Quinta Edição";
        nomePersonagem = "";
        levelPersonagem = 1;
        vidaPersonagem = "0";
        racaPersonagem = "";
        classePersonagem = "";
        metodoAtributos = "";

        currentRacaId = 0;
        currentClassId = 0;
        currentAntecedenteId = 0;
        currentSubclassId = "";

        forca = 0;
        destreza = 0;
        constituicao = 0;
        inteligencia = 0;
        sabedoria = 0;
        carisma = 0;

        deslocamento = 0;
        vidaMaxima = 0;
        vidaAtual = 0;

        detalhesPrincipaisJson = "{}";
        classInfoJson = "{}";
        backgroundInfoJson = "{}";
        inventarioJson = null;
        slotsMagiasJson = "{}";
        magiasJson = null;
        caminhoImagemToken = "";
        imagemTokenPath = "";
        
        // Resetar bônus também
        bonusRacaDict["FOR"] = 0; bonusRacaDict["DEX"] = 0; bonusRacaDict["CON"] = 0;
        bonusRacaDict["INT"] = 0; bonusRacaDict["SAB"] = 0; bonusRacaDict["CAR"] = 0;
        
        bonusClasseDict["FOR"] = 0; bonusClasseDict["DEX"] = 0; bonusClasseDict["CON"] = 0;
        bonusClasseDict["INT"] = 0; bonusClasseDict["SAB"] = 0; bonusClasseDict["CAR"] = 0;
        
        bonusAntecedenteDict["FOR"] = 0; bonusAntecedenteDict["DEX"] = 0; bonusAntecedenteDict["CON"] = 0;
        bonusAntecedenteDict["INT"] = 0; bonusAntecedenteDict["SAB"] = 0; bonusAntecedenteDict["CAR"] = 0;

        bonusTalentoDict["FOR"] = 0; bonusTalentoDict["DEX"] = 0; bonusTalentoDict["CON"] = 0;
        bonusTalentoDict["INT"] = 0; bonusTalentoDict["SAB"] = 0; bonusTalentoDict["CAR"] = 0;

        bonusAtaqueDistancia      = 0;
        bonusAtaqueMelee          = 0;
        bonusCA                   = 0;
        bonusIniciativa           = 0;
        temProficienciaIniciativa = false;
        ourosIniciais             = 0;

        vantagensCondicionais.Clear();
        magiasTalentos.Clear();
        escolhasStatIncreasePendentes.Clear();

        modoSubirNivel = false;
        modoMulticlasse = false;
        multiclassesSelecionadas.Clear();

        // Limpa os marcadores de restauração deixados por CarregarRestauracaoDoSnapshot (chamado ao
        // "Continuar Criação"/"Editar Ficha"/"Subir Nível" numa ficha EXISTENTE). Sem isso, esses
        // campos ficavam presos depois que o jogador saía desse fluxo, e a PRÓXIMA ficha nova e sem
        // relação nenhuma podia herdar silenciosamente a classe (com nível!) da ficha anterior assim
        // que chegasse no passo de Classe (ClassSelector.CriarPasso_1_Classes lê precisaRestaurarFluxo
        // + multiclassesSelecionadas pra decidir se restaura em vez de pedir escolha ao jogador) —
        // sem o jogador nunca ter escolhido nada disso pra ficha nova.
        raceIdRestauracao = "";
        subraceIdRestauracao = ""; // irmã dos outros 4 campos de restauração — mesma classe de bug,
                                    // só tinha ficado de fora da correção anterior
        classIdRestauracao = "";
        subclassIdRestauracao = "";
        backgroundIdRestauracao = "";
        precisaRestaurarFluxo = false;
        RaceSelector.RestauracaoInfo = null;
        ClassSelector.RestauracaoInfo = null;
        ClassSelector.RestauracaoInfoPorClasse.Clear();
        BackgroundSelector.RestauracaoInfo = null;

        // Mesmo motivo acima, mas pra estáticas de OUTRAS classes que também guardam "estado da
        // ficha atual" sem nenhuma auto-detecção de troca de ficha própria (ver
        // TalentosSelector/ProficiencySelector — recebem defesa adicional no próprio OnEnable deles,
        // isto aqui é a camada central que garante que ninguém comece uma ficha nova/diferente com
        // sobra de outra).
        EquipmentSelector.EquipamentosAntecedenteLocal = new System.Collections.Generic.List<string>();
        EquipmentSelector.ImportacaoLocalPendente = false;
        TalentosSelector.NiveisPendentesParaTalento.Clear();
        TalentosSelector.talentosEscolhidosNestaSessao.Clear();
        AttributeManager.CurrentConModifier = 0;
        AttributeManager.HitDieMaximo = 0;
        ProficiencySelector.ResetListasLocais();
    }

    public static void ResetAllForNewFicha()
    {
        ResetSelectionIds();
        ResetCharacterSnapshot();
    }

    /// <summary>Único ponto de entrada sancionado pra começar a trabalhar numa ficha — nova, editar,
    /// subir nível, continuar rascunho, ou trocar pra uma ficha diferente. SEMPRE reseta primeiro,
    /// incondicionalmente, e só depois define os IDs da ficha alvo — nunca o contrário. Chamadores
    /// que precisam restaurar dados de uma ficha existente (CarregarRestauracaoDoSnapshot) devem
    /// chamar isso ANTES da restauração, não evitar o reset como o código antigo fazia (ver
    /// FichaLoaderUI.cs, comentário histórico sobre reset e restauração serem "mutuamente
    /// exclusivos" — não são: reset limpa, restauração sobrescreve por cima do estado limpo).
    /// Dispara OnSessaoDeFichaIniciada pra que componentes como AutoSaveAttributesManager recarreguem
    /// na hora, sem depender de OnEnable/Start (que não redisparam se a tela já estava ativa).</summary>
    public static void IniciarSessaoParaFicha(int novoFichasGeneralId, int novoFichasDndId)
    {
        ResetCharacterSnapshot();
        fichasGeneralId = novoFichasGeneralId;
        fichasDndId = novoFichasDndId;
        OnSessaoDeFichaIniciada?.Invoke();
    }

    /// <summary>Disparado sempre que IniciarSessaoParaFicha roda — ou seja, toda vez que "qual ficha
    /// está ativa" muda, incluindo o reset completo que acabou de acontecer. Quem precisa recarregar
    /// dados próprios ao trocar de ficha (e não pode confiar em OnEnable/Start, já que SetActive(true)
    /// numa tela já ativa não redispara isso) deve assinar este evento.</summary>
    public static event Action OnSessaoDeFichaIniciada;
}