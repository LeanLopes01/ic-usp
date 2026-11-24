# Especificação Técnica: Motor de Efeitos e Modificadores Dinâmicos (DiceVTT)

Este documento especifica a evolução da arquitetura do **CharacterState** do DiceVTT. O modelo original baseado em um dicionário estático simples é substituído por um **Pipeline de Cálculo de Atributos Orientado a Eventos**, resolvendo o acoplamento de modificadores e automatizando o cálculo de regras complexas do sistema SRD de D&D 5e.

---

## 1. O Problema do Dicionário Estático
No modelo básico, se um personagem equipa um item que dá `+2 de Força` (ex: Manoplas de Força de Ogro), o C# precisa alterar diretamente o valor de `"str"` no dicionário. Isso gera os seguintes problemas:
1. **Perda de Rastreabilidade:** O sistema não sabe de onde veio o bônus de `+2`.
2. **Dificuldade de Desequipamento:** Ao remover o item, o sistema precisa saber exatamente quanto subtrair (e se havia outros efeitos aplicando bônus idênticos que não deveriam acumular).
3. **Regras de Empilhamento (Stacking):** Em sistemas de RPG, bônus do mesmo tipo (ex: duas magias de bônus de "Armadura de Égide") geralmente não se acumulam.

---

## 2. A Solução: Pipeline Dinâmico de Modificadores (Modifier Pipeline)
Em vez de salvar apenas o valor final, o **CharacterState** armazena o **Valor Base** de cada propriedade e uma lista de **Modificadores Ativos**. O valor final é calculado dinamicamente passando os modificadores por um filtro de prioridade, tipo e empilhamento.

```
[Valor Base: Força = 13] 
       │
       ▼
[Modificador: +2 (Item, Tipo: Melhoria)] ──► [Filtro de Empilhamento] ──► [Valor Calculado: Força = 15]
       ▲
[Modificador: +1 (Magia, Tipo: Moral)]
```

### Estrutura do Modificador no XML de Itens/Talentos
Os modificadores são declarados de forma puramente descritiva no XML do conteúdo:

```xml
<Item id="gauntlets_of_ogre_power" type="Equipment" slot="Hands">
    <Name>Manoplas de Força de Ogro</Name>
    <Description>Seu valor de Força se torna 19 enquanto você usar estas manoplas.</Description>
    <Modifiers>
        <!-- Define um modificador de override (substituição) em vez de adição -->
        <Modifier property="str" type="Override" value="19" condition="is_equipped" priority="10" />
    </Modifiers>
</Item>

<Spell id="bless">
    <Name>Abençoar (Bless)</Name>
    <Modifiers>
        <!-- Modificador aditivo que aplica uma fórmula matemática a jogadas de ataque -->
        <Modifier property="attack_bonus" type="Additive" value="1d4" tag="BlessEffect" />
    </Modifiers>
</Spell>
```

---

## 3. Implementação em C# (Unity)

Abaixo está o modelo de dados evoluído para suportar modificadores dinâmicos rastreáveis e reativos.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

public enum ModifierType
{
    Additive,  // Soma ao valor base (ex: +2)
    Multiplier, // Multiplica o valor (ex: x1.5)
    Override,   // Substitui o valor se for maior (ex: Força se torna 19)
    Flat        // Define o valor de forma absoluta ignorando o base
}

public class Modifier
{
    public string SourceId { get; set; }      // ID do item, magia ou talento gerador
    public string PropertyId { get; set; }    // Ex: "str", "ac", "speed"
    public ModifierType ModType { get; set; }
    public float Value { get; set; }
    public string StackingGroup { get; set; } // Modificadores do mesmo grupo não acumulam (apenas o maior se aplica)
    public string Condition { get; set; }     // Fórmula avaliada pelo interpretador (ex: "is_equipped")
    public int Priority { get; set; }         // Ordem de aplicação (menor aplica primeiro)
}

public class CharacterProperty
{
    public string Id { get; set; }
    public float BaseValue { get; set; }
    public string Formula { get; set; }       // Opcional para propriedades derivadas (ex: modificador de atributo)
}

public class CharacterState
{
    public Dictionary<string, CharacterProperty> CoreProperties = new Dictionary<string, CharacterProperty>();
    public List<Modifier> ActiveModifiers = new List<Modifier>();
    public HashSet<string> ActiveTraits = new HashSet<string>();

