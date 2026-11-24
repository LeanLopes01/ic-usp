using System;
using System.Collections.Generic;

/// <summary>
/// Converte uma cena de Campanha (CampanhaMapaData — mapa+grid+paredes+tokens de NPC) pra dentro dos
/// dados persistidos de uma Sala (SalaData). Chamado pelo botão "Enviar para Sala" em
/// Campanhas/Grid/CampanhaMapItemUI.cs (painel de seleção em Campanhas/Grid/CampanhaMapGaleriaUI.cs).
///
/// Primeira (e única, até agora) ponte de código entre Campanhas/ e Salas/ — decisão consciente de
/// viver em Salas/ (não em Campanhas/), já que referencia SalaData/SalaDataStorage como alvo.
///
/// Não precisa da sala estar aberta/ativa — se estiver FECHADA, grava direto em disco via
/// SalaDataStorage, mesma forma que TelaSalaUIManager já usa antes de a sala ser carregada em
/// Salas/Grid/MapManager.cs. Se a sala-alvo já estiver ABERTA em memória, delega pra
/// MapManager.ImportarCenaDeCampanha em vez disso — gravar direto em disco nesse caso seria
/// sobrescrito pelo próximo MapManager.SalvarEstadoVTT() da sessão aberta. Sempre ADICIONA um
/// MapSaveData novo (nunca sobrescreve um mapa existente na sala, mesmo com nome igual) — decisão
/// do usuário, pra nunca arriscar perder uma edição feita manualmente dentro da Sala.
/// </summary>
public static class CampanhaParaSalaConverter
{
    public static void EnviarMapaParaSala(CampanhaMapaData mapa, string campanhaId, string salaLocalId)
    {
        if (mapa == null || string.IsNullOrWhiteSpace(campanhaId) || string.IsNullOrWhiteSpace(salaLocalId))
        {
            VTTLog.ForjaError("[CampanhaParaSalaConverter] Parâmetros inválidos — não é possível enviar.");
            return;
        }

        // Se a sala-alvo já está aberta em memória (MapManager carregado com essa sessão), delega
        // pro método feito sob medida pra esse caso (mexe em todosOsMapas + SalvarEstadoVTT) em vez
        // de gravar direto em disco — gravar direto seria sobrescrito pela PRÓXIMA chamada de
        // SalvarEstadoVTT feita pela sala aberta (que reconstrói dados.mapas inteiramente a partir
        // do que está em memória), perdendo silenciosamente a cena recém-enviada.
        MapManager mapManagerAberto = UnityEngine.Object.FindFirstObjectByType<MapManager>();
        if (mapManagerAberto != null && mapManagerAberto.SessaoAtivaPara(salaLocalId)
            && mapManagerAberto.ImportarCenaDeCampanha(mapa, campanhaId))
        {
            VTTLog.Forja($"[CampanhaParaSalaConverter] Cena '{mapa.nome}' enviada pra sala '{salaLocalId}' (sala aberta — via memória).");
            return;
        }

        SalaData dados = SalaDataStorage.HasSalaData(salaLocalId)
            ? SalaDataStorage.LoadSalaData(salaLocalId)
            : new SalaData { salaLocalId = salaLocalId };
        dados ??= new SalaData { salaLocalId = salaLocalId };

        // ── Mapa (imagem copiada pra dentro da sala — decoupled da campanha de origem) ──────────
        string imagemMapaPersistida = SalaDataStorage.PersistirImagem(mapa.imagemPath, salaLocalId, $"mapa_campanha_{mapa.mapaId}");

        List<TokenInstanceSaveData> tokensDeNpcs = ImportarTokensDeNpcs(mapa, campanhaId, salaLocalId);

        var novoMapa = new MapSaveData
        {
            nome              = mapa.nome,
            imagemPath        = imagemMapaPersistida,
            gridColunas       = mapa.gridColunas,
            gridLinhas        = mapa.gridLinhas,
            unidadesPorCelula = mapa.unidadesPorCelula,
            escalaVisual      = mapa.escalaVisual,
            ehAtivo           = false,
            vetorizado        = mapa.vetorizado,
            paredes           = mapa.paredes != null ? new List<WallLine>(mapa.paredes) : new List<WallLine>(),
            // Tokens de NPC vão direto no MapSaveData DESTA cena (não em dados.tokensNoMapa, a lista
            // legada da sala inteira — ver ImportarTokensDeNpcs) pra aparecerem no mapa certo.
            tokensNoMapa      = tokensDeNpcs,
        };

        var mapasLista = new List<MapSaveData>(dados.mapas ?? Array.Empty<MapSaveData>()) { novoMapa };
        dados.mapas = mapasLista.ToArray();

        SalaDataStorage.SaveSalaData(dados);
        VTTLog.Forja($"[CampanhaParaSalaConverter] Cena '{mapa.nome}' enviada pra sala '{salaLocalId}' " +
            $"({tokensDeNpcs.Count} token(s) de NPC).");
    }

