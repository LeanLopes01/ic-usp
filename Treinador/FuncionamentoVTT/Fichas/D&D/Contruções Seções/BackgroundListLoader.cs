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
/// Carrega lista de antecedentes e ao clicar mostra os detalhes (parse do XML) em campos separados.
/// </summary>
public class BackgroundListLoader : MonoBehaviour
{
    [Header("API")]
    [Tooltip("URL completa para get_antecedentes.php")]
    public string apiUrl = "https://docjediwho.com.br/database_vtt/get_antecedentes.php";

    [Tooltip("Opcional: endpoint para buscar 1 antecedente por id. Use {0} como placeholder para id. Se vazio, usará apiUrl?id={0}.")]
    public string apiSingleUrl = "";

    [Header("UI - Lista")]
    public Transform contentParent;      // Content do Scroll View
    public GameObject itemTemplate;      // Template (inativo) que será instanciado
    public string nameTextChild = "nomeAntecedente";   // nome do child com TMP_Text
    public string originTextChild = "origemAntecedente"; // nome do child com TMP_Text

    [Header("UI - Detalhes (preencha no Inspector)")]
    public GameObject detalhesPanel; // painel que será mostrado com os detalhes
    public TMP_Text nomeDetailText;
    public TMP_Text summaryText;
    public TMP_Text metadataText;
    public TMP_Text equipmentText;
    public TMP_Text featureText;
    public TMP_Text personalityText;
    public TMP_Text notesText;

    [Header("Configuração Duplo Clique")]
    public float doubleClickTime = 0.5f; // tempo máximo entre cliques para ser considerado duplo clique
    public TMP_Text feedbackText; // opcional: para mostrar feedback ao usuário

    // internos
    [Serializable]
    private class Background { 
        public int id; 
        public string nome; 
        public string origem; 
        public string xml; 
        public string xml_path;
        public string xml_error;
    }
    
    [Serializable]
    private class BackgroundList { 
        public Background[] backgrounds; 
    }

    // cache local de xml por id
    private Dictionary<string, string> xmlCache = new Dictionary<string, string>();
    
    // Controle de duplo clique
    private string lastClickedBackgroundKey = "";
    private float lastClickTime = 0f;
    private bool isProcessingDoubleClick = false;

    // URL para salvar o antecedente na ficha
    private string saveBackgroundUrl = "https://docjediwho.com.br/database_vtt/save_background_to_ficha.php";

    void Start()
    {
        if (string.IsNullOrEmpty(apiUrl)) {
            Debug.LogError("BackgroundListLoader: apiUrl não definido.");
            return;
        }
        if (contentParent == null || itemTemplate == null) {
            Debug.LogError("BackgroundListLoader: arraste contentParent e itemTemplate no inspector.");
            return;
        }

        if (detalhesPanel != null)
            detalhesPanel.SetActive(false);

        StartCoroutine(LoadBackgroundsCoroutine());
    }

