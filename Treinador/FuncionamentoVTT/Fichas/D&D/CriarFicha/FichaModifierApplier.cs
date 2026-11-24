using System.Xml.Linq;
using UnityEngine;

/// <summary>
/// Lê o bloco &lt;metadata&gt;&lt;modifiers&gt; de um XML de talento ou antecedente
/// e aplica os efeitos a GlobalFichaData / ProficiencySelector.
/// </summary>
public static class FichaModifierApplier
{
    // Mapeamento de targets de atributo (códigos em inglês, como usados nos XMLs) para as
    // chaves em português usadas nos dicts de bônus. Público para reuso por TalentosSelector
    // ao montar passos de escolha para alvos compostos (ex: "STR_or_DEX") ou "any".
    public static readonly System.Collections.Generic.Dictionary<string, string> AtributoMap =
        new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "STR", "FOR" }, { "FOR", "FOR" },
            { "DEX", "DEX" },
            { "CON", "CON" },
            { "INT", "INT" },
            { "WIS", "SAB" }, { "SAB", "SAB" },
            { "CHA", "CAR" }, { "CAR", "CAR" },
        };

    /// <summary>
    /// Conveniência: recebe o XML bruto, parseia e aplica.
    /// </summary>
    public static void AplicarDeXml(string xml, string fonte = "")
    {
        if (string.IsNullOrWhiteSpace(xml)) return;
        try
        {
            XDocument doc = XDocument.Parse(xml);
            XElement metadata = doc.Root?.Element("metadata");
            if (metadata == null) return;
            AplicarModificadores(metadata, fonte);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[FichaModifierApplier] Falha ao parsear XML ({fonte}): {e.Message}");
        }
    }

    /// <summary>
    /// Recebe o nó &lt;metadata&gt; já parseado e aplica cada &lt;modifier&gt;.
    /// </summary>
    public static void AplicarModificadores(XElement metadataNode, string fonte = "")
    {
        XElement modifiersNode = metadataNode?.Element("modifiers");
        if (modifiersNode == null) return;

        foreach (XElement mod in modifiersNode.Elements("modifier"))
        {
            string tipo      = mod.Attribute("type")?.Value?.ToLowerInvariant() ?? "";
            string alvo      = mod.Attribute("target")?.Value ?? "";
            string valorStr  = mod.Attribute("value")?.Value ?? "";
            string condicao  = mod.Attribute("condition")?.Value ?? "";
            int.TryParse(valorStr, out int valorInt);

            switch (tipo)
            {
                case "proficiency":
                    AplicarProficiency(alvo, valorStr, fonte);
                    break;

                case "bonus":
                    AplicarBonus(alvo, valorInt, fonte);
                    break;

                case "stat_increase":
                    AplicarStatIncrease(alvo, valorInt, fonte);
                    break;

                case "asi":
                    // ASI genérico — exige passo de escolha, tratado pelo ClassSelector/TalentosSelector
                    Debug.Log($"[FichaModifierApplier] ASI pendente ({alvo}, +{valorStr}) de '{fonte}' — requer passo de UI");
                    break;

                case "wealth":
                    AplicarWealth(alvo, valorInt, fonte);
                    break;

                case "advantage":
                    GlobalFichaData.RegistrarVantagem(alvo, condicao, fonte);
                    break;

                case "spellcasting":
                    GlobalFichaData.RegistrarMagiaTalento(alvo, valorInt, fonte);
                    break;

                default:
                    Debug.Log($"[FichaModifierApplier] Tipo desconhecido '{tipo}' (target='{alvo}') de '{fonte}'");
                    break;
            }
        }
    }

    // ── Handlers privados ──────────────────────────────────────────────────────

    private static void AplicarProficiency(string alvo, string valor, string fonte)
    {
        string alvoLow = alvo.ToLowerInvariant();

        if (alvoLow == "initiative")
        {
            GlobalFichaData.temProficienciaIniciativa = true;
            Debug.Log($"[FichaModifierApplier] '{fonte}': Proficiência em Iniciativa ativada");
            return;
        }

        if (alvoLow == "skill")
        {
            if (!string.IsNullOrWhiteSpace(valor) &&
                !ProficiencySelector.PericiasClasseLocal.Contains(valor))
            {
                ProficiencySelector.PericiasClasseLocal.Add(valor);
                ProficiencySelector.OnImportacaoSolicitada?.Invoke();
                Debug.Log($"[FichaModifierApplier] '{fonte}': Perícia adicionada → {valor}");
            }
            return;
        }

        if (alvoLow == "tool")
        {
            if (!string.IsNullOrWhiteSpace(valor) &&
                !ProficiencySelector.FerramentasClasseLocal.Contains(valor))
            {
                ProficiencySelector.FerramentasClasseLocal.Add(valor);
                ProficiencySelector.OnImportacaoSolicitada?.Invoke();
                Debug.Log($"[FichaModifierApplier] '{fonte}': Ferramenta adicionada → {valor}");
            }
            return;
        }

        Debug.Log($"[FichaModifierApplier] '{fonte}': proficiency target '{alvo}' não mapeado");
    }

    private static void AplicarBonus(string alvo, int valor, string fonte)
    {
        string alvoLow = alvo.ToLowerInvariant();

        if (alvoLow == "ranged_attacks")
        {
            GlobalFichaData.bonusAtaqueDistancia += valor;
            Debug.Log($"[FichaModifierApplier] '{fonte}': +{valor} em ataques à distância (total={GlobalFichaData.bonusAtaqueDistancia})");
            return;
        }

        if (alvoLow == "melee_attacks")
        {
            GlobalFichaData.bonusAtaqueMelee += valor;
            Debug.Log($"[FichaModifierApplier] '{fonte}': +{valor} em ataques corpo-a-corpo");
            return;
        }

        if (alvoLow == "armor_class" || alvoLow == "ac")
        {
            GlobalFichaData.bonusCA += valor;
            Debug.Log($"[FichaModifierApplier] '{fonte}': +{valor} CA");
            return;
        }

        if (alvoLow == "initiative")
        {
            GlobalFichaData.bonusIniciativa += valor;
            Debug.Log($"[FichaModifierApplier] '{fonte}': +{valor} Iniciativa");
            return;
        }

        // Bônus direto de atributo (ex: target="STR")
        if (AtributoMap.TryGetValue(alvo, out string chaveBonusAtrib))
        {
            GlobalFichaData.bonusTalentoDict[chaveBonusAtrib] += valor;
            Debug.Log($"[FichaModifierApplier] '{fonte}': +{valor} em {chaveBonusAtrib} (talento)");
            return;
        }

        Debug.Log($"[FichaModifierApplier] '{fonte}': bonus target '{alvo}' não mapeado");
    }

    private static void AplicarStatIncrease(string alvo, int valor, string fonte)
    {
        // Alvos compostos como "STR_or_DEX" — registra como pendente para resolução posterior
        if (alvo.Contains("_or_", System.StringComparison.OrdinalIgnoreCase))
        {
            GlobalFichaData.RegistrarEscolhaStatIncrease(alvo, valor, fonte);
            Debug.Log($"[FichaModifierApplier] '{fonte}': stat_increase '{alvo}' +{valor} — requer escolha do jogador");
            return;
        }

        if (AtributoMap.TryGetValue(alvo, out string chave))
        {
            GlobalFichaData.bonusTalentoDict[chave] += valor;
            Debug.Log($"[FichaModifierApplier] '{fonte}': +{valor} em {chave}");
        }
        else
        {
            Debug.Log($"[FichaModifierApplier] '{fonte}': stat_increase target '{alvo}' não reconhecido");
        }
    }

    private static void AplicarWealth(string alvo, int valor, string fonte)
    {
        if (alvo.Equals("gp", System.StringComparison.OrdinalIgnoreCase))
        {
            GlobalFichaData.ourosIniciais += valor;
            Debug.Log($"[FichaModifierApplier] '{fonte}': +{valor} GP iniciais (total={GlobalFichaData.ourosIniciais})");
        }
        else
        {
            Debug.Log($"[FichaModifierApplier] '{fonte}': wealth target '{alvo}' não mapeado");
        }
    }
}
