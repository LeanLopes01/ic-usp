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
/// Carrega lista de classes e ao clicar mostra os detalhes (parse do XML) em campos separados.
/// </summary>
public class ClassListLoader : MonoBehaviour
{
    [Header("API")]
    [Tooltip("URL completa para get_classes.php (ex: https://docjediwho.com.br/database_vtt/get_classes.php)")]
    public string apiUrl = "https://docjediwho.com.br/database_vtt/get_classes.php";

    [Tooltip("Opcional: endpoint para buscar 1 classe por id. Use {0} como placeholder para id (ex: https://.../get_class.php?id={0}). Se vazio, usará apiUrl?id={0}.")]
    public string apiSingleUrl = "";

    [Header("UI - Lista")]
    public Transform contentParent;      // Content do Scroll View
    public GameObject itemTemplate;      // Template (inativo) que será instanciado
    public string nameTextChild = "nomeClasse";   // nome do child com TMP_Text
    public string originTextChild = "origemClasse"; // nome do child com TMP_Text

    [Header("UI - Detalhes (preencha no Inspector)")]
    public GameObject detalhesPanel; // painel que será mostrado com os detalhes
    public TMP_Text nomeDetailText;
    public TMP_Text metadataText;
    public TMP_Text featuresText;
    public TMP_Text subclassesText;
    public TMP_Text notesText;

    [Header("Configuração Duplo Clique")]
    public float doubleClickTime = 0.5f; // tempo máximo entre cliques para ser considerado duplo clique
    public TMP_Text feedbackText; // opcional: para mostrar feedback ao usuário

    // internos
    [Serializable]
    private class Class { public int id; public string nome; public string origem; public string xml; public string classIdString; }
    [Serializable]
    private class ClassList { public Class[] classes; }

    // cache local de xml por id (útil se apiSingleUrl não existir)
    private Dictionary<string, string> xmlCache = new Dictionary<string, string>();
    
    // Controle de duplo clique
    private string lastClickedClassKey = "";
    private float lastClickTime = 0f;
    private bool isProcessingDoubleClick = false;

    // URL para salvar a classe na ficha
    private string saveClassUrl = "https://docjediwho.com.br/database_vtt/save_class_to_ficha.php";

    void Start()
    {
        if (string.IsNullOrEmpty(apiUrl)) {
            Debug.LogError("ClassListLoader: apiUrl não definido.");
            return;
        }
        if (contentParent == null || itemTemplate == null) {
            Debug.LogError("ClassListLoader: arraste contentParent e itemTemplate no inspector.");
            return;
        }

        if (detalhesPanel != null)
            detalhesPanel.SetActive(false);

        StartCoroutine(LoadClassesCoroutine());
    }

