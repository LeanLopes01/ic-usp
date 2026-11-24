using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.UI;
using System.Xml.Linq;
using System.Text;

/// <summary>
/// Carrega lista de subclasses quando o personagem atinge nível 3 e salva como JSON em detalhes_principais
/// </summary>
public class SubclassListLoader : MonoBehaviour
{
    [Header("API")]
    public string apiFichaLevelUrl = "https://docjediwho.com.br/database_vtt/get_ficha_level.php";
    public string apiSaveFichaDetailsUrl = "https://docjediwho.com.br/database_vtt/save_ficha_details.php";

    [Header("UI - Lista")]
    public Transform contentParent;
    public GameObject itemTemplate;
    public string nameTextChild = "nomeSubclasse";
    public string descTextChild = "descSubclasse";

    [Header("UI - Detalhes")]
    public GameObject detalhesPanel;
    public TMP_Text nomeDetailText;
    public TMP_Text descDetailText;
    public TMP_Text featuresText;
    
    [Header("UI - Escolhas de Habilidades")]
    public GameObject habilidadesPanel;
    public Transform habilidadesContentParent;
    public GameObject habilidadeTemplate;
    public TMP_Text habilidadesTitleText;

    [Header("Configuração")]
    public TMP_Text feedbackText;
    public float doubleClickTime = 0.5f;

    // Internos
    [Serializable]
    private class FichaLevel { 
        public int level_pc; 
        public int classes_dnd_id; 
        public string detalhes_principais; // JSON existente
    }
    
    [Serializable]
    private class SubclassData { 
        public string id; 
        public string name; 
        public string summary; 
        public List<SubclassFeature> features;
        public List<SubclassChoice> choices; // Novas escolhas disponíveis
    }

    [Serializable]
    private class SubclassFeature {
        public string id;
        public string name;
        public string description;
        public int level;
        public bool isChoice; // Se é uma habilidade que precisa ser escolhida
        public List<string> options; // Opções disponíveis se for escolha
    }

    [Serializable]
    private class SubclassChoice {
        public string featureId;
        public string featureName;
        public List<string> options;
        public int minChoices;
        public int maxChoices;
    }

    [Serializable]
    private class FichaDetails {
        public SubclasseDetails subclasse;
        public RacialsDetails racionais; // Para habilidades raciais escolhidas
        // Outras seções podem ser adicionadas aqui
    }

    [Serializable]
    private class SubclasseDetails {
        public string id;
        public string nome;
        public List<FeatureChoice> habilidades_escolhidas;
        public DateTime data_escolha;
    }

    [Serializable]
    private class FeatureChoice {
        public string feature_id;
        public string feature_nome;
        public List<string> opcoes_escolhidas;
    }

    [Serializable]
    private class RacialsDetails {
        public List<RacialChoice> habilidades_escolhidas;
    }

    [Serializable]
    private class RacialChoice {
        public string racial_id;
        public string racial_nome;
        public List<string> opcoes_escolhidas;
    }

    private List<SubclassData> subclasses = new List<SubclassData>();
    private SubclassData currentSubclass;
    private string lastClickedSubclassId = "";
    private float lastClickTime = 0f;
    private bool isProcessingDoubleClick = false;
    private int currentClassId = 0;
    private FichaDetails currentFichaDetails;
    private List<SubclassChoice> currentChoices = new List<SubclassChoice>();
    private Dictionary<string, List<string>> selectedOptions = new Dictionary<string, List<string>>();

    void Start()
    {
        if (contentParent == null || itemTemplate == null) {
            Debug.LogError("SubclassListLoader: arraste contentParent e itemTemplate no inspector.");
            return;
        }

        if (detalhesPanel != null)
            detalhesPanel.SetActive(false);

        if (habilidadesPanel != null)
            habilidadesPanel.SetActive(false);

        // Verifica se temos uma ficha criada
        if (GlobalFichaData.fichasDndId <= 0) {
            Debug.LogError("SubclassListLoader: Nenhuma ficha DnD criada anteriormente.");
            ShowFeedback("Erro: Nenhuma ficha DnD criada anteriormente.", true);
            return;
        }

        StartCoroutine(CheckLevelAndLoadSubclasses());
    }

    private IEnumerator CheckLevelAndLoadSubclasses()
    {
        // Busca o nível da ficha, classe atual e detalhes existentes
        string url = apiFichaLevelUrl + "?fichas_dnd_id=" + GlobalFichaData.fichasDndId;
        
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError($"Erro ao buscar nível da ficha: {req.error} - {req.downloadHandler.text}");
                ShowFeedback("Erro ao carregar informações da ficha.", true);
                yield break;
            }

