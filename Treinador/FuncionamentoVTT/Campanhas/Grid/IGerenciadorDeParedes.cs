using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Contrato comum de autoria de paredes vetoriais (WallLine), implementado por CampanhaMapManager
/// (Campanhas, offline) e Salas/Grid/MapManager (online) — permite que WallEditorTools desenhe/apague
/// paredes sem saber em qual dos dois contextos está rodando. Cada implementação continua dona do seu
/// próprio dado (CampanhaMapaData vs. MapConfig) e da própria reconstrução visual/física; a interface
/// só expõe os pontos de entrada que a ferramenta de edição precisa.
/// </summary>
public interface IGerenciadorDeParedes
{
    /// <summary>Câmera do grid ativo — usada por WallEditorTools pra converter posição de tela em
    /// posição de mundo, sem precisar configurar a mesma referência duas vezes no Inspector.</summary>
    Camera CameraAtiva { get; }

    /// <summary>Inverso de NormalizadoParaMundo — converte um clique (em posição de mundo) na
    /// coordenada normalizada (0..1, relativa ao tamanho da imagem do mapa) que WallLine guarda.</summary>
    Vector2 MundoParaNormalizado(Vector3 mundo);

    /// <summary>Adiciona uma parede desenhada manualmente ao mapa ativo, reconstrói a visualização e
    /// persiste imediatamente.</summary>
    void AdicionarParedeManual(WallLine linha);

    /// <summary>Apaga só a porção de cada WallLine candidata que cai dentro do círculo da borracha
    /// (centroMundo, raioMundo) — não persiste no disco sozinho, ver PersistirMapaAtivo. Retorna true
    /// se algo mudou.</summary>
    bool ApagarParedesNoRaio(IEnumerable<WallLine> candidatas, Vector3 centroMundo, float raioMundo);

    /// <summary>Persiste o mapa ativo no disco — chamado após AdicionarParedeManual e ao concluir um
    /// arrasto de borracha em ApagarParedesNoRaio.</summary>
    void PersistirMapaAtivo();

    /// <summary>Liga/desliga a camada de depuração (overlay visual) das paredes detectadas — os
    /// colisores continuam funcionando normalmente mesmo escondidas, isso só afeta se dá pra VER onde
    /// elas estão. Ligado ao botão de WallEditorTools.AlternarVisibilidadeParedes.</summary>
    void AlternarOverlayParedes();

    // ── Fog manual (ver Salas/Grid/FogRect.cs, FogEditorTools.cs) ───────────────────────────────
    // Nome da interface ficou "paredes" por herança histórica, mas já é o contrato genérico que
    // WallEditorTools/FogEditorTools usam pra falar com qualquer um dos dois managers — extensão
    // pragmática em vez de duplicar a interface só por causa do nome.

    /// <summary>Converte uma posição de mundo na célula do grid (col, lin) que a contém — usado por
    /// FogEditorTools pra "prender" o retângulo ao grid, não à imagem do mapa.</summary>
    (int col, int lin) MundoParaCelula(Vector3 posicaoMundo);

    /// <summary>Bounds de mundo (centro/tamanho) da célula (col, lin) — inverso de MundoParaCelula,
    /// usado por FogEditorTools pra desenhar o preview do retângulo enquanto arrasta.</summary>
    Bounds CelulaParaBoundsMundo(int col, int lin);

    /// <summary>Adiciona um retângulo de fog manual ao mapa ativo, reconstrói a visualização e
    /// persiste imediatamente (host-gated em Salas/Grid/MapManager — Campanhas não tem conceito de
    /// host).</summary>
    void AdicionarFogRect(FogRect rect);

    /// <summary>Remove fog manual dentro do retângulo de células [colMin..colMax, linMin..linMax]
    /// (inclusive, mesma convenção de AdicionarFogRect) — SUBTRAI essa área de qualquer FogRect já
    /// existente que a sobreponha, mantendo o resto de cada retângulo afetado (podem sobrar de 0 a 4
    /// pedaços por retângulo original — ver SubtrairRetangulo em MapManager/CampanhaMapManager), em
    /// vez de apagar um retângulo inteiro de um clique só. Reconstrói a visualização e persiste
    /// imediatamente se algo mudou. Retorna true se algo mudou.</summary>
    bool ApagarFogNoRetangulo(int colMin, int linMin, int colMax, int linMax);

    // ── Fontes de luz estáticas (tocha — ver Salas/Grid/LightSource.cs, LightEditorTools.cs) ─────
    // Mesmo raciocínio do bloco de Fog acima: extensão pragmática da interface em vez de duplicá-la.

    /// <summary>Adiciona uma fonte de luz ao mapa ativo, recalcula a visão dinâmica e persiste
    /// imediatamente (host-gated em Salas/Grid/MapManager — Campanhas não tem conceito de host).</summary>
    void AdicionarFonteDeLuz(LightSource fonte);

    /// <summary>Remove a fonte de luz mais próxima cujo marcador (raio fixo pequeno, não o raio de
    /// luz real) contém posicaoMundo — persiste imediatamente se algo foi removido. Retorna true se
    /// removeu.</summary>
    bool RemoverFonteDeLuzEm(Vector3 posicaoMundo);

    /// <summary>Todas as fontes de luz do mapa ativo, já convertidas pra coordenadas de MUNDO
    /// (posição e raio) — usado só por LightEditorTools pra desenhar marcadores de edição das luzes
    /// já plantadas, sem precisar expor NormalizadoParaMundo/ConverterMetrosParaMundo na interface.
    /// raioMetros vem junto (além de raioMundo, já convertido) pra popular o slider de cada marcador
    /// (ver LightMarcadorUI) — a UI de edição sempre trabalha em metros, igual sliderRaioLuz.</summary>
    IEnumerable<(string id, Vector3 posicaoMundo, float raioMundo, float raioMetros)> ObterFontesDeLuzEmMundo();

    /// <summary>Muda o raio de uma fonte de luz JÁ plantada, ao vivo — recalcula a visão mas NÃO
    /// persiste/propaga sozinho (ver PersistirRaioDaFonteDeLuz). Chamado a cada tick de arrasto do
    /// slider de um LightMarcadorUI — mesmo motivo dos sliders de Iluminação não persistirem a cada
    /// frame: seria caro à toa enquanto o valor ainda está mudando.</summary>
    void DefinirRaioDaFonteDeLuz(string id, float raioMetros);

    /// <summary>Aplica (garantindo o valor final) + persiste + propaga pra rede o raio de uma fonte
    /// de luz — chamado ao SOLTAR o slider de um LightMarcadorUI (PointerUp), não a cada tick.</summary>
    void PersistirRaioDaFonteDeLuz(string id, float raioMetros);
}
