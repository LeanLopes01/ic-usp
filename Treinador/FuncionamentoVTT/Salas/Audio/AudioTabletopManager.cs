using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>13 categorias de som ambiente — identificadores sem acento/espaço (convenção C#); o
/// rótulo em português exibido na aba fica em AudioTabletopPainelUI.CategoriaAudioSlot.nomeExibicao
/// (texto livre no Inspector), não neste enum.</summary>
public enum TipoCategoriaAudio
{
    Clima, Multidao, AmbientesNaturais, AmbientesUrbanos, Masmorras, Combate,
    Tecnologia, Magia, Criaturas, Veiculos, Tensao, TrilhasSonoras, Interface
}

/// <summary>Um som da biblioteca — autorado inteiramente no Inspector (imagem + clipe + categoria).
/// O ÍNDICE desta entrada na lista biblioteca é o identificador estável usado em rede/persistência
/// (ver AudioTabletopManager.DefinirVolume) — mesma convenção posicional já usada por outras listas
/// autoradas/carregadas do projeto, nunca reordenadas em runtime.</summary>
[Serializable]
public class SomTabletopEntry
{
    public string nome;
    public Sprite imagem;
    public AudioClip som;
    public TipoCategoriaAudio categoria;
}

/// <summary>
/// Sistema de áudio ambiente (tabletop audio) — mesmo padrão Mestre-sync-persist de
/// Salas/Efeitos/EfeitoClimaManager.cs: quem decide volume é sempre o Mestre, a mudança é aplicada
/// localmente, transmitida pra todos via NetworkSyncManager e persistida nos dados do lobby Steam.
/// Cada som tocando é uma AudioSource própria, criada sob demanda quando o volume passa de 0 e
/// destruída quando volta a 0 ("volume zerado é desligado").
/// </summary>
public class AudioTabletopManager : MonoBehaviour
{
    [Header("Dependências")]
    [SerializeField] private SteamLobbyManager steamLobbyManager;

    [Header("Biblioteca de Sons (autorada no Inspector: imagem + clipe + categoria)")]
    [SerializeField] private List<SomTabletopEntry> biblioteca = new List<SomTabletopEntry>();

    [Header("Persistência Steam Lobby")]
    [SerializeField] private string chaveLobbyAudio = "SalaAudioTabletopAtivos"; // só o Mestre escreve

    // Esparso: ausente = volume 0/desligado. Só entradas com volume>0 aparecem aqui.
    private readonly Dictionary<int, float> volumes = new Dictionary<int, float>();
    // Esparso: ausente = true (loop contínuo — comportamento default de sempre, antes deste campo
    // existir). Só guarda entradas com loop==false, que é o caso menos comum.
    private readonly Dictionary<int, bool> loops = new Dictionary<int, bool>();
    // Uma AudioSource por índice ATIVO, criada sob demanda — evita alocar N AudioSources ociosas
    // para uma biblioteca potencialmente grande da qual só um punhado toca ao mesmo tempo.
    private readonly Dictionary<int, AudioSource> fontesAtivas = new Dictionary<int, AudioSource>();

    /// <summary>Disparado sempre que UM som muda de volume (local ou recebido) — carrega o ÍNDICE
    /// alterado, não um Action genérico, pra que a UI atualize só o item afetado dentro da categoria
    /// aberta, sem reconstruir a galeria a cada tick de arrasto de slider. índice == -1 é o sentinel
    /// de "reset em massa" (saída de sala / restauração do lobby).</summary>
    public event Action<int> OnVolumeAlterado;

    private bool SouMestre() =>
        Steamworks.SteamClient.IsValid && steamLobbyManager?.SalaAtual?.IsOwnedBy(Steamworks.SteamClient.SteamId) == true;

    private void Awake()
    {
        if (steamLobbyManager == null) steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();
    }

    private void OnEnable()
    {
        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso  += OnSalaDisponivel;
            steamLobbyManager.OnEntrouNaSalaComSucesso += OnSalaDisponivel;
            steamLobbyManager.OnSaiuDaSalaComSucesso   += OnSaiuDaSala;
        }
        RestaurarAudioDoLobby();
    }

    private void OnDisable()
    {
        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso  -= OnSalaDisponivel;
            steamLobbyManager.OnEntrouNaSalaComSucesso -= OnSalaDisponivel;
            steamLobbyManager.OnSaiuDaSalaComSucesso   -= OnSaiuDaSala;
        }
    }

    private void OnSalaDisponivel(Steamworks.Data.Lobby lobby) => RestaurarAudioDoLobby();

    // Só local — para todas as fontes tocando ao sair, sem rebroadcast (evita "vazar" áudio pra fora
    // da Sala, mesmo raciocínio de EfeitoClimaManager.OnSaiuDaSala).
    private void OnSaiuDaSala()
    {
        foreach (int indice in new List<int>(fontesAtivas.Keys))
            PararEDestruirFonte(indice);
        volumes.Clear();
        loops.Clear();
        OnVolumeAlterado?.Invoke(-1);
    }

    /// <summary>Pausa (não destrói) todas as fontes tocando localmente — usado ao navegar pra fora
    /// da tela da Sala sem clicar em "Sair" (ver TelaSalaUIManager.OnTelaAlterada, mesmo raciocínio de
    /// EfeitoClimaManager.PausarEfeitosLocal). Não mexe em volumes/loops/rede — é só um "mute" local;
    /// RetomarAudioLocal desfaz de onde parou.</summary>
    public void PausarAudioLocal()
    {
        foreach (AudioSource fonte in fontesAtivas.Values)
            if (fonte != null) fonte.Pause();
    }

    /// <summary>Desfaz PausarAudioLocal — usado ao voltar pra tela da Sala com a sessão ainda
    /// ativa.</summary>
    public void RetomarAudioLocal()
    {
        foreach (AudioSource fonte in fontesAtivas.Values)
            if (fonte != null) fonte.UnPause();
    }

    // ── Leitura (UI) ─────────────────────────────────────────────────────────

    public IReadOnlyList<SomTabletopEntry> ObterBiblioteca() => biblioteca;
    public float ObterVolume(int indice) => volumes.TryGetValue(indice, out float v) ? v : 0f;
    public bool ObterLoop(int indice) => loops.TryGetValue(indice, out bool l) ? l : true;

    // ── Escrita (Mestre-only) ────────────────────────────────────────────────

    public void DefinirVolume(int indice, float volume)
    {
        if (!SouMestre()) return;
        AplicarVolumeRecebido(indice, volume);
        NetworkSyncManager.Instancia?.EnviarAudioParaTodos(indice, ObterVolume(indice), ObterLoop(indice));
        SalvarAudioNoLobby();
    }

    /// <summary>Chamado pelo NetworkSyncManager quando o volume chega de outro peer, e também na
    /// restauração ao entrar/reconectar (RestaurarAudioDoLobby) — SEM rebroadcast. Mesmo padrão de
    /// EfeitoClimaManager.AplicarClimaRecebido.</summary>
    public void AplicarVolumeRecebido(int indice, float volume)
    {
        if (indice < 0 || indice >= biblioteca.Count) return;
        float v = Mathf.Clamp01(volume);

        if (v <= 0f)
        {
            volumes.Remove(indice);
            PararEDestruirFonte(indice);
        }
        else
        {
            volumes[indice] = v;
            AudioSource fonte = ObterOuCriarFonte(indice);
            if (fonte != null)
            {
                fonte.volume = v;
                if (!fonte.isPlaying) fonte.Play();
            }
        }
        OnVolumeAlterado?.Invoke(indice);
    }

    /// <summary>Chamado pelo NetworkSyncManager ANTES de AplicarVolumeRecebido (ver case 12 —
    /// ObterOuCriarFonte precisa ler o loop já atualizado se a AudioSource estiver sendo criada agora)
    /// e também na restauração do lobby. SEM rebroadcast, mesmo padrão de AplicarVolumeRecebido.</summary>
    public void AplicarLoopRecebido(int indice, bool loop)
    {
        if (indice < 0 || indice >= biblioteca.Count) return;
        loops[indice] = loop;

        if (fontesAtivas.TryGetValue(indice, out AudioSource fonte) && fonte != null)
            fonte.loop = loop;
    }

    // ── Reprodução ───────────────────────────────────────────────────────────

    private AudioSource ObterOuCriarFonte(int indice)
    {
        if (fontesAtivas.TryGetValue(indice, out AudioSource existente) && existente != null)
            return existente;

        SomTabletopEntry entrada = biblioteca[indice];
        if (entrada?.som == null) return null;

        GameObject go = new GameObject($"Som_{indice}_{entrada.nome}");
        go.transform.SetParent(transform, false);
        AudioSource fonte = go.AddComponent<AudioSource>();
        fonte.clip = entrada.som;
        fonte.loop = ObterLoop(indice); // default true — ambiência contínua, salvo loop==false explícito
        fonte.playOnAwake = false;
        fonte.spatialBlend = 0f;    // 2D — mixer de mesa, não som posicional de cena
        fontesAtivas[indice] = fonte;
        return fonte;
    }

    private void PararEDestruirFonte(int indice)
    {
        if (fontesAtivas.TryGetValue(indice, out AudioSource fonte) && fonte != null)
            Destroy(fonte.gameObject);
        fontesAtivas.Remove(indice);
    }

    // ── Persistência Steam Lobby (esparsa: só entradas com volume>0) ───────────

    [Serializable] private class AudioLobbyEntry { public int indice; public float volume; public bool loop = true; }
    [Serializable] private class AudioLobbyPayload { public List<AudioLobbyEntry> ativos = new List<AudioLobbyEntry>(); }

    private void SalvarAudioNoLobby()
    {
        if (!SouMestre() || steamLobbyManager == null) return;

        var payload = new AudioLobbyPayload();
        foreach (KeyValuePair<int, float> par in volumes)
            payload.ativos.Add(new AudioLobbyEntry { indice = par.Key, volume = par.Value, loop = ObterLoop(par.Key) });
        steamLobbyManager.DefinirDadoDaSala(chaveLobbyAudio, JsonUtility.ToJson(payload));
    }

    private void RestaurarAudioDoLobby()
    {
        if (steamLobbyManager == null || !steamLobbyManager.SalaAtual.HasValue) return;

        string json = steamLobbyManager.ObterDadoDaSala(chaveLobbyAudio);
        if (string.IsNullOrWhiteSpace(json)) return;

        AudioLobbyPayload payload;
        try { payload = JsonUtility.FromJson<AudioLobbyPayload>(json); }
        catch (Exception e) { Debug.LogWarning("[AudioTabletop] JSON de áudio inválido no lobby: " + e.Message); return; }
        if (payload?.ativos == null) return;

        foreach (AudioLobbyEntry entrada in payload.ativos)
        {
            // Loop ANTES do volume — mesma ordem e mesmo motivo do case 12 de NetworkSyncManager.
            AplicarLoopRecebido(entrada.indice, entrada.loop);
            AplicarVolumeRecebido(entrada.indice, entrada.volume); // bounds-checked dentro do método
        }
    }
}