            string json = req.downloadHandler.text;
            FichaLevel fichaInfo = null;
            try {
                fichaInfo = JsonUtility.FromJson<FichaLevel>(json);
            } catch (Exception ex) {
                Debug.LogError("Falha ao desserializar FichaLevel: " + ex.Message + "\nJSON: " + json);
            }

            if (fichaInfo == null) {
                Debug.LogError("Não foi possível obter informações da ficha.");
                ShowFeedback("Erro: Ficha não encontrada.", true);
                yield break;
            }

            // Carrega os detalhes existentes ou cria novos
            if (!string.IsNullOrEmpty(fichaInfo.detalhes_principais)) {
                try {
                    currentFichaDetails = JsonUtility.FromJson<FichaDetails>(fichaInfo.detalhes_principais);
                } catch {
                    currentFichaDetails = new FichaDetails();
                }
            } else {
                currentFichaDetails = new FichaDetails();
            }

            // Verifica se o nível é 3 ou superior
            if (fichaInfo.level_pc < 3) {
                Debug.Log($"Personagem não atingiu o nível 3 ainda. Nível atual: {fichaInfo.level_pc}");
                ShowFeedback($"Personagem não atingiu o nível 3 ainda. Nível atual: {fichaInfo.level_pc}", false);
                yield break;
            }

            currentClassId = fichaInfo.classes_dnd_id;