    private IEnumerator LoadClassesCoroutine()
    {
        using (UnityWebRequest req = UnityWebRequest.Get(apiUrl))
        {
            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError($"Erro ao buscar classes: {req.error} - {req.downloadHandler.text}");
                yield break;
            }

            string json = req.downloadHandler.text;
            ClassList list = null;
            try {
                list = JsonUtility.FromJson<ClassList>(json);
            } catch (Exception ex) {
                Debug.LogError("Falha ao desserializar JSON: " + ex.Message + "\nJSON: " + json);
            }

            if (list == null || list.classes == null) {
                Debug.LogWarning("Nenhuma classe retornada ou formato inesperado. JSON: " + json);
                yield break;
            }

            // Limpa filhos pré-existentes (exceto template)
            List<Transform> toDestroy = new List<Transform>();
            foreach (Transform t in contentParent) {
                if (t.gameObject != itemTemplate) toDestroy.Add(t);
            }
            foreach (var t in toDestroy) Destroy(t.gameObject);

            foreach (var c in list.classes)
            {
                GameObject go = Instantiate(itemTemplate, contentParent);
                go.SetActive(true);

                // procura os TMP nas children pelos nomes configurados
                var nameT = go.transform.Find(nameTextChild)?.GetComponent<TextMeshProUGUI>();
                var originT = go.transform.Find(originTextChild)?.GetComponent<TextMeshProUGUI>();

                if (nameT != null) nameT.text = string.IsNullOrEmpty(c.nome) ? "(sem nome)" : c.nome;
                if (originT != null) originT.text = string.IsNullOrEmpty(c.origem) ? "(origem desconhecida)" : c.origem;

                // cache: se o JSON trouxe XML, guardamos localmente por classId (prefira c.classIdString ou c.id)
                string key = GetClassKey(c);
                if (!string.IsNullOrEmpty(c.xml))
                {
                    if (!xmlCache.ContainsKey(key))
                        xmlCache[key] = c.xml;
                }

                // Se o item tem um Selectable (Button, Toggle, etc)
                var selectable = go.GetComponent<Selectable>();
                if (selectable != null) {
                    var btn = selectable as Button;
                    if (btn != null) {
                        // captura locais para evitar closure problem
                        string idLocal = key;
                        string xmlLocal = c.xml;
                        string nomeLocal = c.nome;
                        btn.onClick.AddListener(() => OnClassClicked(idLocal, nomeLocal, xmlLocal));
                    }
                } else {
                    // se não tem selectable, mas o nomeText é selectable (ex: adicionou Button no TMP), tente associar listener no próprio TMP (se tiver Button no mesmo GO)
                    var buttonOnChild = go.GetComponentInChildren<Button>(true);
                    if (buttonOnChild != null) {
                        string idLocal = GetClassKey(c);
                        string xmlLocal = c.xml;
                        string nomeLocal = c.nome;
                        buttonOnChild.onClick.AddListener(() => OnClassClicked(idLocal, nomeLocal, xmlLocal));
                    }
                }
            }
        }
    }

    // Gera uma chave string estável para a classe: prefira string id (quando o XML usa <class id="warrior">) ou o int id
    private string GetClassKey(Class c) {
        // Tenta extrair um ID numérico do XML (atributo id em <class id="...">).
        if (!string.IsNullOrEmpty(c.xml)) {
            try {
                var xdoc = XDocument.Parse(c.xml);
                var classEl = xdoc.Element("class");
                if (classEl != null && classEl.Attribute("id") != null) {
                    var idAttr = classEl.Attribute("id").Value;
                    if (int.TryParse(idAttr, out int xmlId)) {
                        // usa o id numérico do XML
                        return xmlId.ToString();
                    }
                }
            } catch {
                // ignora parse errors e segue para fallback
            }
        }

        // Fallback: sempre retornar o ID numérico vindo do JSON
        return c.id.ToString();
    }

    // Chamado quando usuário clica no item
    private void OnClassClicked(string classKey, string nomeExibicao, string xmlProvided)
    {
        Debug.Log($"OnClassClicked: key={classKey} nome={nomeExibicao} xmlProvided={(string.IsNullOrEmpty(xmlProvided) ? "no" : "yes")}");

        // Verifica duplo clique
        float currentTime = Time.time;
        if (classKey == lastClickedClassKey && (currentTime - lastClickTime) < doubleClickTime)
        {
            // Duplo clique detectado
            if (!isProcessingDoubleClick)
            {
                isProcessingDoubleClick = true;
                OnDoubleClickClass(classKey, nomeExibicao);
            }
        }
        else
        {
            // Clique simples - mostra detalhes normalmente
            lastClickedClassKey = classKey;
            lastClickTime = currentTime;

            // Se já temos xml no cache (ou foi enviado no objeto), usa ele.
            if (!string.IsNullOrEmpty(xmlProvided)) {
                ParseAndDisplayXml(xmlProvided, nomeExibicao);
                return;
            }
            if (xmlCache.ContainsKey(classKey) && !string.IsNullOrEmpty(xmlCache[classKey])) {
                ParseAndDisplayXml(xmlCache[classKey], nomeExibicao);
                return;
            }

            // Caso contrário, busca no servidor por id
            if (!string.IsNullOrEmpty(apiSingleUrl)) {
                string url = string.Format(apiSingleUrl, UnityWebRequest.EscapeURL(classKey));
                StartCoroutine(FetchSingleClassAndDisplay(url, classKey, nomeExibicao));
            } else {
                // fallback: tenta apiUrl?id=classKey
                string url = apiUrl + "?id=" + UnityWebRequest.EscapeURL(classKey);
                StartCoroutine(FetchSingleClassAndDisplay(url, classKey, nomeExibicao));
            }
        }
    }

    // Chamado quando detectado duplo clique
    private void OnDoubleClickClass(string classKey, string className)
    {
        Debug.Log($"Duplo clique na classe: {classKey} - {className}");
        
        // Verifica se temos uma ficha criada
        if (GlobalFichaData.fichasDndId <= 0)
        {
            ShowFeedback("Erro: Nenhuma ficha DnD criada anteriormente.", true);
            isProcessingDoubleClick = false;
            return;
        }

        ShowFeedback($"Salvando classe {className} na ficha...", false);
        StartCoroutine(SaveClassToFicha(classKey, className));
    }

    private IEnumerator SaveClassToFicha(string classKey, string className)
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
        form.AddField("classes_dnd_id", classKey);

        Debug.Log($"Enviando classe para ficha: ficha_dnd_id={GlobalFichaData.fichasDndId}, class_id={classKey}");

        using (UnityWebRequest www = UnityWebRequest.Post(saveClassUrl, form))
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
                    SaveClassResponse errorResponse = JsonUtility.FromJson<SaveClassResponse>(responseText);
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
                ShowFeedback($"Erro ao salvar classe: {errorDetail}", true);
            }
            else
            {
                Debug.Log($"Resposta do servidor: {responseText}");

                try
                {
                    SaveClassResponse response = JsonUtility.FromJson<SaveClassResponse>(responseText);
                    if (response != null && !string.IsNullOrEmpty(response.status) && response.status.ToLower() == "ok")
                    {
                        ShowFeedback($"✓ Classe '{className}' salva com sucesso!", false);
                        Debug.Log($"Classe {classKey} associada à ficha DnD ID: {GlobalFichaData.fichasDndId}");
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
    private class SaveClassResponse
    {
        public string status;
        public string message;
    }

    private IEnumerator FetchSingleClassAndDisplay(string url, string classKey, string nomeExibicao)
    {
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            Debug.Log("Buscando classe no servidor: " + url);
            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError($"Erro ao buscar classe: {req.error} - {req.downloadHandler.text}");
                yield break;
            }

            string content = req.downloadHandler.text;
            // supondo que o endpoint retorne apenas o XML da classe (ou um JSON com campo xml). Tentamos detectar:
            string xml = null;
            content = content.Trim();
            if (content.StartsWith("<")) {
                // é XML direto
                xml = content;
            } else {
                // tenta extrair campo "xml" de um JSON simples: {"xml": "<class...>"}
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
            xmlCache[classKey] = xml;
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
        SetTextSafe(metadataText, "Carregando...");
        SetTextSafe(featuresText, "Carregando...");
        SetTextSafe(subclassesText, "Carregando...");
        SetTextSafe(notesText, "Carregando...");

        try {
            XDocument doc = XDocument.Parse(xml);
            XElement classElement = doc.Element("class");

            // METADATA
            StringBuilder metaSb = new StringBuilder();
            var metadata = classElement.Element("metadata");
            if (metadata != null) {
                foreach (var el in metadata.Elements())
                {
                    string name = el.Name.LocalName;
                    string value = el.Value.Trim();
                    var unitAttr = el.Attribute("unit");
                    if (unitAttr != null) value += $" ({unitAttr.Value})";
                    metaSb.AppendLine($"{ToTitle(name)}: {value}");
                }
            } else metaSb.AppendLine("(sem metadata)");
            SetTextSafe(metadataText, metaSb.ToString().Trim());

            // FEATURES
            StringBuilder featuresSb = new StringBuilder();
            var features = classElement.Element("features");
            if (features != null) {
                foreach (var f in features.Elements("feature")) {
                    string fname = f.Element("name")?.Value ?? f.Attribute("id")?.Value ?? "(feature)";
                    string desc = f.Element("description")?.Value ?? "";
                    string level = f.Attribute("level")?.Value ?? "";
                    
                    if (!string.IsNullOrEmpty(level))
                        featuresSb.AppendLine($"• Nível {level}: {fname}");
                    else
                        featuresSb.AppendLine($"• {fname}");
                    
                    if (!string.IsNullOrEmpty(desc))
                        featuresSb.AppendLine($"  {desc}\n");
                }
            } else featuresSb.AppendLine("(sem features)");
            SetTextSafe(featuresText, featuresSb.ToString().Trim());

            // SUBCLASSES
            StringBuilder subclassesSb = new StringBuilder();
            var subclasses = classElement.Element("subclasses");
            if (subclasses != null) {
                foreach (var s in subclasses.Elements("subclass")) {
                    // Puxa o nome e o resumo
                    string sname = s.Element("name")?.Value ?? s.Attribute("id")?.Value ?? "(subclass)";
                    string summary = s.Element("summary")?.Value ?? "";
                    
                    subclassesSb.AppendLine($"<size=120%><b>{sname.ToUpper()}</b></size>");
                    if (!string.IsNullOrEmpty(summary))
                        subclassesSb.AppendLine($"<i>{summary}</i>\n");
                        
                    // Puxa as habilidades da subclasse
                    var subFeatures = s.Element("features");
                    if (subFeatures != null) {
                        foreach (var f in subFeatures.Elements("feature")) {
                            string fname = f.Element("name")?.Value ?? f.Attribute("id")?.Value ?? "(feature)";
                            string fdesc = f.Element("description")?.Value ?? "";
                            string flevel = f.Element("level")?.Value ?? "";
                            
                            // Monta a habilidade com o nível
                            string nivelTexto = !string.IsNullOrEmpty(flevel) ? $" <size=80%>(Nível {flevel})</size>" : "";
                            subclassesSb.AppendLine($"<b>• {fname}</b>{nivelTexto}");
                            
                            if (!string.IsNullOrEmpty(fdesc))
                                subclassesSb.AppendLine($"<color=#333333>{fdesc}</color>\n");
                        }
                    }
                    subclassesSb.AppendLine("-----------------------------\n");
                }
            } else subclassesSb.AppendLine("(sem subclasses)");
            
            // Joga tudo no seu TMP_Text 'subclassesText'
            SetTextSafe(subclassesText, subclassesSb.ToString().Trim());

            // NOTES
            var notes = classElement.Element("notes");
            SetTextSafe(notesText, notes != null ? notes.Value.Trim() : "(sem notes)");

        } catch (Exception ex) {
            Debug.LogError("Erro ao parsear XML da classe: " + ex.Message + "\nXML: " + Truncate(xml, 2000));
            SetTextSafe(metadataText, "(erro no parse do XML)");
            SetTextSafe(featuresText, "");
            SetTextSafe(subclassesText, "");
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
}