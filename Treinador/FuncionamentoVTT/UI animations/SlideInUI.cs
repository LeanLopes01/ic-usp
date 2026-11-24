using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class SlidePanel
{
    public string panelName;
    public RectTransform painel;
    public Vector2 posicaoFinal;
    public Button botaoAssociado; // Opcional: botão específico para este painel
    [HideInInspector] public Vector2 posicaoForaTela;
    [HideInInspector] public bool aberto = false;
    [HideInInspector] public bool animando = false;
}

public class SlideInUI : MonoBehaviour
{
    [Header("Lista de Painéis")]
    public List<SlidePanel> paineis = new List<SlidePanel>();

    [Header("Configuração da animação")]
    public float duracao = 0.5f;  // tempo da animação em segundos
    public DirecaoAnimacao direcaoEntrada = DirecaoAnimacao.Direita;

    public enum DirecaoAnimacao
    {
        Direita,
        Esquerda,
        Cima,
        Baixo
    }

    private SlidePanel painelAtual = null;
    private Dictionary<string, SlidePanel> paineisDict = new Dictionary<string, SlidePanel>();

    void Start()
    {
        InicializarPaineis();
    }

    private void InicializarPaineis()
    {
        paineisDict.Clear();

        foreach (SlidePanel panel in paineis)
        {
            if (panel.painel != null)
            {
                // Calcula posição fora da tela baseada na direção
                panel.posicaoForaTela = CalcularPosicaoForaTela(panel.painel);
                panel.painel.anchoredPosition = panel.posicaoForaTela;
                panel.painel.gameObject.SetActive(false);
                panel.aberto = false;
                panel.animando = false;

                // Adiciona ao dicionário para acesso rápido
                paineisDict[panel.panelName] = panel;

                // Configura listener do botão se existir
                if (panel.botaoAssociado != null)
                {
                    string panelName = panel.panelName; // Captura local para a lambda
                    panel.botaoAssociado.onClick.AddListener(() => AlternarPainel(panelName));
                }
            }
        }
    }

    private Vector2 CalcularPosicaoForaTela(RectTransform painel)
    {
        float larguraTela = Screen.width;
        float alturaTela = Screen.height;

        switch (direcaoEntrada)
        {
            case DirecaoAnimacao.Direita:
                return new Vector2(larguraTela + painel.rect.width, painel.anchoredPosition.y);
            case DirecaoAnimacao.Esquerda:
                return new Vector2(-larguraTela - painel.rect.width, painel.anchoredPosition.y);
            case DirecaoAnimacao.Cima:
                return new Vector2(painel.anchoredPosition.x, alturaTela + painel.rect.height);
            case DirecaoAnimacao.Baixo:
                return new Vector2(painel.anchoredPosition.x, -alturaTela - painel.rect.height);
            default:
                return new Vector2(larguraTela + painel.rect.width, painel.anchoredPosition.y);
        }
    }

    // Método público para alternar painel pelo nome
    public void AlternarPainel(string nomePainel)
    {
        if (paineisDict.ContainsKey(nomePainel))
        {
            SlidePanel panel = paineisDict[nomePainel];
            
            if (panel.animando) return;

            if (panel.aberto)
            {
                StartCoroutine(AnimarSaida(panel));
            }
            else
            {
                // Fecha o painel atual se existir
                if (painelAtual != null && painelAtual != panel && painelAtual.aberto)
                {
                    StartCoroutine(AnimarSaida(painelAtual));
                }
                
                StartCoroutine(AnimarEntrada(panel));
                painelAtual = panel;
            }
        }
        else
        {
            Debug.LogWarning($"Painel '{nomePainel}' não encontrado!");
        }
    }

    // Método público para abrir painel específico (força abertura)
    public void AbrirPainel(string nomePainel)
    {
        if (paineisDict.ContainsKey(nomePainel))
        {
            SlidePanel panel = paineisDict[nomePainel];
            
            if (!panel.aberto && !panel.animando)
            {
                // Fecha o painel atual se existir
                if (painelAtual != null && painelAtual != panel && painelAtual.aberto)
                {
                    StartCoroutine(AnimarSaida(painelAtual));
                }
                
                StartCoroutine(AnimarEntrada(panel));
                painelAtual = panel;
            }
        }
    }

