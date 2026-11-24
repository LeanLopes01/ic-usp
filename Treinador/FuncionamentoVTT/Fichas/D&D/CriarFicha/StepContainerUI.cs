using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UI;

// Gerencia um passo de criação de ficha.
// isMainStep = true  → usa mainOptionPrefab (MainOptionButtonUI) — para Classe/Raça/Antecedente/Talento.
// isMainStep = false → usa subOptionPrefab (SubChoiceOptionUI)  — para Proficiências, Equipamentos, ASI, etc.
//
// Configure dois prefabs de StepContainerUI no Unity:
//   MainStepContainerPrefab  (isMainStep = true,  mainOptionPrefab atribuído)
//   SubStepContainerPrefab   (isMainStep = false, subOptionPrefab atribuído)
public class StepContainerUI : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text stepTitleText;
    public Transform optionsContent;
    public Button titleButton; // Opcional: presente em sub-passos (Prefab 3). Clicar desfaz a escolha.

    [Header("Prefabs de Opção")]
    public GameObject mainOptionPrefab; // MainOptionButtonUI — para seleção principal
    public GameObject subOptionPrefab;  // SubChoiceOptionUI  — para sub-escolhas

    [Header("Modo")]
    public bool isMainStep = false;

    private readonly List<MainOptionButtonUI> mainOptions = new List<MainOptionButtonUI>();
    private readonly List<SubChoiceOptionUI> subOptions = new List<SubChoiceOptionUI>();

    private System.Action<string> onStepCompleted;
    private System.Action<string> onSelectCallback;
    private System.Action onDeselectCallback;

    private MainOptionButtonUI mainSelecionada;
    private SubChoiceOptionUI subSelecionada;

    private bool passoConfirmado;

    public void SetupStep(string title, Dictionary<string, string> options,
        System.Action<string> onSelectUnico, System.Action<string> onCompleted,
        bool permitirDuploCliqueDeselecionar = false, System.Action onDeselect = null)
    {
        if (stepTitleText)
        {
            stepTitleText.text = title;
            stepTitleText.color = Color.black;
        }

        onStepCompleted = onCompleted;
        onSelectCallback = onSelectUnico;
        onDeselectCallback = onDeselect;
        passoConfirmado = false;
        mainSelecionada = null;
        subSelecionada = null;

        foreach (Transform child in optionsContent) Destroy(child.gameObject);
        mainOptions.Clear();
        subOptions.Clear();

        if (titleButton != null)
        {
            titleButton.onClick.RemoveAllListeners();
            titleButton.onClick.AddListener(() => { if (passoConfirmado) DesfazerSelecao(); });
        }

        if (options == null || options.Count == 0)
        {
            // Backstop genérico: sem opções pra mostrar, não existe botão nenhum pro jogador
            // clicar — se ninguém chamar onStepCompleted, a fila de passos do chamador (ex:
            // ClassSelector.filaDePassos) trava pra sempre nesse passo, sem UI visível e sem erro
            // no log. Alguns chamadores já se protegem checando Count==0 ANTES de chamar SetupStep
            // (ex: CriarPasso_EscolhaPoderClasse), mas nem todos — isto cobre os que esquecerem.
            onSelectCallback?.Invoke(string.Empty);
            passoConfirmado = true;
            onStepCompleted?.Invoke(string.Empty);
            return;
        }

        if (isMainStep)
            CriarOpcoesMain(options, onSelectUnico, permitirDuploCliqueDeselecionar);
        else
            CriarOpcoesSub(options, onSelectUnico, permitirDuploCliqueDeselecionar);

        RebuildLayout();
    }

    private void CriarOpcoesMain(Dictionary<string, string> options, System.Action<string> onSelectUnico, bool permitirDeselecionar)
    {
        if (mainOptionPrefab == null)
        {
            Debug.LogError("[StepContainerUI] mainOptionPrefab não atribuído no prefab principal.");
            return;
        }

        foreach (var kvp in options)
        {
            var go = Instantiate(mainOptionPrefab, optionsContent, false);
            var opt = go.GetComponent<MainOptionButtonUI>();
            if (opt == null) { Destroy(go); continue; }

            opt.Setup(kvp.Key, kvp.Value,
                (clicked) =>
                {
                    if (passoConfirmado)
                    {
                        // Passo já confirmado: um clique simples só mostra os detalhes da opção
                        // de novo (mesmo comportamento de antes de confirmar) — não mexe na
                        // seleção. Desfazer continua exigindo duplo clique (segundo callback
                        // abaixo, via ConfirmarMain/DesfazerSelecao), senão a opção confirmada
                        // ficava "inclicável" pra só consultar o que já foi escolhido.
                        onSelectUnico?.Invoke(clicked.OptionId);
                        return;
                    }
                    if (mainSelecionada == clicked) { DesfazerSelecao(); return; }
                    mainSelecionada = clicked;
                    foreach (var o in mainOptions) o.SetVisualSelected(o == clicked);
                    onSelectUnico?.Invoke(clicked.OptionId);
                },
                (clicked) =>
                {
                    if (permitirDeselecionar && passoConfirmado) { DesfazerSelecao(); return; }
                    ConfirmarMain(clicked);
                }
            );
            mainOptions.Add(opt);
        }
    }

    private void CriarOpcoesSub(Dictionary<string, string> options, System.Action<string> onSelectUnico, bool permitirDeselecionar)
    {
        if (subOptionPrefab == null)
        {
            Debug.LogError("[StepContainerUI] subOptionPrefab não atribuído no prefab de sub-passo.");
            return;
        }

        foreach (var kvp in options)
        {
            var go = Instantiate(subOptionPrefab, optionsContent, false);
            var opt = go.GetComponent<SubChoiceOptionUI>();
            if (opt == null) { Destroy(go); continue; }

            opt.Setup(kvp.Key, kvp.Value,
                (clicked) =>
                {
                    if (passoConfirmado)
                    {
                        // Ver mesmo raciocínio em CriarOpcoesMain: clique simples numa opção já
                        // confirmada só reexibe os detalhes, sem alterar a seleção.
                        onSelectUnico?.Invoke(clicked.OptionId);
                        return;
                    }
                    if (subSelecionada == clicked) { DesfazerSelecao(); return; }
                    subSelecionada = clicked;
                    foreach (var o in subOptions) o.SetVisualSelected(o == clicked);
                    onSelectUnico?.Invoke(clicked.OptionId);
                },
                (clicked) =>
                {
                    if (permitirDeselecionar && passoConfirmado) { DesfazerSelecao(); return; }
                    ConfirmarSub(clicked);
                }
            );
            subOptions.Add(opt);
        }
    }

    private void ConfirmarMain(MainOptionButtonUI escolha)
    {
        passoConfirmado = true;
        foreach (var o in mainOptions)
            if (o != escolha) o.gameObject.SetActive(false);
        escolha.SetVisualSelected(true);
        RebuildLayout();
        onStepCompleted?.Invoke(escolha.OptionId);
    }

    private void ConfirmarSub(SubChoiceOptionUI escolha)
    {
        passoConfirmado = true;
        foreach (var o in subOptions)
            if (o != escolha) o.gameObject.SetActive(false);
        escolha.SetVisualSelected(true);
        RebuildLayout();
        onStepCompleted?.Invoke(escolha.OptionId);
    }

    private void DesfazerSelecao()
    {
        passoConfirmado = false;
        mainSelecionada = null;
        subSelecionada = null;

        foreach (var o in mainOptions) { o.SetVisualSelected(false); o.gameObject.SetActive(true); }
        foreach (var o in subOptions)  { o.SetVisualSelected(false); o.gameObject.SetActive(true); }

        onSelectCallback?.Invoke(string.Empty);
        RebuildLayout();
        onDeselectCallback?.Invoke();
    }

    public void AutoSelecionarOpcao(string optionId)
    {
        if (string.IsNullOrEmpty(optionId) || passoConfirmado) return;

        if (isMainStep)
        {
            var opt = mainOptions.FirstOrDefault(o =>
                string.Equals(o.OptionId, optionId, System.StringComparison.OrdinalIgnoreCase));
            if (opt != null) ConfirmarMain(opt);
        }
        else
        {
            var opt = subOptions.FirstOrDefault(o =>
                string.Equals(o.OptionId, optionId, System.StringComparison.OrdinalIgnoreCase));
            if (opt != null) ConfirmarSub(opt);
        }
    }

    private void RebuildLayout()
    {
        // Força o Canvas a processar as mudanças pendentes (ex: dezenas de opções recém
        // instanciadas no mesmo frame, como a lista completa de talentos) ANTES de recalcular
        // o layout — sem isso, o VerticalLayoutGroup/ContentSizeFitter pode calcular a altura
        // do Content do ScrollRect com base em tamanhos de filhos ainda não registrados,
        // deixando o scroll quebrado quando a lista é grande.
        Canvas.ForceUpdateCanvases();

        RectTransform rect = GetComponent<RectTransform>();
        if (rect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

        // Também reconstrói o pai para que o VerticalLayoutGroup do ScrollView
        // recalcule posições após o step encolher ao confirmar uma opção.
        if (transform.parent != null)
        {
            RectTransform parentRect = transform.parent.GetComponent<RectTransform>();
            if (parentRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
        }
    }
}
