# Especificação Técnica: Motor de Interface e UI Declarativa (DiceVTT)

Este documento especifica a refatoração da camada de visualização do DiceVTT. No modelo básico de arquitetura, o código C# possuía dependências rígidas (hardcoded switches) que amarravam tipos específicos de dados XML a Prefabs fixos de interface (ex: `PointBuyUI` ou `SelectionUI`). 

A evolução especificada aqui introduz um **Motor de UI Declarativa Baseado em Componentes** (similar ao conceito de XML do Android, HTML ou XAML).

---

## 1. O Problema da UI Acoplada ao C#
No modelo original de arquitetura:
```csharp
switch (type) {
    case "selection": uiInstance = Instantiate(prefabSelectionUI, container); break;
    case "point_buy": uiInstance = Instantiate(prefabPointBuyUI, container); break;
}
```
Isso limita o VTT das seguintes maneiras:
1. **Layouts Rígidos:** Modders não conseguem mudar a disposição das caixas, cores ou ordem dos inputs na tela de criação de personagens ou na ficha de jogo.
2. **Adição de Atributos Limitada:** Se um modder inventar um atributo personalizado no XML (ex: "Insanidade" ou "Mana"), ele precisará reescrever e compilar códigos C# na Unity para que esse novo atributo ganhe uma caixa com barra deslizante ou um campo de edição na interface.

---

## 2. A Solução: Componentes de UI Declarados no XML
A interface de criação e visualização da ficha passa a ser montada dinamicamente através de uma árvore de nós estruturada em XML de layout. O arquivo de definição do sistema de RPG dita a estrutura visual e os componentes de entrada:

### Exemplo de Definição de Interface no XML (Ficha de Personagem)
```xml
<UserInterface id="character_sheet">
    <!-- Layout de Grid de 3 Colunas -->
    <Grid columns="3" padding="10" spacing="15">
        
        <!-- Coluna 1: Atributos Primários (D&D Estilo) -->
        <Section title="Atributos">
            <VerticalLayout spacing="5">
                <AttributeRow label="Força" bind_val="str" bind_mod="str_mod" />
                <AttributeRow label="Destreza" bind_val="dex" bind_mod="dex_mod" />
                <AttributeRow label="Constituição" bind_val="con" bind_mod="con_mod" />
                <AttributeRow label="Inteligência" bind_val="int" bind_mod="int_mod" />
                <AttributeRow label="Sabedoria" bind_val="wis" bind_mod="wis_mod" />
                <AttributeRow label="Carisma" bind_val="cha" bind_mod="cha_mod" />
            </VerticalLayout>
        </Section>

        <!-- Coluna 2: Status Atuais e Pontos de Vida -->
        <Section title="Status de Combate">
            <VerticalLayout spacing="10">
                <HorizontalLayout>
                    <Label text="Pontos de Vida:" bold="true" />
                    <!-- Input de texto interativo que sincroniza automaticamente bidirecional com a variável hp -->
                    <InputNumber bind="hp" min="0" max="max_hp" />
                    <Label text="/" />
                    <!-- Exibe apenas o valor derivado máximo calculado -->
                    <Label bind="max_hp" />
                </HorizontalLayout>
                
                <HorizontalLayout>
                    <Label text="Classe de Armadura:" />
                    <Label bind="ac" font_size="24" alignment="Center" />
                </HorizontalLayout>
            </VerticalLayout>
        </Section>

        <!-- Coluna 3: Inventário e Equipamentos -->
        <Section title="Inventário">
            <ListView data_source="inventory" item_template="inventory_item_row" />
        </Section>

    </Grid>
</UserInterface>
```

---

## 3. Arquitetura de Implementação na Unity (C#)

O Unity instanciará elementos de layout de forma recursiva baseando-se no arquivo XML lido. Em vez de termos prefabs massivos para cada tela inteira, criamos **Micro-Prefabs anatômicos**:

* `prefab_Grid` (Container que organiza filhos em tabela)
* `prefab_VerticalLayout` (Organiza filhos de cima para baixo)
* `prefab_Label` (Texto estático ou reativo)
* `prefab_InputNumber` (Campo para alterar valores numéricos)
* `prefab_AttributeRow` (Componente de bônus e valor combinado de RPG)

