using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Monta um TokenStatBlockData pronto pra exibição a partir de uma LocalFichaData (personagem)
/// ou de um MonstroDado (monstro/inimigo). Mesmas fórmulas de FichaGeralManager (modificador de
/// atributo, bônus de proficiência, CD mágico) replicadas aqui — ver FichaJsonModelsTooltip.cs
/// para o porquê da duplicação em vez de reaproveitar as classes privadas de lá.
/// </summary>
public static class TokenStatBlockBuilder
{
    private static readonly Dictionary<string, int> AtributoConjuracaoPorClasse = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "Mago", 3 }, { "Clérigo", 4 }, { "Druida", 4 }, { "Patrulheiro", 4 },
        { "Bardo", 5 }, { "Feiticeiro", 5 }, { "Bruxo", 5 }, { "Paladino", 5 },
    };

    private static readonly string[] NomesAtributosCurtos = { "FOR", "DES", "CON", "INT", "SAB", "CAR" };

    public static TokenStatBlockData ConstruirDeFicha(LocalFichaData ficha, ModoStatBlockTooltip modo = ModoStatBlockTooltip.Atacar)
    {
        var resultado = new TokenStatBlockData
        {
            nome = ficha.name_pc,
            vidaAtual = ficha.vida_atual,
            vidaMaxima = ficha.vida_maxima,
            vidaFormatada = string.IsNullOrWhiteSpace(ficha.dado_vida)
                ? $"{ficha.vida_atual}/{ficha.vida_maxima}"
                : $"{ficha.vida_atual}/{ficha.vida_maxima} {ficha.dado_vida}"
        };

        int[] scores =
        {
            ficha.atributos?.forca ?? 10, ficha.atributos?.destreza ?? 10, ficha.atributos?.constituicao ?? 10,
            ficha.atributos?.inteligencia ?? 10, ficha.atributos?.sabedoria ?? 10, ficha.atributos?.carisma ?? 10
        };

        int[] modificadores = new int[6];
        for (int i = 0; i < 6; i++)
        {
            modificadores[i] = Mathf.FloorToInt((scores[i] - 10) / 2f);
            resultado.modificadoresAtributos[i] = $"{NomesAtributosCurtos[i]} {modificadores[i]}";
        }

        int bonusProficiencia = Mathf.FloorToInt((Mathf.Max(1, ficha.level_pc) - 1) / 4f) + 2;

        PreencherPericias(ficha, modificadores, bonusProficiencia, resultado);
        PreencherCdMagico(ficha, modificadores, bonusProficiencia, resultado);

        // Testes: TODAS as perícias/salvaguardas (treinadas ou não), cada uma clicável pra rolar —
        // substitui ações/magias, que não fazem sentido nesse modo.
        if (modo == ModoStatBlockTooltip.Testes)
        {
            PreencherTestes(ficha, modificadores, bonusProficiencia, resultado);
        }
        else
        {
            // Curar: itens de cura do inventário no lugar das ações, só magias de cura em vez de todas.
            if (modo == ModoStatBlockTooltip.Curar)
                PreencherItensDeCura(ficha, resultado);
            else
                PreencherAcoes(ficha, resultado);

            PreencherMagiasPreparadas(ficha, modo, modificadores, bonusProficiencia, resultado);
        }

        return resultado;
    }

    // Modificador total de UMA perícia/salvaguarda — mesma fórmula pros dois (só difere em qual
    // lista de SkillSaveItemTooltip vem, ver ProficienciesInfoWrapperTooltip.skills/saves).
    // valorTotal != 0 é um override manual (ex: perícia com bônus customizado na ficha); senão soma
    // modificador de atributo + bônus de proficiência (só se treinado) + bônus extra.
    private static int CalcularTotal(SkillSaveItemTooltip item, int[] modificadores, int bonusProficiencia)
    {
        int modBase = (item.attrIndex >= 0 && item.attrIndex < 6) ? modificadores[item.attrIndex] : 0;
        return item.valorTotal != 0 ? item.valorTotal : modBase + (item.treinado ? bonusProficiencia : 0) + item.miscBonus;
    }

    private static void PreencherPericias(LocalFichaData ficha, int[] modificadores, int bonusProficiencia, TokenStatBlockData resultado)
    {
        if (string.IsNullOrWhiteSpace(ficha.proficiencies_info_json))
            return;

        try
        {
            var prof = JsonUtility.FromJson<ProficienciesInfoWrapperTooltip>(ficha.proficiencies_info_json);
            if (prof?.skills == null) return;

            foreach (SkillSaveItemTooltip skill in prof.skills)
            {
                if (skill == null || !skill.treinado) continue;
                resultado.periciasTreinadas.Add($"{AbreviarNome(skill.nome)} {CalcularTotal(skill, modificadores, bonusProficiencia)}");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TokenStatBlockBuilder] Falha ao ler perícias de '{ficha.name_pc}': {e.Message}");
        }
    }

    // Modo Testes: TODAS as perícias e salvaguardas, treinadas ou não — diferente de
    // PreencherPericias acima (que só lista as treinadas, formatado como texto compacto pro
    // cabeçalho do tooltip nos outros modos).
    private static void PreencherTestes(LocalFichaData ficha, int[] modificadores, int bonusProficiencia, TokenStatBlockData resultado)
    {
        if (string.IsNullOrWhiteSpace(ficha.proficiencies_info_json))
            return;

        try
        {
            var prof = JsonUtility.FromJson<ProficienciesInfoWrapperTooltip>(ficha.proficiencies_info_json);
            if (prof == null) return;

            if (prof.skills != null)
                foreach (SkillSaveItemTooltip skill in prof.skills)
                    if (skill != null)
                        resultado.periciasTestaveis.Add(new TesteStatBlock { nome = skill.nome, modificador = CalcularTotal(skill, modificadores, bonusProficiencia) });

            if (prof.saves != null)
                foreach (SkillSaveItemTooltip save in prof.saves)
                    if (save != null)
                        resultado.salvaguardasTestaveis.Add(new TesteStatBlock { nome = save.nome, modificador = CalcularTotal(save, modificadores, bonusProficiencia) });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TokenStatBlockBuilder] Falha ao ler testes de '{ficha.name_pc}': {e.Message}");
        }
    }

    private static void PreencherCdMagico(LocalFichaData ficha, int[] modificadores, int bonusProficiencia, TokenStatBlockData resultado)
    {
        int cdManual = -1;
        if (!string.IsNullOrWhiteSpace(ficha.combate_extra_json))
        {
            try
            {
                CombateExtraDataTooltip extra = JsonUtility.FromJson<CombateExtraDataTooltip>(ficha.combate_extra_json);
                if (extra != null) cdManual = extra.cd_magia_manual;
            }
            catch { }
        }

        string classePrincipal = ficha.classes?.PrimaryClass()?.class_name ?? string.Empty;
        bool temClasseConjuradora = !string.IsNullOrWhiteSpace(classePrincipal)
            && AtributoConjuracaoPorClasse.ContainsKey(classePrincipal.Trim());

        if (cdManual < 0 && !temClasseConjuradora)
            return; // sem override e sem classe conjuradora conhecida — não mostra CD mágico

        resultado.temCdMagico = true;
        resultado.cdMagico = cdManual >= 0 ? cdManual : 8 + CalcularBonusAtaqueMagico(ficha, modificadores, bonusProficiencia);
    }

    // Bônus de ataque mágico = bônus de proficiência + modificador do atributo de conjuração da
    // classe principal — mesma base usada pra CD mágica (CD = 8 + isto, sem o "+8"). 0 se a classe
    // não for conjuradora conhecida (ver AtributoConjuracaoPorClasse). Reaproveitado tanto pela CD
    // mágica quanto pelo bônus de ataque de magias em PreencherMagiasPreparadas.
    private static int CalcularBonusAtaqueMagico(LocalFichaData ficha, int[] modificadores, int bonusProficiencia)
    {
        string classePrincipal = ficha.classes?.PrimaryClass()?.class_name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(classePrincipal)) return 0;
        if (!AtributoConjuracaoPorClasse.TryGetValue(classePrincipal.Trim(), out int attrIdx)) return 0;
        return bonusProficiencia + modificadores[attrIdx];
    }

    private static void PreencherAcoes(LocalFichaData ficha, TokenStatBlockData resultado)
    {
        if (string.IsNullOrWhiteSpace(ficha.acoes_info_json))
            return;

        try
        {
            AcoesInfoDataTooltip info = JsonUtility.FromJson<AcoesInfoDataTooltip>(ficha.acoes_info_json);
            if (info?.actions == null) return;

            foreach (AcaoDataTooltip a in info.actions)
            {
                if (a == null || string.IsNullOrWhiteSpace(a.nome)) continue;
                string textoAlcanceAcao = $"{a.distancia} {a.detalhes}";
                int raioAreaAcao = AlcanceExtrator.ExtrairRaioAreaQuadrados(textoAlcanceAcao);
                FormaAreaAcao formaAcao = FormaAreaExtrator.ExtrairForma(textoAlcanceAcao, raioAreaAcao);
                (bool ehResistenciaAcao, string atributoAcao) = TesteResistenciaExtrator.Extrair(textoAlcanceAcao);
                int cdAcao = int.TryParse(a.cd, out int cdAcaoParseada) ? cdAcaoParseada : TesteResistenciaExtrator.ExtrairCD(textoAlcanceAcao);
                resultado.acoesOuAtaques.Add(new AcaoStatBlock
                {
                    nome = a.nome,
                    descricao = FormatarDanoAcao(a),
                    descricaoCompleta = a.detalhes,
                    bonusAtaque = ExtrairBonusAtaque(a.ataque),
                    raioAreaQuadrados = raioAreaAcao,
                    alcanceQuadrados = AlcanceExtrator.ExtrairAlcanceQuadrados(textoAlcanceAcao),
                    formaArea = formaAcao,
                    origemNoToken = FormaAreaExtrator.ExtrairOrigemSelf(textoAlcanceAcao, formaAcao),
                    ehResistencia = ehResistenciaAcao,
                    atributoResistencia = atributoAcao,
                    cd = cdAcao,
                });
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TokenStatBlockBuilder] Falha ao ler ações de '{ficha.name_pc}': {e.Message}");
        }
    }

    // Ação de ficha tem campo de dano estruturado — usa direto, sem precisar de regex.
    private static string FormatarDanoAcao(AcaoDataTooltip a)
    {
        if (string.IsNullOrWhiteSpace(a.dano)) return "-";
        return string.IsNullOrWhiteSpace(a.tipoDano) ? a.dano : $"{a.dano} {a.tipoDano}";
    }

    // "ataque" de ficha vem formatado pra exibição (ex: "+5"/"-1") — 0 se vazio/não numérico.
    private static int ExtrairBonusAtaque(string ataque)
    {
        if (string.IsNullOrWhiteSpace(ataque)) return 0;
        return int.TryParse(ataque.Trim().Replace("+", ""), out int valor) ? valor : 0;
    }

    // Nem magia nem ação de monstro têm um campo de dano separado no XML — o dano vem embutido
    // no texto livre da descrição (ex: "recebe 12d6 de dano de fogo"). Extrai TODAS as notações
    // de dado encontradas (ex: "12d6", "2d6+3") e junta com "+" — antes só pegava a primeira, então um
    // texto com dois termos de dano (ex: "8d6 de fogo, mais 2d6 necrótico") perdia o segundo. Magias
    // sem dano direto (efeitos, buffs) não terão match e caem no fallback "-". Mesmo lookahead
    // (?!\s*d) de RolagemDados.RegexNotacao, mesma razão (evita o modificador engolir outro termo).
    private static readonly Regex RegexDado = new Regex(@"\d+\s*d\s*\d+(\s*[+-]\s*\d+(?!\s*d))?", RegexOptions.IgnoreCase);

    private static string ExtrairDanoDoTexto(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "-";

        MatchCollection matches = RegexDado.Matches(texto);
        if (matches.Count == 0) return "-";

        var termos = new List<string>();
        foreach (Match m in matches)
            termos.Add(Regex.Replace(m.Value, @"\s+", ""));

        return string.Join("+", termos);
    }

    private static void PreencherMagiasPreparadas(LocalFichaData ficha, ModoStatBlockTooltip modo,
        int[] modificadores, int bonusProficiencia, TokenStatBlockData resultado)
    {
        if (ficha.magias_json?.magias == null || ficha.magias_json.magias.Count == 0)
            return;

        // Mesmo bônus pra TODA magia da lista (ver decisão no plano: magias de resistência não têm
        // "ataque" de verdade, mas por simplicidade toda magia rola ataque+dano por ora).
        int bonusAtaqueMagico = CalcularBonusAtaqueMagico(ficha, modificadores, bonusProficiencia);

        foreach (GrimorioSaveItem sp in ficha.magias_json.magias)
        {
            if (sp == null) continue;

            (string nomeCatalogo, int nivelCatalogo, string descricaoCatalogo, string formaXml, string origemXml,
                string tempoConjuracaoXml, string rangeXml, string componentesXml, string duracaoXml) = FichaMagiaCatalogoLoader.ObterInfo(sp.id);
            string nome = !string.IsNullOrWhiteSpace(sp.nomeOverride) ? sp.nomeOverride : nomeCatalogo;
            bool isTruque = nivelCatalogo <= 0;

            // Truques (nível 0) são sempre considerados preparados; magias de nível exigem preparada=true.
            if (!isTruque && !sp.preparada) continue;

            // Curar: só entra magia de cura (aproximação por nome — não existe campo estruturado
            // "tipo de efeito" nos dados locais).
            if (modo == ModoStatBlockTooltip.Curar && !EhDeCura(nome)) continue;

            // Cascata de prioridade (mais confiável primeiro): <area>/<origem> explícitos > campo
            // <range> estruturado (novo — "Self (15-foot cone)", "60", "Touch" etc., ver
            // AlcanceExtrator.ExtrairDeCampoRange) > regex melhor-esforço sobre a descrição livre
            // (única opção pras magias que ainda não ganharam os campos novos).
            AlcanceExtrator.RangeInfo rangeInfo = AlcanceExtrator.ExtrairDeCampoRange(rangeXml);

            int raioAreaMagia = rangeInfo.raioAreaQuadrados > 0
                ? rangeInfo.raioAreaQuadrados
                : AlcanceExtrator.ExtrairRaioAreaQuadrados(descricaoCatalogo);

            FormaAreaAcao formaMagia = ParsearFormaXml(formaXml)
                ?? rangeInfo.forma
                ?? FormaAreaExtrator.ExtrairForma(descricaoCatalogo, raioAreaMagia);

            bool? origemXmlParseada = ParsearOrigemXml(origemXml);
            bool origemSelfMagia = origemXmlParseada ?? (rangeInfo.reconhecido
                ? rangeInfo.origemSelf
                : FormaAreaExtrator.ExtrairOrigemSelf(descricaoCatalogo, formaMagia));

            int alcanceMagia = origemSelfMagia ? 0
                : rangeInfo.reconhecido && rangeInfo.alcanceQuadrados > 0 ? rangeInfo.alcanceQuadrados
                : AlcanceExtrator.ExtrairAlcanceQuadrados(descricaoCatalogo);

            // CD de magia é SEMPRE calculada do conjurador (8 + prof + mod), nunca impressa por
            // magia — reaproveita resultado.cdMagico, já calculado por PreencherCdMagico antes desta
            // função no mesmo ConstruirDeFicha (ver TokenStatBlockData.temCdMagico/cdMagico).
            (bool ehResistenciaMagia, string atributoMagia) = TesteResistenciaExtrator.Extrair(descricaoCatalogo);
            int cdMagia = resultado.temCdMagico ? resultado.cdMagico : 0;

            resultado.magiasPreparadas.Add(new AcaoStatBlock
            {
                nome = nome,
                descricao = ExtrairDanoDoTexto(descricaoCatalogo),
                descricaoCompleta = descricaoCatalogo,
                bonusAtaque = bonusAtaqueMagico,
                raioAreaQuadrados = raioAreaMagia,
                alcanceQuadrados = alcanceMagia,
                formaArea = formaMagia,
                origemNoToken = origemSelfMagia,
                tempoConjuracao = tempoConjuracaoXml,
                alcanceTexto = FormatarAlcanceTexto(rangeXml),
                componentes = componentesXml,
                duracaoTexto = duracaoXml,
                ehResistencia = ehResistenciaMagia,
                atributoResistencia = atributoMagia,
                cd = cdMagia,
            });
        }
    }

    // "<area>circulo</area>" (ver FichaMagiaCatalogoLoader/plano de Forma de Área) — null se a tag
    // não existir nesse arquivo de magia ainda ou não bater nenhum valor conhecido, pra quem chama
    // cair no fallback por texto (FormaAreaExtrator) em vez de assumir Quadrado.
    private static FormaAreaAcao? ParsearFormaXml(string formaXml)
    {
        if (string.IsNullOrWhiteSpace(formaXml)) return null;
        switch (formaXml.Trim().ToLowerInvariant())
        {
            case "circulo": case "círculo": case "esfera": case "cilindro": return FormaAreaAcao.Circulo;
            case "cone": return FormaAreaAcao.Cone;
            case "linha": case "line": return FormaAreaAcao.Linha;
            case "quadrado": case "cubo": return FormaAreaAcao.Quadrado;
            default: return null;
        }
    }

    // "60" → "60 pés", "0" → "Pessoal", "Touch" → "Toque", "Self (15-foot cone)" → "Pessoal
    // (15-foot cone)" — texto cru (ex: "Special") passa direto, melhor mostrar algo que nada.
    private static string FormatarAlcanceTexto(string rangeXml)
    {
        if (string.IsNullOrWhiteSpace(rangeXml)) return string.Empty;
        string texto = rangeXml.Trim();

        if (int.TryParse(texto, out int pes)) return pes <= 0 ? "Pessoal" : $"{pes} pés";
        if (texto.Equals("touch", System.StringComparison.OrdinalIgnoreCase)) return "Toque";
        if (texto.StartsWith("self", System.StringComparison.OrdinalIgnoreCase)) return "Pessoal" + texto.Substring(4);
        return texto;
    }

    private static bool? ParsearOrigemXml(string origemXml)
    {
        if (string.IsNullOrWhiteSpace(origemXml)) return null;
        string valor = origemXml.Trim().ToLowerInvariant();
        if (valor == "self") return true;
        if (valor == "ponto") return false;
        return null;
    }

    // Ficha: substitui a lista de ações pelos itens de cura carregados (poções, etc.) quando o
    // modo é Curar. Mesma aproximação por nome usada nas magias — sem campo estruturado "tipo".
    private static void PreencherItensDeCura(LocalFichaData ficha, TokenStatBlockData resultado)
    {
        if (ficha.inventario_json?.itens == null)
            return;

        try
        {
            foreach (InventorySaveItem item in ficha.inventario_json.itens)
            {
                if (item == null) continue;

                (string nomeCatalogo, string descricaoCatalogo) = ItemCatalogoLoader.ObterInfo(item.id);
                string nome = !string.IsNullOrWhiteSpace(item.customName) ? item.customName : nomeCatalogo;
                if (string.IsNullOrWhiteSpace(nome) || !EhDeCura(nome)) continue;

                string descricao = !string.IsNullOrWhiteSpace(item.descricaoOverride) ? item.descricaoOverride : descricaoCatalogo;
                int raioAreaItem = AlcanceExtrator.ExtrairRaioAreaQuadrados(descricao);
                FormaAreaAcao formaItem = FormaAreaExtrator.ExtrairForma(descricao, raioAreaItem);
                resultado.acoesOuAtaques.Add(new AcaoStatBlock
                {
                    nome = nome,
                    descricao = ExtrairDanoDoTexto(descricao),
                    descricaoCompleta = descricao,
                    raioAreaQuadrados = raioAreaItem,
                    alcanceQuadrados = AlcanceExtrator.ExtrairAlcanceQuadrados(descricao),
                    formaArea = formaItem,
                    origemNoToken = FormaAreaExtrator.ExtrairOrigemSelf(descricao, formaItem),
                });
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TokenStatBlockBuilder] Falha ao ler itens de cura de '{ficha.name_pc}': {e.Message}");
        }
    }

    private static bool EhDeCura(string nome) => !string.IsNullOrWhiteSpace(nome) && nome.IndexOf("cura", StringComparison.OrdinalIgnoreCase) >= 0;

    // Monstros não têm inventário nem grimório neste modelo de dados — modo Curar simplesmente não
    // encontra nada pra mostrar (seções ficam ocultas, ver TokenStatBlockTooltipUI).
    public static TokenStatBlockData ConstruirDeMonstro(MonstroDado monstro, ModoStatBlockTooltip modo = ModoStatBlockTooltip.Atacar)
    {
        var resultado = new TokenStatBlockData { nome = monstro.nome };

        resultado.vidaMaxima = ExtrairPrimeiroNumero(monstro.hp);
        resultado.vidaAtual = resultado.vidaMaxima; // monstro recém-colocado — sem estado de dano salvo ainda

        string dadoDeVida = ExtrairDadoDeVida(monstro.hp);
        resultado.vidaFormatada = string.IsNullOrEmpty(dadoDeVida)
            ? $"{resultado.vidaAtual}/{resultado.vidaMaxima}"
            : $"{resultado.vidaAtual}/{resultado.vidaMaxima} {dadoDeVida}";

        string[] atributosBrutos = { monstro.str, monstro.dex, monstro.con, monstro.intel, monstro.wis, monstro.cha };
        for (int i = 0; i < 6; i++)
        {
            int mod = ExtrairModificador(atributosBrutos[i]);
            resultado.modificadoresAtributos[i] = $"{NomesAtributosCurtos[i]} {mod}";
        }

        // Monstros não separam perícia por perícia no XML — a string já vem pré-formatada
        // (ex: "Percepção +4, Furtividade +6"); divide por vírgula e abrevia cada nome.
        if (!string.IsNullOrWhiteSpace(monstro.pericias))
        {
            foreach (string parte in monstro.pericias.Split(','))
            {
                string limpo = parte.Trim();
                if (string.IsNullOrEmpty(limpo)) continue;

                int ultimoEspaco = limpo.LastIndexOf(' ');
                if (ultimoEspaco > 0)
                {
                    string nomeParte = limpo.Substring(0, ultimoEspaco).Trim();
                    string valorParte = limpo.Substring(ultimoEspaco + 1).Trim().Replace("+", "");
                    resultado.periciasTreinadas.Add($"{AbreviarNome(nomeParte)} {valorParte}");

                    // Testes: mesmo parsing, só que guarda o número (não o texto abreviado) — o
                    // bônus já vem pronto no texto do monstro, sem precisar calcular nada.
                    if (modo == ModoStatBlockTooltip.Testes && int.TryParse(valorParte, out int valorNumerico))
                        resultado.periciasTestaveis.Add(new TesteStatBlock { nome = nomeParte, modificador = valorNumerico });
                }
                else
                {
                    resultado.periciasTreinadas.Add(limpo);
                }
            }
        }

        if (modo == ModoStatBlockTooltip.Testes)
        {
            // Monstro não tem campo próprio de salvaguarda neste modelo — usa o modificador de
            // atributo bruto direto, assumindo NENHUMA proficiência (melhor esforço, sem dado melhor
            // disponível; pode ficar errado pra monstros proficientes num save específico).
            string[] nomesSalvaguardas = { "Força", "Destreza", "Constituição", "Inteligência", "Sabedoria", "Carisma" };
            for (int i = 0; i < 6; i++)
                resultado.salvaguardasTestaveis.Add(new TesteStatBlock { nome = nomesSalvaguardas[i], modificador = ExtrairModificador(atributosBrutos[i]) });
        }
        else if (modo != ModoStatBlockTooltip.Curar && monstro.acoes != null)
        {
            foreach (MonstroAcao a in monstro.acoes)
            {
                int raioAreaMonstro = AlcanceExtrator.ExtrairRaioAreaQuadrados(a.descricao);
                FormaAreaAcao formaMonstro = FormaAreaExtrator.ExtrairForma(a.descricao, raioAreaMonstro);
                (bool ehResistenciaMonstro, string atributoMonstro) = TesteResistenciaExtrator.Extrair(a.descricao);
                resultado.acoesOuAtaques.Add(new AcaoStatBlock
                {
                    nome = a.nome,
                    descricao = ExtrairDanoDoTexto(a.descricao),
                    descricaoCompleta = a.descricao,
                    bonusAtaque = ExtrairBonusAtaqueDoTexto(a.descricao),
                    raioAreaQuadrados = raioAreaMonstro,
                    alcanceQuadrados = AlcanceExtrator.ExtrairAlcanceQuadrados(a.descricao),
                    formaArea = formaMonstro,
                    origemNoToken = FormaAreaExtrator.ExtrairOrigemSelf(a.descricao, formaMonstro),
                    ehResistencia = ehResistenciaMonstro,
                    atributoResistencia = atributoMonstro,
                    // Stat block de monstro sempre imprime a própria CD no texto ("CD 13 de Destreza").
                    cd = TesteResistenciaExtrator.ExtrairCD(a.descricao),
                });
            }
        }

        return resultado;
    }

    // Público — reaproveitado por MapTokenDraggable.GarantirVidaInicializada pra extrair a vida
    // máxima de um monstro (mesmo texto "45 (6d8+18)" usado aqui) sem duplicar o parsing.
    public static int ExtrairPrimeiroNumero(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0;

        var digitos = new System.Text.StringBuilder();
        foreach (char c in texto)
        {
            if (char.IsDigit(c)) digitos.Append(c);
            else if (digitos.Length > 0) break;
        }
        return digitos.Length > 0 ? int.Parse(digitos.ToString()) : 0;
    }

    // "16 (+3)" → +3
    private static int ExtrairModificador(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0;

        int inicio = texto.IndexOf('(');
        int fim = texto.IndexOf(')');
        if (inicio < 0 || fim < 0 || fim <= inicio) return 0;

        string dentro = texto.Substring(inicio + 1, fim - inicio - 1).Replace("+", "");
        return int.TryParse(dentro, out int v) ? v : 0;
    }

    // Ação de monstro não tem bônus de ataque estruturado — melhor esforço via regex sobre o texto
    // livre, ex: "Ataque corpo a corpo com arma: +5 para acertar, alcance 5 pés..." ou o equivalente
    // em inglês "+5 to hit". 0 (rola d20 puro) se não achar o padrão.
    private static readonly Regex RegexBonusAtaque = new Regex(@"\+(\d+)\s*(para acertar|to hit)", RegexOptions.IgnoreCase);

    private static int ExtrairBonusAtaqueDoTexto(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0;

        Match match = RegexBonusAtaque.Match(texto);
        return match.Success && int.TryParse(match.Groups[1].Value, out int v) ? v : 0;
    }

    // "45 (6d8+18)" → "6d8+18"
    private static string ExtrairDadoDeVida(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        int inicio = texto.IndexOf('(');
        int fim = texto.IndexOf(')');
        if (inicio < 0 || fim < 0 || fim <= inicio) return string.Empty;

        return texto.Substring(inicio + 1, fim - inicio - 1).Trim();
    }

    // "Furtividade" → "Furt" — só pra caber compacto na linha de perícias do tooltip.
    private static string AbreviarNome(string nome, int tamanho = 4)
    {
        if (string.IsNullOrWhiteSpace(nome)) return string.Empty;
        nome = nome.Trim();
        return nome.Length <= tamanho ? nome : nome.Substring(0, tamanho);
    }
}
