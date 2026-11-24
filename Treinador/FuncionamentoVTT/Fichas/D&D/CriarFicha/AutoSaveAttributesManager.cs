using System.Collections;
using UnityEngine;

/// <summary>
/// Script independente para sincronizar e salvar continuamente os atributos em LocalFichaStorage.
/// Coloque este script na GameObject da TelaCriarFichaDnd para garantir salvamento automático.
/// </summary>
public class AutoSaveAttributesManager : MonoBehaviour
{
    private int ultimoFor = 0;
    private int ultimoDex = 0;
    private int ultimoCon = 0;
    private int ultimoInt = 0;
    private int ultimoSab = 0;
    private int ultimoCar = 0;
    
    private float tempoUltimaSincronizacao = 0f;
    private const float INTERVALO_SINCRONIZACAO = 0.5f; // Sincronizar a cada 0.5 segundos

    // fichasDndId ativo no momento em que o cache (ultimoFor..ultimoCar) foi carregado/recarregado
    // pela última vez. SalvarAtributosAgora compara isto contra GlobalFichaData.fichasDndId atual
    // antes de gravar — se a ficha ativa mudou sem esse cache ter sido recarregado (o que costumava
    // acontecer em qualquer troca de ficha que não desativa/reativa esta tela, já que
    // SetActive(true) numa tela já ativa não redispara OnEnable/Start), o timer de 0.5s gravaria
    // silenciosamente os atributos da ficha ANTERIOR no arquivo da ficha NOVA.
    private int fichaIdCarregada = 0;

    void OnEnable()
    {
        // Não dá pra confiar só em OnEnable/Start pra saber quando a ficha ativa mudou — troca de
        // ficha "no meio do caminho", com esta tela permanecendo ativa, chama
        // GlobalFichaData.IniciarSessaoParaFicha sem nunca desativar este GameObject. Este evento é
        // disparado por esse portão sempre que isso acontece, independente do ciclo de vida do
        // GameObject.
        GlobalFichaData.OnSessaoDeFichaIniciada += OnSessaoDeFichaIniciada;
    }

    void OnDisable()
    {
        GlobalFichaData.OnSessaoDeFichaIniciada -= OnSessaoDeFichaIniciada;
    }

    private void OnSessaoDeFichaIniciada()
    {
        tempoUltimaSincronizacao = 0f;
        CarregarAtributosSalvos();
    }

    void Start()
    {
        // Carregar atributos salvos ao iniciar
        CarregarAtributosSalvos();
        VTTLog.Save("AutoSave iniciado - sincronização automática ativa");
    }

    void Update()
    {
        // Sincronizar periodicamente e quando detectar mudanças
        tempoUltimaSincronizacao += Time.deltaTime;
        
        if (tempoUltimaSincronizacao >= INTERVALO_SINCRONIZACAO)
        {
            tempoUltimaSincronizacao = 0f;
            VerificarMudancasESincronizar();
        }
    }

    private void VerificarMudancasESincronizar()
    {
        // Verificar se os atributos em GlobalFichaData mudaram
        if (GlobalFichaData.forca != ultimoFor ||
            GlobalFichaData.destreza != ultimoDex ||
            GlobalFichaData.constituicao != ultimoCon ||
            GlobalFichaData.inteligencia != ultimoInt ||
            GlobalFichaData.sabedoria != ultimoSab ||
            GlobalFichaData.carisma != ultimoCar)
        {
            // Atributos mudaram, salvar
            SalvarAtributosAgora();
            
            // Atualizar cache de valores
            ultimoFor = GlobalFichaData.forca;
            ultimoDex = GlobalFichaData.destreza;
            ultimoCon = GlobalFichaData.constituicao;
            ultimoInt = GlobalFichaData.inteligencia;
            ultimoSab = GlobalFichaData.sabedoria;
            ultimoCar = GlobalFichaData.carisma;
        }
    }