    // Evento disparado quando um cálculo muda para que a UI se atualize automaticamente
    public event Action<string, float> OnPropertyValueChanged;

    public void AddModifier(Modifier mod)
    {
        ActiveModifiers.Add(mod);
        TriggerRecalculation(mod.PropertyId);
    }

    public void RemoveModifierBySource(string sourceId)
    {
        var modsToRemove = ActiveModifiers.Where(m => m.SourceId == sourceId).ToList();
        foreach (var mod in modsToRemove)
        {
            ActiveModifiers.Remove(mod);
            TriggerRecalculation(mod.PropertyId);
        }
    }

    public float GetValue(string propertyId)
    {
        if (!CoreProperties.ContainsKey(propertyId)) return 0;

        var prop = CoreProperties[propertyId];
        
        // Se for propriedade derivada por fórmula matemática (ex: HP máximo baseado na classe)
        if (!string.IsNullOrEmpty(prop.Formula))
        {
            return ResolveFormula(prop.Formula);
        }

        // Se for um atributo base (ex: Força), resolve através do pipeline de modificadores
        return CalculateWithModifiers(prop);
    }

    private float CalculateWithModifiers(CharacterProperty prop)
    {
        float currentValue = prop.BaseValue;

        // 1. Filtra modificadores ativos que passam na condição
        var validMods = ActiveModifiers
            .Where(m => m.PropertyId == prop.Id)
            .OrderBy(m => m.Priority)
            .ToList();

        // 2. Resolve empilhamento (apenas o maior bônus por grupo de empilhamento se aplica)
        var stackedMods = ResolveStacking(validMods);

        // 3. Aplica os modificadores respeitando a hierarquia matemática de operações
        // Ordem: Overrides -> Additive -> Multipliers
        var overrides = stackedMods.Where(m => m.ModType == ModifierType.Override);
        if (overrides.Any())
        {
            currentValue = overrides.Max(m => m.Value); // Garante que o maior override vence
        }

        float additiveSum = stackedMods.Where(m => m.ModType == ModifierType.Additive).Sum(m => m.Value);
        currentValue += additiveSum;

        float multiplierProduct = stackedMods.Where(m => m.ModType == ModifierType.Multiplier)
            .Aggregate(1.0f, (acc, m) => acc * m.Value);
        currentValue *= multiplierProduct;

        return currentValue;
    }

    private List<Modifier> ResolveStacking(List<Modifier> rawModifiers)
    {
        var resolved = new List<Modifier>();
        var grouped = rawModifiers.GroupBy(m => m.StackingGroup);

        foreach (var group in grouped)
        {
            if (string.IsNullOrEmpty(group.Key))
            {
                // Sem grupo de empilhamento: acumula todos
                resolved.AddRange(group);
            }
            else
            {
                // Mesmo grupo: aplica apenas o maior modificador da lista
                resolved.Add(group.OrderByDescending(m => m.Value).First());
            }
        }
        return resolved;
    }

    private void TriggerRecalculation(string propertyId)
    {
        float newValue = GetValue(propertyId);
        OnPropertyValueChanged?.Invoke(propertyId, newValue);
        
        // Propaga recálculo para propriedades dependentes (ex: mudar "str" recalcula "str_mod")
        PropagateDependentProperties(propertyId);
    }

    private void PropagateDependentProperties(string changedPropertyId)
    {
        // Encontra propriedades cuja fórmula contenha o ID alterado
        foreach (var prop in CoreProperties.Values)
        {
            if (!string.IsNullOrEmpty(prop.Formula) && prop.Formula.Contains($"{{{changedPropertyId}}}"))
            {
                TriggerRecalculation(prop.Id);
            }
        }
    }

    private float ResolveFormula(string formula)
    {
        // O interpretador de fórmulas (ExpressionEvaluator) processará e resolverá a string.
        return ExpressionEvaluator.Instance.Evaluate(formula, this);
    }
}
```

---

## 4. Ganhos de Modularidade com esta Abordagem
* **Modularidade Extrema:** Talentos, raças, magias e itens agora apenas injetam e removem objetos do tipo `Modifier` na lista ativa de atributos.
* **Segurança de Dados:** O estado base (`BaseValue`) do personagem nunca é alterado ou corrompido por itens equipados temporariamente.
* **Reatividade Robusta:** O sistema de eventos notifica a UI e recalcula propriedades derivadas em cascata de forma limpa e automática.
