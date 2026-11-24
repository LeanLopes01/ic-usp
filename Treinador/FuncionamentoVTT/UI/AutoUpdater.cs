using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class AutoUpdater : MonoBehaviour
{
    [Header("Configurações")]
    public string versionJsonUrl = "https://leanlopes01.github.io/dicevtt.github.io/version.json";

    // Nome do executável do seu jogo (ex: DiceVTT.exe)
    public string nomeExecutavelDoJogo = "DiceVTT.exe";

    [Header("Interface (UI)")]
    [Tooltip("Painel/janela que avisa sobre a nova versão (fica oculto até uma atualização ser encontrada)")]
    public GameObject painelAvisoAtualizacao;
    [Tooltip("Texto que mostra a versão atual e a nova versão disponível")]
    public TMP_Text textoVersaoDisponivel;
    [Tooltip("Painel invisível que bloqueia cliques durante o download")]
    public GameObject painelBloqueioCliques;

    [Header("Checagem de Versão (retry)")]
    [Tooltip("Timeout (segundos) de cada tentativa de checar versão — sem isso, sem internet/servidor " +
        "fora do ar pode deixar a requisição pendurada por muito tempo antes de falhar.")]
    public int timeoutChecagemSegundos = 10;
    [Tooltip("Quantas vezes tentar checar a versão antes de desistir silenciosamente.")]
    public int maxTentativasChecagem = 3;
    [Tooltip("Espera entre uma tentativa falha e a próxima.")]
    public float delayEntreTentativasSegundos = 5f;

    // ── Eventos ──────────────────────────────────────────────────────────
    /// <summary>Disparado quando a checagem de atualização termina (com ou sem atualização
    /// encontrada, ou após esgotar as tentativas). Não espera o usuário concluir um download — só
    /// sinaliza que a checagem em si acabou.</summary>
    public event Action OnVerificacaoConcluida;

    private string urlDoZipPendente = "";

    // Estrutura para ler o JSON
    [Serializable]
    private class VersionData
    {
        public string version;
        public string zipUrl;
    }

    private void Start()
    {
        if (painelAvisoAtualizacao != null)
            painelAvisoAtualizacao.SetActive(false);

        if (painelBloqueioCliques != null)
            painelBloqueioCliques.SetActive(false);

        StartCoroutine(ChecarAtualizacao());
    }

    // Falha de rede (sem internet, servidor de versão fora do ar) é uma condição ESPERADA aqui, não
    // um bug do jogo — por isso vira UpdaterWarn (Debug.LogWarning), não UpdaterError
    // (Debug.LogError). Um LogError, se "Error Pause" estiver ligado no Console do Editor, pausa o
    // Play Mode inteiro — é o motivo mais provável do "trava o programa todo" reportado, já que essa
    // checagem roda sozinha em todo Start(). Também tenta de novo algumas vezes (com timeout em cada
    // tentativa) antes de desistir silenciosamente — checar atualização nunca deveria impedir o
    // jogo de abrir.
    private IEnumerator ChecarAtualizacao()
    {
        for (int tentativa = 1; tentativa <= maxTentativasChecagem; tentativa++)
        {
            VTTLog.Updater($"Checando atualizações... (tentativa {tentativa}/{maxTentativasChecagem})");
            bool sucesso;

            using (UnityWebRequest request = UnityWebRequest.Get(versionJsonUrl))
            {
                request.timeout = timeoutChecagemSegundos;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    VTTLog.UpdaterWarn($"Erro ao checar versão (tentativa {tentativa}/{maxTentativasChecagem}): {request.error}");
                    sucesso = false;
                }
                else
                {
                    sucesso = ProcessarRespostaDeVersao(request.downloadHandler.text);
                }
            }

            if (sucesso)
            {
                OnVerificacaoConcluida?.Invoke();
                yield break;
            }
            if (tentativa < maxTentativasChecagem)
                yield return new WaitForSecondsRealtime(delayEntreTentativasSegundos);
        }

        VTTLog.UpdaterWarn("Não foi possível checar atualizações após múltiplas tentativas — seguindo sem checar.");
        OnVerificacaoConcluida?.Invoke();
    }

    // Retorna true se a resposta foi entendida com sucesso (mesmo que a conclusão seja "já está
    // atualizado") — false só quando o JSON veio malformado/incompleto, sinal pra ChecarAtualizacao
    // tentar de novo.
    private bool ProcessarRespostaDeVersao(string respostaBruta)
    {
        // Remove o BOM (caractere invisível) que alguns servidores incluem no início do JSON
        string json = respostaBruta.Trim().TrimStart('﻿');
        VTTLog.Updater("JSON recebido: " + json);

        VersionData serverData;
        try
        {
            serverData = JsonUtility.FromJson<VersionData>(json);
        }
        catch (Exception e)
        {
            VTTLog.UpdaterWarn($"Erro ao fazer parse do JSON: {e.Message}\nConteúdo recebido: {json}");
            return false;
        }

        if (serverData == null || string.IsNullOrEmpty(serverData.version))
        {
            VTTLog.UpdaterWarn("JSON inválido ou campos ausentes. Conteúdo: " + json);
            return false;
        }

        // Compara a versão
        if (serverData.version != Application.version)
        {
            VTTLog.Updater($"Nova versão encontrada: {serverData.version}. Aguardando ação do usuário.");

            // Salva a URL para usar quando o botão "Ok" for clicado
            urlDoZipPendente = serverData.zipUrl;

            if (textoVersaoDisponivel != null)
            {
                textoVersaoDisponivel.text = $"Nova versão disponível: {serverData.version}\nSua versão: {Application.version}";
            }

            if (painelAvisoAtualizacao != null)
            {
                painelAvisoAtualizacao.SetActive(true);
            }
        }
        else
        {
            VTTLog.Updater("O jogo já está na versão mais recente.");
        }

        return true;
    }

    /// <summary>
    /// Esse é o método que você vai colocar no evento "On Click ()" do botão "Ok" da interface
    /// </summary>
    public void IniciarAtualizacaoPeloBotao()
    {
        if (string.IsNullOrEmpty(urlDoZipPendente))
        {
            VTTLog.UpdaterError("urlDoZipPendente está vazia — botão clicado antes da checagem terminar?");
            return;
        }

        if (painelAvisoAtualizacao != null)
            painelAvisoAtualizacao.SetActive(false);

        if (painelBloqueioCliques != null)
            painelBloqueioCliques.SetActive(true);

        VTTLog.Updater("Usuário confirmou a atualização. Iniciando download...");

        StartCoroutine(BaixarEInstalarAtualizacao(urlDoZipPendente));
    }

    private IEnumerator BaixarEInstalarAtualizacao(string zipUrl)
    {
        string pastaDoJogo = Path.GetDirectoryName(Application.dataPath);
        string caminhoZip = Path.Combine(pastaDoJogo, "update.zip");

        using (UnityWebRequest request = UnityWebRequest.Get(zipUrl))
        {
            request.downloadHandler = new DownloadHandlerFile(caminhoZip);

            if (PopupManager.Instance != null)
                PopupManager.Instance.MostrarCarregamento("Atualizando DiceVTT", "Conectando ao servidor...");

            var operation = request.SendWebRequest();

            ulong bytesAnteriores = 0;
            float tempoAnterior = Time.unscaledTime;
            float taxaSuavizada = 0f;

            while (!operation.isDone)
            {
                float progresso = operation.progress;
                ulong bytesBaixados = request.downloadedBytes;

                float agora = Time.unscaledTime;
                float deltaTempo = agora - tempoAnterior;
                if (deltaTempo >= 0.2f)
                {
                    float bytesPorSegundo = (bytesBaixados - bytesAnteriores) / deltaTempo;
                    // Suaviza a taxa para o texto não "piscar" a cada frame
                    taxaSuavizada = taxaSuavizada <= 0f ? bytesPorSegundo : Mathf.Lerp(taxaSuavizada, bytesPorSegundo, 0.3f);
                    bytesAnteriores = bytesBaixados;
                    tempoAnterior = agora;
                }

                if (PopupManager.Instance != null)
                {
                    string totalStr = ObterTamanhoTotal(request, out long totalBytes);
                    string detalhe = totalBytes > 0
                        ? $"{FormatarBytes((long)bytesBaixados)} / {totalStr} — {FormatarBytes((long)taxaSuavizada)}/s"
                        : $"{FormatarBytes((long)bytesBaixados)} — {FormatarBytes((long)taxaSuavizada)}/s";

                    PopupManager.Instance.AtualizarProgresso(progresso,
                        $"Baixando atualização... {Mathf.RoundToInt(progresso * 100)}%",
                        detalhe);
                }
                yield return null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                VTTLog.UpdaterError("Erro ao baixar atualização: " + request.error);

                if (painelBloqueioCliques != null)
                    painelBloqueioCliques.SetActive(false);

                if (PopupManager.Instance != null)
                {
                    PopupManager.Instance.MostrarErro("Falha no Download",
                        "Não foi possível baixar a atualização. Verifique a internet.",
                        onOk: () =>
                        {
                            if (painelAvisoAtualizacao != null)
                                painelAvisoAtualizacao.SetActive(true);
                        });
                }
                yield break;
            }

            if (PopupManager.Instance != null)
                PopupManager.Instance.MostrarCarregamento("Download Concluído",
                    "Extraindo arquivos. O VTT será reiniciado automaticamente...");

            yield return new WaitForSeconds(1.5f);

            AplicarAtualizacaoEReiniciar(pastaDoJogo, caminhoZip);
        }
    }

    /// <summary>Lê o header Content-Length da resposta e retorna o tamanho total formatado.</summary>
    private string ObterTamanhoTotal(UnityWebRequest request, out long totalBytes)
    {
        totalBytes = 0;
        string contentLength = request.GetResponseHeader("Content-Length");
        if (!string.IsNullOrEmpty(contentLength) && long.TryParse(contentLength, out totalBytes))
            return FormatarBytes(totalBytes);
        return "?";
    }

    private static string FormatarBytes(long bytes)
    {
        string[] unidades = { "B", "KB", "MB", "GB" };
        double valor = bytes;
        int i = 0;
        while (valor >= 1024 && i < unidades.Length - 1)
        {
            valor /= 1024;
            i++;
        }
        return $"{valor:0.#} {unidades[i]}";
    }

    private void AplicarAtualizacaoEReiniciar(string pastaDoJogo, string caminhoZip)
    {
        string batPath = Path.Combine(pastaDoJogo, "atualizador.bat");
        string ps1Path = Path.Combine(pastaDoJogo, "atualizador.ps1");

        // O .ps1 faz todo o trabalho pesado (extrair, sobrescrever, reabrir o jogo).
        // É muito mais confiável do que tentar embutir comandos PowerShell dentro
        // de um .bat com aspas aninhadas, que falham silenciosamente com facilidade.
        string ps1Content = $@"
$ErrorActionPreference = 'SilentlyContinue'
Set-Location -LiteralPath $PSScriptRoot

Write-Host ""Aguardando o encerramento completo do VTT para liberar os arquivos...""

# Pega o nome do processo (ex: tira o .exe do final)
$processName = ""{nomeExecutavelDoJogo}"".Replace("".exe"", """")

# Loop que segura o script enquanto o jogo ainda estiver rodando no fundo
while (Get-Process -Name $processName) {{
    Start-Sleep -Seconds 1
}}
# Da um tempinho extra para garantir que o disco soltou os arquivos
Start-Sleep -Seconds 2

Write-Host ""Extraindo a nova versao do DiceVTT...""
Expand-Archive -Path 'update.zip' -DestinationPath 'update_temp' -Force
Remove-Item 'update.zip' -Force

# Se o zip continha uma unica pasta dentro (em vez de arquivos soltos), entra nela
$items = Get-ChildItem -LiteralPath 'update_temp'
if ($items.Count -eq 1 -and $items[0].PSIsContainer) {{
    $src = $items[0].FullName
}} else {{
    $src = Join-Path $PSScriptRoot 'update_temp'
}}

Write-Host ""Instalando os novos arquivos...""
Copy-Item -Path ""$src\*"" -Destination $PSScriptRoot -Recurse -Force

Write-Host ""Limpando arquivos temporarios...""
Remove-Item 'update_temp' -Recurse -Force

if (Test-Path '{nomeExecutavelDoJogo}') {{
    Write-Host ""Atualizacao concluida! Reabrindo o DiceVTT...""
    Start-Process -FilePath '{nomeExecutavelDoJogo}'
}} else {{
    Write-Host ""=======================================================""
    Write-Host ""ERRO CRITICO: O arquivo {nomeExecutavelDoJogo} nao foi encontrado!""
    Write-Host ""=======================================================""
    Write-Host ""O seu arquivo atualizacao.zip que esta no servidor provavelmente""
    Write-Host ""foi zipado de forma incorreta (com uma pasta dentro dele em vez""
    Write-Host ""dos arquivos soltos).""
    Write-Host """"
    Read-Host ""Pressione Enter para fechar""
}}

Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
";
        File.WriteAllText(ps1Path, ps1Content);

        string batContent = $@"
