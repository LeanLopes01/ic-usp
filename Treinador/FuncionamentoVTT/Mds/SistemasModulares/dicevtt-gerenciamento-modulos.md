# Especificação Técnica: Organização de Pacotes e Estratégia de Carregamento (DiceVTT)

Este documento especifica a estrutura física de arquivos, o empacotamento de dados ("Data Packs") e a arquitetura de **Lazy Loading (carregamento sob demanda)** para o DiceVTT na engine Unity. Isso garante que o sistema suporte SRDs extensos (como D&D 5e e Pathfinder 2e) sem sofrer vazamento de memória ou perda de performance [561].

---

## 1. Estrutura de Diretórios de um Módulo (Data Pack)
Cada sistema de jogo é empacotado em uma pasta autônoma que funciona como um "Mod". O DiceVTT varre a pasta `/Modules` do jogo ao ser inicializado para indexar os sistemas disponíveis.

```
/DiceVTT_Data/
├── /Modules/
│   ├── /dnd5e_srd/
│   │   ├── system_rules.xml            <-- Regras mestres, atributos e UI de criação
│   │   ├── manifest.json               <-- Nome do módulo, autor, dependências e versão
│   │   ├── /Races/
│   │   │   ├── dwarf.xml               <-- Definição completa de raça e traços
│   │   │   └── elf.xml
│   │   ├── /Classes/
│   │   │   ├── fighter.xml             <-- Progresso de nível, HP, proficiências
│   │   │   └── wizard.xml
│   │   ├── /Spells/
│   │   │   ├── magic_missile.xml       <-- Dano, escola de magia, alcance e custos
│   │   │   └── fireball.xml
│   │   └── /Assets/
│   │       ├── /Icons/                 <-- Imagens de interface e ilustrações (WebP/PNG)
│   │       └── /Tokens/                <-- Tokens padrões inclusos
```

### Exemplo de `manifest.json` do Módulo
```json
{
  "id": "dnd5e_srd",
  "name": "Dungeons & Dragons 5e SRD",
  "version": "1.0.4",
  "author": "DiceVTT Community",
  "dependencies": [],
  "supported_languages": ["pt-BR", "en-US"],
  "entry_file": "system_rules.xml"
}
```

---

## 2. A Estratégia de Performance: Indexação vs Lazy Loading
Sistemas de RPG completos contêm centenas de magias, itens e monstros [718]. Carregar todos esses XMLs de uma vez ao abrir a mesa causaria congelamento da thread principal da Unity e estouro de memória RAM.

A estratégia adotada divide o processo em duas etapas:

1. **Indexação Leve (Boot):**
   * Ao carregar o módulo, a Unity lê apenas arquivos de índice simplificados (`index.xml` ou `index.json`) de cada diretório.
   * O índice possui apenas `id`, `name` e caminho do arquivo de origem.
   * A lista de seleção exibe esses dados instantaneamente.

2. **Carregamento sob Demanda (Lazy Load):**
   * Apenas quando o usuário seleciona um item na lista (ex: clica para ver a magia *Bola de Fogo*) é que o C# abre o arquivo `fireball.xml`, processando sua descrição e modificadores aplicáveis [561].

---

## 3. Implementação do Sistema de Indexação e Lazy Loading (C#)

```csharp
using UnityEngine;
using System.IO;
using System.Xml;
using System.Collections.Generic;

public class IndexEntry
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string FilePath { get; set; }
}

public class ModuleManager : MonoBehaviour
{
    // Armazena índices leves em memória para busca rápida na interface
    private Dictionary<string, List<IndexEntry>> _categoryIndexes = new Dictionary<string, List<IndexEntry>>();
    
    private string _modulesDirectory;

    private void Awake()
    {
        _modulesDirectory = Path.Combine(Application.persistentDataPath, "Modules");
        if (!Directory.Exists(_modulesDirectory))
        {
            Directory.CreateDirectory(_modulesDirectory);
        }
    }

    // Varre o diretório do módulo para indexar itens de forma leve
    public void IndexModule(string moduleId)
    {
        string modulePath = Path.Combine(_modulesDirectory, moduleId);
        
        // Indexa magias
        string spellsPath = Path.Combine(modulePath, "Spells");
        if (Directory.Exists(spellsPath))
        {
            _categoryIndexes["spells"] = new List<IndexEntry>();
            var files = Directory.GetFiles(spellsPath, "*.xml");
            
            foreach (var file in files)
            {
                // Carrega de forma leve extraindo apenas os atributos do nó principal
                using (XmlReader reader = XmlReader.Create(file))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element && reader.Name == "Spell")
                        {
                            string id = reader.GetAttribute("id");
                            string name = reader.GetAttribute("name");
                            
                            _categoryIndexes["spells"].Add(new IndexEntry {
                                Id = id,
                                Name = name,
                                FilePath = file
                            });
                            break; // Interrompe a leitura deste arquivo (leitura ultrarrápida de boot)
                        }
                    }
                }
            }
        }
    }

    // Retorna a lista de itens indexados para popular uma lista na UI sem pesar
    public List<IndexEntry> GetCategoryIndex(string category)
    {
        return _categoryIndexes.ContainsKey(category) ? _categoryIndexes[category] : new List<IndexEntry>();
    }

    // Carrega o conteúdo detalhado apenas quando o usuário selecionar de fato (Lazy Load)
    public XmlDocument LoadDetailedData(string category, string itemId)
    {
        var categoryList = GetCategoryIndex(category);
        var entry = categoryList.Find(x => x.Id == itemId);

        if (entry != null && File.Exists(entry.FilePath))
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(entry.FilePath); // Carrega o XML completo em memória sob demanda
            return doc;
        }

        Debug.LogError($"Erro no Lazy Loading: Não foi possível encontrar o arquivo para {category}:{itemId}");
        return null;
    }
}
```

---

## 4. Otimização de Assets de Imagens (WebP em Unity)
Texturas PNG ou TGA de alta resolução em mapas pesam intensamente no consumo de VRAM e largura de banda de upload [53, 54, 111].
* **Garantia de Performance:** A estratégia de assets exige o uso do formato **WebP** para todos os ícones de talentos, mapas e tokens de monstros [496].
* **Resultados:** Arquivos WebP alcançam taxas de compressão até 70% maiores do que PNGs normais, permitindo carregamento veloz até mesmo em conexões domésticas padrão (requisito para viabilizar o multiplayer P2P dinâmico via rede Steam).

---

## 5. Como os Data Packs se Conectam ao Marketplace DiceVTT
Como o preço final do software para o público nacional é de **R$ 99 a R$ 119 (Compra Única)**, a monetização estendida da plataforma virá de comissões do **Marketplace Nativo**:
1. Criadores e artistas brasileiros empacotam suas aventuras e artes na estrutura de pastas especificada acima.
2. Compactam em formato nativo comprimido (.dvpack).
3. Ao venderem no marketplace nativo, a plataforma retém a taxa padrão de mercado de **30%** (assim como a Steam ou Roll20) e repassa **70%** diretamente para o criador, promovendo o ecossistema nacional e garantindo renda passiva recorrente para manter a evolução do motor gráfico e recursos modulares no-code.
