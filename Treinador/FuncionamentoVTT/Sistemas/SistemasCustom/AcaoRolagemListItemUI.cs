using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Item da lista de Ações de Teste do Motor de Dados, em CustomSystemManager — nome editável +
/// parâmetros do tipo de rolagem ATIVO do sistema (painelLinearCheck: count/size/modifier/cd,
/// painelDicePool: pool_size — só um dos dois fica visível, conforme o tipo ativo no momento de
/// Configurar), botão Remover e botão Testar (abre o painel de teste, ver
/// CustomSystemManager.AbrirPainelTeste).
///
/// Cada campo de parâmetro escreve DIRETO em DefinicaoAcaoRolagem.parametros (mesma referência
/// guardada em CustomSystemManager._motorDeDados.acoes) — não existe um ObterDados() separado,
/// aoEditado só avisa o dono pra persistir (auto-save).
///
/// Prefab precisa ter:
///   - TMP_InputField: inputNome, inputCount, inputSize, inputModifier, inputCd, inputPoolSize
///   - TMP_Text: textoTipo
///   - GameObject: painelLinearCheck (contém count/size/modifier/cd), painelDicePool (contém pool_size)
///   - Button: botaoRemover, botaoTestar
/// </summary>
public class AcaoRolagemListItemUI : MonoBehaviour
{
    [Header("Base")]
    [SerializeField] private TMP_InputField inputNome;
    [SerializeField] private TMP_Text       textoTipo;
    [SerializeField] private Button         botaoRemover;
    [SerializeField] private Button         botaoTestar;

    [Header("Parâmetros — linear_check")]
    [SerializeField] private GameObject     painelLinearCheck;
    [SerializeField] private TMP_InputField inputCount;
    [SerializeField] private TMP_InputField inputSize;
    [SerializeField] private TMP_InputField inputModifier;
    [SerializeField] private TMP_InputField inputCd;

    [Header("Parâmetros — dice_pool")]
    [SerializeField] private GameObject     painelDicePool;
    [SerializeField] private TMP_InputField inputPoolSize;

    private DefinicaoAcaoRolagem _acao;
    private Action _aoEditado;

    public void Configurar(DefinicaoAcaoRolagem acao, string idTipoRolagemAtivo, Action aoRemover, Action aoTestar, Action aoEditado)
    {
        _acao = acao;
        _aoEditado = aoEditado;
        if (acao == null) return;

        if (inputNome != null)
        {
            inputNome.SetTextWithoutNotify(acao.nome);
            inputNome.onEndEdit.RemoveListener(AoNomeEditado);
            inputNome.onEndEdit.AddListener(AoNomeEditado);
        }
        if (textoTipo != null) textoTipo.text = acao.idTipoRolagem;

        bool ehDicePool = idTipoRolagemAtivo == "dice_pool";
        if (painelLinearCheck != null) painelLinearCheck.SetActive(!ehDicePool);
        if (painelDicePool != null) painelDicePool.SetActive(ehDicePool);

        ConfigurarParametro(inputCount, "count", "1");
        ConfigurarParametro(inputSize, "size", "20");
        ConfigurarParametro(inputModifier, "modifier", "0");
        ConfigurarParametro(inputCd, "cd", "0");
        ConfigurarParametro(inputPoolSize, "pool_size", "1");

        if (botaoRemover != null)
        {
            botaoRemover.onClick.RemoveAllListeners();
            botaoRemover.onClick.AddListener(() => aoRemover?.Invoke());
        }
        if (botaoTestar != null)
        {
            botaoTestar.onClick.RemoveAllListeners();
            botaoTestar.onClick.AddListener(() => aoTestar?.Invoke());
        }
    }

    private void AoNomeEditado(string novoNome)
    {
        if (_acao == null || string.IsNullOrWhiteSpace(novoNome)) return;
        _acao.nome = novoNome.Trim();
        _aoEditado?.Invoke();
    }

    private void ConfigurarParametro(TMP_InputField input, string chave, string valorPadrao)
    {
        if (input == null || _acao == null) return;

        string valorAtual = _acao.parametros.TryGetValue(chave, out string v) ? v : valorPadrao;
        input.SetTextWithoutNotify(valorAtual);

        input.onEndEdit.RemoveAllListeners();
        input.onEndEdit.AddListener(novoValor =>
        {
            _acao.parametros[chave] = novoValor;
            _aoEditado?.Invoke();
        });
    }
}
