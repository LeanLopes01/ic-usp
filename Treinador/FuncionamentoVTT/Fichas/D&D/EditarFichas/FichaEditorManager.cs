using UnityEngine;

public class FichaEditorManager : MonoBehaviour
{
    public static FichaEditorManager Instance { get; private set; }

    [Header("Referencias de Telas")]
    public GameObject TelaFichas;
    public GameObject TelaInicial;
    public GameObject TelaCriarFichasDnd;

    private int currentEditGeneralId;
    private int currentEditDndId;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (transform.parent == null)
        {
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            VTTLog.SheetWarn("FichaEditorManager em objeto filho. DontDestroyOnLoad ignorado para não remover a tela da hierarquia.");
        }
    }

    public void OpenFichaForEdit(int generalId, int dndId, string nome, int level)
    {
        VTTLog.Sheet($"OpenFichaForEdit - GeneralID: {generalId}, DnDID: {dndId}");

        // Flush do autosave pendente da ficha que estava ativa ANTES do portão central resetar
        // tudo — ver o mesmo raciocínio em ClearSelectionContext() abaixo.
        FlushAutoSavePendente();

        currentEditGeneralId = generalId;
        currentEditDndId = dndId;

        // Portão central: sempre reseta primeiro, incondicionalmente, e só então aponta para a
        // ficha alvo — garante que nenhum resíduo (classe, atributos, proficiências etc.) de uma
        // ficha anterior sobreviva nas estáticas usadas pelo assistente de criação/edição.
        GlobalFichaData.IniciarSessaoParaFicha(generalId, dndId);
        GlobalFichaData.nomePersonagem = nome;
        GlobalFichaData.levelPersonagem = level;
        // Inicia o log escopado para esta ficha, limpando histórico de fichas anteriores
        FichaLogManager.IniciarParaFicha(dndId);

        // Fechar telas atuais
        if (TelaFichas != null) TelaFichas.SetActive(false);
        if (TelaInicial != null) TelaInicial.SetActive(false);

        // Abrir tela de edição
        if (TelaCriarFichasDnd != null) 
        {
            TelaCriarFichasDnd.SetActive(true);
            
            // --- CORREÇÃO: Usar FichaDisplayUI ao invés de CurrentSelectionDisplay ---
            var displayUI = TelaCriarFichasDnd.GetComponentInChildren<FichaDisplayUI>(true);
            
            if (displayUI != null)
            {
                // Atualiza com os dados carregados
                displayUI.AtualizarTudo();
            }
            else
            {
                // Tenta buscar na cena se não achou nos filhos
                var foundDisplay = FindObjectOfType<FichaDisplayUI>();
                if (foundDisplay != null) foundDisplay.AtualizarTudo();
            }
        }
        else
        {
            VTTLog.SheetError("TelaCriarFichasDnd é NULL!");
        }
    }

    public void OpenForNewFicha()
    {
        VTTLog.Sheet("OpenForNewFicha - Nova Ficha");

        PrepareForNewFicha();

        if (TelaFichas != null) TelaFichas.SetActive(false);
        if (TelaInicial != null) TelaInicial.SetActive(false);
        
        if (TelaCriarFichasDnd != null) 
        {
            TelaCriarFichasDnd.SetActive(true);
            
            // --- CORREÇÃO: Usar FichaDisplayUI ---
            var displayUI = TelaCriarFichasDnd.GetComponentInChildren<FichaDisplayUI>(true);
            
            if (displayUI != null)
            {
                // Como os IDs são 0, o AtualizarTudo vai limpar os textos
                displayUI.AtualizarTudo(); 
            }
        }
    }

    public void ClearSelectionContext()
    {
        // Flush do autosave pendente ANTES de zerar os IDs: FichaSaveManager/CriacaoFichaProgressaoDnd
        // fazem um save síncrono no OnDisable da tela de criação, mas esse OnDisable só dispara
        // DEPOIS que este método já zerou GlobalFichaData.fichasDndId (ex: ToggleObjects.ShowScreen
        // chama ClearSelectionContext antes da corrotina de transição desativar a tela antiga). Sem
        // este flush explícito aqui, o guard "fichasDndId <= 0" dentro do save o transformava num
        // no-op silencioso, perdendo as últimas edições feitas antes de trocar de tela.
        FlushAutoSavePendente();

        currentEditGeneralId = 0;
        currentEditDndId = 0;
        GlobalFichaData.ResetSelectionIds();
        FichaLogManager.LimparHistorico();
    }

    public void PrepareForNewFicha()
    {
        // Mesmo raciocínio de ClearSelectionContext(): se havia uma ficha em edição, garante que
        // ela seja salva antes do portão central resetar tudo pra começar a nova.
        FlushAutoSavePendente();

        currentEditGeneralId = 0;
        currentEditDndId = 0;
        GlobalFichaData.IniciarSessaoParaFicha(0, 0);
        FichaLogManager.LimparHistorico();
    }

    /// <summary>Salva de forma síncrona o que estiver pendente na ficha atualmente ativa em
    /// GlobalFichaData, se houver — chamado sempre antes de qualquer operação que troque/zere o
    /// contexto de ficha ativo, para não depender do OnDisable (que dispara tarde demais, depois
    /// que os IDs já teriam sido zerados/trocados).</summary>
    private void FlushAutoSavePendente()
    {
        if (GlobalFichaData.fichasDndId > 0)
            FichaSaveManager.Instance?.SalvarLocalSincronamente();
    }

    public bool IsEditMode => currentEditGeneralId > 0 && currentEditDndId > 0;
    public int CurrentGeneralId => currentEditGeneralId;
    public int CurrentDndId => currentEditDndId;
}