            // Se atingiu nível 3 e tem uma classe, carrega as subclasses
            if (currentClassId > 0) {
                // Verifica se já tem uma subclasse escolhida
                if (currentFichaDetails.subclasse != null && !string.IsNullOrEmpty(currentFichaDetails.subclasse.id)) {
                    ShowFeedback($"Personagem já possui a subclasse: {currentFichaDetails.subclasse.nome}", false);
                    yield break;
                }

                ShowFeedback("Carregando subclasses disponíveis...", false);
                yield return StartCoroutine(LoadSubclassesForClass(currentClassId));
            } else {
                ShowFeedback("Erro: Personagem não possui uma classe definida.", true);
            }
        }
    }

    private IEnumerator LoadSubclassesForClass(int classId)
    {
        // Busca o XML da classe para extrair as subclasses
        string classUrl = "https://docjediwho.com.br/database_vtt/get_classes.php?id=" + classId;
        
        using (UnityWebRequest req = UnityWebRequest.Get(classUrl))
        {
            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError($"Erro ao buscar classe: {req.error} - {req.downloadHandler.text}");
                ShowFeedback("Erro ao carregar informações da classe.", true);
                yield break;
            }

            string content = req.downloadHandler.text;
            string xml = ExtractXmlFromResponse(content);

            if (string.IsNullOrEmpty(xml)) {
                Debug.LogError("Não foi possível extrair XML da classe.");
                ShowFeedback("Erro: Não foi possível carregar subclasses.", true);
                yield break;
            }

            // Parse do XML para extrair subclasses
            ParseSubclassesFromXml(xml);
            
            if (subclasses.Count > 0) {
                PopulateSubclassList();
                ShowFeedback($"Encontradas {subclasses.Count} subclasses disponíveis.", false);
            } else {
                ShowFeedback("Nenhuma subclasse encontrada para esta classe.", false);
            }
        }
    }

    private string ExtractXmlFromResponse(string response)
    {
        response = response.Trim();
        
        if (response.StartsWith("<")) {
            return response;
        }
        
        try {
            var wrapper = JsonUtility.FromJson<SingleXmlWrapper>(response);
            if (wrapper != null && !string.IsNullOrEmpty(wrapper.xml)) {
                return wrapper.xml;
            }
        } catch {
            // Ignora erros
        }
        
        return response;
    }

    private void ParseSubclassesFromXml(string xml)
    {
        subclasses.Clear();
        
        try {
            XDocument doc = XDocument.Parse(xml);
            XElement classElement = doc.Element("class");
            
            var subclassesElement = classElement.Element("subclasses");
            if (subclassesElement != null) {
                foreach (var subclassElement in subclassesElement.Elements("subclass")) {
                    SubclassData subclass = new SubclassData();
                    subclass.id = subclassElement.Attribute("id")?.Value;
                    subclass.name = subclassElement.Element("name")?.Value;
                    subclass.summary = subclassElement.Element("summary")?.Value;
                    subclass.features = new List<SubclassFeature>();
                    subclass.choices = new List<SubclassChoice>();

                    // Extrai features da subclasse
                    var featuresElement = subclassElement.Element("features");
                    if (featuresElement != null) {
                        foreach (var featureElement in featuresElement.Elements("feature")) {
                            SubclassFeature feature = new SubclassFeature();
                            feature.id = featureElement.Attribute("id")?.Value;
                            feature.name = featureElement.Element("name")?.Value;
                            feature.description = featureElement.Element("description")?.Value;
                            
                            string levelStr = featureElement.Attribute("level")?.Value ?? featureElement.Element("level")?.Value;
                            if (int.TryParse(levelStr, out int level)) {
                                feature.level = level;
                            }
                            
                            // Verifica se é uma feature com escolhas
                            var choicesElement = featureElement.Element("choices");
                            if (choicesElement != null) {
                                feature.isChoice = true;
                                feature.options = new List<string>();
                                
                                foreach (var option in choicesElement.Elements("option")) {
                                    feature.options.Add(option.Value);
                                }
                                
                                // Adiciona como uma escolha
                                SubclassChoice choice = new SubclassChoice();
                                choice.featureId = feature.id;
                                choice.featureName = feature.name;
                                choice.options = feature.options;
                                choice.minChoices = int.Parse(choicesElement.Attribute("min")?.Value ?? "1");
                                choice.maxChoices = int.Parse(choicesElement.Attribute("max")?.Value ?? "1");
                                
                                subclass.choices.Add(choice);
                            }
                            
                            subclass.features.Add(feature);
                        }
                    }
                    
                    subclasses.Add(subclass);
                }
            }
        } catch (Exception ex) {
            Debug.LogError("Erro ao parsear subclasses do XML: " + ex.Message);
        }
    }

    private void PopulateSubclassList()
    {
        // Limpa a lista
        foreach (Transform child in contentParent) {
            if (child.gameObject != itemTemplate)
                Destroy(child.gameObject);
        }

        foreach (SubclassData subclass in subclasses)
        {
            GameObject go = Instantiate(itemTemplate, contentParent);
            go.SetActive(true);

            TMP_Text nameText = go.transform.Find(nameTextChild)?.GetComponent<TMP_Text>();
            TMP_Text descText = go.transform.Find(descTextChild)?.GetComponent<TMP_Text>();

            if (nameText != null) nameText.text = subclass.name ?? "(sem nome)";
            if (descText != null) descText.text = subclass.summary ?? "(sem descrição)";

            // Adiciona listener de clique
            Button btn = go.GetComponentInChildren<Button>();
            if (btn != null) {
                btn.onClick.AddListener(() => OnSubclassClicked(subclass.id, subclass.name));
            }
        }
    }

    private void OnSubclassClicked(string subclassId, string subclassName)
    {
        float currentTime = Time.time;
        if (subclassId == lastClickedSubclassId && (currentTime - lastClickTime) < doubleClickTime)
        {
            // Duplo clique detectado
            if (!isProcessingDoubleClick)
            {
                isProcessingDoubleClick = true;
                OnDoubleClickSubclass(subclassId, subclassName);
            }
        }
        else
        {
            // Clique simples - mostra detalhes
            lastClickedSubclassId = subclassId;
            lastClickTime = currentTime;

            SubclassData subclass = subclasses.Find(s => s.id == subclassId);
            if (subclass != null)
                ShowSubclassDetails(subclass);
        }
    }

    private void OnDoubleClickSubclass(string subclassId, string subclassName)
    {
        Debug.Log($"Duplo clique na subclasse: {subclassId} - {subclassName}");
        
        SubclassData subclass = subclasses.Find(s => s.id == subclassId);
        if (subclass == null) {
            ShowFeedback("Erro: Subclasse não encontrada.", true);
            isProcessingDoubleClick = false;
            return;
        }

        currentSubclass = subclass;
        
        // Verifica se a subclasse tem escolhas a serem feitas
        if (subclass.choices != null && subclass.choices.Count > 0) {
            ShowHabilidadesSelection(subclass);
        } else {
            // Se não tem escolhas, salva diretamente
            SaveSubclassToFicha();
        }
    }

    private void ShowHabilidadesSelection(SubclassData subclass)
    {
        if (habilidadesPanel != null) {
            habilidadesPanel.SetActive(true);
            
            if (habilidadesTitleText != null)
                habilidadesTitleText.text = $"Escolhas para {subclass.name}";
            
            // Limpa a lista de habilidades
            foreach (Transform child in habilidadesContentParent) {
                if (child.gameObject != habilidadeTemplate)
                    Destroy(child.gameObject);
            }

            currentChoices = subclass.choices;
            selectedOptions.Clear();

            // Cria UI para cada escolha
            foreach (var choice in subclass.choices) {
                GameObject go = Instantiate(habilidadeTemplate, habilidadesContentParent);
                go.SetActive(true);

                TMP_Text titleText = go.transform.Find("Title")?.GetComponent<TMP_Text>();
                TMP_Text descText = go.transform.Find("Description")?.GetComponent<TMP_Text>();
                Transform optionsParent = go.transform.Find("Options");

                if (titleText != null) 
                    titleText.text = $"{choice.featureName} (Escolha {choice.minChoices}-{choice.maxChoices})";
                
                if (descText != null) 
                    descText.text = $"Selecione entre {choice.minChoices} e {choice.maxChoices} opções:";

                // Cria botões para cada opção
                if (optionsParent != null && choice.options != null) {
                    foreach (string option in choice.options) {
                        GameObject optionGo = new GameObject("Option", typeof(RectTransform));
                        optionGo.transform.SetParent(optionsParent);
                        
                        // Adiciona componentes para o botão
                        optionGo.AddComponent<Image>();
                        Button optionBtn = optionGo.AddComponent<Button>();
                        TMP_Text optionText = optionGo.AddComponent<TextMeshProUGUI>();
                        
                        optionText.text = option;
                        optionText.alignment = TextAlignmentOptions.Center;
                        optionText.color = Color.black;
                        
                        // Configura o botão
                        optionBtn.onClick.AddListener(() => OnOptionSelected(choice.featureId, option, optionBtn));
                    }
                }
            }
        }
    }

    private void OnOptionSelected(string featureId, string option, Button button)
    {
        if (!selectedOptions.ContainsKey(featureId))
            selectedOptions[featureId] = new List<string>();

        List<string> currentSelections = selectedOptions[featureId];

        if (currentSelections.Contains(option)) {
            // Deseleciona
            currentSelections.Remove(option);
            button.image.color = Color.white;
        } else {
            // Seleciona
            currentSelections.Add(option);
            button.image.color = Color.green;
        }

        // Verifica limites
        SubclassChoice choice = currentChoices.Find(c => c.featureId == featureId);
        if (choice != null && currentSelections.Count > choice.maxChoices) {
            // Remove a seleção mais antiga
            string removed = currentSelections[0];
            currentSelections.RemoveAt(0);
            
            // Atualiza visualmente todos os botões desta feature
            UpdateOptionButtonsVisual(featureId, choice.options);
        }
    }

    private void UpdateOptionButtonsVisual(string featureId, List<string> options)
    {
        List<string> currentSelections = selectedOptions.ContainsKey(featureId) ? selectedOptions[featureId] : new List<string>();
        
        // Percorre todos os botões desta feature e atualiza as cores
        foreach (Transform choiceGroup in habilidadesContentParent) {
            Transform optionsParent = choiceGroup.Find("Options");
            if (optionsParent != null) {
                foreach (Transform optionTransform in optionsParent) {
                    TMP_Text optionText = optionTransform.GetComponent<TMP_Text>();
                    if (optionText != null && options.Contains(optionText.text)) {
                        Button optionBtn = optionTransform.GetComponent<Button>();
                        if (optionBtn != null) {
                            optionBtn.image.color = currentSelections.Contains(optionText.text) ? Color.green : Color.white;
                        }
                    }
                }
            }
        }
    }

    // Chamado por um botão "Confirmar Escolhas" na UI
    public void OnConfirmHabilidades()
    {
        // Valida as escolhas
        foreach (var choice in currentChoices) {
            if (!selectedOptions.ContainsKey(choice.featureId))
                selectedOptions[choice.featureId] = new List<string>();

            List<string> selections = selectedOptions[choice.featureId];
            
            if (selections.Count < choice.minChoices) {
                ShowFeedback($"Erro: Selecione pelo menos {choice.minChoices} opção(ões) para {choice.featureName}", true);
                return;
            }
            
            if (selections.Count > choice.maxChoices) {
                ShowFeedback($"Erro: Selecione no máximo {choice.maxChoices} opção(ões) para {choice.featureName}", true);
                return;
            }
        }

        // Fecha o painel de habilidades e salva
        if (habilidadesPanel != null)
            habilidadesPanel.SetActive(false);

        SaveSubclassToFicha();
    }

    private void SaveSubclassToFicha()
    {
        ShowFeedback($"Salvando subclasse {currentSubclass.name} na ficha...", false);
        
        // Prepara os dados da subclasse
        SubclasseDetails subclassDetails = new SubclasseDetails();
        subclassDetails.id = currentSubclass.id;
        subclassDetails.nome = currentSubclass.name;
        subclassDetails.habilidades_escolhidas = new List<FeatureChoice>();
        subclassDetails.data_escolha = DateTime.Now;

        // Adiciona as escolhas de habilidades
        foreach (var choice in currentChoices) {
            if (selectedOptions.ContainsKey(choice.featureId) && selectedOptions[choice.featureId].Count > 0) {
                FeatureChoice featureChoice = new FeatureChoice();
                featureChoice.feature_id = choice.featureId;
                featureChoice.feature_nome = choice.featureName;
                featureChoice.opcoes_escolhidas = selectedOptions[choice.featureId];
                
                subclassDetails.habilidades_escolhidas.Add(featureChoice);
            }
        }

        // Atualiza os detalhes da ficha
        if (currentFichaDetails.subclasse == null)
            currentFichaDetails.subclasse = subclassDetails;
        else
            currentFichaDetails.subclasse = subclassDetails;

        // Converte para JSON e envia para o servidor
        string jsonData = JsonUtility.ToJson(currentFichaDetails, true);
        StartCoroutine(SaveFichaDetails(jsonData));
    }

    private IEnumerator SaveFichaDetails(string jsonData)
    {
        WWWForm form = new WWWForm();
        form.AddField("fichas_dnd_id", GlobalFichaData.fichasDndId);
        form.AddField("detalhes_principais", jsonData);

        using (UnityWebRequest www = UnityWebRequest.Post(apiSaveFichaDetailsUrl, form))
        {
            yield return www.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
#else
            if (www.isNetworkError || www.isHttpError)
#endif
            {
                string errorDetail = www.downloadHandler?.text ?? "Erro desconhecido";
                Debug.LogError($"Erro ao salvar detalhes da ficha: {www.error} - {errorDetail}");
                ShowFeedback($"Erro ao salvar subclasse: {errorDetail}", true);
            }
            else
            {
                string responseText = www.downloadHandler?.text;
                Debug.Log($"Resposta do servidor: {responseText}");

                try {
                    SaveDetailsResponse response = JsonUtility.FromJson<SaveDetailsResponse>(responseText);
                    if (response != null && response.status.ToLower() == "ok") {
                        ShowFeedback($"✓ Subclasse '{currentSubclass.name}' salva com sucesso!", false);
                        
                        // Fecha o painel de detalhes
                        if (detalhesPanel != null)
                            detalhesPanel.SetActive(false);
                    } else {
                        string errorMsg = response?.message ?? "Erro desconhecido do servidor";
                        ShowFeedback($"Erro: {errorMsg}", true);
                    }
                } catch (Exception ex) {
                    Debug.LogError($"Falha ao processar resposta: {ex.Message}");
                    ShowFeedback("Erro: Resposta inválida do servidor", true);
                }
            }
        }

        isProcessingDoubleClick = false;
    }

    private void ShowSubclassDetails(SubclassData subclass)
    {
        if (detalhesPanel != null)
            detalhesPanel.SetActive(true);

        if (nomeDetailText != null)
            nomeDetailText.text = subclass.name ?? "(sem nome)";

        if (descDetailText != null)
            descDetailText.text = subclass.summary ?? "(sem descrição)";

        if (featuresText != null)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var feature in subclass.features)
            {
                sb.AppendLine($"• {feature.name} (Nível {feature.level})");
                if (!string.IsNullOrEmpty(feature.description))
                    sb.AppendLine($"  {feature.description}\n");
                
                // Mostra opções se for uma escolha
                if (feature.isChoice && feature.options != null) {
                    sb.AppendLine($"  Opções: {string.Join(", ", feature.options)}");
                }
            }
            featuresText.text = sb.ToString().Trim();
        }
    }

    private void ShowFeedback(string message, bool isError)
    {
        if (feedbackText != null)
        {
            feedbackText.text = message;
            feedbackText.color = isError ? Color.red : Color.green;
            feedbackText.gameObject.SetActive(true);
            StartCoroutine(HideFeedbackAfterDelay(3f));
        }
        
        Debug.Log(isError ? $"ERRO: {message}" : $"INFO: {message}");
    }

    private IEnumerator HideFeedbackAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (feedbackText != null)
            feedbackText.gameObject.SetActive(false);
    }

    [Serializable]
    private class SingleXmlWrapper { public string xml; }

    [Serializable]
    private class SaveDetailsResponse
    {
        public string status;
        public string message;
    }
}