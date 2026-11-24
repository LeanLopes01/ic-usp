using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class LogReporter : MonoBehaviour
{
    [Header("Configurações do Google Drive")]
    [Tooltip("Cole aqui a URL do App da Web gerada no Google Apps Script")]
    public string googleScriptUrl = "https://script.google.com/macros/s/AKfycbwT5wVg3fybilfjIhzwPA1Sm1pIxv8bTfQbDCM04Bjj2UdcX_efI_8FQOrcVTMzPyB2/exec";

    [Header("Gatilhos")]
    public bool enviarAutomaticamenteNoErro = true;
    
    private bool enviandoLog = false;

    private void OnEnable()
    {
        // Inscreve-se para escutar todos os logs que a Unity gera
        Application.logMessageReceived += TratarLog;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= TratarLog;
    }

    private void TratarLog(string logString, string stackTrace, LogType type)
    {
        // Se der um erro crítico ou exception e não estivermos já enviando, dispara o envio
        if (enviarAutomaticamenteNoErro && !enviandoLog)
        {
            if (type == LogType.Exception || type == LogType.Error)
            {
                Debug.LogWarning("[LogReporter] Erro detectado. Iniciando envio do log para o servidor...");
                StartCoroutine(LerEEnviarLog("EnvioAutomatico_Erro"));
            }
        }
    }

    /// <summary>
    /// Pode ser chamado por um botão na UI (ex: "Enviar Feedback/Bug")
    /// </summary>
    public void EnviarLogManualmente()
    {
        if (!enviandoLog)
        {
            StartCoroutine(LerEEnviarLog("EnvioManual_Usuario"));
        }
    }

    private IEnumerator LerEEnviarLog(string motivoEnvio)
    {
        enviandoLog = true;

        string logPath = Application.consoleLogPath;
        string logCompleto = "";

        // Lê o Player.log contornando o bloqueio de arquivo do Windows
        try
        {
            using (FileStream fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader sr = new StreamReader(fs))
            {
                logCompleto = sr.ReadToEnd();
            }
        }
        catch (System.Exception e)
        {
            logCompleto = "Não foi possível ler o arquivo de log local. Erro: " + e.Message;
        }

        // Adiciona um cabeçalho útil no arquivo antes de enviar
        string conteudoFinal = $"=== RELATÓRIO DE LOG ===\n" +
                               $"Data: {System.DateTime.Now}\n" +
                               $"Versão do Jogo: {Application.version}\n" +
                               $"Motivo do Envio: {motivoEnvio}\n" +
                               $"Sistema Operacional: {SystemInfo.operatingSystem}\n" +
                               $"========================\n\n" +
                               logCompleto;

        // Prepara os dados para mandar pro Google
        WWWForm form = new WWWForm();
        form.AddField("logContent", conteudoFinal);
        form.AddField("playerName", System.Environment.UserName); // Pega o nome do usuário do Windows

        Debug.Log("[LogReporter] Conectando ao Google Drive...");

        using (UnityWebRequest www = UnityWebRequest.Post(googleScriptUrl, form))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[LogReporter] Falha ao enviar log para o Drive: " + www.error);
            }
            else
            {
                Debug.Log("[LogReporter] Log enviado com sucesso para o Google Drive!");
            }
        }

        // Dá um pequeno cooldown para não flodar o Google Drive se muitos erros acontecerem em sequência
        yield return new WaitForSeconds(10f); 
        enviandoLog = false;
    }
}