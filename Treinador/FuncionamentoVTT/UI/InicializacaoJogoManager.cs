using System;
using UnityEngine;

/// <summary>
/// Orquestra a sequência de boot do jogo: espera o AutoUpdater terminar de checar atualizações e só
/// então dispara a conexão com o Steam. Se a conexão falhar, mostra um painel de aviso em vez de
/// fechar o jogo.
///
/// Este script é o ÚNICO responsável por chamar SteamManager.IniciarConexaoSteam(). Se não estiver
/// presente e habilitado na cena, o Steam nunca conecta.
/// </summary>
public class InicializacaoJogoManager : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private AutoUpdater autoUpdater;
    [SerializeField] private SteamManager steamManager;
    [SerializeField] private GameObject painelSteamNaoConectado;

    private void Awake()
    {
        if (autoUpdater == null) autoUpdater = GetComponent<AutoUpdater>();
        if (steamManager == null) steamManager = GetComponent<SteamManager>();

        if (painelSteamNaoConectado != null)
            painelSteamNaoConectado.SetActive(false);
    }

    private void OnEnable()
    {
        if (autoUpdater != null)
            autoUpdater.OnVerificacaoConcluida += OnVerificacaoDeAtualizacaoConcluida;

        if (steamManager != null)
        {
            steamManager.OnConectadoComSucesso += OnSteamConectado;
            steamManager.OnFalhaAoConectar      += OnSteamFalhou;
        }
    }

    private void OnDisable()
    {
        if (autoUpdater != null)
            autoUpdater.OnVerificacaoConcluida -= OnVerificacaoDeAtualizacaoConcluida;

        if (steamManager != null)
        {
            steamManager.OnConectadoComSucesso -= OnSteamConectado;
            steamManager.OnFalhaAoConectar      -= OnSteamFalhou;
        }
    }

    private void OnVerificacaoDeAtualizacaoConcluida()
    {
        if (steamManager == null)
        {
            Debug.LogWarning("[InicializacaoJogoManager] SteamManager não encontrado — Steam não será conectado.");
            return;
        }
        steamManager.IniciarConexaoSteam();
    }

    private void OnSteamConectado()
    {
        if (painelSteamNaoConectado != null)
            painelSteamNaoConectado.SetActive(false);
    }

    private void OnSteamFalhou(string mensagem)
    {
        Debug.LogWarning($"[InicializacaoJogoManager] Falha ao conectar ao Steam: {mensagem}");
        if (painelSteamNaoConectado != null)
            painelSteamNaoConectado.SetActive(true);
    }

    /// <summary>Ligue isso no OnClick() de um botão "Tentar novamente" no painel de aviso, se quiser
    /// um. Nota: chamar de novo após já ter conectado reexecuta login/refresh — inofensivo, mas não
    /// é um no-op puro.</summary>
    public void TentarNovamente()
    {
        if (painelSteamNaoConectado != null)
            painelSteamNaoConectado.SetActive(false);
        steamManager?.IniciarConexaoSteam();
    }
}
