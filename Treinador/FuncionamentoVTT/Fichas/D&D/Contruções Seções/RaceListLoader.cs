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
/// Carrega lista de raças e ao clicar mostra os detalhes (parse do XML) em campos separados.
/// </summary>
public class RaceListLoader : MonoBehaviour
{
    [Header("API")]
    [Tooltip("URL completa para get_racas.php (ex: https://docjediwho.com.br/database_vtt/get_racas.php)")]
    public string apiUrl = "https://docjediwho.com.br/database_vtt/get_racas.php";

    [Tooltip("Opcional: endpoint para buscar 1 raça por id. Use {0} como placeholder para id (ex: https://.../get_raca.php?id={0}). Se vazio, usará apiUrl?id={0}.")]
    public string apiSingleUrl = "";

    [Header("UI - Lista")]
    public Transform contentParent;      // Content do Scroll View
    public GameObject itemTemplate;      // Template (inativo) que será instanciado
    public string nameTextChild = "nomeRaca";   // nome do child com TMP_Text
    public string originTextChild = "origemRaca"; // nome do child com TMP_Text

    [Header("UI - Detalhes (preencha no Inspector)")]
    public GameObject detalhesPanel; // painel que será mostrado com os detalhes
    public TMP_Text nomeDetailText;
    public TMP_Text metadataText;
    public TMP_Text appearanceText;
    public TMP_Text languagesText;
    public TMP_Text abilityScoresText;
    public TMP_Text traitsText;
    public TMP_Text notesText;

    [Header("Configuração Duplo Clique")]
    public float doubleClickTime = 0.5f; // tempo máximo entre cliques para ser considerado duplo clique
    public TMP_Text feedbackText; // opcional: para mostrar feedback ao usuário

    // internos
    [Serializable]
    private class Race { public int id; public string nome; public string origem; public string xml; public string raceIdString; }
    [Serializable]
    private class RaceList { public Race[] races; }

    // cache local de xml por id (útil se apiSingleUrl não existir)
    private Dictionary<string, string> xmlCache = new Dictionary<string, string>();
    
    // Controle de duplo clique
    private string lastClickedRaceKey = "";
    private float lastClickTime = 0f;
    private bool isProcessingDoubleClick = false;

    // URL para salvar a raça na ficha
    private string saveRaceUrl = "https://docjediwho.com.br/database_vtt/save_race_to_ficha.php";

    void Start()
    {
        if (string.IsNullOrEmpty(apiUrl)) {
            Debug.LogError("RaceListLoader: apiUrl não definido.");
            return;
        }
        if (contentParent == null || itemTemplate == null) {
            Debug.LogError("RaceListLoader: arraste contentParent e itemTemplate no inspector.");
            return;
        }

        if (detalhesPanel != null)
            detalhesPanel.SetActive(false);

        StartCoroutine(LoadRacesCoroutine());
    }

