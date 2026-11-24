/// <summary>
/// Contrato comum de gerenciamento de tokens JÁ plantados no mapa, implementado por MapManager
/// (Salas, online) e CampanhaMapManager (Campanhas, offline) — permite que
/// Salas/Grid/TokenButtonUI.cs (compartilhado pelos dois contextos) chame Remover/Duplicar/avisar
/// mudança de raio de visão sem saber qual dos dois managers concretos está por trás. Mesmo
/// raciocínio de Campanhas/Grid/IGerenciadorDeParedes.cs, só que pro lado de TOKENS em vez de
/// edição de mapa (paredes/fog/luz) — interface separada de propósito, pra não misturar os dois
/// domínios numa interface só.
/// </summary>
public interface IGerenciadorDeTokens
{
    /// <summary>Remove o token com este TokenId do mapa ativo, se existir.</summary>
    void RemoverTokenDoMapa(string tokenId);

    /// <summary>Duplica o token com este TokenId — a cópia nasce ao lado do original, com um novo
    /// TokenId.</summary>
    void DuplicarToken(string tokenId);

    /// <summary>Chamado depois de ITokenNoMapa.DefinirRaioVisao(...) — recalcula qualquer visão
    /// dinâmica que dependa desse token e persiste o novo raio.</summary>
    void NotificarRaioVisaoAlterado(ITokenNoMapa token);
}