@echo off
:: Força o Windows a rodar este script exatamente na pasta onde ele esta
cd /d ""%~dp0""

powershell.exe -nologo -noprofile -ExecutionPolicy Bypass -File ""atualizador.ps1""

del ""%~f0""
";
        File.WriteAllText(batPath, batContent);

        // Importante: NÃO usar System.Diagnostics.Process.Start aqui. Em builds IL2CPP,
        // o P/Invoke de CreateProcess está com um bug conhecido: ele falha
        // internamente, GetLastError() retorna 0, e o .NET lança um Win32Exception
        // cuja mensagem é "Success" — independentemente do executável/caminho usado.
        // Application.OpenURL usa o ShellExecute nativo do próprio Unity, contornando
        // esse bug, e funciona normalmente para abrir um .bat (equivalente a um duplo clique).
        try
        {
            Application.OpenURL(batPath);

#if UNITY_EDITOR
            // Application.Quit() é ignorado dentro do Editor, o que deixaria
            // o jogo aberto enquanto o .bat tenta sobrescrever os arquivos em uso.
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        catch (Exception e)
        {
            VTTLog.UpdaterError($"[Updater] Falha ao iniciar o .bat: {e.Message}");

            if (painelBloqueioCliques != null)
                painelBloqueioCliques.SetActive(false);

            if (PopupManager.Instance != null)
            {
                PopupManager.Instance.MostrarErro("Falha ao Atualizar",
                    "Não foi possível iniciar o instalador da atualização.",
                    onOk: () =>
                    {
                        if (painelAvisoAtualizacao != null)
                            painelAvisoAtualizacao.SetActive(true);
                    });
            }
        }
    }
}