    private void SalvarAtributosAgora()
    {
        if (GlobalFichaData.fichasDndId <= 0)
        {
            return; // Nenhuma ficha carregada
        }

        if (GlobalFichaData.fichasDndId != fichaIdCarregada)
        {
            // O cache local (ultimoFor..ultimoCar) foi carregado pra uma ficha diferente da que
            // está ativa agora — gravar aqui escreveria os atributos errados no arquivo da ficha
            // nova. Isso deveria se resolver sozinho no próximo tick assim que
            // OnSessaoDeFichaIniciada recarregar o cache; se persistir, é sinal de que algum ponto
            // de entrada trocou de ficha sem passar por GlobalFichaData.IniciarSessaoParaFicha.
            Debug.LogWarning($"[AutoSaveAttributesManager] Pulando auto-save: cache carregado para ficha {fichaIdCarregada}, mas a ficha ativa agora é {GlobalFichaData.fichasDndId}.");
            return;
        }

        // NÃO salvar se todos os atributos estão em 10 (valor padrão vazio)
        bool todosDez = GlobalFichaData.forca == 10 && GlobalFichaData.destreza == 10 && 
                        GlobalFichaData.constituicao == 10 && GlobalFichaData.inteligencia == 10 && 
                        GlobalFichaData.sabedoria == 10 && GlobalFichaData.carisma == 10;
        
        if (todosDez)
        {
            return; // Não salvar valores padrão
        }

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        bool ok = LocalFichaStorage.SaveAttributes(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            GlobalFichaData.forca,
            GlobalFichaData.destreza,
            GlobalFichaData.constituicao,
            GlobalFichaData.inteligencia,
            GlobalFichaData.sabedoria,
            GlobalFichaData.carisma
        );

        if (ok)
        {
            VTTLog.Save($"✓ Auto-salvou: FOR={GlobalFichaData.forca}, DEX={GlobalFichaData.destreza}, CON={GlobalFichaData.constituicao}, INT={GlobalFichaData.inteligencia}, SAB={GlobalFichaData.sabedoria}, CAR={GlobalFichaData.carisma}");
        }
    }

    private void CarregarAtributosSalvos()
    {
        if (GlobalFichaData.fichasDndId <= 0)
        {
            VTTLog.Load("Nenhuma ficha carregada, pulando carregamento.");
            fichaIdCarregada = 0;
            return;
        }

        fichaIdCarregada = GlobalFichaData.fichasDndId;

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        if (LocalFichaStorage.TryLoadAttributes(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            out int forca,
            out int destreza,
            out int constituicao,
            out int inteligencia,
            out int sabedoria,
            out int carisma))
        {
            // Validar que os valores não são todos 10 (padrão vazio)
            bool valoresValidos = !(forca == 10 && destreza == 10 && constituicao == 10 && 
                                    inteligencia == 10 && sabedoria == 10 && carisma == 10);
            
            // Se os valores salvos forem diferentes dos atuais E válidos, atualizar GlobalFichaData
            if (valoresValidos && (GlobalFichaData.forca == 0 || GlobalFichaData.destreza == 0))
            {
                GlobalFichaData.forca = forca;
                GlobalFichaData.destreza = destreza;
                GlobalFichaData.constituicao = constituicao;
                GlobalFichaData.inteligencia = inteligencia;
                GlobalFichaData.sabedoria = sabedoria;
                GlobalFichaData.carisma = carisma;

                // Atualizar cache
                ultimoFor = forca;
                ultimoDex = destreza;
                ultimoCon = constituicao;
                ultimoInt = inteligencia;
                ultimoSab = sabedoria;
                ultimoCar = carisma;

                VTTLog.Load($"✓ Carregados de LocalFichaStorage: FOR={forca}, DEX={destreza}, CON={constituicao}, INT={inteligencia}, SAB={sabedoria}, CAR={carisma}");
            }
        }
    }

    /// <summary>
    /// Força uma sincronização imediata (pode ser chamado por outros scripts quando necessário)
    /// </summary>
    public void ForcarSincronizacao()
    {
        VTTLog.Save("Sincronização forçada");
        SalvarAtributosAgora();
        
        ultimoFor = GlobalFichaData.forca;
        ultimoDex = GlobalFichaData.destreza;
        ultimoCon = GlobalFichaData.constituicao;
        ultimoInt = GlobalFichaData.inteligencia;
        ultimoSab = GlobalFichaData.sabedoria;
        ultimoCar = GlobalFichaData.carisma;
    }

    /// <summary>
    /// Força recarregar dos valores salvos em LocalFichaStorage (útil quando subraca é selecionada)
    /// </summary>
    public void ForcarRecarregamento()
    {
        VTTLog.Save("Recarregamento forçado de LocalFichaStorage");
        
        // Salvar primeiro
        SalvarAtributosAgora();
        
        // Depois carregar de volta
        CarregarAtributosSalvos();
    }

    void OnApplicationQuit()
    {
        // Salvar atributos uma última vez ao sair
        if (GlobalFichaData.fichasDndId > 0)
        {
            SalvarAtributosAgora();
            VTTLog.Save("Atributos salvos ao encerrar aplicação");
        }
    }
}
