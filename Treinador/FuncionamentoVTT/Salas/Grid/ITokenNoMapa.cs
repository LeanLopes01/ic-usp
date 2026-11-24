/// <summary>
/// Contrato comum de um token JÁ plantado no mapa, implementado por MapTokenDraggable (Salas,
/// online) e CampanhaTokenDraggable (Campanhas, offline) — permite que Salas/Grid/TokenButtonUI.cs
/// (compartilhado pelos dois contextos) mostre vida/lock/esconder/raio de visão sem saber qual dos
/// dois componentes concretos está no GameObject. Mesmo raciocínio de
/// Campanhas/Grid/IGerenciadorDeParedes.cs pro lado dos managers de mapa.
/// </summary>
public interface ITokenNoMapa
{
    string TokenId { get; }
    bool Bloqueado { get; }
    bool Escondido { get; }
    int VidaAtual { get; }
    int VidaMaxima { get; }
    float RaioVisaoMetros { get; }

    void AlternarBloqueado();
    void AlternarEscondido();
    void DefinirRaioVisao(float metros);

    /// <summary>Dono do token, host da sala, ou token sem dono definido (Salas) — sempre true em
    /// Campanhas, que não tem conceito de dono/host (modo de preparação single-user).</summary>
    bool TenhoPermissaoSobreToken();
}