    private IEnumerator LoadBackgroundsCoroutine()
    {
        Debug.Log("=== INICIANDO REQUEST ANTECEDENTES ===");
        Debug.Log($"URL: {apiUrl}");
        
        using (UnityWebRequest req = UnityWebRequest.Get(apiUrl))
        {
            // Adicionar headers manualmente se necessário
            req.SetRequestHeader("Content-Type", "application/json");
            
            yield return req.SendWebRequest();

            Debug.Log($"=== RESPOSTA COMPLETA ===");
            Debug.Log($"Result: {req.result}");
            Debug.Log($"Response Code: {req.responseCode}");
            Debug.Log($"Error: {req.error}");
            Debug.Log($"Download Handler Text: {req.downloadHandler?.text}");
            Debug.Log($"=== FIM RESPOSTA ===");

#if UNITY_2020_1_OR_NEWER
            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError($"ERRO DETALHADO:");
                Debug.LogError($"- URL: {apiUrl}");
                Debug.LogError($"- Código: {req.responseCode}");
                Debug.LogError($"- Erro: {req.error}");
                Debug.LogError($"- Resposta: {req.downloadHandler?.text}");
                
                if (feedbackText != null)
                {
                    feedbackText.text = $"Erro {req.responseCode}: {req.downloadHandler?.text ?? req.error}";
                    feedbackText.color = Color.red;
                    feedbackText.gameObject.SetActive(true);
                }
                yield break;
            }

            string json = req.downloadHandler.text;
            Debug.Log($"JSON recebido (primeiros 500 chars): {Truncate(json, 500)}");
            
            BackgroundList list = null;
            try {
                list = JsonUtility.FromJson<BackgroundList>(json);
            } catch (Exception ex) {
                Debug.LogError("Falha ao desserializar JSON: " + ex.Message);
            }

            if (list == null || list.backgrounds == null) {
                Debug.LogWarning("Nenhum antecedente retornado ou formato inesperado. JSON: " + (json.Length>1000?json.Substring(0,1000)+"...":json));
                
                // Tentar parsear como erro JSON estruturado
                try {
                    var errorResponse = JsonUtility.FromJson<ErrorResponse>(json);
                    if (errorResponse != null && !string.IsNullOrEmpty(errorResponse.message)) {
                        Debug.LogError($"Erro do servidor: {errorResponse.message}");
                        if (feedbackText != null) {
                            feedbackText.text = errorResponse.message;
                            feedbackText.color = Color.red;
                            feedbackText.gameObject.SetActive(true);
                        }
                    }
                } catch {
                    // Ignora se não for um erro formatado
                }
                
                yield break;
            }

            // Limpa filhos pré-existentes (exceto template)
            List<Transform> toDestroy = new List<Transform>();
            foreach (Transform t in contentParent) {
                if (t.gameObject != itemTemplate) toDestroy.Add(t);
            }
            foreach (var t in toDestroy) Destroy(t.gameObject);

            foreach (var b in list.backgrounds)
            {
                GameObject go = Instantiate(itemTemplate, contentParent);
                go.SetActive(true);

                // procura os TMP nas children pelos nomes configurados
                var nameT = go.transform.Find(nameTextChild)?.GetComponent<TextMeshProUGUI>();
                var originT = go.transform.Find(originTextChild)?.GetComponent<TextMeshProUGUI>();

                if (nameT != null) nameT.text = string.IsNullOrEmpty(b.nome) ? "(sem nome)" : b.nome;
                if (originT != null) originT.text = string.IsNullOrEmpty(b.origem) ? "(origem desconhecida)" : b.origem;

                // cache: se o JSON trouxe XML, guardamos localmente
                string key = GetBackgroundKey(b);
                if (!string.IsNullOrEmpty(b.xml))
                {
                    if (!xmlCache.ContainsKey(key))
                        xmlCache[key] = b.xml;
                }

                // Se o item tem um Selectable (Button, Toggle, etc)
                var selectable = go.GetComponent<Selectable>();
                if (selectable != null) {
                    var btn = selectable as Button;
                    if (btn != null) {
                        // captura locais para evitar closure problem
                        string idLocal = key;
                        string xmlLocal = b.xml;
                        string nomeLocal = b.nome;
                        btn.onClick.AddListener(() => OnBackgroundClicked(idLocal, nomeLocal, xmlLocal));
                    }
                } else {
                    // se não tem selectable, tenta associar listener no Button do child
                    var buttonOnChild = go.GetComponentInChildren<Button>(true);
                    if (buttonOnChild != null) {
                        string idLocal = GetBackgroundKey(b);
                        string xmlLocal = b.xml;
                        string nomeLocal = b.nome;
                        buttonOnChild.onClick.AddListener(() => OnBackgroundClicked(idLocal, nomeLocal, xmlLocal));
                    }
                }
            }
        }
    }

    // Gera uma chave string estável para o antecedente
    private string GetBackgroundKey(Background b) {
        // Tenta extrair um ID do XML (atributo id em <background id="...">).
        if (!string.IsNullOrEmpty(b.xml)) {
            try {
                var xdoc = XDocument.Parse(b.xml);
                var backgroundEl = xdoc.Element("background");
                if (backgroundEl != null && backgroundEl.Attribute("id") != null) {
                    return backgroundEl.Attribute("id").Value;
                }
            } catch {
                // ignora parse errors e segue para fallback
            }
        }

        // Fallback: sempre retornar o ID numérico vindo do JSON
        return b.id.ToString();
    }

    // Chamado quando usuário clica no item
    private void OnBackgroundClicked(string backgroundKey, string nomeExibicao, string xmlProvided)
    {
        Debug.Log($"OnBackgroundClicked: key={backgroundKey} nome={nomeExibicao} xmlProvided={(string.IsNullOrEmpty(xmlProvided) ? "no" : "yes")}");

        // Verifica duplo clique
        float currentTime = Time.time;
        if (backgroundKey == lastClickedBackgroundKey && (currentTime - lastClickTime) < doubleClickTime)
        {
            // Duplo clique detectado
            if (!isProcessingDoubleClick)
            {
                isProcessingDoubleClick = true;
                OnDoubleClickBackground(backgroundKey, nomeExibicao);
            }
        }
        else
        {
            // Clique simples - mostra detalhes normalmente
            lastClickedBackgroundKey = backgroundKey;
            lastClickTime = currentTime;

            // Se já temos xml no cache (ou foi enviado no objeto), usa ele.
            if (!string.IsNullOrEmpty(xmlProvided)) {
                ParseAndDisplayXml(xmlProvided, nomeExibicao);
                return;
            }
            if (xmlCache.ContainsKey(backgroundKey) && !string.IsNullOrEmpty(xmlCache[backgroundKey])) {
                ParseAndDisplayXml(xmlCache[backgroundKey], nomeExibicao);
                return;
            }

            // Caso contrário, busca no servidor por id
            if (!string.IsNullOrEmpty(apiSingleUrl)) {
                string url = string.Format(apiSingleUrl, UnityWebRequest.EscapeURL(backgroundKey));
                StartCoroutine(FetchSingleBackgroundAndDisplay(url, backgroundKey, nomeExibicao));
            } else {
                // fallback: tenta apiUrl?id=backgroundKey
                string url = apiUrl + "?id=" + UnityWebRequest.EscapeURL(backgroundKey);
                StartCoroutine(FetchSingleBackgroundAndDisplay(url, backgroundKey, nomeExibicao));
            }
        }
    }

    // Chamado quando detectado duplo clique
    private void OnDoubleClickBackground(string backgroundKey, string backgroundName)
    {
        Debug.Log($"Duplo clique no antecedente: {backgroundKey} - {backgroundName}");
        
        // Verifica se temos uma ficha criada
        if (GlobalFichaData.fichasDndId <= 0)
        {
            ShowFeedback("Erro: Nenhuma ficha DnD criada anteriormente.", true);
            isProcessingDoubleClick = false;
            return;
        }

        ShowFeedback($"Salvando antecedente {backgroundName} na ficha...", false);
        StartCoroutine(SaveBackgroundToFicha(backgroundKey, backgroundName));
    }

    private IEnumerator SaveBackgroundToFicha(string backgroundKey, string backgroundName)
    {
        // Verifica se temos uma ficha válida
        if (GlobalFichaData.fichasDndId <= 0)
        {
            ShowFeedback("Erro: Nenhuma ficha DnD válida encontrada. Crie uma ficha primeiro.", true);
            isProcessingDoubleClick = false;
            yield break;
        }

        WWWForm form = new WWWForm();
        form.AddField("fichas_dnd_id", GlobalFichaData.fichasDndId);
        form.AddField("antecedentes_dnd_id", backgroundKey);

        Debug.Log($"Enviando antecedente para ficha: ficha_dnd_id={GlobalFichaData.fichasDndId}, background_id={backgroundKey}");

        using (UnityWebRequest www = UnityWebRequest.Post(saveBackgroundUrl, form))
        {
            yield return www.SendWebRequest();

            string responseText = www.downloadHandler != null ? www.downloadHandler.text : "";
            Debug.Log($"Resposta completa: {responseText}");

#if UNITY_2020_1_OR_NEWER
            if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
#else
            if (www.isNetworkError || www.isHttpError)
#endif
            {
                // Tenta extrair mensagem de erro da resposta JSON
                string errorDetail = "Erro desconhecido";
                try
                {
                    SaveBackgroundResponse errorResponse = JsonUtility.FromJson<SaveBackgroundResponse>(responseText);
                    if (errorResponse != null && !string.IsNullOrEmpty(errorResponse.message))
                    {
                        errorDetail = errorResponse.message;
                    }
                }
                catch
                {
                    // Se não for JSON, usa o texto da resposta
                    errorDetail = responseText;
                }

                string errorMsg = $"Erro {www.responseCode}: {www.error}\nDetalhes: {errorDetail}";
                Debug.LogError(errorMsg);
                ShowFeedback($"Erro ao salvar antecedente: {errorDetail}", true);
            }
            else
            {
                Debug.Log($"Resposta do servidor: {responseText}");

                try
                {
                    SaveBackgroundResponse response = JsonUtility.FromJson<SaveBackgroundResponse>(responseText);
                    if (response != null && !string.IsNullOrEmpty(response.status) && response.status.ToLower() == "ok")
                    {
                        ShowFeedback($"✓ Antecedente '{backgroundName}' salvo com sucesso!", false);
                        Debug.Log($"Antecedente {backgroundKey} associado à ficha DnD ID: {GlobalFichaData.fichasDndId}");
                    }
                    else
                    {
                        string errorMsg = response?.message ?? "Erro desconhecido do servidor";
                        ShowFeedback($"Erro: {errorMsg}", true);
                    }
                }
                catch (Exception ex)
                {
                    string errorMsg = $"Falha ao processar resposta: {ex.Message}\nResposta: {responseText}";
                    Debug.LogError(errorMsg);
                    ShowFeedback("Erro: Resposta inválida do servidor", true);
                }
            }
        }

        isProcessingDoubleClick = false;
    }

    [Serializable]
    private class SaveBackgroundResponse
    {
        public string status;
        public string message;
    }

    private IEnumerator FetchSingleBackgroundAndDisplay(string url, string backgroundKey, string nomeExibicao)
    {
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            Debug.Log("Buscando antecedente no servidor: " + url);
            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError($"Erro ao buscar antecedente: {req.error} - {req.downloadHandler.text}");
                yield break;
            }

            string content = req.downloadHandler.text;
            // supondo que o endpoint retorne apenas o XML do antecedente (ou um JSON com campo xml). Tentamos detectar:
            string xml = null;
            content = content.Trim();
            if (content.StartsWith("<")) {
                // é XML direto
                xml = content;
            } else {
                // tenta extrair campo "xml" de um JSON simples: {"xml": "<background...>"}
                try {
                    var wrapper = JsonUtility.FromJson<SingleXmlWrapper>(content);
                    if (wrapper != null && !string.IsNullOrEmpty(wrapper.xml)) xml = wrapper.xml;
                } catch { /* ignora */ }
            }

            if (string.IsNullOrEmpty(xml)) {
                Debug.LogWarning("Resposta do servidor não continha XML detectável. Conteúdo: " + Truncate(content, 1000));
                yield break;
            }

            // cacheia e mostra
            xmlCache[backgroundKey] = xml;
            ParseAndDisplayXml(xml, nomeExibicao);
        }
    }

    [Serializable]
    private class SingleXmlWrapper { public string xml; }

    // Faz parse do XML e preenche os campos UI
    private void ParseAndDisplayXml(string xml, string nomeExibicao = null)
    {
        if (detalhesPanel != null)
            detalhesPanel.SetActive(true);

        if (nomeDetailText != null)
            nomeDetailText.text = string.IsNullOrEmpty(nomeExibicao) ? "(sem nome)" : nomeExibicao;

        // limpa campos enquanto parse ocorre
        SetTextSafe(summaryText, "Carregando...");
        SetTextSafe(metadataText, "Carregando...");
        SetTextSafe(equipmentText, "Carregando...");
        SetTextSafe(featureText, "Carregando...");
        SetTextSafe(personalityText, "Carregando...");
        SetTextSafe(notesText, "Carregando...");

        try {
            XDocument doc = XDocument.Parse(xml);
            XElement background = doc.Element("background");

            // Nome: se não veio por parâmetro, tenta pegar do XML
            if (string.IsNullOrEmpty(nomeExibicao))
            {
                var nameElement = background.Element("name");
                if (nameElement != null)
                    nomeDetailText.text = nameElement.Value.Trim();
            }

            // SUMMARY
            var summary = background.Element("summary");
            SetTextSafe(summaryText, summary != null ? summary.Value.Trim() : "(sem resumo)");

            // METADATA
            StringBuilder metaSb = new StringBuilder();
            var metadata = background.Element("metadata");
            if (metadata != null) {
                // Proficiências de habilidades
                var skillProfs = metadata.Element("skill-proficiencies");
                if (skillProfs != null) {
                    metaSb.AppendLine("Proficiências em Habilidades:");
                    foreach (var skill in skillProfs.Elements("skill")) {
                        metaSb.AppendLine($"- {skill.Value.Trim()}");
                    }
                    metaSb.AppendLine();
                }

                // Proficiências de ferramentas
                var toolProfs = metadata.Element("tool-proficiencies");
                if (toolProfs != null) {
                    metaSb.AppendLine("Proficiências em Ferramentas:");
                    foreach (var tool in toolProfs.Elements("tool")) {
                        metaSb.AppendLine($"- {tool.Value.Trim()}");
                    }
                    metaSb.AppendLine();
                }

                // Idiomas
                var languages = metadata.Element("languages");
                if (languages != null) {
                    metaSb.AppendLine("Idiomas:");
                    if (languages.HasElements) {
                        foreach (var lang in languages.Elements("language")) {
                            metaSb.AppendLine($"- {lang.Value.Trim()}");
                        }
                    } else {
                        metaSb.AppendLine("- Nenhum");
                    }
                    metaSb.AppendLine();
                }

                // Riqueza inicial
                var wealth = metadata.Element("starting-wealth");
                if (wealth != null) {
                    metaSb.AppendLine($"Riqueza Inicial: {wealth.Value.Trim()}");
                }
            } else {
                metaSb.AppendLine("(sem metadata)");
            }
            SetTextSafe(metadataText, metaSb.ToString().Trim());

            // EQUIPMENT
            StringBuilder equipSb = new StringBuilder();
            var equipment = background.Element("equipment");
            if (equipment != null) {
                foreach (var item in equipment.Elements("item")) {
                    equipSb.AppendLine($"• {item.Value.Trim()}");
                }
            } else {
                equipSb.AppendLine("(sem equipment)");
            }
            SetTextSafe(equipmentText, equipSb.ToString().Trim());

            // FEATURE
            StringBuilder featureSb = new StringBuilder();
            var feature = background.Element("feature");
            if (feature != null) {
                var featureName = feature.Element("name")?.Value ?? "(sem nome)";
                var featureDesc = feature.Element("description")?.Value ?? "(sem descrição)";
                featureSb.AppendLine($"{featureName}");
                featureSb.AppendLine();
                featureSb.AppendLine($"{featureDesc}");
            } else {
                featureSb.AppendLine("(sem feature)");
            }
            SetTextSafe(featureText, featureSb.ToString().Trim());

            // PERSONALITY
            StringBuilder personalitySb = new StringBuilder();
            var personality = background.Element("personality");
            if (personality != null) {
                // Traços
                var traits = personality.Element("traits");
                if (traits != null) {
                    personalitySb.AppendLine("Traços de Personalidade (escolha um):");
                    foreach (var option in traits.Elements("option")) {
                        personalitySb.AppendLine($"- {option.Value.Trim()}");
                    }
                    personalitySb.AppendLine();
                }

                // Ideais
                var ideals = personality.Element("ideals");
                if (ideals != null) {
                    personalitySb.AppendLine("Ideais (escolha um):");
                    foreach (var option in ideals.Elements("option")) {
                        personalitySb.AppendLine($"- {option.Value.Trim()}");
                    }
                    personalitySb.AppendLine();
                }

                // Vínculos
                var bonds = personality.Element("bonds");
                if (bonds != null) {
                    personalitySb.AppendLine("Vínculos (escolha um):");
                    foreach (var option in bonds.Elements("option")) {
                        personalitySb.AppendLine($"- {option.Value.Trim()}");
                    }
                    personalitySb.AppendLine();
                }

                // Defeitos
                var flaws = personality.Element("flaws");
                if (flaws != null) {
                    personalitySb.AppendLine("Defeitos (escolha um):");
                    foreach (var option in flaws.Elements("option")) {
                        personalitySb.AppendLine($"- {option.Value.Trim()}");
                    }
                }
            } else {
                personalitySb.AppendLine("(sem personality)");
            }
            SetTextSafe(personalityText, personalitySb.ToString().Trim());

            // NOTES
            var notes = background.Element("notes");
            SetTextSafe(notesText, notes != null ? notes.Value.Trim() : "(sem notes)");

        } catch (Exception ex) {
            Debug.LogError("Erro ao parsear XML do antecedente: " + ex.Message + "\nXML: " + Truncate(xml, 2000));
            SetTextSafe(summaryText, "(erro no parse do XML)");
            SetTextSafe(metadataText, "");
            SetTextSafe(equipmentText, "");
            SetTextSafe(featureText, "");
            SetTextSafe(personalityText, "");
            SetTextSafe(notesText, "");
        }
    }

    // util: seta texto com segurança (null-check)
    private void SetTextSafe(TMP_Text t, string value)
    {
        if (t != null) t.text = value;
    }

    private void ShowFeedback(string message, bool isError)
    {
        if (feedbackText != null)
        {
            feedbackText.text = message;
            feedbackText.color = isError ? Color.red : Color.green;
            feedbackText.gameObject.SetActive(true);
            
            // Auto-hide após 3 segundos
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

    private string ToTitle(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0]) + s.Substring(1).Replace('-', ' ');
    }

    private string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (text.Length <= max) return text;
        return text.Substring(0, max) + "...(truncated)";
    }

    // Adicione esta classe auxiliar (pode ficar perto das outras classes internas)
    [Serializable]
    private class ErrorResponse {
        public bool error;
        public string message;
    }
}