### O Script de UI Dinâmica Recursiva (UIBuilder)
```csharp
using UnityEngine;
using System.Xml;
using System.Collections.Generic;

public class UIBuilder : MonoBehaviour
{
    [Header("Componentes de Layout")]
    public GameObject prefabGrid;
    public GameObject prefabVerticalLayout;
    public GameObject prefabHorizontalLayout;
    public GameObject prefabSection;

    [Header("Componentes de Entrada / Dados")]
    public GameObject prefabLabel;
    public GameObject prefabInputNumber;
    public GameObject prefabAttributeRow;

    private CharacterState _characterState;

    public void BuildInterface(XmlNode uiRootNode, Transform parentContainer, CharacterState state)
    {
        _characterState = state;
        BuildElement(uiRootNode, parentContainer);
    }

    private void BuildElement(XmlNode node, Transform parent)
    {
        GameObject currentInstance = null;

        switch (node.Name.ToLower())
        {
            // --- TRATAMENTO DE CONTAINERS E LAYOUTS ---
            case "grid":
                currentInstance = Instantiate(prefabGrid, parent);
                ConfigureGrid(node, currentInstance);
                break;

            case "verticallayout":
                currentInstance = Instantiate(prefabVerticalLayout, parent);
                break;

            case "horizontallayout":
                currentInstance = Instantiate(prefabHorizontalLayout, parent);
                break;

            case "section":
                currentInstance = Instantiate(prefabSection, parent);
                currentInstance.GetComponent<SectionUI>().SetTitle(node.Attributes["title"]?.Value);
                break;

            // --- TRATAMENTO DE COMPONENTES ATÔMICOS ---
            case "label":
                currentInstance = Instantiate(prefabLabel, parent);
                var labelComponent = currentInstance.GetComponent<ReactiveLabel>();
                
                if (node.Attributes["bind"] != null)
                {
                    // Liga o componente de texto reativamente ao estado
                    labelComponent.BindToProperty(node.Attributes["bind"].Value, _characterState);
                }
                else
                {
                    labelComponent.SetText(node.Attributes["text"]?.Value);
                }
                break;

            case "inputnumber":
                currentInstance = Instantiate(prefabInputNumber, parent);
                var inputComponent = currentInstance.GetComponent<ReactiveInputNumber>();
                string bindProp = node.Attributes["bind"]?.Value;
                inputComponent.Setup(bindProp, _characterState);
                break;

            case "attributerow":
                currentInstance = Instantiate(prefabAttributeRow, parent);
                var attrRow = currentInstance.GetComponent<ReactiveAttributeRow>();
                attrRow.Setup(
                    node.Attributes["label"]?.Value,
                    node.Attributes["bind_val"]?.Value,
                    node.Attributes["bind_mod"]?.Value,
                    _characterState
                );
                break;
        }

        // Se o elemento criado possui filhos, processa-os recursivamente colocando-os dentro do container gerado
        if (currentInstance != null && node.HasChildNodes)
        {
            Transform innerContainer = currentInstance.GetComponent<LayoutContainer>()?.ContentParent ?? currentInstance.transform;
            foreach (XmlNode child in node.ChildNodes)
            {
                BuildElement(child, innerContainer);
            }
        }
    }

    private void ConfigureGrid(XmlNode node, GameObject gridObj)
    {
        var gridLayout = gridObj.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        if (gridLayout != null && node.Attributes["columns"] != null)
        {
            int cols = int.Parse(node.Attributes["columns"].Value);
            // Configura colunas dinamicamente baseado no tamanho do canvas
        }
    }
}
```

---

## 4. Comunicação Bidirecional de Dados (Data-Binding)
Os scripts que acompanham os micro-prefabs precisam escutar o `CharacterState` para atualizar a tela caso um valor mude no motor de modificadores (ex: itens equipados). 

### Exemplo de Componente Reativo (`ReactiveLabel.cs`)
```csharp
using UnityEngine;
using TMPro;

public class ReactiveLabel : MonoBehaviour
{
    private TextMeshProUGUI _textMesh;
    private string _boundPropertyId;
    private CharacterState _state;

    private void Awake()
    {
        _textMesh = GetComponent<TextMeshProUGUI>();
    }

    public void SetText(string text)
    {
        _textMesh.text = text;
    }

    public void BindToProperty(string propertyId, CharacterState state)
    {
        _state = state;
        _boundPropertyId = propertyId;
        
        // Se inscreve para escutar mudanças imediatas desta variável
        _state.OnPropertyValueChanged += UpdateValue;
        
        // Aplica o valor inicial
        UpdateValue(_boundPropertyId, _state.GetValue(_boundPropertyId));
    }

    private void UpdateValue(string propertyId, float newValue)
    {
        if (propertyId == _boundPropertyId)
        {
            _textMesh.text = newValue.ToString();
        }
    }

    private void OnDestroy()
    {
        if (_state != null)
        {
            _state.OnPropertyValueChanged -= UpdateValue;
        }
    }
}
```

---

## 5. Vantagens do Motor Declarativo
* **Fácil tradução e acessibilidade:** Mudar a organização da ficha, tamanho de fontes ou posicionamento de itens exige apenas a edição de arquivos XML estruturados simples.
* **Layouts customizáveis por sistema:** Um jogo de D&D 5e e um jogo de Call of Cthulhu podem possuir layouts de ficha totalmente diferentes, compartilhando os mesmos componentes de base (`Label`, `InputNumber`, etc.) sem necessitar de novas compilações da Unity.