    /// <summary>Copia o EntryData (stat block/lore) de cada NPC posicionado numa cena de Campanha +
    /// persiste o retrato pra dentro da pasta de NPCs da sala de DESTINO, e monta a lista
    /// TokenInstanceSaveData correspondente. Extraído de EnviarMapaParaSala pra ser reaproveitado por
    /// MapManager.ImportarCenaDeCampanha (import "ao vivo", sala já aberta) sem duplicar a cópia.</summary>
    public static List<TokenInstanceSaveData> ImportarTokensDeNpcs(CampanhaMapaData mapa, string campanhaId, string salaLocalId)
    {
        var tokensNovos = new List<TokenInstanceSaveData>();
        if (mapa?.tokens == null || mapa.tokens.Count == 0) return tokensNovos;

        string pastaNpcsCampanha = CampanhasLocalStorage.NpcsFolderPath(campanhaId);
        List<EntryData> npcsDaCampanha = DynamicXmlBuilder.LoadEntriesFromFolder(pastaNpcsCampanha, "npcs", Dnd5eNpcFieldCatalog.AllFields);
        var npcsPorId = new Dictionary<string, EntryData>();
        foreach (EntryData npc in npcsDaCampanha)
        {
            string id = DynamicXmlBuilder.ResolverEntryId(npc, 0);
            if (!npcsPorId.ContainsKey(id)) npcsPorId[id] = npc;
        }

        var npcsJaEnviados = new HashSet<string>();

        foreach (CampanhaTokenData t in mapa.tokens)
        {
            if (t == null || string.IsNullOrWhiteSpace(t.npcId)) continue;
            if (!npcsPorId.TryGetValue(t.npcId, out EntryData npc)) continue;

            string nome = NpcEntryFormatter.ObterNome(npc);
            string retratoPersistido = SalaDataStorage.PersistirImagem(
                NpcEntryFormatter.ObterRetratoPath(npc), salaLocalId, $"token_npc_{t.npcId}");

            tokensNovos.Add(new TokenInstanceSaveData
            {
                nomeToken       = string.IsNullOrWhiteSpace(nome) ? "NPC" : nome,
                imagemPath      = retratoPersistido,
                tokenId         = Guid.NewGuid().ToString("N"),
                posX            = t.posX,
                posY            = t.posY,
                posZ            = 0f,
                fichasGeneralId = 0,
                fichasDndId     = 0,
                ownerSteamId    = "",
                ownerPlayerId   = 0,
                monstroId       = "",
                npcIdCampanha   = t.npcId,
                raioVisaoMetros = t.raioVisaoMetros,
            });

            // Leva o EntryData INTEIRO (stat block + lore — Dnd5eNpcFieldCatalog.AllFields já cobre
            // tudo) pra dentro da sala, uma vez só por NPC (vários tokens do mesmo NPC não duplicam).
            if (npcsJaEnviados.Add(t.npcId))
                DynamicXmlBuilder.SaveEntriesAsXml(new List<EntryData> { npc }, SalaDataStorage.NpcsCampanhaFolderPath(salaLocalId), "npcs");
        }
        return tokensNovos;
    }
}
