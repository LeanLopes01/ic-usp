using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI; // Necessário para usar o Button
using TMPro;

// Um botão que abre uma URL fixa — configure quantos quiser na lista BuscadorDeNotas.links.
[System.Serializable]
public class LinkConfig
{
    [Tooltip("Apenas para organização no Inspector, não é usado em código.")]
    public string nomeAnotacao;
    public Button botao;
    public string url;
}

public class BuscadorDeNotas : MonoBehaviour
{
    [Header("UI - Textos")]
    [Tooltip("Componente onde o texto das notas será exibido.")]
    [SerializeField] private TMP_Text textoNotas;
    
    [Tooltip("Componente que exibirá a versão atual do jogo.")]
    [SerializeField] private TMP_Text textoVersao;

    [Header("UI - Interação")]
    [Tooltip("Botão que o jogador vai clicar para abrir o site de notas.")]
    [SerializeField] private Button botaoSiteNotas;

    [Tooltip("Botão que ativa o sistema de tutorial (TutorialManager.IniciarTutorial) — mostra os " +
        "passos da tela atual (ver ToggleObjects.TelaAtual / TutorialManager.tutoriaisPorTela).")]
    [SerializeField] private Button botaoTutorial;

    [Header("Configurações de Links")]
    [Tooltip("Sempre use o link RAW do GitHub para baixar o texto!")]
    [SerializeField] private string urlDoArquivoRaw = "https://raw.githubusercontent.com/LeanLopes01/dicevtt.github.io/main/NotasAtualizacao.md";
    
    [Tooltip("Link da página web bonitinha que vai abrir no navegador do jogador.")]
    [SerializeField] private string urlDaPaginaWeb = "https://leanlopes01.github.io/dicevtt.github.io/"; // Ajuste para o link real da sua página

    [Header("Links Genéricos")]
    [Tooltip("Configure quantos botões quiser — cada um abre a URL correspondente ao clicar (ex: redes sociais, Discord, itch.io etc).")]
    [SerializeField] private List<LinkConfig> links = new List<LinkConfig>();

    private void Start()
    {
        // 1. Exibe a versão atual do jogo configurada no Player Settings
        if (textoVersao != null)
        {
            textoVersao.text = "Versão " + Application.version;
        }

        // 2. Configura o clique do botão para abrir o site
        if (botaoSiteNotas != null)
        {
            botaoSiteNotas.onClick.AddListener(AbrirPaginaDeNotas);
        }

        // 2b. Configura o clique do botão de tutorial
        if (botaoTutorial != null)
        {
            botaoTutorial.onClick.AddListener(AbrirTutorial);
        }

        // 2c. Configura o clique de cada botão de link genérico
        foreach (LinkConfig link in links)
        {
            if (link == null || link.botao == null) continue;
            string url = link.url; // captura por valor — cada botão precisa da SUA própria URL
            link.botao.onClick.AddListener(() => AbrirLink(url));
        }

        // 3. Começa a baixar as notas de atualização
        if (textoNotas != null)
        {
            textoNotas.text = "Carregando notas de atualização...";
            StartCoroutine(BaixarTextoDoGithub());
        }
        else
        {
            Debug.LogWarning("O campo 'textoNotas' não foi atribuído no Inspector!");
        }
    }

    private void OnDestroy()
    {
        // Limpa o evento do botão para evitar erros se o objeto for destruído
        if (botaoSiteNotas != null)
        {
            botaoSiteNotas.onClick.RemoveListener(AbrirPaginaDeNotas);
        }

        if (botaoTutorial != null)
        {
            botaoTutorial.onClick.RemoveListener(AbrirTutorial);
        }

        // Os listeners de links foram registrados como lambdas (cada um fechando sua própria URL) —
        // não dá pra remover por referência de método, então limpa tudo do botão de uma vez.
        foreach (LinkConfig link in links)
        {
            if (link != null && link.botao != null)
                link.botao.onClick.RemoveAllListeners();
        }
    }

    private void AbrirPaginaDeNotas()
    {
        // Abre o navegador padrão do PC do jogador no link configurado
        Application.OpenURL(urlDaPaginaWeb);
    }

    private void AbrirLink(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        Application.OpenURL(url);
    }

    private void AbrirTutorial()
    {
        TutorialManager.Instancia?.IniciarTutorial();
    }

    private IEnumerator BaixarTextoDoGithub()
    {
        using (UnityWebRequest webRequest = UnityWebRequest.Get(urlDoArquivoRaw))
        {
            yield return webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.ConnectionError || 
                webRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"Erro ao baixar arquivo: {webRequest.error}");
                textoNotas.text = "Não foi possível carregar as notas de atualização. Verifique sua conexão.";
            }
            else
            {
                string textoBaixado = webRequest.downloadHandler.text;
                textoNotas.text = textoBaixado;
            }
        }
    }
}