    private IEnumerator LoadRacesCoroutine()
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
                Debug.LogError($"Erro ao buscar raças: {req.error} - {req.downloadHandler.text}");
                yield break;
            }

            string json = req.downloadHandler.text;
            RaceList list = null;
            try {
                list = JsonUtility.FromJson<RaceList>(json);
            } catch (Exception ex) {
                Debug.LogError("Falha ao desserializar JSON: " + ex.Message + "\nJSON: " + json);
            }

            if (list == null || list.races == null) {
                Debug.LogWarning("Nenhuma raça retornada ou formato inesperado. JSON: " + json);
                yield break;
            }

            // Limpa filhos pré-existentes (exceto template)
            List<Transform> toDestroy = new List<Transform>();
            foreach (Transform t in contentParent) {
                if (t.gameObject != itemTemplate) toDestroy.Add(t);
            }
            foreach (var t in toDestroy) Destroy(t.gameObject);

            foreach (var r in list.races)
            {
                GameObject go = Instantiate(itemTemplate, contentParent);
                go.SetActive(true);

                // procura os TMP nas children pelos nomes configurados
                var nameT = go.transform.Find(nameTextChild)?.GetComponent<TextMeshProUGUI>();
                var originT = go.transform.Find(originTextChild)?.GetComponent<TextMeshProUGUI>();

                if (nameT != null) nameT.text = string.IsNullOrEmpty(r.nome) ? "(sem nome)" : r.nome;
                if (originT != null) originT.text = string.IsNullOrEmpty(r.origem) ? "(origem desconhecida)" : r.origem;

                // cache: se o JSON trouxe XML, guardamos localmente por raceId (prefira r.raceIdString ou r.id)
                string key = GetRaceKey(r);
                if (!string.IsNullOrEmpty(r.xml))
                {
                    if (!xmlCache.ContainsKey(key))
                        xmlCache[key] = r.xml;
                }

                // Se o item tem um Selectable (Button, Toggle, etc)
                var selectable = go.GetComponent<Selectable>();
                if (selectable != null) {
                    var btn = selectable as Button;
                    if (btn != null) {
                        // captura locais para evitar closure problem
                        string idLocal = key;
                        string xmlLocal = r.xml;
                        string nomeLocal = r.nome;
                        btn.onClick.AddListener(() => OnRaceClicked(idLocal, nomeLocal, xmlLocal));
                    }
                } else {
                    // se não tem selectable, mas o nomeText é selectable (ex: adicionou Button no TMP), tente associar listener no próprio TMP (se tiver Button no mesmo GO)
                    var buttonOnChild = go.GetComponentInChildren<Button>(true);
                    if (buttonOnChild != null) {
                        string idLocal = GetRaceKey(r);
                        string xmlLocal = r.xml;
                        string nomeLocal = r.nome;
                        buttonOnChild.onClick.AddListener(() => OnRaceClicked(idLocal, nomeLocal, xmlLocal));
                    }
                }
            }
        }
    }

    // Gera uma chave string estável para a raça: prefira string id (quando o XML usa <race id="human">) ou o int id
    private string GetRaceKey(Race r) {
	// Tenta extrair um ID numérico do XML (atributo id em <race id="...">).
	if (!string.IsNullOrEmpty(r.xml)) {
		try {
			var xdoc = XDocument.Parse(r.xml);
			var raceEl = xdoc.Element("race");
			if (raceEl != null && raceEl.Attribute("id") != null) {
				var idAttr = raceEl.Attribute("id").Value;
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
	return r.id.ToString();
    }

    // Chamado quando usuário clica no item
    private void OnRaceClicked(string raceKey, string nomeExibicao, string xmlProvided)
    {
        Debug.Log($"OnRaceClicked: key={raceKey} nome={nomeExibicao} xmlProvided={(string.IsNullOrEmpty(xmlProvided) ? "no" : "yes")}");

        // Verifica duplo clique
        float currentTime = Time.time;
        if (raceKey == lastClickedRaceKey && (currentTime - lastClickTime) < doubleClickTime)
        {
            // Duplo clique detectado
            if (!isProcessingDoubleClick)
            {
                isProcessingDoubleClick = true;
                OnDoubleClickRace(raceKey, nomeExibicao);
            }
        }
        else
        {
            // Clique simples - mostra detalhes normalmente
            lastClickedRaceKey = raceKey;
            lastClickTime = currentTime;

            // Se já temos xml no cache (ou foi enviado no objeto), usa ele.
            if (!string.IsNullOrEmpty(xmlProvided)) {
                ParseAndDisplayXml(xmlProvided, nomeExibicao);
                return;
            }
            if (xmlCache.ContainsKey(raceKey) && !string.IsNullOrEmpty(xmlCache[raceKey])) {
                ParseAndDisplayXml(xmlCache[raceKey], nomeExibicao);
                return;
            }

            // Caso contrário, busca no servidor por id
            if (!string.IsNullOrEmpty(apiSingleUrl)) {
                string url = string.Format(apiSingleUrl, UnityWebRequest.EscapeURL(raceKey));
                StartCoroutine(FetchSingleRaceAndDisplay(url, raceKey, nomeExibicao));
            } else {
                // fallback: tenta apiUrl?id=raceKey
                string url = apiUrl + "?id=" + UnityWebRequest.EscapeURL(raceKey);
                StartCoroutine(FetchSingleRaceAndDisplay(url, raceKey, nomeExibicao));
            }
        }
    }

    // Chamado quando detectado duplo clique
    private void OnDoubleClickRace(string raceKey, string raceName)
    {
        Debug.Log($"Duplo clique na raça: {raceKey} - {raceName}");
        
        // Verifica se temos uma ficha criada
        if (GlobalFichaData.fichasDndId <= 0)
        {
            ShowFeedback("Erro: Nenhuma ficha DnD criada anteriormente.", true);
            isProcessingDoubleClick = false;
            return;
        }

        ShowFeedback($"Salvando raça {raceName} na ficha...", false);
        StartCoroutine(SaveRaceToFicha(raceKey, raceName));
    }

    private IEnumerator SaveRaceToFicha(string raceKey, string raceName)
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
        form.AddField("racas_dnd_id", raceKey);

        Debug.Log($"Enviando raça para ficha: ficha_dnd_id={GlobalFichaData.fichasDndId}, race_id={raceKey}");

        using (UnityWebRequest www = UnityWebRequest.Post(saveRaceUrl, form))
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
                    SaveRaceResponse errorResponse = JsonUtility.FromJson<SaveRaceResponse>(responseText);
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
                ShowFeedback($"Erro ao salvar raça: {errorDetail}", true);
            }
            else
            {
                Debug.Log($"Resposta do servidor: {responseText}");

                try
                {
                    SaveRaceResponse response = JsonUtility.FromJson<SaveRaceResponse>(responseText);
                    if (response != null && !string.IsNullOrEmpty(response.status) && response.status.ToLower() == "ok")
                    {
                        ShowFeedback($"✓ Raça '{raceName}' salva com sucesso!", false);
                        Debug.Log($"Raça {raceKey} associada à ficha DnD ID: {GlobalFichaData.fichasDndId}");
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
    private class SaveRaceResponse
    {
        public string status;
        public string message;
    }

    private IEnumerator FetchSingleRaceAndDisplay(string url, string raceKey, string nomeExibicao)
    {
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            Debug.Log("Buscando raça no servidor: " + url);
            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError($"Erro ao buscar raça: {req.error} - {req.downloadHandler.text}");
                yield break;
            }

            string content = req.downloadHandler.text;
            // supondo que o endpoint retorne apenas o XML da raça (ou um JSON com campo xml). Tentamos detectar:
            string xml = null;
            content = content.Trim();
            if (content.StartsWith("<")) {
                // é XML direto
                xml = content;
            } else {
                // tenta extrair campo "xml" de um JSON simples: {"xml": "<race...>"}
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
            xmlCache[raceKey] = xml;
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
        SetTextSafe(appearanceText, "Carregando...");
        SetTextSafe(languagesText, "Carregando...");
        SetTextSafe(abilityScoresText, "Carregando...");
        SetTextSafe(traitsText, "Carregando...");
        SetTextSafe(notesText, "Carregando...");

        try {
            XDocument doc = XDocument.Parse(xml);
            XElement race = doc.Element("race");

            // METADATA
            StringBuilder metaSb = new StringBuilder();
            var metadata = race.Element("metadata");
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

            // APPEARANCE
            StringBuilder appSb = new StringBuilder();
            var appearance = race.Element("appearance");
            if (appearance != null) {
                var desc = appearance.Element("description");
                if (desc != null) appSb.AppendLine(desc.Value.Trim());
                var notable = appearance.Element("notable-features");
                if (notable != null && !string.IsNullOrWhiteSpace(notable.Value))
                    appSb.AppendLine("Destaques: " + notable.Value.Trim());
            } else appSb.AppendLine("(sem appearance)");
            SetTextSafe(appearanceText, appSb.ToString().Trim());

            // LANGUAGES
            StringBuilder langSb = new StringBuilder();
            var languages = race.Element("languages");
            if (languages != null) {
                foreach (var l in languages.Elements("language"))
                    langSb.AppendLine("- " + l.Value.Trim());
            } else langSb.AppendLine("(sem languages)");
            SetTextSafe(languagesText, langSb.ToString().Trim());

            // ABILITY SCORES
            StringBuilder abilSb = new StringBuilder();
            var abilities = race.Element("ability_scores");
            if (abilities != null) {
                foreach (var a in abilities.Elements("ability")) {
                    var nameAttr = a.Attribute("name")?.Value ?? "??";
                    var modAttr = a.Attribute("mod")?.Value ?? "0";
                    abilSb.AppendLine($"{nameAttr}: {modAttr}");
                }
            } else abilSb.AppendLine("(sem ability scores)");
            SetTextSafe(abilityScoresText, abilSb.ToString().Trim());

            // TRAITS
            StringBuilder traitsSb = new StringBuilder();
            var traits = race.Element("traits");
            if (traits != null) {
                foreach (var t in traits.Elements("trait")) {
                    string tname = t.Element("name")?.Value ?? t.Attribute("id")?.Value ?? "(trait)";
                    string desc = t.Element("description")?.Value ?? "";
                    traitsSb.AppendLine($"• {tname}\n{desc}\n");
                }
            } else traitsSb.AppendLine("(sem traits)");
            SetTextSafe(traitsText, traitsSb.ToString().Trim());

            // NOTES
            var notes = race.Element("notes");
            SetTextSafe(notesText, notes != null ? notes.Value.Trim() : "(sem notes)");

        } catch (Exception ex) {
            Debug.LogError("Erro ao parsear XML da raça: " + ex.Message + "\nXML: " + Truncate(xml, 2000));
            SetTextSafe(metadataText, "(erro no parse do XML)");
            SetTextSafe(appearanceText, "");
            SetTextSafe(languagesText, "");
            SetTextSafe(abilityScoresText, "");
            SetTextSafe(traitsText, "");
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