    // Método público para fechar painel específico
    public void FecharPainel(string nomePainel)
    {
        if (paineisDict.ContainsKey(nomePainel))
        {
            SlidePanel panel = paineisDict[nomePainel];
            
            if (panel.aberto && !panel.animando)
            {
                StartCoroutine(AnimarSaida(panel));
                if (painelAtual == panel)
                {
                    painelAtual = null;
                }
            }
        }
    }

    // Método público para fechar todos os painéis
    public void FecharTodosPaineis()
    {
        foreach (SlidePanel panel in paineis)
        {
            if (panel.aberto && !panel.animando)
            {
                StartCoroutine(AnimarSaida(panel));
            }
        }
        painelAtual = null;
    }

    // Método para verificar se um painel está aberto
    public bool EstaAberto(string nomePainel)
    {
        return paineisDict.ContainsKey(nomePainel) && paineisDict[nomePainel].aberto;
    }

    // Método para obter o painel atualmente aberto
    public string GetPainelAtual()
    {
        return painelAtual != null ? painelAtual.panelName : "";
    }

    private IEnumerator AnimarEntrada(SlidePanel panel)
    {
        panel.animando = true;
        panel.painel.gameObject.SetActive(true);

        float tempo = 0f;
        Vector2 inicio = panel.posicaoForaTela;
        Vector2 fim = panel.posicaoFinal;

        while (tempo < duracao)
        {
            tempo += Time.deltaTime;
            float t = Mathf.SmoothStep(0, 1, tempo / duracao);
            panel.painel.anchoredPosition = Vector2.Lerp(inicio, fim, t);
            yield return null;
        }

        panel.painel.anchoredPosition = fim;
        panel.aberto = true;
        panel.animando = false;
    }

    private IEnumerator AnimarSaida(SlidePanel panel)
    {
        panel.animando = true;

        float tempo = 0f;
        Vector2 inicio = panel.painel.anchoredPosition;
        Vector2 fim = panel.posicaoForaTela;

        while (tempo < duracao)
        {
            tempo += Time.deltaTime;
            float t = Mathf.SmoothStep(0, 1, tempo / duracao);
            panel.painel.anchoredPosition = Vector2.Lerp(inicio, fim, t);
            yield return null;
        }

        panel.painel.anchoredPosition = fim;
        panel.painel.gameObject.SetActive(false);
        panel.aberto = false;
        panel.animando = false;
    }

    // Método para adicionar painel dinamicamente (útil para criação em runtime)
    public void AdicionarPainel(SlidePanel novoPainel)
    {
        if (!paineisDict.ContainsKey(novoPainel.panelName))
        {
            // Inicializa o novo painel
            novoPainel.posicaoForaTela = CalcularPosicaoForaTela(novoPainel.painel);
            novoPainel.painel.anchoredPosition = novoPainel.posicaoForaTela;
            novoPainel.painel.gameObject.SetActive(false);
            novoPainel.aberto = false;
            novoPainel.animando = false;

            paineis.Add(novoPainel);
            paineisDict[novoPainel.panelName] = novoPainel;

            // Configura listener se houver botão associado
            if (novoPainel.botaoAssociado != null)
            {
                string panelName = novoPainel.panelName;
                novoPainel.botaoAssociado.onClick.AddListener(() => AlternarPainel(panelName));
            }
        }
        else
        {
            Debug.LogWarning($"Já existe um painel com o nome '{novoPainel.panelName}'!");
        }
    }

    // Método para remover painel
    public void RemoverPainel(string nomePainel)
    {
        if (paineisDict.ContainsKey(nomePainel))
        {
            SlidePanel panel = paineisDict[nomePainel];
            
            // Fecha o painel se estiver aberto
            if (panel.aberto)
            {
                FecharPainel(nomePainel);
            }

            // Remove dos containers
            paineis.Remove(panel);
            paineisDict.Remove(nomePainel);

            // Remove listener do botão
            if (panel.botaoAssociado != null)
            {
                panel.botaoAssociado.onClick.RemoveAllListeners();
            }
        }
    }
}